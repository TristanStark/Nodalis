using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One reviewed new or updated EXI within a durable mixed transaction.</summary>
public sealed record ProjectAiMixedEntry
{
    /// <summary>Human-approved operation, either "update" or "create".</summary>
    public required string Operation { get; init; }
    /// <summary>External idempotency key; never a Nodalis business identity.</summary>
    public required Guid ProposalId { get; init; }
    /// <summary>Actual Nodalis-owned requirement identity.</summary>
    public required Guid RequirementId { get; init; }
    /// <summary>Canonical EXI reference, attributed by Nodalis for new cards.</summary>
    public required string Reference { get; init; }
    /// <summary>Canonical workspace-relative EXI source/target.</summary>
    public required string RelativePath { get; init; }
    /// <summary>Internal deterministic immutable snapshot filename.</summary>
    public required string Slot { get; init; }
    /// <summary>Pre-review source SHA, null for new cards.</summary>
    public string? OriginalSha256 { get; init; }
    /// <summary>SHA-256 of the complete, approved Markdown.</summary>
    public required string PublishedSha256 { get; init; }
    /// <summary>Original local title, null for a new card.</summary>
    public string? OriginalTitle { get; init; }
    /// <summary>Reviewed final title.</summary>
    public required string ReviewedTitle { get; init; }
    /// <summary>Human justification, if supplied.</summary>
    public string? Reason { get; init; }
}

/// <summary>Persisted, bounded mixed EXI intent with no raw AI output.</summary>
public sealed record ProjectAiMixedJournal
{
    public required string Schema { get; init; }
    public required Guid ProjectId { get; init; }
    public required string RawResponseSha256 { get; init; }
    public required string PlanSha256 { get; init; }
    public required List<ProjectAiMixedEntry> Entries { get; init; }
}

/// <summary>Result of one human-initiated mixed EXI publication.</summary>
public enum ProjectAiMixedCommitStatus
{
    Applied,
    AlreadyApplied
}

/// <summary>
/// Read-only, independently verifiable recovery summary from a local journal.
/// The user must explicitly approve replay; it is never scheduled or automatic.
/// </summary>
/// <param name="PlanSha256">Retained journal key.</param>
/// <param name="ProjectId">Canonically verified project owner.</param>
/// <param name="Entries">All original reviewed actions.</param>
/// <param name="Published">Targets already at approved bytes.</param>
/// <param name="Pending">Targets needing publication.</param>
public sealed record ProjectAiMixedRecoveryPreview(
    string PlanSha256, Guid ProjectId,
    IReadOnlyList<ProjectAiMixedEntry> Entries, int Published, int Pending);

