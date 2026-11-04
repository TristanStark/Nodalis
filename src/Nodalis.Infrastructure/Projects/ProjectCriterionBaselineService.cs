using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable, strictly opt-in observation of project-local CA fingerprints.
/// No Markdown, test, REC campaign, business status or link is ever rewritten.
/// Snapshots are appended under the project, never silently overwritten.
/// </summary>
public sealed class ProjectCriterionBaselineService
{
    private const string Folder = ".nodalis-ca-baselines";
    private const string Schema = "nodalis.ca-baseline.v1";
    private const int MaxSnapshots = 256;
    private const int MaxSnapshotBytes = 8 * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Creates a workspace-scoped service without filesystem writes.</summary>
    /// <param name="workspaceRoot">Canonical single workspace root.</param>
    public ProjectCriterionBaselineService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Reads and verifies the complete append-only observation chain,
    /// returning a conservative comparison to a caller-supplied canonical
    /// catalog. No changes are ever made during this operation.
    /// </summary>
    /// <param name="projectDirectory">Canonical selected project.</param>
    /// <param name="catalog">Fresh verified EXI source projection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Persisted baseline drift or explicit untracked state.</returns>
    public async Task<ProjectCriterionBaselineOverview> InspectAsync(
        string projectDirectory, ProjectRequirementCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        (string directory, ProjectManifest project) = await VerifyProjectAsync(
            projectDirectory, cancellationToken);
        if (project.Id != catalog.ProjectId)
        {
            throw new InvalidDataException(
                "Le catalogue EXI ne correspond pas au projet sélectionné.");
        }

        IReadOnlyList<ProjectCriterionBaselineSnapshot> snapshots =
            await ReadSnapshotsAsync(directory, project.Id, cancellationToken);
        return ProjectCriterionBaselineProjection.Compare(catalog,
            snapshots.Count == 0 ? null : snapshots[^1]);
    }

