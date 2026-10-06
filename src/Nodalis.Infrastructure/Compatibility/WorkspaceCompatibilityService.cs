using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Compatibility;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Compatibility;

public sealed class WorkspaceCompatibilityService
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a compatibility service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    public WorkspaceCompatibilityService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(
                workspaceRoot));
    }

    /// <summary>
    /// Detects the workspace schema and evaluates the central compatibility policy without writing.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The compatibility decision.</returns>
    public async Task<WorkspaceCompatibilityDecision> EvaluateAsync(
            CancellationToken cancellationToken = default)
    {
        int schemaVersion = await ReadSchemaVersionAsync(
            cancellationToken);

        return WorkspaceCompatibilityPolicy.Evaluate(
            schemaVersion);
    }

    /// <summary>
    /// Creates an isolated temporary snapshot for safe fallback reading.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The snapshot lifetime object.</returns>
    public async Task<ReadOnlyWorkspaceSnapshot> CreateReadOnlySnapshotAsync(
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Compatibility.WorkspaceCompatibilityDecision decision =
            await EvaluateAsync(
                cancellationToken);

        if (!decision.CanRead ||
            decision.CanWrite)
        {
            throw new InvalidOperationException(
                "Un snapshot de lecture seule n'est autorisé que pour un schéma lisible mais non inscriptible.");
        }

        EnsureNoReparsePoints(
            _workspaceRoot,
            cancellationToken);

        string snapshotRoot = Path.Combine(
            Path.GetTempPath(),
            "Nodalis",
            "ReadOnlySnapshots",
            Guid.NewGuid().ToString(
                "N"));

        try
        {
            await CopyDirectoryAsync(
                _workspaceRoot,
                snapshotRoot,
                cancellationToken);

            return new ReadOnlyWorkspaceSnapshot(
                _workspaceRoot,
                snapshotRoot,
                decision.WorkspaceSchemaVersion);
        }
        catch
        {
            if (Directory.Exists(
                    snapshotRoot))
            {
                Directory.Delete(
                    snapshotRoot,
                    recursive: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Reads only the schema marker from the workspace manifest.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Zero for a legacy unversioned manifest, otherwise the explicit schema.</returns>
    private async Task<int> ReadSchemaVersionAsync(
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.WorkspaceManifestFileName);

        if (!File.Exists(
                manifestPath))
        {
            throw new FileNotFoundException(
                "Le workspace ne contient pas de manifest .workspace.json.",
                manifestPath);
        }

        await using global::System.IO.FileStream stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Le manifest .workspace.json doit contenir un objet JSON.");
        }

        if (!document.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schemaElement))
        {
            return WorkspaceCompatibilityPolicy.LegacyUnversionedSchemaVersion;
        }

        if (schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(
                out int schemaVersion))
        {
            throw new InvalidDataException(
                "La propriété schemaVersion doit être un entier.");
        }

        return schemaVersion;
    }

    /// <summary>
    /// Refuses symbolic links and junctions before copying a fallback snapshot.
    /// </summary>
    /// <param name="root">The source root.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void EnsureNoReparsePoints(
            string root,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.Stack<string> pending =
            new Stack<string>();
        pending.Push(
            root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string current = pending.Pop();
            global::System.IO.DirectoryInfo directory =
                new DirectoryInfo(
                    current);

            foreach (global::System.IO.FileSystemInfo entry in directory.EnumerateFileSystemInfos(
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"La lecture seule sécurisée refuse le lien symbolique ou la jonction '{entry.FullName}'.");
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(
                        childDirectory.FullName);
                }
            }
        }
    }

    /// <summary>
    /// Copies every ordinary file and empty directory into an isolated temporary root.
    /// </summary>
    /// <param name="sourceRoot">The source workspace.</param>
    /// <param name="destinationRoot">The temporary destination.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static Task CopyDirectoryAsync(
            string sourceRoot,
            string destinationRoot,
            CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                Directory.CreateDirectory(
                    destinationRoot);

                foreach (string directory in Directory.EnumerateDirectories(
                             sourceRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Directory.CreateDirectory(
                        Path.Combine(
                            destinationRoot,
                            Path.GetRelativePath(
                                sourceRoot,
                                directory)));
                }

                foreach (string file in Directory.EnumerateFiles(
                             sourceRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string destinationPath = Path.Combine(
                        destinationRoot,
                        Path.GetRelativePath(
                            sourceRoot,
                            file));
                    string? parent = Path.GetDirectoryName(
                        destinationPath);

                    if (string.IsNullOrWhiteSpace(
                            parent))
                    {
                        throw new InvalidDataException(
                            $"Le fichier '{file}' n'a pas de destination valide dans le snapshot.");
                    }

                    Directory.CreateDirectory(
                        parent);
                    File.Copy(
                        file,
                        destinationPath,
                        overwrite: false);
                }
            },
            cancellationToken);
}

