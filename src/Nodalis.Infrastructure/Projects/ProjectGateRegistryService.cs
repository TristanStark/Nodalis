using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Project-local, explicit gate registry with optimistic byte revisions and
/// immutable append-only decision history. Reads are side-effect-free.
/// </summary>
public sealed class ProjectGateRegistryService
{
    public const string RegistryFileName = ".project-gates.json";

    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectJourneyMilestoneCatalogService _milestones;

    /// <summary>Creates a workspace-scoped gate registry service.</summary>
    /// <param name="workspaceRoot">Current workspace directory.</param>
    public ProjectGateRegistryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _milestones = new ProjectJourneyMilestoneCatalogService(_workspaceRoot);
    }

    /// <summary>
    /// Returns the exact snapshot of the existing registry, or an empty
    /// in-memory proposal if no gate file was created. No files are written.
    /// </summary>
    /// <param name="projectDirectory">Existing project location.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registry snapshot and optional byte-level SHA.</returns>
    public async Task<ProjectGateRegistrySnapshot> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest manifest) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        string path = Path.Combine(directory, RegistryFileName);
        if (!File.Exists(path))
        {
            return new ProjectGateRegistrySnapshot(
                new ProjectGateRegistry { ProjectId = manifest.Id }, null);
        }

        _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(path));
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        ProjectGateRegistry registry =
            JsonSerializer.Deserialize<ProjectGateRegistry>(
                bytes, JsonDefaults.Options) ??
            throw new InvalidDataException("Registre des gates vide.");
        ProjectGateValidation.Validate(registry);
        if (registry.ProjectId != manifest.Id)
        {
            throw new InvalidDataException(
                "Registre des gates associé à un autre projet.");
        }

        return new ProjectGateRegistrySnapshot(registry,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Saves the complete user-confirmed registry. An existing decision
    /// cannot be removed, edited or reordered even if the caller supplies a
    /// changed copy. The only new decisions must be appended at the end.
    /// </summary>
    /// <param name="projectDirectory">Active project folder.</param>
    /// <param name="registry">Reviewed registry state.</param>
    /// <param name="expectedSha256">Exact revision previously read, or null for first save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New persisted revision.</returns>
    public async Task<ProjectGateRegistrySnapshot> SaveAsync(
        string projectDirectory, ProjectGateRegistry registry,
        string? expectedSha256, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest manifest) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Un projet archivé est consultable mais ses gates sont immuables.");
        }

        ProjectGateValidation.Validate(registry);
        if (registry.ProjectId != manifest.Id)
        {
            throw new InvalidDataException(
                "Registre des gates associé au mauvais projet.");
        }

        string path = Path.Combine(directory, RegistryFileName);
        ProjectGateRegistrySnapshot old = await ReadAsync(
            directory, cancellationToken);
        if (!string.Equals(old.Sha256, expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le registre des gates a changé : relecture explicite requise.");
        }

        for (int index = 0; index < old.Registry.Decisions.Count; index++)
        {
            if (index >= registry.Decisions.Count ||
                !string.Equals(
                    JsonSerializer.Serialize(old.Registry.Decisions[index],
                        JsonDefaults.Options),
                    JsonSerializer.Serialize(registry.Decisions[index],
                        JsonDefaults.Options),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Une décision passée ne peut être supprimée ou réécrite.");
            }
        }

        // Reservation actions are append-only just like the verdicts. A
        // caller submitting a rewritten historical event is never trusted.
        for (int index = 0; index < old.Registry.ReservationEvents.Count; index++)
        {
            if (index >= registry.ReservationEvents.Count ||
                !string.Equals(
                    JsonSerializer.Serialize(old.Registry.ReservationEvents[index],
                        JsonDefaults.Options),
                    JsonSerializer.Serialize(registry.ReservationEvents[index],
                        JsonDefaults.Options),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Un événement historique de réserve ne peut être effacé " +
                    "ou réécrit.");
            }
        }

        // A newly linked ID must be a real, project-local, unambiguous
        // persisted Markdown GUID. Verify once per write, not once per row.
        // Existing references may become stale: allow unrelated edits and
        // explicit unlink without rewriting or migrating source milestones.
        HashSet<Guid> newReferences = [];
        foreach (ProjectGateDefinition gate in registry.Gates)
        {
            ProjectGateDefinition? previous = old.Registry.Gates.FirstOrDefault(
                item => item.Id == gate.Id);
            foreach (Guid id in gate.MilestoneIds)
            {
                if (previous is null || !previous.MilestoneIds.Contains(id))
                {
                    newReferences.Add(id);
                }
            }
        }

        if (newReferences.Count > 0)
        {
            ProjectJourneyMilestoneCatalog catalog =
                await _milestones.ListAsync(directory, cancellationToken);
            HashSet<Guid> eligible = catalog.Candidates.Select(item => item.Id)
                .ToHashSet();
            if (newReferences.Any(id => !eligible.Contains(id)))
            {
                throw new InvalidDataException(
                    "Lien refusé : jalon absent, sans GUID enregistré, " +
                    "dupliqué ou extérieur au projet. Actualisez les jalons.");
            }
        }

        string json = JsonSerializer.Serialize(registry, JsonDefaults.Options);
        string sha = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(json)));
        if (expectedSha256 is null)
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                path, json, cancellationToken);
        }
        else
        {
            if (expectedSha256.Length != 64 ||
                !expectedSha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("Révision de gate invalide.");
            }

            string stagedPath = Path.Combine(directory,
                "." + RegistryFileName + "." + Guid.NewGuid().ToString("N") +
                ".stage");
            try
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    stagedPath, json, cancellationToken);
                await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                    path, expectedSha256, stagedPath, sha, cancellationToken);
            }
            finally
            {
                if (File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }
            }
        }

        return new ProjectGateRegistrySnapshot(registry, sha);
    }

    /// <summary>
    /// Captures the exact SHA-256 of one explicitly selected existing proof
    /// using a bounded file size. Stored hashes are frozen on the decision.
    /// </summary>
    /// <param name="relativePath">Workspace-relative evidence.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Proof snapshot without importing or modifying the source.</returns>
    public async Task<ProjectGateEvidenceSnapshot> CaptureEvidenceAsync(
        string relativePath, CancellationToken cancellationToken = default)
    {
        string fullPath = _sources.ResolveExistingFile(relativePath);
        FileInfo info = new(fullPath);
        if (info.Length > 25 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Preuve trop volumineuse (>25 Mio) : sélectionnez un document " +
                "de synthèse validé plutôt qu'un fichier brut.");
        }

        await using FileStream stream = new(fullPath, FileMode.Open,
            FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
        string sha = Convert.ToHexString(await SHA256.HashDataAsync(
            stream, cancellationToken));
        return new ProjectGateEvidenceSnapshot
        {
            RelativePath = relativePath,
            Sha256 = sha
        };
    }

    /// <summary>Resolves a project folder without following junctions.</summary>
    /// <param name="directory">Candidate project path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validated project location and manifest.</returns>
    private async Task<(string Directory, ProjectManifest Manifest)> ResolveProjectAsync(
        string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        if (Path.IsPathRooted(relative) ||
            relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Projet hors workspace.");
        }

        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (segments.Length < 3 ||
            !(string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0],
                WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Projet Nodalis invalide.");
        }

        string manifestPath = Path.Combine(full,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans GUID stable.");
        }

        return (full, manifest);
    }
}
