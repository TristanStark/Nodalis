using System.Security.Cryptography;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Explicit, portable and optimistic persistence of project journeys.
/// Merely loading a project or previewing a template never writes any file.
/// </summary>
public sealed class ProjectJourneyService
{
    public const string JourneyFileName = ".project-journey.json";
    public const string TemplateCatalogFileName = "project-journeys.json";

    private readonly string _workspaceRoot;
    private readonly string _templatePath;

    /// <summary>Creates a journey service bound to exactly one workspace.</summary>
    /// <param name="workspaceRoot">Workspace directory.</param>
    public ProjectJourneyService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _templatePath = Path.Combine(_workspaceRoot,
            WorkspaceLayout.TemplatesDirectoryName, TemplateCatalogFileName);
    }

    /// <summary>
    /// Reads a workspace override if present, otherwise returns built-in templates.
    /// Missing templates and legacy projects are never modified on read.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current workspace template catalog.</returns>
    public async Task<ProjectJourneyTemplateCatalog> LoadTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        (await ReadTemplatesSnapshotAsync(cancellationToken)).Catalog;

    /// <summary>
    /// Reads the catalog and the SHA-256 of the exact bytes deserialized.
    /// Built-in defaults have a null revision and never create an override.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Frozen catalog copy with optional optimistic revision.</returns>
    public async Task<ProjectJourneyTemplateSnapshot> ReadTemplatesSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_templatePath))
        {
            return new ProjectJourneyTemplateSnapshot(
                ProjectJourneyDefaults.CreateCatalog(), null);
        }

        EnsureNoReparsePoints(_templatePath);
        byte[] bytes = await File.ReadAllBytesAsync(
            _templatePath, cancellationToken);
        ProjectJourneyTemplateCatalog catalog =
            JsonSerializer.Deserialize<ProjectJourneyTemplateCatalog>(
                bytes, JsonDefaults.Options) ??
            throw new InvalidDataException("Catalogue de parcours vide.");
        ValidateCatalog(catalog);
        return new ProjectJourneyTemplateSnapshot(
            catalog, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Explicitly saves workspace-specific templates; requires the exact
    /// previous byte hash when updating an existing catalog.
    /// </summary>
    /// <param name="catalog">User-reviewed template definitions.</param>
    /// <param name="expectedSha256">Prior SHA-256; null only when creating a new override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SHA-256 of the newly stored catalog.</returns>
    public async Task<string> SaveTemplatesAsync(
        ProjectJourneyTemplateCatalog catalog, string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalog(catalog);
        string templateDirectory = Path.GetDirectoryName(_templatePath)!;
        Directory.CreateDirectory(templateDirectory);
        EnsureNoReparsePoints(templateDirectory);
        return await SaveCheckedAsync(_templatePath,
            JsonSerializer.Serialize(catalog, JsonDefaults.Options),
            expectedSha256, cancellationToken);
    }

    /// <summary>
    /// Lists all templates matching the project's authoritative complexity,
    /// without creating any files. Ultra-simple gets no default selection,
    /// but may explicitly opt into a custom template.
    /// </summary>
    /// <param name="projectDirectory">Existing project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Applicable, non-activated model proposals.</returns>
    public async Task<IReadOnlyList<ProjectJourneyTemplate>> ListApplicableTemplatesAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ProjectManifest project = await LoadProjectAsync(
            ValidateProjectDirectory(projectDirectory), cancellationToken);
        ProjectJourneyTemplateCatalog catalog =
            await LoadTemplatesAsync(cancellationToken);
        return catalog.Templates.Where(item =>
            item.Complexity == project.InitialComplexity).ToArray();
    }

    /// <summary>
    /// Read-only proposal for a project's initial complexity. Ultra-simple
    /// projects intentionally receive no default journey, even when templates
    /// are customized. Creating a journey always requires an explicit call.
    /// </summary>
    /// <param name="projectDirectory">Existing project location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Proposed template or null for Ultra-simple.</returns>
    public async Task<ProjectJourneyTemplate?> PreviewDefaultAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ProjectManifest project = await LoadProjectAsync(
            ValidateProjectDirectory(projectDirectory), cancellationToken);
        if (project.InitialComplexity == ProjectComplexity.UltraSimple)
        {
            return null;
        }

        ProjectJourneyTemplateCatalog catalog = await LoadTemplatesAsync(cancellationToken);
        return catalog.Templates.FirstOrDefault(item =>
            item.Complexity == project.InitialComplexity);
    }

    /// <summary>
    /// Loads an existing project journey without adding defaults or modifying
    /// legacy project files. Archived projects remain readable.
    /// </summary>
    /// <param name="projectDirectory">Existing project location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A revisioned snapshot, or null when no journey was created.</returns>
    public async Task<ProjectJourneySnapshot?> ReadAsync(string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string directory = ValidateProjectDirectory(projectDirectory);
        ProjectManifest project = await LoadProjectAsync(directory, cancellationToken);
        string path = Path.Combine(directory, JourneyFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        EnsureNoReparsePoints(path);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        ProjectJourneyDocument document =
            JsonSerializer.Deserialize<ProjectJourneyDocument>(
                bytes, JsonDefaults.Options) ??
            throw new InvalidDataException("Parcours projet vide.");
        ValidateDocument(document, project.Id);
        return new ProjectJourneySnapshot(
            document, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Explicitly creates the first project journey from a frozen template.
    /// No existing journey can be overwritten by concurrent initialization.
    /// Pass a template key to opt an Ultra-simple project into a custom model.
    /// </summary>
    /// <param name="projectDirectory">Active project location.</param>
    /// <param name="templateKey">Optional explicit template key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Committed first revision.</returns>
    public async Task<ProjectJourneySnapshot> InitializeAsync(
        string projectDirectory, string? templateKey = null,
        CancellationToken cancellationToken = default)
    {
        string directory = ValidateProjectDirectory(projectDirectory);
        ProjectManifest project = await LoadProjectAsync(directory, cancellationToken);
        EnsureActive(project);
        ProjectJourneyTemplateCatalog catalog = await LoadTemplatesAsync(cancellationToken);
        ProjectJourneyTemplate? template = templateKey is null
            ? project.InitialComplexity == ProjectComplexity.UltraSimple
                ? null
                : catalog.Templates.FirstOrDefault(item =>
                    item.Complexity == project.InitialComplexity)
            : catalog.Templates.SingleOrDefault(item =>
                string.Equals(item.Key, templateKey, StringComparison.OrdinalIgnoreCase) &&
                item.Complexity == project.InitialComplexity);
        if (template is null)
        {
            throw new InvalidOperationException(
                "Aucun modèle de parcours applicable : choix explicite requis.");
        }

        ProjectJourneyDocument document = new()
        {
            ProjectId = project.Id,
            TemplateKey = template.Key,
            Steps = template.Steps.OrderBy(item => item.Order).Select(step =>
                new ProjectJourneyStep
                {
                    Id = Guid.NewGuid(),
                    TemplateKey = step.Key,
                    Name = step.Name,
                    Order = step.Order,
                    Operations = step.Operations.Select(operation =>
                        new ProjectJourneyOperation
                        {
                            Id = Guid.NewGuid(),
                            TemplateKey = operation.Key,
                            Name = operation.Name,
                            PromptKey = operation.PromptKey
                        }).ToList()
                }).ToList()
        };
        ValidateDocument(document, project.Id);
        string json = JsonSerializer.Serialize(document, JsonDefaults.Options);
        string path = Path.Combine(directory, JourneyFileName);
        await AtomicFileWriter.WriteNewAllTextAsync(path, json, cancellationToken);
        return new ProjectJourneySnapshot(document, ComputeHash(json));
    }

    /// <summary>
    /// Persists a user-reviewed edit under a conditional atomic replacement.
    /// An external writer changing the file after ReadAsync is never overwritten.
    /// This does not modify documents, tasks, milestones or archive status.
    /// </summary>
    /// <param name="projectDirectory">Active project location.</param>
    /// <param name="document">Complete revised journey, preserving stable IDs.</param>
    /// <param name="expectedSha256">Exact SHA-256 obtained from ReadAsync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New revision snapshot.</returns>
    public async Task<ProjectJourneySnapshot> SaveAsync(
        string projectDirectory, ProjectJourneyDocument document,
        string expectedSha256, CancellationToken cancellationToken = default)
    {
        string directory = ValidateProjectDirectory(projectDirectory);
        ProjectManifest project = await LoadProjectAsync(directory, cancellationToken);
        EnsureActive(project);
        ValidateDocument(document, project.Id);
        string json = JsonSerializer.Serialize(document, JsonDefaults.Options);
        string path = Path.Combine(directory, JourneyFileName);
        await SaveCheckedAsync(path, json, expectedSha256, cancellationToken);
        return new ProjectJourneySnapshot(document, ComputeHash(json));
    }

    /// <summary>Writes a new file without overwrite or replaces an approved revision.</summary>
    /// <param name="path">Validated target path.</param>
    /// <param name="json">Complete UTF-8 JSON.</param>
    /// <param name="expectedSha256">Previous revision or null on first creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New serialized bytes' SHA-256.</returns>
    private static async Task<string> SaveCheckedAsync(string path, string json,
        string? expectedSha256, CancellationToken cancellationToken)
    {
        if (expectedSha256 is null)
        {
            await AtomicFileWriter.WriteNewAllTextAsync(path, json, cancellationToken);
            return ComputeHash(json);
        }

        ValidateSha256(expectedSha256);
        if (!File.Exists(path))
        {
            throw new InvalidDataException("Document supprimé depuis la lecture.");
        }

        EnsureNoReparsePoints(path);
        string snapshot = Path.Combine(Path.GetDirectoryName(path)!,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".stage");
        try
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                snapshot, json, cancellationToken);
            await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                path, expectedSha256, snapshot, ComputeHash(json), cancellationToken);
        }
        finally
        {
            if (File.Exists(snapshot))
            {
                File.Delete(snapshot);
            }
        }

        return ComputeHash(json);
    }

    /// <summary>Ensures paths do not escape the workspace or traverse junctions.</summary>
    /// <param name="projectDirectory">Untrusted project location.</param>
    /// <returns>Validated absolute directory path.</returns>
    private string ValidateProjectDirectory(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        string applications = Path.Combine(_workspaceRoot,
            WorkspaceLayout.ApplicationsDirectoryName);
        string archives = Path.Combine(_workspaceRoot,
            WorkspaceLayout.ArchivesDirectoryName);
        if ((!IsWithin(full, applications) && !IsWithin(full, archives)) ||
            !Directory.Exists(full) ||
            !File.Exists(Path.Combine(full, WorkspaceLayout.ProjectManifestFileName)))
        {
            throw new InvalidDataException("Chemin hors d'un projet Nodalis existant.");
        }

        EnsureNoReparsePoints(full);
        EnsureNoReparsePoints(Path.Combine(full, WorkspaceLayout.ProjectManifestFileName));
        return full;
    }

    /// <summary>Loads the authoritative project identity.</summary>
    /// <param name="directory">Validated existing project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project manifest.</returns>
    private static async Task<ProjectManifest> LoadProjectAsync(string directory,
        CancellationToken cancellationToken)
    {
        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            Path.Combine(directory, WorkspaceLayout.ProjectManifestFileName),
            cancellationToken);
        if (project.Id == Guid.Empty || !Enum.IsDefined(project.InitialComplexity))
        {
            throw new InvalidDataException("Manifeste de projet invalide.");
        }

        return project;
    }

    /// <summary>Denies project mutations while archived.</summary>
    /// <param name="project">Current project manifest.</param>
    private static void EnsureActive(ProjectManifest project)
    {
        if (project.ArchivedUtc.HasValue || project.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Un parcours archivé est consultable mais non modifiable.");
        }
    }

    /// <summary>Rejects symlinks and reparse points in a file's ancestry.</summary>
    /// <param name="path">Existing path.</param>
    private static void EnsureNoReparsePoints(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null && File.Exists(current) || current is not null &&
               Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Chemin symbolique interdit dans un parcours projet.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>Checks directory containment with a separator boundary.</summary>
    /// <param name="path">Candidate path.</param>
    /// <param name="root">Authorized parent.</param>
    /// <returns>True only for descendants of the root.</returns>
    private static bool IsWithin(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates workspace templates, including unique keys.</summary>
    /// <param name="catalog">User-controlled template definitions.</param>
    private static void ValidateCatalog(ProjectJourneyTemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != ProjectJourneyTemplateCatalog.CurrentSchemaVersion ||
            catalog.Templates is null)
        {
            throw new InvalidDataException("Version de modèles de parcours non prise en charge.");
        }

        HashSet<string> templateKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectJourneyTemplate template in catalog.Templates)
        {
            if (template is null || !IsKey(template.Key) ||
                !templateKeys.Add(template.Key) ||
                string.IsNullOrWhiteSpace(template.Name) ||
                !Enum.IsDefined(template.Complexity) || template.Steps is null)
            {
                throw new InvalidDataException("Modèle de parcours invalide ou dupliqué.");
            }

            HashSet<string> stepKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<int> orders = [];
            foreach (ProjectJourneyStepTemplate step in template.Steps)
            {
                if (step is null || !IsKey(step.Key) || !stepKeys.Add(step.Key) ||
                    step.Order < 0 || !orders.Add(step.Order) ||
                    string.IsNullOrWhiteSpace(step.Name) ||
                    step.Operations is null)
                {
                    throw new InvalidDataException("Étape de modèle invalide ou dupliquée.");
                }

                HashSet<string> operationKeys = new(StringComparer.OrdinalIgnoreCase);
                foreach (ProjectJourneyOperationTemplate operation in step.Operations)
                {
                    if (operation is null || !IsKey(operation.Key) ||
                        !operationKeys.Add(operation.Key) ||
                        string.IsNullOrWhiteSpace(operation.Name) ||
                        (operation.PromptKey is not null && !IsKey(operation.PromptKey)))
                    {
                        throw new InvalidDataException(
                            "Opération de modèle invalide ou dupliquée.");
                    }
                }
            }
        }
    }

    /// <summary>Validates a saved journey without consulting mutable templates.</summary>
    /// <param name="document">Saved project journey.</param>
    /// <param name="projectId">Authoritative manifest GUID.</param>
    private static void ValidateDocument(ProjectJourneyDocument document, Guid projectId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != ProjectJourneyDocument.CurrentSchemaVersion ||
            document.ProjectId != projectId || !IsKey(document.TemplateKey) ||
            document.Steps is null)
        {
            throw new InvalidDataException("Parcours incompatible ou associé au mauvais projet.");
        }

        HashSet<Guid> identities = [];
        HashSet<int> orders = [];
        foreach (ProjectJourneyStep step in document.Steps)
        {
            if (step is null || step.Id == Guid.Empty || !identities.Add(step.Id) ||
                string.IsNullOrWhiteSpace(step.Name) || step.Order < 0 ||
                !orders.Add(step.Order) || !Enum.IsDefined(step.Status) ||
                (step.TemplateKey is not null && !IsKey(step.TemplateKey)) ||
                step.Operations is null)
            {
                throw new InvalidDataException("Étape de parcours invalide.");
            }

            foreach (ProjectJourneyOperation operation in step.Operations)
            {
                if (operation is null || operation.Id == Guid.Empty ||
                    !identities.Add(operation.Id) ||
                    string.IsNullOrWhiteSpace(operation.Name) ||
                    (operation.TemplateKey is not null &&
                     !IsKey(operation.TemplateKey)) ||
                    (operation.PromptKey is not null && !IsKey(operation.PromptKey)) ||
                    operation.SourcePaths is null || operation.GateIds is null ||
                    operation.MilestoneIds is null ||
                    operation.SourcePaths.Any(source => !IsSafeSourcePath(source)) ||
                    operation.GateIds.Any(id => id == Guid.Empty) ||
                    operation.MilestoneIds.Any(id => id == Guid.Empty))
                {
                    throw new InvalidDataException("Opération de parcours invalide.");
                }
            }
        }
    }

    /// <summary>Restricts stable template keys to portable ASCII slugs.</summary>
    /// <param name="key">Candidate key.</param>
    /// <returns>Whether the key is portable.</returns>
    private static bool IsKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= 80 &&
        key.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    /// <summary>Accepts only workspace-relative, non-traversing source references.</summary>
    /// <param name="path">Candidate source path.</param>
    /// <returns>Whether the path is safe to retain.</returns>
    private static bool IsSafeSourcePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Length <= 1024 &&
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') &&
        path.Split('/').All(part => part is not ("" or "." or "..")) &&
        !path.Any(char.IsControl);

    /// <summary>Computes a byte-level hash of the UTF-8 JSON representation.</summary>
    /// <param name="json">Serialized document.</param>
    /// <returns>Uppercase SHA-256.</returns>
    private static string ComputeHash(string json) =>
        Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json)));

    /// <summary>Rejects truncated or malformed optimistic revisions.</summary>
    /// <param name="hash">Expected prior byte hash.</param>
    private static void ValidateSha256(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Empreinte de révision invalide.");
        }
    }
}
