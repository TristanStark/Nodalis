using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One exact original and approved Markdown revision in an AI batch.</summary>
public sealed record ProjectAiBatchTitleFile
{
    public required Guid ProposalId { get; init; }
    public required Guid RequirementId { get; init; }
    public required string Reference { get; init; }
    public required string RelativePath { get; init; }
    public required string Slot { get; init; }
    public required string OriginalSha256 { get; init; }
    public required string PublishedSha256 { get; init; }
    public required string OriginalTitle { get; init; }
    public required string ReviewedTitle { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Durable, replayable, project-owned batch intent; no auto-approval.</summary>
public sealed record ProjectAiBatchTitleJournal
{
    public required string Schema { get; init; }
    public required Guid ProjectId { get; init; }
    public required string RawResponseSha256 { get; init; }
    public required string PlanSha256 { get; init; }
    public required List<ProjectAiBatchTitleFile> Files { get; init; }
}

/// <summary>Idempotent result of a human-approved multi-file title publication.</summary>
public enum ProjectAiBatchTitleCommitStatus
{
    Applied,
    AlreadyApplied
}

/// <summary>
/// A batch is prepared completely before the first title is published.
/// Windows does not provide one atomic rename across different files:
/// an interrupted batch is therefore explicitly REPLAYABLE, not advertised
/// as all-or-nothing. Divergent external edits block any further publication.
/// </summary>
public sealed class ProjectAiBatchTitleImportTransactionService
{
    private const int MaxFiles = 20;
    private const int MaxDocumentBytes = 1024 * 1024;
    private const string JournalFolder = ".nodalis-ai-import";
    private const string OriginalFolder = "original";
    private const string ApprovedFolder = "approved";
    private const string ManifestFile = "intent.json";
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates an isolated, workspace-bound batch executor.</summary>
    /// <param name="workspaceRoot">The current single workspace.</param>
    public ProjectAiBatchTitleImportTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>Returns the stable, non-secret plan key before a write.</summary>
    /// <param name="plan">Human-reviewed, snapshot-bound plan.</param>
    /// <returns>SHA-256 of canonical, ordered authorization data.</returns>
    public static string PlanFingerprint(ProjectAiPreparedReviewPlan plan)
    {
        ProjectAiPreparedTitleChange[] changes = ValidatePlan(plan);
        string serialized = JsonSerializer.Serialize(new
        {
            version = 1,
            raw = plan.RawResponseSha256,
            changes = changes.Select(change => new
            {
                change.ProposalId,
                change.ProjectId,
                change.RequirementId,
                change.Reference,
                change.RelativePath,
                change.ExpectedSha256,
                change.CurrentTitle,
                change.ReviewedTitle,
                change.Reason
            }).ToArray()
        });
        return Hash(Encoding.UTF8.GetBytes(serialized));
    }

    /// <summary>
    /// Prevalidates ALL current files before starting, snapshots originals
    /// and approved revisions, then writes a durable intent BEFORE modifying
    /// any EXI. Replays the exact journal after a partial interruption.
    /// </summary>
    /// <param name="projectDirectory">Existing, active project root.</param>
    /// <param name="plan">Human-approved changes (2..20 titles only).</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Whether at least one title was published.</returns>
    public Task<ProjectAiBatchTitleCommitStatus> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken cancellationToken = default) =>
        CommitWithCheckpointAsync(projectDirectory, plan, cancellationToken, null);

    /// <summary>
    /// Test-only injection point after each successful file replacement.
    /// The journal must remain recoverable even if this callback throws.
    /// </summary>
    internal async Task<ProjectAiBatchTitleCommitStatus> CommitWithCheckpointAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken cancellationToken, Action<int>? checkpoint)
    {
        ProjectAiPreparedTitleChange[] changes = ValidatePlan(plan);
        string key = PlanFingerprint(plan);
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        Guid projectId = changes[0].ProjectId;
        await VerifyProjectAsync(project, projectId, cancellationToken);

        // Shared with single-title updates and requirement creations.
        string journalRoot = Path.Combine(project, JournalFolder);
        Directory.CreateDirectory(journalRoot);
        RequireNormalDirectory(journalRoot);
        string lockFile = Path.Combine(journalRoot, ".lock");
        await using FileStream gate = new(lockFile, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockFile) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou d'import IA non sûr.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.Combine(journalRoot, "batch-" + key);
        string manifestPath = Path.Combine(directory, ManifestFile);
        ProjectAiBatchTitleJournal journal;
        if (Directory.Exists(directory))
        {
            RequireNormalDirectory(directory);
            journal = await LoadManifestAsync(manifestPath, cancellationToken);
            ValidateManifest(journal, plan, changes, key);
        }
        else
        {
            // No snapshots can be prepared from a stale or ambiguous catalog.
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_workspaceRoot)
                    .ListAsync(project, cancellationToken);
            if (catalog.ProjectId != projectId)
            {
                throw new InvalidDataException(
                    "Projet propriétaire modifié depuis la qualification IA.");
            }

            Dictionary<Guid, ProjectRequirementEntry> byId =
                catalog.Requirements.ToDictionary(item => item.Card.Id);
            List<(string Path, byte[] Original, string Published)> stages = [];
            List<ProjectAiBatchTitleFile> files = [];
            for (int index = 0; index < changes.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProjectAiPreparedTitleChange change = changes[index];
                if (!byId.TryGetValue(change.RequirementId,
                        out ProjectRequirementEntry? entry) ||
                    entry.Card.ProjectId != projectId ||
                    entry.Card.Reference != change.Reference ||
                    entry.Card.Title != change.CurrentTitle ||
                    entry.RelativePath != change.RelativePath ||
                    !string.Equals(entry.SourceSha256,
                        change.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Exigence modifiée, absente ou ambiguë : " +
                        change.Reference + " ; réexaminer tout le lot.");
                }

                string target = ResolveCanonical(project, change.RelativePath,
                    change.Reference);
                byte[] original = await ReadBoundedAsync(target, cancellationToken);
                string hash = Hash(original);
                if (!string.Equals(hash, change.ExpectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Fichier modifié depuis le plan : " + change.Reference);
                }

                string text = new UTF8Encoding(false, true).GetString(original);
                ProjectRequirementCard card =
                    ProjectRequirementMarkdownCodec.Parse(text);
                if (card.Id != change.RequirementId ||
                    card.ProjectId != projectId ||
                    card.Reference != change.Reference ||
                    card.Title != change.CurrentTitle)
                {
                    throw new InvalidDataException(
                        "Métadonnées EXI modifiées : " + change.Reference);
                }

                string modified = ProjectAiTitleImportTransactionService
                    .RewriteTitle(text, card, change.ReviewedTitle);
                string slot = index.ToString("D4") + ".md";
                stages.Add((target, original, modified));
                files.Add(new ProjectAiBatchTitleFile
                {
                    ProposalId = change.ProposalId,
                    RequirementId = change.RequirementId,
                    Reference = change.Reference,
                    RelativePath = change.RelativePath,
                    Slot = slot,
                    OriginalSha256 = hash,
                    PublishedSha256 = Hash(new UTF8Encoding(false).GetBytes(modified)),
                    OriginalTitle = change.CurrentTitle,
                    ReviewedTitle = change.ReviewedTitle,
                    Reason = change.Reason
                });
            }

            // All source hashes were checked before creating the journal.
            Directory.CreateDirectory(directory);
            RequireNormalDirectory(directory);
            string originalRoot = Path.Combine(directory, OriginalFolder);
            string approvedRoot = Path.Combine(directory, ApprovedFolder);
            Directory.CreateDirectory(originalRoot);
            Directory.CreateDirectory(approvedRoot);
            RequireNormalDirectory(originalRoot);
            RequireNormalDirectory(approvedRoot);
            for (int index = 0; index < stages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string target, byte[] original, string approved) = stages[index];
                string slot = files[index].Slot;
                // File.Copy retains exact bytes including any BOM.
                File.Copy(target, Path.Combine(originalRoot, slot),
                    overwrite: false);
                if (!string.Equals(Hash(await ReadBoundedAsync(
                        Path.Combine(originalRoot, slot), cancellationToken)),
                        files[index].OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Source modifiée pendant la préparation : " +
                        files[index].Reference);
                }

                await AtomicFileWriter.WriteNewAllTextAsync(
                    Path.Combine(approvedRoot, slot), approved,
                    cancellationToken);
            }

            journal = new ProjectAiBatchTitleJournal
            {
                Schema = "nodalis.ai-title-batch.v1",
                ProjectId = projectId,
                RawResponseSha256 = plan.RawResponseSha256,
                PlanSha256 = key,
                Files = files
            };
            // No live Markdown is published without this complete intent.
            await AtomicFileWriter.WriteNewAllTextAsync(manifestPath,
                JsonSerializer.Serialize(journal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
        }

        // Re-check all copies AND all live files before touching ANY file.
        List<(ProjectAiBatchTitleFile Entry, string Live, string Staged,
            bool Pending)> pending = [];
        string oldRoot = Path.Combine(directory, OriginalFolder);
        string newRoot = Path.Combine(directory, ApprovedFolder);
        RequireNormalDirectory(oldRoot);
        RequireNormalDirectory(newRoot);
        foreach (ProjectAiBatchTitleFile file in journal.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string originalSnapshot = Path.Combine(oldRoot, file.Slot);
            string approvedSnapshot = Path.Combine(newRoot, file.Slot);
            await AssertSnapshotAsync(originalSnapshot,
                file.OriginalSha256, cancellationToken);
            await AssertSnapshotAsync(approvedSnapshot,
                file.PublishedSha256, cancellationToken);
            string live = ResolveCanonical(project, file.RelativePath,
                file.Reference);
            string current = Hash(await ReadBoundedAsync(live, cancellationToken));
            bool isOriginal = string.Equals(current,
                file.OriginalSha256, StringComparison.OrdinalIgnoreCase);
            bool isPublished = string.Equals(current,
                file.PublishedSha256, StringComparison.OrdinalIgnoreCase);
            if (!isOriginal && !isPublished)
            {
                throw new InvalidDataException(
                    "Import groupé partiel bloqué : " + file.Reference +
                    " diverge des deux instantanés du journal. " +
                    "Aucun fichier supplémentaire publié.");
            }

            pending.Add((file, live, approvedSnapshot, !isPublished));
        }

        if (pending.All(item => !item.Pending))
        {
            return ProjectAiBatchTitleCommitStatus.AlreadyApplied;
        }

        int completed = 0;
        foreach ((ProjectAiBatchTitleFile file, string live,
                     string approvedSnapshot, bool needsPublishing) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!needsPublishing)
            {
                continue;
            }

            // Re-check under a Windows read/share-delete lock, including the
            // NTFS file identity, just before each conditional File.Replace.
            await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                live, file.OriginalSha256, approvedSnapshot,
                file.PublishedSha256, cancellationToken);
            string current = Hash(await ReadBoundedAsync(live, CancellationToken.None));
            if (!string.Equals(current, file.PublishedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Publication de lot non conforme : journal conservé.");
            }

            completed++;
            checkpoint?.Invoke(completed);
        }

        return ProjectAiBatchTitleCommitStatus.Applied;
    }

