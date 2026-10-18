using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nodalis.Core.Views;

namespace Nodalis.Infrastructure.Views;

/// <summary>
/// Stores only portable definitions in workspace JSON or private local settings.
/// No projection, Markdown content or cached view results are persisted.
/// </summary>
public sealed class FileSystemWorkspaceViewStore
{
    private const long MaxFileSize = 1024 * 1024;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly string _workspacePath;
    private readonly string _privatePath;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>
    /// Constructs a view-definition store for a single workspace GUID, keeping
    /// private views outside the workspace to avoid sharing them in backups.
    /// </summary>
    /// <param name="workspaceRoot">Absolute or relative workspace folder.</param>
    /// <param name="workspaceId">Stable workspace GUID.</param>
    /// <param name="privateRoot">Optional test or authorized user settings root.</param>
    public FileSystemWorkspaceViewStore(
        string workspaceRoot, Guid workspaceId, string? privateRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("Le workspace doit avoir un GUID stable.", nameof(workspaceId));
        }

        string root = Path.GetFullPath(workspaceRoot);
        string local = privateRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nodalis", "views");
        _workspacePath = Path.Combine(root, ".nodalis-views.json");
        _privatePath = Path.Combine(Path.GetFullPath(local),
            workspaceId.ToString("N"), "views-private.json");
        if (IsWithin(_privatePath, root))
        {
            throw new ArgumentException(
                "Les définitions privées doivent être stockées hors du workspace.",
                nameof(privateRoot));
        }
    }

    /// <summary>Returns the expected canonical definition file path for a chosen visibility.</summary>
    /// <param name="visibility">Workspace-visible or private.</param>
    /// <returns>Exact JSON file location.</returns>
    public string FilePath(WorkspaceViewVisibility visibility) => visibility switch
    {
        WorkspaceViewVisibility.Workspace => _workspacePath,
        WorkspaceViewVisibility.Private => _privatePath,
        _ => throw new ArgumentOutOfRangeException(nameof(visibility))
    };

    /// <summary>
    /// Reads an optional JSON definition document. A future schema may be
    /// browsed but is read-only; invalid JSON is never silently reset.
    /// </summary>
    /// <param name="visibility">Storage boundary.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Definitions plus a revision token for conflict detection.</returns>
    public async Task<WorkspaceViewDefinitionSnapshot> LoadAsync(
        WorkspaceViewVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        string path = FilePath(visibility);
        if (!File.Exists(path))
        {
            return new WorkspaceViewDefinitionSnapshot(visibility, [], string.Empty, false);
        }

        FileInfo file = new(path);
        if (file.Length > MaxFileSize)
        {
            throw new InvalidDataException("Fichier de vues trop volumineux : " + path);
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.LongLength > MaxFileSize)
        {
            throw new InvalidDataException("Fichier de vues trop volumineux : " + path);
        }

        WorkspaceViewDefinitionsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WorkspaceViewDefinitionsDocument>(bytes, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Fichier de définitions de vues invalide. Aucune donnée n'a été modifiée.",
                exception);
        }

        if (document is null || document.SchemaVersion < 1)
        {
            throw new InvalidDataException("Schéma des définitions de vues absent ou invalide.");
        }

        Validate(document.Views, isFuture: document.SchemaVersion >
            WorkspaceViewDefinitionsDocument.CurrentSchemaVersion);
        return new WorkspaceViewDefinitionSnapshot(visibility, document.Views,
            Convert.ToHexString(SHA256.HashData(bytes)),
            document.SchemaVersion > WorkspaceViewDefinitionsDocument.CurrentSchemaVersion);
    }

    /// <summary>
    /// Atomically replaces one definitions document if no external write
    /// changed the revision. This API never writes query results or Markdown.
    /// </summary>
    /// <param name="visibility">Storage target.</param>
    /// <param name="views">Complete new list of portable definitions.</param>
    /// <param name="expectedRevision">Revision returned by LoadAsync.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>New persisted snapshot and revision for subsequent safe writes.</returns>
    public async Task<WorkspaceViewDefinitionSnapshot> SaveAsync(
        WorkspaceViewVisibility visibility,
        IReadOnlyList<WorkspaceViewDefinition> views,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(expectedRevision);
        Validate(views, isFuture: false);
        await _writer.WaitAsync(cancellationToken);
        try
        {
            WorkspaceViewDefinitionSnapshot existing =
                await LoadAsync(visibility, cancellationToken);
            if (existing.IsReadOnly)
            {
                throw new InvalidDataException(
                    "Les définitions proviennent d'une version future : écriture interdite.");
            }

            if (!string.Equals(existing.Revision, expectedRevision, StringComparison.Ordinal))
            {
                throw new WorkspaceViewDefinitionConflictException(
                    "Les vues ont été modifiées ailleurs : recharger avant d'enregistrer.");
            }

            WorkspaceViewDefinitionsDocument document = new() { Views = views };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, Options);
            if (bytes.LongLength > MaxFileSize)
            {
                throw new InvalidDataException("La définition des vues dépasse 1 Mo.");
            }

            string path = FilePath(visibility);
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return new WorkspaceViewDefinitionSnapshot(visibility, views,
                Convert.ToHexString(SHA256.HashData(bytes)), false);
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <summary>Checks data size, GUID uniqueness, fields and query bounds.</summary>
    /// <param name="views">Definitions from disk or caller.</param>
    /// <param name="isFuture">Future versions cannot be fully validated against this schema.</param>
    private static void Validate(
        IReadOnlyList<WorkspaceViewDefinition> views, bool isFuture)
    {
        if (views is null || views.Count > 1000)
        {
            throw new InvalidDataException("Nombre de vues invalide (maximum 1000).");
        }

        HashSet<Guid> ids = [];
        foreach (WorkspaceViewDefinition item in views)
        {
            if (item.Id == Guid.Empty || !ids.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 ||
                item.Query is null || item.Columns.Count > 24 || item.Order < 0)
            {
                throw new InvalidDataException("Définition de vue incomplète ou dupliquée.");
            }

            if (!isFuture)
            {
                WorkspaceViewQueryEngine.Execute([], item.Query);
            }
        }
    }

    /// <summary>Prevents private definitions from being nested inside the workspace.</summary>
    /// <param name="path">Proposed private JSON location.</param>
    /// <param name="workspace">Canonical workspace root.</param>
    /// <returns>Whether path lies inside the workspace.</returns>
    private static bool IsWithin(string path, string workspace)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspace)) + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedPath, Path.GetFullPath(workspace),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Configures the versioned, human-readable JSON format.</summary>
    /// <returns>Options shared by reader/writer.</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