/// <summary>
/// Human-controlled, crash-recoverable import of 1..20 existing EXI title
/// changes AND 1..20 newly created draft EXIs (at most 20 total). Every file
/// is prevalidated before staging; snapshots precede all business writes.
/// Each file is atomic, but a multi-file batch is explicitly NOT globally atomic.
/// </summary>
public sealed partial class ProjectAiMixedImportTransactionService
{
    private const int MaxEntries = 20;
    private const int MaxBytes = 1024 * 1024;
    private const string RootName = ".nodalis-ai-import";
    private const string ManifestName = "intent.json";
    private const string OriginalFolder = "original";
    private const string ApprovedFolder = "approved";
    private const string ReceiptFolder = "receipts";
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Initializes one workspace-constrained executor without writing.</summary>
    /// <param name="workspaceRoot">Current single workspace root.</param>
    public ProjectAiMixedImportTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Fingerprint of the complete human-reviewed plan, independent of row order.
    /// No Nodalis-assigned identity or dynamic timestamp enters this fingerprint.
    /// </summary>
    /// <param name="plan">Explicit field decisions and canonical source hashes.</param>
    /// <returns>Portable uppercase SHA-256 transaction key.</returns>
    public static string PlanFingerprint(ProjectAiPreparedReviewPlan plan)
    {
        (ProjectAiPreparedTitleChange[] updates,
            ProjectAiPreparedRequirementCreation[] creates, _) = ValidatePlan(plan);
        string json = JsonSerializer.Serialize(new
        {
            schema = 1,
            raw = plan.RawResponseSha256,
            updates = updates.Select(item => new
            {
                item.ProposalId, item.ProjectId, item.RequirementId,
                item.Reference, item.RelativePath, item.ExpectedSha256,
                item.CurrentTitle, item.ReviewedTitle, item.Reason
            }).ToArray(),
            creates = creates.Select(item => new
            {
                item.ProposalId, item.ProjectId, item.ReviewedTitle, item.Reason
            }).ToArray()
        });
        return Hash(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Read-only estimate of EXI codes assigned for new cards. A retry returns
    /// EXACT prior journal assignments. Codes are rechecked under lock at commit.
    /// </summary>
    /// <param name="projectDirectory">Selected project.</param>
    /// <param name="plan">Human-approved mixed plan.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Proposed codes ordered by creation proposal GUID.</returns>
    public async Task<IReadOnlyList<string>> ProposeReferencesAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken token = default)
    {
        (_, ProjectAiPreparedRequirementCreation[] creates,
            Guid projectId) = ValidatePlan(plan);
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        await VerifyProjectAsync(project, projectId, token);
        string journalRoot = Path.Combine(project, RootName);
        if (Directory.Exists(journalRoot))
        {
            RequireNormalDirectory(journalRoot);
        }

        string transaction = TransactionDirectory(project, PlanFingerprint(plan));
        if (Directory.Exists(transaction))
        {
            RequireNormalDirectory(transaction);
            ProjectAiMixedJournal journal = await ReadJournalAsync(
                Path.Combine(transaction, ManifestName), token);
            ValidateJournal(journal, plan);
            return journal.Entries.Where(item => item.Operation == "create")
                .OrderBy(item => item.ProposalId)
                .Select(item => item.Reference).ToArray();
        }

        ProjectRequirementCatalog catalog =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(project, token);
        if (catalog.ProjectId != projectId || catalog.Diagnostics.Count != 0)
        {
            throw new InvalidDataException(
                "Registre EXI ambigu : régler les diagnostics avant la création.");
        }

        return AllocateReferences(catalog.NextReference, creates.Length);
    }

    /// <summary>
    /// Publishes an explicitly confirmed mixed batch or verifies/replays its
    /// identical durable journal. Never silently reallocates EXI references.
    /// </summary>
    /// <param name="projectDirectory">Selected active project.</param>
    /// <param name="plan">Complete human-approved plan.</param>
    /// <param name="approvedReferences">Exact Nodalis codes shown during confirmation.</param>
    /// <param name="token">Cancellation before each file replacement.</param>
    /// <returns>Applied or no-op identical replay.</returns>
    public Task<ProjectAiMixedCommitStatus> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        IReadOnlyList<string> approvedReferences,
        CancellationToken token = default) =>
        CommitWithCheckpointAsync(projectDirectory, plan, approvedReferences,
            token, null);