    /// <summary>Rejects unscoped, duplicated or unreviewed proposed writes.</summary>
    private static ProjectAiPreparedTitleChange[] ValidatePlan(
        ProjectAiPreparedReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Creations.Count != 0 ||
            plan.Changes.Count is < 2 or > MaxFiles ||
            plan.RawResponseSha256 is not { Length: 64 } ||
            !plan.RawResponseSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "L'import groupé n'autorise que 2 à 20 titres EXI existants.");
        }

        Guid projectId = plan.Changes[0].ProjectId;
        HashSet<Guid> ids = [];
        HashSet<Guid> proposals = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectAiPreparedTitleChange change in plan.Changes)
        {
            if (change.ProjectId == Guid.Empty || change.ProjectId != projectId ||
                change.RequirementId == Guid.Empty || change.ProposalId == Guid.Empty ||
                !ids.Add(change.RequirementId) || !proposals.Add(change.ProposalId) ||
                !paths.Add(change.RelativePath) ||
                !references.Add(change.Reference) ||
                !ProjectRequirementMarkdownCodec.IsReference(change.Reference) ||
                change.ExpectedSha256 is not { Length: 64 } ||
                !change.ExpectedSha256.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(change.CurrentTitle) ||
                string.IsNullOrWhiteSpace(change.ReviewedTitle) ||
                change.ReviewedTitle.Length > 240 ||
                change.ReviewedTitle != change.ReviewedTitle.Trim() ||
                change.ReviewedTitle.Any(char.IsControl) ||
                change.ReviewedTitle == change.CurrentTitle ||
                change.Reason is { Length: > 1200 })
            {
                throw new InvalidDataException(
                    "Lot IA ambigu ou contenant une modification non qualifiée.");
            }
        }

        return plan.Changes.OrderBy(item => item.Reference,
            StringComparer.Ordinal).ToArray();
    }

    /// <summary>Validates active project ownership and manifest path safety.</summary>
    private async Task VerifyProjectAsync(
        string project, Guid projectId, CancellationToken cancellationToken)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] sections = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || sections.Length < 4 ||
            !string.Equals(sections[0], WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !sections.Skip(2).Any(section => string.Equals(section,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Lot IA hors d'un projet actif du workspace.");
        }

        string manifestPath = Path.Combine(
            project, WorkspaceLayout.ProjectManifestFileName);
        string safe = _sources.GetReferenceForExistingFile(manifestPath);
        _sources.ResolveExistingFile(safe);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != projectId || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Lot IA interdit pour ce projet archivé ou déplacé.");
        }
    }

    /// <summary>Resolves only canonical EXI files within the chosen project.</summary>
    private string ResolveCanonical(string project, string relative, string reference)
    {
        string path = _sources.ResolveExistingFile(relative);
        string expected = Path.GetFullPath(Path.Combine(project,
            "Documentation", "Exigences", reference + ".md"));
        if (!string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Chemin EXI non canonique dans le lot.");
        }

        return path;
    }

    /// <summary>Matches a retained journal against the complete approved plan.</summary>
    private static void ValidateManifest(ProjectAiBatchTitleJournal journal,
        ProjectAiPreparedReviewPlan plan,
        ProjectAiPreparedTitleChange[] changes, string planHash)
    {
        if (journal.Schema != "nodalis.ai-title-batch.v1" ||
            journal.ProjectId != changes[0].ProjectId ||
            journal.RawResponseSha256 != plan.RawResponseSha256 ||
            journal.PlanSha256 != planHash ||
            journal.Files is null || journal.Files.Count != changes.Length)
        {
            throw new InvalidDataException(
                "Journal de lot différent du plan humain confirmé.");
        }

        HashSet<string> slots = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < changes.Length; index++)
        {
            ProjectAiPreparedTitleChange change = changes[index];
            ProjectAiBatchTitleFile file = journal.Files[index];
            if (file is null ||
                file.ProposalId != change.ProposalId ||
                file.RequirementId != change.RequirementId ||
                file.Reference != change.Reference ||
                file.RelativePath != change.RelativePath ||
                !string.Equals(file.OriginalSha256, change.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                file.OriginalTitle != change.CurrentTitle ||
                file.ReviewedTitle != change.ReviewedTitle ||
                file.Reason != change.Reason ||
                file.Slot != index.ToString("D4") + ".md" ||
                !slots.Add(file.Slot) ||
                file.PublishedSha256 is not { Length: 64 } ||
                !file.PublishedSha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    "Inventaire journalisé incompatible ou altéré.");
            }
        }
    }

    /// <summary>Reads a bounded, non-link journal; malformed JSON fails closed.</summary>
    private static async Task<ProjectAiBatchTitleJournal> LoadManifestAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 16384)
        {
            throw new InvalidDataException(
                "Journal de lot incomplet : récupération manuelle requise.");
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<ProjectAiBatchTitleJournal>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidDataException("Journal de lot vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Journal de lot malformé : écriture refusée.", exception);
        }
    }

    /// <summary>Ensures a snapshot is not a link and its bytes match the intent.</summary>
    private static async Task AssertSnapshotAsync(string path, string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(Hash(await ReadBoundedAsync(path, cancellationToken)),
                expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Sauvegarde du lot altérée : aucune publication autorisée.");
        }
    }

    /// <summary>Checks every existing transaction directory for junctions.</summary>
    private static void RequireNormalDirectory(string directory)
    {
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier de lot inexistant ou via lien symbolique.");
        }
    }

    /// <summary>Reads bounded byte-level snapshot or EXI source.</summary>
    private static async Task<byte[]> ReadBoundedAsync(
        string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxDocumentBytes)
        {
            throw new InvalidDataException("Fiche EXI trop volumineuse.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxDocumentBytes)
        {
            throw new InvalidDataException("Fiche EXI modifiée durant la lecture.");
        }

        return bytes;
    }

    /// <summary>Computes an uppercase byte-accurate SHA-256.</summary>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