public sealed class ReadOnlyWorkspaceSnapshot : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Initializes one isolated read-only fallback snapshot.
    /// </summary>
    /// <param name="sourceRoot">The untouched source workspace.</param>
    /// <param name="snapshotRoot">The temporary snapshot root.</param>
    /// <param name="sourceSchemaVersion">The source workspace schema.</param>
    internal ReadOnlyWorkspaceSnapshot(
            string sourceRoot,
            string snapshotRoot,
            int sourceSchemaVersion)
    {
        SourceRoot = sourceRoot;
        SnapshotRoot = snapshotRoot;
        SourceSchemaVersion = sourceSchemaVersion;
    }

    public string SourceRoot { get; }

    public string SnapshotRoot { get; }

    public int SourceSchemaVersion { get; }

    /// <summary>
    /// Deletes the temporary snapshot while never touching the source workspace.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (Directory.Exists(
                    SnapshotRoot))
            {
                Directory.Delete(
                    SnapshotRoot,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class ReadOnlyWorkspaceNavigationBuilder
{
    /// <summary>
    /// Builds a metadata-agnostic tree that exposes only Markdown files and ordinary directories.
    /// </summary>
    /// <param name="snapshotRoot">The isolated snapshot root.</param>
    /// <param name="displayName">The source workspace display name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The fallback navigation root.</returns>
    public Task<WorkspaceNavigationNode> BuildAsync(
            string snapshotRoot,
            string displayName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            snapshotRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            displayName);

        string root = Path.GetFullPath(
            snapshotRoot);

        global::Nodalis.Core.Navigation.WorkspaceNavigationNode result =
            new WorkspaceNavigationNode
            {
                Id = CreateDeterministicId(
                    "."),
                DisplayName = displayName,
                Kind = WorkspaceNodeKind.Workspace,
                FullPath = root,
                Children = BuildChildren(
                    root,
                    root,
                    cancellationToken)
            };

        return Task.FromResult(
            result);
    }

    /// <summary>
    /// Recursively creates fallback navigation nodes without parsing any Nodalis metadata.
    /// </summary>
    /// <param name="root">The snapshot root.</param>
    /// <param name="directory">The current directory.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The child nodes.</returns>
    private static List<WorkspaceNavigationNode> BuildChildren(
            string root,
            string directory,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> nodes =
            new List<WorkspaceNavigationNode>();

        foreach (string childDirectory in Directory.EnumerateDirectories(
                     directory,
                     "*",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(
                     path => Path.GetFileName(
                         path),
                     StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directoryName = Path.GetFileName(
                childDirectory);

            if (directoryName.StartsWith(
                    ".",
                    StringComparison.Ordinal))
            {
                continue;
            }

            List<WorkspaceNavigationNode> children = BuildChildren(
                root,
                childDirectory,
                cancellationToken);

            if (children.Count == 0)
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(
                root,
                childDirectory);

            nodes.Add(
                new WorkspaceNavigationNode
                {
                    Id = CreateDeterministicId(
                        "folder:" + relativePath),
                    DisplayName = directoryName,
                    Kind = WorkspaceNodeKind.Folder,
                    FullPath = childDirectory,
                    Children = children
                });
        }

        foreach (string markdownPath in Directory.EnumerateFiles(
                     directory,
                     "*.md",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(
                     path => Path.GetFileName(
                         path),
                     StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(
                root,
                markdownPath);

            nodes.Add(
                new WorkspaceNavigationNode
                {
                    Id = CreateDeterministicId(
                        "document:" + relativePath),
                    DisplayName = Path.GetFileNameWithoutExtension(
                        markdownPath),
                    Kind = WorkspaceNodeKind.Document,
                    FullPath = markdownPath
                });
        }

        return nodes;
    }

    /// <summary>
    /// Creates a stable GUID from a fallback relative-path identity.
    /// </summary>
    /// <param name="value">The stable identity text.</param>
    /// <returns>The deterministic GUID.</returns>
    private static Guid CreateDeterministicId(
            string value)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                value.ToUpperInvariant()));
        byte[] guidBytes = new byte[16];

        Array.Copy(
            hash,
            guidBytes,
            guidBytes.Length);

        return new Guid(
            guidBytes);
    }
}
