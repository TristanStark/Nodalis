using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Explicit, optimistic N:N activity/functionality relationships in one JSON
/// document. Link creation and deletion are human-controlled and never produce
/// duplicate ACT Markdown or infer a dependency from path names.
/// </summary>
public sealed class SfdActivityFunctionalityService
{
    public const string FileName = ".sfd-activity-functionality.json";

    private readonly ProjectJourneySourceService _sources;
    private readonly SfdDocumentRegistryService _documents;

    /// <summary>Binds link operations to one workspace security boundary.</summary>
    /// <param name="workspaceRoot">Current workspace root.</param>
    public SfdActivityFunctionalityService(string workspaceRoot)
    {
        _sources = new ProjectJourneySourceService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
    }

    /// <summary>Reads all existing associations, or a clean in-memory default.</summary>
    /// <param name="projectDirectory">SFD project directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Unmodified registry and exact file revision if it exists.</returns>
    public async Task<SfdActivityFunctionalitySnapshot> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(projectDirectory,
            cancellationToken);
        string root = Path.GetFullPath(projectDirectory);
        string path = Path.Combine(root, FileName);
        if (!File.Exists(path))
        {
            return new SfdActivityFunctionalitySnapshot(new SfdActivityFunctionalityRegistry
            {
                ProjectId = catalog.ProjectId
            }, null);
        }

        _sources.GetReferenceForExistingFile(path);
        FileInfo info = new(path);
        if (info.Length > 1024 * 1024)
        {
            throw new InvalidDataException("Registre de liens SFD trop volumineux.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        SfdActivityFunctionalityRegistry registry =
            JsonSerializer.Deserialize<SfdActivityFunctionalityRegistry>(bytes,
                JsonDefaults.Options) ??
            throw new InvalidDataException("Registre de liens SFD vide.");
        Validate(registry, catalog.ProjectId);
        return new SfdActivityFunctionalitySnapshot(registry,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Persists an explicitly reviewed complete association set using
    /// optimistic revision protection; creates on first write without overwrite.
    /// </summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="registry">Human-confirmed, complete relationship set.</param>
    /// <param name="expectedSha256">Exact read revision, null on first creation.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New exact persisted snapshot.</returns>
    public async Task<SfdActivityFunctionalitySnapshot> SaveAsync(
        string projectDirectory, SfdActivityFunctionalityRegistry registry,
        string? expectedSha256, CancellationToken cancellationToken = default)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(projectDirectory,
            cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Corrigez les fiches SFD ambiguës avant de modifier les relations.");
        }

        string root = Path.GetFullPath(projectDirectory);
        string manifestPath = Path.Combine(root, WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != catalog.ProjectId ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("Projet SFD archivé ou identité modifiée.");
        }

        Validate(registry, catalog.ProjectId);
        HashSet<Guid> activities = catalog.Entries
            .Where(item => item.Card.Kind == SfdDocumentKind.Activity)
            .Select(item => item.Card.Id).ToHashSet();
        HashSet<Guid> functions = catalog.Entries
            .Where(item => item.Card.Kind == SfdDocumentKind.Functionality)
            .Select(item => item.Card.Id).ToHashSet();
        if (registry.Links.Any(link =>
            !activities.Contains(link.ActivityId) ||
            !functions.Contains(link.FunctionalityId)))
        {
            throw new InvalidDataException(
                "Une relation ACT/FCT pointe vers un objet absent ou d'un mauvais type.");
        }

        SfdActivityFunctionalitySnapshot previous = await ReadAsync(
            root, cancellationToken);
        if (!string.Equals(previous.Sha256, expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Les relations SFD ont changé depuis la lecture : relecture requise.");
        }

        string json = JsonSerializer.Serialize(registry, JsonDefaults.Options);
        string newHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        string path = Path.Combine(root, FileName);
        if (expectedSha256 is null)
        {
            await AtomicFileWriter.WriteNewAllTextAsync(path, json, cancellationToken);
        }
        else
        {
            string snapshot = Path.Combine(root,
                "." + FileName + "." + Guid.NewGuid().ToString("N") + ".stage");
            try
            {
                await AtomicFileWriter.WriteNewAllTextAsync(snapshot, json,
                    cancellationToken);
                _sources.GetReferenceForExistingFile(path);
                await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(path,
                    expectedSha256, snapshot, newHash, cancellationToken);
            }
            finally
            {
                if (File.Exists(snapshot))
                {
                    File.Delete(snapshot);
                }
            }
        }

        return new SfdActivityFunctionalitySnapshot(registry, newHash);
    }

    /// <summary>Adds one user-selected ACT↔FCT relationship, idempotently.</summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="activityId">Existing ACT GUID.</param>
    /// <param name="functionalityId">Existing FCT GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current committed associations.</returns>
    public async Task<SfdActivityFunctionalitySnapshot> LinkAsync(
        string projectDirectory, Guid activityId, Guid functionalityId,
        CancellationToken cancellationToken = default)
    {
        SfdActivityFunctionalitySnapshot current = await ReadAsync(
            projectDirectory, cancellationToken);
        if (current.Registry.Links.Any(link =>
            link.ActivityId == activityId && link.FunctionalityId == functionalityId))
        {
            return current;
        }

        SfdActivityFunctionalityRegistry updated = current.Registry with
        {
            Links = current.Registry.Links.Concat(
                new[] { new SfdActivityFunctionalityLink
                {
                    ActivityId = activityId,
                    FunctionalityId = functionalityId
                } }).ToList()
        };
        return await SaveAsync(projectDirectory, updated, current.Sha256,
            cancellationToken);
    }

    /// <summary>Explicitly removes an existing ACT↔FCT association.</summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="activityId">Existing ACT GUID.</param>
    /// <param name="functionalityId">Existing FCT GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current committed associations.</returns>
    public async Task<SfdActivityFunctionalitySnapshot> UnlinkAsync(
        string projectDirectory, Guid activityId, Guid functionalityId,
        CancellationToken cancellationToken = default)
    {
        SfdActivityFunctionalitySnapshot current = await ReadAsync(
            projectDirectory, cancellationToken);
        if (!current.Registry.Links.Any(link =>
            link.ActivityId == activityId && link.FunctionalityId == functionalityId))
        {
            return current;
        }

        SfdActivityFunctionalityRegistry updated = current.Registry with
        {
            Links = current.Registry.Links.Where(link =>
                link.ActivityId != activityId || link.FunctionalityId != functionalityId)
                .ToList()
        };
        return await SaveAsync(projectDirectory, updated, current.Sha256,
            cancellationToken);
    }

    /// <summary>Verifies unique, non-empty, correctly owned and versioned links.</summary>
    /// <param name="registry">Candidate relation registry.</param>
    /// <param name="projectId">Canonical owner.</param>
    private static void Validate(SfdActivityFunctionalityRegistry registry,
        Guid projectId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.SchemaVersion !=
                SfdActivityFunctionalityRegistry.CurrentSchemaVersion ||
            registry.ProjectId != projectId ||
            registry.Links is null || registry.Links.Count > 10000 ||
            registry.Links.Any(link => link is null ||
                link.ActivityId == Guid.Empty || link.FunctionalityId == Guid.Empty) ||
            registry.Links.GroupBy(link =>
                (link.ActivityId, link.FunctionalityId))
                .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("Registre de liens ACT/FCT invalide.");
        }
    }
}