    /// <summary>
    /// Appends a project-local CA observation only upon explicit human action.
    /// A later snapshot acknowledges a new reference, NOT a test outcome.
    /// Changes since the UI preview or another writer are refused.
    /// </summary>
    /// <param name="projectDirectory">Active, non-archived project.</param>
    /// <param name="expectedCatalog">Exact EXI revisions shown to the user.</param>
    /// <param name="expectedPreviousSequence">Previous baseline sequence shown.</param>
    /// <param name="reason">Explicit human justification for an observation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Just-appended snapshot; never an automatic approval.</returns>
    public async Task<ProjectCriterionBaselineSnapshot> RecordAsync(
        string projectDirectory, ProjectRequirementCatalog expectedCatalog,
        int expectedPreviousSequence, string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedCatalog);
        if (expectedPreviousSequence is < 0 or >= MaxSnapshots ||
            string.IsNullOrWhiteSpace(reason) ||
            reason != reason.Trim() || reason.Length > 500 ||
            reason.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "Motif de référence CA invalide (1 à 500 caractères, une ligne).");
        }

        (string directory, ProjectManifest project) = await VerifyProjectAsync(
            projectDirectory, cancellationToken);
        if (project.ArchivedUtc.HasValue ||
            project.ArchiveOriginalRelativePath is not null ||
            !string.Equals(Path.GetRelativePath(_workspaceRoot, directory)
                    .Split(Path.DirectorySeparatorChar)[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            project.Id != expectedCatalog.ProjectId)
        {
            throw new InvalidOperationException(
                "Observation CA interdite sur projet archivé ou étranger.");
        }

        IReadOnlyList<ProjectCriterionBaselineEntry> expected =
            ProjectCriterionBaselineProjection.Capture(expectedCatalog);
        if (expected.Count == 0)
        {
            throw new InvalidDataException(
                "Aucun critère CA identifiable à enregistrer.");
        }

        // Re-read source Markdown, not a stale UI or derived index.
        ProjectRequirementCatalog fresh =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(directory, cancellationToken);
        IReadOnlyList<ProjectCriterionBaselineEntry> actual =
            ProjectCriterionBaselineProjection.Capture(fresh);
        if (fresh.ProjectId != project.Id ||
            !actual.SequenceEqual(expected))
        {
            throw new InvalidDataException(
                "Les fiches EXI/CA ont changé depuis l'aperçu : " +
                "actualiser la matrice avant de confirmer.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string folder = Path.Combine(directory, Folder);
        Directory.CreateDirectory(folder);
        RequireNormalDirectory(folder);

        // One project-local lock prevents racing writers from both claiming
        // the next sequence. Existing files are never rewritten.
        string lockPath = Path.Combine(folder, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou de référence CA non sûr.");
        }

        IReadOnlyList<(ProjectCriterionBaselineSnapshot Snapshot, string Sha)>
            observations = await LoadChainAsync(folder, project.Id,
                cancellationToken);
        int last = observations.Count;
        if (last != expectedPreviousSequence || last >= MaxSnapshots)
        {
            throw new InvalidDataException(
                "Une autre référence CA a été enregistrée : " +
                "actualiser et reconfirmer.");
        }

        // Recheck source content under the writer's serialization gate.
        // The original EXI sources are never locked or modified by us;
        // a third-party edit after this check can only cause a later drift.
        ProjectRequirementCatalog latest =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(directory, cancellationToken);
        if (latest.ProjectId != project.Id ||
            !ProjectCriterionBaselineProjection.Capture(latest)
                .SequenceEqual(expected))
        {
            throw new InvalidDataException(
                "Modification EXI simultanée détectée : référence non écrite.");
        }

        ProjectCriterionBaselineSnapshot recorded = new()
        {
            Schema = Schema,
            ProjectId = project.Id,
            Sequence = last + 1,
            RecordedUtc = DateTimeOffset.UtcNow,
            Reason = reason,
            PreviousSha256 = last == 0 ? null : observations[^1].Sha,
            Entries = expected
        };
        string file = Path.Combine(folder, FileName(recorded.Sequence));
        await AtomicFileWriter.WriteNewAllTextAsync(file,
            JsonSerializer.Serialize(recorded, JsonOptions), cancellationToken);
        return recorded;
    }

    /// <summary>Reads all immutable observations after verifying the project.</summary>
    /// <param name="projectDirectory">Selected project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>History in creation order, never changing REC state.</returns>
    public async Task<IReadOnlyList<ProjectCriterionBaselineSnapshot>>
        ListHistoryAsync(string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest project) = await VerifyProjectAsync(
            projectDirectory, cancellationToken);
        return await ReadSnapshotsAsync(directory, project.Id, cancellationToken);
    }

    /// <summary>Read-only chain projection for the selected project.</summary>
    /// <param name="projectDirectory">Verified project directory.</param>
    /// <param name="projectId">Manifest identity.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Immutable events from oldest to newest.</returns>
    private async Task<IReadOnlyList<ProjectCriterionBaselineSnapshot>>
        ReadSnapshotsAsync(string projectDirectory, Guid projectId,
        CancellationToken token)
    {
        string folder = Path.Combine(projectDirectory, Folder);
        return Directory.Exists(folder)
            ? (await LoadChainAsync(folder, projectId, token))
                .Select(item => item.Snapshot).ToArray()
            : [];
    }

    /// <summary>Validates sequential files and predecessor hashes.</summary>
    /// <param name="folder">Existing baseline directory.</param>
    /// <param name="projectId">Owning manifest GUID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified files and exact raw byte SHA-256 hashes.</returns>
    private async Task<IReadOnlyList<(
        ProjectCriterionBaselineSnapshot Snapshot, string Sha)>>
        LoadChainAsync(string folder, Guid projectId, CancellationToken token)
    {
        RequireNormalDirectory(folder);
        string[] files = Directory.GetFiles(folder,
            "baseline-*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (files.Length > MaxSnapshots)
        {
            throw new InvalidDataException(
                "Historique CA trop long : contrôle humain nécessaire.");
        }

        List<(ProjectCriterionBaselineSnapshot Snapshot, string Sha)> result = [];
        string? previousSha = null;
        for (int i = 0; i < files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetFileName(files[i]) != FileName(i + 1) ||
                (File.GetAttributes(files[i]) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Historique CA incomplet, renommé ou sous lien symbolique.");
            }

            FileInfo info = new(files[i]);
            if (info.Length is <= 0 or > MaxSnapshotBytes)
            {
                throw new InvalidDataException(
                    "Instantané CA vide ou trop volumineux.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(files[i], token);
            if (bytes.Length > MaxSnapshotBytes)
            {
                throw new InvalidDataException("Instantané CA hors limite.");
            }

            ProjectCriterionBaselineSnapshot item;
            try
            {
                item = JsonSerializer.Deserialize<ProjectCriterionBaselineSnapshot>(
                    new UTF8Encoding(false, true).GetString(bytes),
                    JsonOptions) ?? throw new InvalidDataException(
                    "Instantané CA illisible.");
            }
            catch (Exception exception) when (exception is JsonException or
                DecoderFallbackException)
            {
                throw new InvalidDataException(
                    "Instantané CA hors contrat JSON.", exception);
            }

            if (item.Schema != Schema || item.ProjectId != projectId ||
                item.Sequence != i + 1 ||
                item.RecordedUtc == default ||
                item.Reason is null || item.Reason.Length is < 1 or > 500 ||
                item.Entries is null || item.Entries.Count is < 1 or > 20000 ||
                !string.Equals(item.PreviousSha256, previousSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Suite des observations CA contradictoire : " +
                    "aucune référence implicite possible.");
            }

            HashSet<Guid> seen = [];
            foreach (ProjectCriterionBaselineEntry entry in item.Entries)
            {
                if (entry is null ||
                    entry.CriterionId == Guid.Empty ||
                    entry.RequirementId == Guid.Empty ||
                    !seen.Add(entry.CriterionId) ||
                    entry.RequirementReference is null ||
                    entry.CriterionReference is null ||
                    entry.Title is null || entry.RelativePath is null ||
                    !ProjectRequirementMarkdownCodec.IsReference(
                        entry.RequirementReference) ||
                    !Nodalis.Core.Requirements.ProjectRequirementCriterionCodec
                        .IsReference(entry.CriterionReference) ||
                    !ProjectJourneySourcePath.IsSafe(entry.RelativePath) ||
                    !ProjectCriterionBaselineProjection.IsSha(entry.DocumentSha256) ||
                    !ProjectCriterionBaselineProjection.IsSha(entry.TitleSha256) ||
                    entry.BodySha256 is not null &&
                    !ProjectCriterionBaselineProjection.IsSha(entry.BodySha256))
                {
                    throw new InvalidDataException(
                        "Instantané CA contient une identité non sûre.");
                }
            }

            string rawSha = Convert.ToHexString(SHA256.HashData(bytes));
            result.Add((item, rawSha));
            previousSha = rawSha;
        }

        return result;
    }

    /// <summary>Checks workspace containment and the canonical project ID.</summary>
    /// <param name="candidate">Selected project directory.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Canonical project folder and valid manifest.</returns>
    private async Task<(string Directory, ProjectManifest Project)>
        VerifyProjectAsync(string candidate, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        string directory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidate));
        string relative = Path.GetRelativePath(_workspaceRoot, directory);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Projet CA hors workspace.");
        }

        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 3 ||
            !(string.Equals(parts[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(parts[0],
                WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Emplacement projet CA non reconnu.");
        }

        string manifestPath = Path.Combine(directory,
            WorkspaceLayout.ProjectManifestFileName);
        string safe = _sources.GetReferenceForExistingFile(manifestPath);
        _sources.ResolveExistingFile(safe);
        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, token);
        if (project.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet CA sans GUID stable.");
        }

        return (directory, project);
    }

    /// <summary>Canonical zero-padded append-only file name.</summary>
    /// <param name="sequence">One-based record number.</param>
    /// <returns>Portable file name.</returns>
    private static string FileName(int sequence) =>
        "baseline-" + sequence.ToString("D6",
            System.Globalization.CultureInfo.InvariantCulture) + ".json";

    /// <summary>Rejects symlinks/junctions for the observation directory.</summary>
    /// <param name="folder">Existing directory.</param>
    private static void RequireNormalDirectory(string folder)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier de références CA sous lien ou jonction.");
        }
    }
}
