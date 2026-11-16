using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Project-local SFD journey reusing Nodalis v2 stage/operation editing models,
/// but with an independent, opt-in persistence file and no v2 template changes.
/// </summary>
public sealed class SfdJourneyService
{
    public const string JourneyFileName = ".sfd-journey.json";
    private readonly SfdDocumentRegistryService _documents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Binds the SFD journey to a single active workspace boundary.</summary>
    /// <param name="workspaceRoot">Nodalis workspace root.</param>
    public SfdJourneyService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Reads only the authoritative project-kind discriminator for safe
    /// routing of the shared Projet/Worktree journey commands.
    /// </summary>
    /// <param name="projectDirectory">Exact current project folder.</param>
    /// <param name="expectedProjectId">Worktree project ID to verify.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted project kind, without creating files.</returns>
    public async Task<ProjectKind> ReadProjectKindAsync(
        string projectDirectory, Guid expectedProjectId,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(Path.GetFullPath(projectDirectory),
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(path);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, cancellationToken);
        if (manifest.Id == Guid.Empty || manifest.Id != expectedProjectId ||
            !Enum.IsDefined(manifest.Kind))
        {
            throw new InvalidDataException(
                "Identité ou famille de projet invalide : parcours non ouvert.");
        }

        return manifest.Kind;
    }

    /// <summary>Previews the SFD default without writing any project files.</summary>
    /// <param name="projectDirectory">Existing SFD project.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Fresh five-stage proposal, not a persisted journey.</returns>
    public async Task<ProjectJourneyDocument> PreviewAsync(string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        return SfdJourneyDefaults.Create(catalog.ProjectId);
    }

    /// <summary>Reads a saved journey, returning null for a fresh SFD project.</summary>
    /// <param name="projectDirectory">Project SFD root.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Revisioned, user-modifiable journey or null.</returns>
    public async Task<ProjectJourneySnapshot?> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        string path = Path.Combine(Path.GetFullPath(projectDirectory),
            JourneyFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        _sources.GetReferenceForExistingFile(path);
        SfdTextRevision snapshot = await SfdMarkdownRevisionWriter.ReadAsync(
            path, cancellationToken);
        ProjectJourneyDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ProjectJourneyDocument>(
                snapshot.Content, JsonDefaults.Options) ??
                throw new InvalidDataException("Parcours SFD vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Parcours SFD JSON invalide.", exception);
        }

        Validate(document, catalog.ProjectId);
        return new ProjectJourneySnapshot(document, snapshot.Sha256);
    }

    /// <summary>Initializes once only after an explicit human decision.</summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted first revision with stable step and operation GUIDs.</returns>
    public async Task<ProjectJourneySnapshot> InitializeAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ProjectManifest manifest = await RequireActiveAsync(projectDirectory,
            cancellationToken);
        ProjectJourneyDocument document = SfdJourneyDefaults.Create(manifest.Id);
        Validate(document, manifest.Id);
        string json = JsonSerializer.Serialize(document, JsonDefaults.Options);
        string path = Path.Combine(Path.GetFullPath(projectDirectory),
            JourneyFileName);
        await AtomicFileWriter.WriteNewAllTextAsync(path, json, cancellationToken);
        string sha = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(json)));
        return new ProjectJourneySnapshot(document, sha);
    }

    /// <summary>
    /// Saves a human-reviewed edited journey only if its original SHA-256
    /// still matches. Never changes canonical PM, ACT, FCT, RM or v2 journeys.
    /// </summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="document">Complete, explicitly edited journey snapshot.</param>
    /// <param name="expectedSha256">Previously read exact byte revision.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New committed snapshot.</returns>
    public async Task<ProjectJourneySnapshot> SaveAsync(
        string projectDirectory, ProjectJourneyDocument document,
        string expectedSha256, CancellationToken cancellationToken = default)
    {
        ProjectManifest manifest = await RequireActiveAsync(projectDirectory,
            cancellationToken);
        Validate(document, manifest.Id);
        string path = Path.Combine(Path.GetFullPath(projectDirectory),
            JourneyFileName);
        _sources.GetReferenceForExistingFile(path);
        string json = JsonSerializer.Serialize(document, JsonDefaults.Options);
        string sha = await SfdMarkdownRevisionWriter.ReplaceAsync(path,
            json, expectedSha256, cancellationToken);
        return new ProjectJourneySnapshot(document, sha);
    }

    /// <summary>Checks manifest identity, SFD kind and archive immutability.</summary>
    /// <param name="projectDirectory">Candidate project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified active project.</returns>
    private async Task<ProjectManifest> RequireActiveAsync(
        string projectDirectory, CancellationToken cancellationToken)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Le projet SFD présente des identités documentaires ambiguës.");
        }

        string manifestPath = Path.Combine(Path.GetFullPath(projectDirectory),
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != catalog.ProjectId ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("Projet SFD archivé ou identité modifiée.");
        }

        return manifest;
    }

    /// <summary>Validates portable existing v2 journey shape within SFD scope.</summary>
    /// <param name="document">Candidate user-customized journey.</param>
    /// <param name="projectId">Canonical SFD owner ID.</param>
    private static void Validate(ProjectJourneyDocument document, Guid projectId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != ProjectJourneyDocument.CurrentSchemaVersion ||
            document.ProjectId != projectId ||
            document.TemplateKey != SfdJourneyDefaults.TemplateKey ||
            document.Steps is null || document.Steps.Count > 50)
        {
            throw new InvalidDataException("Parcours SFD incompatible.");
        }

        HashSet<Guid> ids = [];
        HashSet<int> orders = [];
        foreach (ProjectJourneyStep step in document.Steps)
        {
            if (step is null || step.Id == Guid.Empty || !ids.Add(step.Id) ||
                step.Order < 0 || !orders.Add(step.Order) ||
                string.IsNullOrWhiteSpace(step.Name) ||
                step.Name.Length > 200 || step.Name.Any(char.IsControl) ||
                !Enum.IsDefined(step.Status) || step.Operations is null ||
                step.Operations.Count > 100)
            {
                throw new InvalidDataException("Étape SFD invalide ou dupliquée.");
            }

            foreach (ProjectJourneyOperation operation in step.Operations)
            {
                if (operation is null || operation.Id == Guid.Empty ||
                    !ids.Add(operation.Id) ||
                    string.IsNullOrWhiteSpace(operation.Name) ||
                    operation.Name.Length > 200 || operation.Name.Any(char.IsControl) ||
                    (operation.PromptKey is string key && (key.Length is < 1 or > 80 ||
                        !key.All(character => char.IsAsciiLetterOrDigit(character) ||
                            character is '-' or '_'))) ||
                    operation.SourcePaths is null ||
                    operation.SourcePaths.Count > 500 ||
                    operation.SourcePaths.Any(path => !ProjectJourneySourcePath.IsSafe(path)) ||
                    operation.GateIds is null || operation.MilestoneIds is null ||
                    operation.GateIds.Any(id => id == Guid.Empty) ||
                    operation.MilestoneIds.Any(id => id == Guid.Empty))
                {
                    throw new InvalidDataException("Opération SFD invalide.");
                }
            }
        }
    }
}