    /// <summary>
    /// Deterministic crash-point for Windows smoke tests, after a completed
    /// per-file publication and any required creation receipt.
    /// </summary>
    internal async Task<ProjectAiMixedCommitStatus> CommitWithCheckpointAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        IReadOnlyList<string> approvedReferences, CancellationToken token,
        Action<int>? checkpoint)
    {
        (ProjectAiPreparedTitleChange[] updates,
            ProjectAiPreparedRequirementCreation[] creates,
            Guid projectId) = ValidatePlan(plan);
        ArgumentNullException.ThrowIfNull(approvedReferences);
        string[] requestedCodes = approvedReferences.ToArray();
        if (requestedCodes.Length != creates.Length ||
            requestedCodes.Any(code => !ProjectRequirementMarkdownCodec.IsReference(code)) ||
            requestedCodes.Distinct(StringComparer.Ordinal).Count() != requestedCodes.Length)
        {
            throw new InvalidDataException(
                "Références EXI non uniques ou absentes de la confirmation humaine.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        await VerifyProjectAsync(project, projectId, token);
        string root = Path.Combine(project, RootName);
        Directory.CreateDirectory(root);
        RequireNormalDirectory(root);
        string lockPath = Path.Combine(root, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou d'import IA sous jonction.");
        }

        token.ThrowIfCancellationRequested();
        string key = PlanFingerprint(plan);
        string transaction = TransactionDirectory(project, key);
        ProjectAiMixedJournal journal;
        if (Directory.Exists(transaction))
        {
            RequireNormalDirectory(transaction);
            journal = await ReadJournalAsync(
                Path.Combine(transaction, ManifestName), token);
            ValidateJournal(journal, plan);
            if (!journal.Entries.Where(item => item.Operation == "create")
                .OrderBy(item => item.ProposalId)
                .Select(item => item.Reference)
                .SequenceEqual(requestedCodes, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Les références réservées dans le journal ont changé.");
            }
        }
        else
        {
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_workspaceRoot)
                    .ListAsync(project, token);
            if (catalog.ProjectId != projectId ||
                catalog.Diagnostics.Count > 0)
            {
                throw new InvalidDataException(
                    "Registre d'exigences incohérent : aucune publication.");
            }

            string[] currentCodes = AllocateReferences(
                catalog.NextReference, creates.Length);
            if (!currentCodes.SequenceEqual(requestedCodes,
                StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Les références EXI disponibles ont changé : revalider le lot.");
            }

            await CheckProposalReuseAsync(root,
                updates.Select(item => item.ProposalId)
                    .Concat(creates.Select(item => item.ProposalId)).ToArray(),
                token);
            journal = await PrepareAsync(project, plan, updates, creates,
                requestedCodes, catalog, transaction, token);
        }

        (int already, List<ProjectAiMixedEntry> pending) =
            await VerifyPendingAsync(project, transaction, journal, token);
        if (pending.Count == 0)
        {
            // Recover a receipt lost between successful no-overwrite
            // publication and its journal confirmation. Never create a
            // receipt without verifying the exact published target first.
            await VerifyAllPublishedAsync(project, journal, CancellationToken.None);
            return ProjectAiMixedCommitStatus.AlreadyApplied;
        }

        int completed = 0;
        foreach (ProjectAiMixedEntry item in pending)
        {
            token.ThrowIfCancellationRequested();
            string destination = TargetPath(project, item.Reference);
            string approved = Path.Combine(
                transaction, ApprovedFolder, item.Slot);
            if (item.Operation == "update")
            {
                string existing = ResolveExistingTarget(project, item);
                await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                    existing, item.OriginalSha256!, approved,
                    item.PublishedSha256, token);
            }
            else
            {
                string docs = Path.Combine(project, "Documentation");
                string exis = Path.Combine(docs, "Exigences");
                Directory.CreateDirectory(docs);
                RequireNormalDirectory(docs);
                Directory.CreateDirectory(exis);
                RequireNormalDirectory(exis);
                string approvedText = new UTF8Encoding(false, true).GetString(
                    await ReadBoundedAsync(approved, token));
                await AtomicFileWriter.WriteNewAllTextAsync(
                    destination, approvedText, token);
                await WriteReceiptAsync(transaction, item,
                    CancellationToken.None);
            }

            string published = Hash(await ReadBoundedAsync(
                destination, CancellationToken.None));
            if (!string.Equals(published, item.PublishedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Résultat de publication divergent : " + item.Reference);
            }

            completed++;
            checkpoint?.Invoke(completed);
        }

        // An external editor can change an earlier file while later files
        // publish. Never report success until all final SHA values match.
        await VerifyAllPublishedAsync(project, journal, CancellationToken.None);
        return ProjectAiMixedCommitStatus.Applied;
    }

    /// <summary>
    /// Read-only discovery of safely recoverable incomplete mixed transactions.
    /// Divergent or corrupted journals remain untouched but are not offered.
    /// </summary>
    /// <param name="projectDirectory">Selected project.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>At most 100 verified incomplete batches.</returns>
    public async Task<IReadOnlyList<ProjectAiMixedRecoveryPreview>>
        ListPendingAsync(string projectDirectory, CancellationToken token = default)
    {
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string root = Path.Combine(project, RootName);
        if (!Directory.Exists(root))
        {
            return [];
        }

        RequireNormalDirectory(root);
        string[] candidates = Directory.GetDirectories(root, "mixed-*",
            SearchOption.TopDirectoryOnly);
        if (candidates.Length > 100)
        {
            throw new InvalidDataException(
                "Plus de 100 journaux mixtes : inspection manuelle requise.");
        }

        List<ProjectAiMixedRecoveryPreview> previews = [];
        foreach (string directory in candidates)
        {
            string name = Path.GetFileName(directory);
            if (name.Length != 70 || !name.StartsWith("mixed-",
                    StringComparison.Ordinal) ||
                !name[6..].All(Uri.IsHexDigit))
            {
                continue;
            }

            try
            {
                ProjectAiMixedRecoveryPreview preview =
                    await ReviewRecoveryAsync(project, name[6..], token);
                if (preview.Pending > 0)
                {
                    previews.Add(preview);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or
                IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Unsafe/corrupt journals remain for manual recovery only.
            }
        }

        return previews.OrderBy(item => item.PlanSha256,
            StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Reconstructs and validates ALL saved human decisions and actual targets
    /// without writing. Suitable for confirmation after restarting Nodalis.
    /// </summary>
    /// <param name="projectDirectory">Selected project.</param>
    /// <param name="planSha256">Exact journal ID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Project-local recoverable actions and progress.</returns>
    public async Task<ProjectAiMixedRecoveryPreview> ReviewRecoveryAsync(
        string projectDirectory, string planSha256,
        CancellationToken token = default)
    {
        (string project, ProjectAiMixedJournal journal, _) =
            await ReadRecoveryAsync(projectDirectory, planSha256, token);
        (int already, List<ProjectAiMixedEntry> pending) =
            await VerifyPendingAsync(project, TransactionDirectory(
                project, planSha256), journal, token);
        return new ProjectAiMixedRecoveryPreview(planSha256,
            journal.ProjectId, journal.Entries.ToArray(), already,
            pending.Count);
    }

    /// <summary>
    /// Replays the journal selected and re-confirmed by the human. The original
    /// pasted AI answer or WPF dialog instance is not required.
    /// </summary>
    /// <param name="projectDirectory">Selected project.</param>
    /// <param name="planSha256">Confirmed key.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Safe replay result.</returns>
    public async Task<ProjectAiMixedCommitStatus> ResumeAsync(
        string projectDirectory, string planSha256,
        CancellationToken token = default)
    {
        (_, ProjectAiMixedJournal journal, ProjectAiPreparedReviewPlan plan) =
            await ReadRecoveryAsync(projectDirectory, planSha256, token);
        string[] codes = journal.Entries.Where(item => item.Operation == "create")
            .OrderBy(item => item.ProposalId)
            .Select(item => item.Reference).ToArray();
        return await CommitAsync(projectDirectory, plan, codes, token);
    }

    /// <summary>Strictly validates independent decisions and owner project.</summary>
    private static (ProjectAiPreparedTitleChange[] Updates,
        ProjectAiPreparedRequirementCreation[] Creates, Guid ProjectId)
        ValidatePlan(ProjectAiPreparedReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Changes is null || plan.Creations is null ||
            plan.Changes.Count < 1 || plan.Creations.Count < 1 ||
            plan.Changes.Count + plan.Creations.Count > MaxEntries ||
            plan.RawResponseSha256 is not { Length: 64 } ||
            !plan.RawResponseSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Lot mixte : au moins une création et une modification, " +
                "maximum 20 opérations humaines.");
        }

        ProjectAiPreparedTitleChange[] updates = plan.Changes.OrderBy(
            item => item.Reference, StringComparer.Ordinal).ToArray();
        ProjectAiPreparedRequirementCreation[] creates = plan.Creations.OrderBy(
            item => item.ProposalId).ToArray();
        Guid projectId = updates[0].ProjectId;
        HashSet<Guid> proposals = [];
        HashSet<Guid> targets = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectAiPreparedTitleChange change in updates)
        {
            if (change.ProposalId == Guid.Empty ||
                change.RequirementId == Guid.Empty ||
                change.ProjectId == Guid.Empty || change.ProjectId != projectId ||
                !proposals.Add(change.ProposalId) ||
                !targets.Add(change.RequirementId) ||
                !paths.Add(change.RelativePath) ||
                !ProjectRequirementMarkdownCodec.IsReference(change.Reference) ||
                change.ExpectedSha256 is not { Length: 64 } ||
                !change.ExpectedSha256.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(change.CurrentTitle) ||
                !ValidTitle(change.ReviewedTitle) ||
                change.ReviewedTitle == change.CurrentTitle ||
                change.Reason is { Length: > 1200 })
            {
                throw new InvalidDataException(
                    "Modification existante EXI invalide dans le lot mixte.");
            }
        }

        foreach (ProjectAiPreparedRequirementCreation create in creates)
        {
            if (create.ProposalId == Guid.Empty || create.ProjectId != projectId ||
                !proposals.Add(create.ProposalId) ||
                !ValidTitle(create.ReviewedTitle) ||
                create.Reason is { Length: > 1200 })
            {
                throw new InvalidDataException(
                    "Création EXI invalide ou dupliquée dans le lot mixte.");
            }
        }

        return (updates, creates, projectId);
    }

    /// <summary>Allows only one-line, human-approved nonempty titles.</summary>
    private static bool ValidTitle(string title) =>
        !string.IsNullOrWhiteSpace(title) &&
        title == title.Trim() &&
        title.Length <= 240 && !title.Any(char.IsControl);

    /// <summary>Assigns successive unused codes from the verified registry.</summary>
    private static string[] AllocateReferences(string? first, int number)
    {
        if (!ProjectRequirementMarkdownCodec.IsReference(first) ||
            !int.TryParse(first![4..], out int numeric) ||
            numeric + number - 1 > 9999)
        {
            throw new InvalidDataException(
                "Références EXI insuffisantes pour ce lot mixte.");
        }

        return Enumerable.Range(numeric, number)
            .Select(value => "EXI-" + value.ToString("D4")).ToArray();
    }

    /// <summary>Validates project ownership, path, and active archive status.</summary>
    private async Task VerifyProjectAsync(
        string project, Guid id, CancellationToken token)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || segments.Length < 4 ||
            !string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !segments.Skip(2).Any(segment => string.Equals(segment,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Lot IA hors du projet actif.");
        }

        string manifest = Path.Combine(
            project, WorkspaceLayout.ProjectManifestFileName);
        string verified = _sources.GetReferenceForExistingFile(manifest);
        _sources.ResolveExistingFile(verified);
        ProjectManifest current = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifest, token);
        if (current.Id != id || id == Guid.Empty ||
            current.ArchivedUtc.HasValue ||
            current.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet archivé ou différent du projet qualifié.");
        }
    }

    /// <summary>Resolves only one canonical EXI target under the chosen project.</summary>
    private string ResolveExistingTarget(string project, ProjectAiMixedEntry item)
    {
        string existing = _sources.ResolveExistingFile(item.RelativePath);
        if (!string.Equals(existing, TargetPath(project, item.Reference),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Chemin EXI non canonique dans le journal mixte.");
        }

        return existing;
    }

    /// <summary>Constructs a project-local, canonical EXI source or target.</summary>
    private static string TargetPath(string project, string reference) =>
        Path.GetFullPath(Path.Combine(project, "Documentation", "Exigences",
            reference + ".md"));

    /// <summary>Builds the exact deterministic directory for one reviewed plan.</summary>
    private static string TransactionDirectory(string project, string hash) =>
        Path.Combine(project, RootName, "mixed-" + hash);

    /// <summary>Rejects junctions and symbolic links in journal directories.</summary>
    private static void RequireNormalDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier d'import absent ou redirigé par lien symbolique.");
        }
    }

    /// <summary>Reads at most 1 MiB; rejects concurrent file growth.</summary>
    private static async Task<byte[]> ReadBoundedAsync(
        string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxBytes)
        {
            throw new InvalidDataException("EXI ou journal trop volumineux.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxBytes)
        {
            throw new InvalidDataException("EXI modifiée durant la lecture.");
        }

        return bytes;
    }

    /// <summary>Reads manifest with bounded JSON and unsafe-link rejection.</summary>
    private static async Task<ProjectAiMixedJournal> ReadJournalAsync(
        string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 16384)
        {
            throw new InvalidDataException(
                "Journal mixte absent, incomplet ou non sûr.");
        }

        try
        {
            string text = await File.ReadAllTextAsync(path, token);
            return JsonSerializer.Deserialize<ProjectAiMixedJournal>(
                text, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidDataException("Journal mixte vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Journal mixte malformé : récupération manuelle requise.",
                exception);
        }
    }

    /// <summary>Hashes exact bytes with portable SHA-256.</summary>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
