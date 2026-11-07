using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One durable, source-approved EXI B successor. The original requirement
/// is never removed or rewritten, and this is NOT a mode-A executor.
/// </summary>
public sealed record ProjectRequirementSuccessorJournal
{
    /// <summary>Unique immutable transaction identity.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Human-signed immutable intention.</summary>
    public required Guid IntentId { get; init; }

    /// <summary>Exact owning source project GUID.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Exact recipient project GUID.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Original EXI GUID kept in its project.</summary>
    public required Guid SourceRequirementId { get; init; }

    /// <summary>Distinct successor GUID created at the destination.</summary>
    public required Guid SuccessorRequirementId { get; init; }

    /// <summary>Stable source EXI workspace-relative Markdown path.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Stable candidate destination EXI workspace-relative path.</summary>
    public required string TargetRelativePath { get; init; }

    /// <summary>Canonical target EXI-xxxx reference.</summary>
    public required string TargetReference { get; init; }

    /// <summary>Source requirement SHA-256, pinned at preparation.</summary>
    public required string SourceSha256 { get; init; }

    /// <summary>SHA-256 of the exact, immutable created successor document.</summary>
    public required string SuccessorSha256 { get; init; }

    /// <summary>
    /// Human-acknowledged original/new CA GUID mapping; no inherited proof.
    /// </summary>
    public IReadOnlyList<ProjectRequirementCriterionSuccession>
        CriterionSuccession { get; init; } = [];

    /// <summary>Human explanation of the new, unverified CA lineage.</summary>
    public string CriteriaReviewRationale { get; init; } = string.Empty;

    /// <summary>Recorded human review time for a succession with CA.</summary>
    public DateTimeOffset? CriteriaReviewedUtc { get; init; }

    /// <summary>Prepared, completed, rollback-prepared or rolled-back.</summary>
    public required string Phase { get; init; }

    /// <summary>Creation date of the first durable journal.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Journaled single-file creation of EXI mode-B successors. Publication uses
/// CreateNew, never overwrites and is recoverable by comparing exact bytes.
/// Rollback moves the exact published target into the private transaction
/// history, never silently deleting an edited document.
/// </summary>
public sealed partial class ProjectRequirementSuccessorTransactionService
{
    private const string TransactionFolder = ".exi-successor-transactions";
    private const string JournalName = "journal.json";
    private const string StagedName = "successor.md";
    private const string RestoredName = "withdrawn-successor.md";
    private readonly string _root;
    private readonly string _transactions;
    private readonly ProjectRequirementHandoverPreviewService _previews;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectJourneySourceService _sources;
    private readonly Action<string>? _checkpoint;

    /// <summary>Creates the bounded workspace transaction owner.</summary>
    /// <param name="workspaceRoot">Existing one-workspace root.</param>
    /// <param name="checkpoint">Optional test-only interruption hook.</param>
    public ProjectRequirementSuccessorTransactionService(
        string workspaceRoot, Action<string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _transactions = Path.Combine(_root, WorkspaceLayout.ImportsDirectoryName,
            TransactionFolder);
        _previews = new ProjectRequirementHandoverPreviewService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _sources = new ProjectJourneySourceService(_root);
        _checkpoint = checkpoint;
    }

    /// <summary>Lists exact durable transactions without filesystem changes.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Newest-first historical transactions.</returns>
    public async Task<IReadOnlyList<ProjectRequirementSuccessorJournal>>
        ListHistoryAsync(CancellationToken token = default)
    {
        IReadOnlyList<(string Directory, ProjectRequirementSuccessorJournal Journal)>
            found = await ReadJournalsAsync(token);
        return found.Select(item => item.Journal)
            .OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    /// <summary>Lists incomplete and potentially interrupted transactions.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Unfinished transaction records only.</returns>
    public async Task<IReadOnlyList<ProjectRequirementSuccessorJournal>>
        ListPendingAsync(CancellationToken token = default)
    {
        IReadOnlyList<ProjectRequirementSuccessorJournal> all =
            await ListHistoryAsync(token);
        return all.Where(item => item.Phase is not
            ("completed" or "rolled-back")).ToArray();
    }

    /// <summary>
    /// Rechecks a fresh, human-reviewed B preview, persists staged bytes and
    /// its journal first, and publishes the destination only with CreateNew.
    /// No source file, acceptance criterion or pre-existing link is modified.
    /// </summary>
    /// <param name="reviewed">Previously presented EXI plan.</param>
    /// <param name="approved">Separate human execution confirmation.</param>
    /// <param name="token">Preparation cancellation.</param>
    /// <param name="approvedCriteriaIds">Individually reviewed source CA GUIDs.</param>
    /// <param name="criteriaReviewRationale">Required explanation of B requalification.</param>
    /// <returns>Durable creation journal or explicit recovery on failure.</returns>
    public async Task<ProjectRequirementSuccessorJournal> ExecuteAsync(
        ProjectRequirementHandoverPreview reviewed, bool approved,
        CancellationToken token = default,
        IReadOnlyList<Guid>? approvedCriteriaIds = null,
        string? criteriaReviewRationale = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!approved || !reviewed.CanPrepare ||
            reviewed.Intent.Method != ProjectHandoverMethod.LinkedSuccessor ||
            reviewed.ProposedId == reviewed.Intent.SourceObjectId)
        {
            throw new InvalidOperationException(
                "Une création EXI B exige une prévisualisation sûre " +
                "et une validation humaine distincte. Le mode A n'est pas exécutable.");
        }

        ProjectRequirementCriterionSuccession[] mapping =
            reviewed.ProposedCriterionSuccession.ToArray();
        Guid[] reviewedIds = approvedCriteriaIds?.ToArray() ?? [];
        Guid[] expected = mapping.Select(item => item.OriginalId)
            .OrderBy(item => item).ToArray();
        if (reviewedIds.Length != reviewedIds.Distinct().Count() ||
            !reviewedIds.OrderBy(item => item).SequenceEqual(expected) ||
            mapping.Length > 0 &&
            (string.IsNullOrWhiteSpace(criteriaReviewRationale) ||
             criteriaReviewRationale.Trim().Length > 2000))
        {
            throw new InvalidOperationException(
                "Chaque CA source du successeur B exige une revue humaine " +
                "individuelle et un motif écrit, sans verdict hérité.");
        }

        await using FileStream operationLock = await AcquireLockAsync(token);
        IReadOnlyList<ProjectRequirementSuccessorJournal> history =
            await ListHistoryAsync(token);
        if (history.Any(item => item.Phase is not
            ("completed" or "rolled-back")))
        {
            throw new InvalidOperationException(
                "Une passation EXI interrompue doit être récupérée avant une nouvelle.");
        }

        if (history.Any(item => item.IntentId == reviewed.Intent.Id))
        {
            throw new InvalidOperationException(
                "Cette intention EXI a déjà été consommée, même après rollback.");
        }

        string sourceProject = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(ResolveExisting(reviewed.SourceRelativePath))!)!)!;
        ProjectRequirementHandoverPreview fresh =
            await _previews.PrepareAsync(sourceProject, reviewed.Intent.Id, token);
        if (!fresh.CanPrepare ||
            fresh.Intent != reviewed.Intent ||
            fresh.ProposedReference != reviewed.ProposedReference ||
            fresh.ProposedId != reviewed.ProposedId ||
            fresh.SourceSha256 != reviewed.SourceSha256 ||
            fresh.SourceRelativePath != reviewed.SourceRelativePath ||
            fresh.ProposedTargetRelativePath !=
                reviewed.ProposedTargetRelativePath ||
            fresh.ProposedMarkdown != reviewed.ProposedMarkdown ||
            !fresh.ProposedCriterionSuccession.SequenceEqual(mapping))
        {
            throw new InvalidDataException(
                "Les exigences, la référence libre ou la proposition ont changé : " +
                "nouvelle validation requise.");
        }

        string targetRelative = fresh.ProposedTargetRelativePath!;
        string targetPath = CombineRelative(targetRelative);
        if (Path.Exists(targetPath))
        {
            throw new InvalidDataException(
                "Un autre processus a réservé la référence EXI.");
        }

        Guid transactionId = Guid.NewGuid();
        string directory = Path.Combine(_transactions,
            transactionId.ToString("N"));
        Directory.CreateDirectory(directory);
        RequirePlain(directory);
        string staged = Path.Combine(directory, StagedName);
        await AtomicFileWriter.WriteNewAllTextAsync(
            staged, fresh.ProposedMarkdown!, token);
        string targetHash = Hash(fresh.ProposedMarkdown!);
        ProjectRequirementSuccessorJournal journal = new()
        {
            TransactionId = transactionId,
            IntentId = fresh.Intent.Id,
            SourceProjectId = fresh.Intent.SourceProjectId,
            TargetProjectId = fresh.Intent.TargetProjectId,
            SourceRequirementId = fresh.Intent.SourceObjectId,
            SuccessorRequirementId = fresh.ProposedId,
            SourceRelativePath = fresh.SourceRelativePath,
            TargetRelativePath = targetRelative,
            TargetReference = fresh.ProposedReference!,
            SourceSha256 = fresh.SourceSha256,
            SuccessorSha256 = targetHash,
            CriterionSuccession = mapping,
            CriteriaReviewRationale = mapping.Length == 0 ? string.Empty :
                criteriaReviewRationale!.Trim(),
            CriteriaReviewedUtc = mapping.Length == 0 ? null :
                DateTimeOffset.UtcNow,
            Phase = "prepared"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, token);
        _checkpoint?.Invoke("prepared");
        return await PublishAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>
    /// Explicitly reconciles incomplete EXI journals, refusing unknown bytes.
    /// It never starts a previously unapproved transaction.
    /// </summary>
    /// <param name="approved">Human recovery decision.</param>
    /// <param name="token">Cancellation before each journal.</param>
    /// <returns>Number of completed recovery operations.</returns>
    public async Task<int> RecoverPendingAsync(bool approved,
        CancellationToken token = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "Reprise EXI impossible sans confirmation humaine.");
        }

        await using FileStream operationLock = await AcquireLockAsync(token);
        IReadOnlyList<(string Directory, ProjectRequirementSuccessorJournal Journal)>
            found = await ReadJournalsAsync(token);
        int count = 0;
        foreach ((string directory, ProjectRequirementSuccessorJournal journal)
            in found)
        {
            if (journal.Phase is "completed" or "rolled-back")
            {
                continue;
            }

            if (journal.Phase == "rollback-prepared")
            {
                await WithdrawAsync(directory, journal, token);
            }
            else
            {
                await PublishAsync(directory, journal, token);
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Explicitly archives exactly the published successor, never an edited
    /// replacement, and retains its byte-exact source for later inspection.
    /// </summary>
    /// <param name="transactionId">Completed durable B transaction.</param>
    /// <param name="approved">Human rollback decision.</param>
    /// <param name="token">Cancellation before journal change.</param>
    /// <returns>Durable reversed transaction journal.</returns>
    public async Task<ProjectRequirementSuccessorJournal> RollbackAsync(
        Guid transactionId, bool approved, CancellationToken token = default)
    {
        if (!approved || transactionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Restauration EXI impossible sans transaction et confirmation.");
        }

        await using FileStream operationLock = await AcquireLockAsync(token);
        string directory = Path.Combine(_transactions, transactionId.ToString("N"));
        ProjectRequirementSuccessorJournal journal =
            await ReadJournalAsync(directory, token);
        if (journal.TransactionId != transactionId ||
            journal.Phase is "prepared" or "rolled-back")
        {
            throw new InvalidOperationException(
                "Seul un successeur EXI publié peut être retiré.");
        }

        if (journal.Phase != "rollback-prepared")
        {
            // Refuse before changing the journal if the target is already
            // human-edited: a future approval must not hijack their changes.
            string target = ResolveExisting(journal.TargetRelativePath);
            await RequireHashAsync(target, journal.SuccessorSha256, token);
            (string sourceProject, string targetProject, string targetFile) =
                await ResolveOwnersAsync(journal, token);
            await new ProjectRequirementSuccessorBacklinkGuard(_root)
                .AssertNoLinksAsync(journal, targetProject, token);
            await AssertNoCriterionBacklinksAsync(journal, token);
            journal = journal with { Phase = "rollback-prepared" };
            await AtomicJsonFile.WriteAsync(
                Path.Combine(directory, JournalName), journal, token);
        }

        return await WithdrawAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Obtains an exclusive workspace-scoped handover write lock.</summary>
    /// <param name="token">Cancellation prior to the lock.</param>
    /// <returns>Exclusive stream; dispose to unlock.</returns>
    private Task<FileStream> AcquireLockAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string imports = Path.GetDirectoryName(_transactions)!;
        Directory.CreateDirectory(imports);
        RequirePlain(imports);
        Directory.CreateDirectory(_transactions);
        RequirePlain(_transactions);
        FileStream lockFile = new(
            Path.Combine(_transactions, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        return Task.FromResult(lockFile);
    }

    /// <summary>Resolves workspace-relative existing file with link checks.</summary>
    /// <param name="relative">Approved workspace-relative path.</param>
    /// <returns>Exact file physical location.</returns>
    private string ResolveExisting(string relative) =>
        _sources.ResolveExistingFile(relative);

    /// <summary>Resolves a reviewed workspace-relative new target path.</summary>
    /// <param name="relative">Canonical planned destination.</param>
    /// <returns>Root-confined physical target file path.</returns>
    private string CombineRelative(string relative)
    {
        if (!ProjectJourneySourcePath.IsSafe(relative))
        {
            throw new InvalidDataException("Chemin EXI non sûr.");
        }

        return Path.GetFullPath(Path.Combine(_root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Rejects symbolic links and junctions for existing directories.</summary>
    /// <param name="path">Canonical existing folder.</param>
    private static void RequirePlain(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier lié interdit pour cette transaction EXI.");
        }
    }

    /// <summary>Hashes canonical proposed UTF-8 bytes.</summary>
    /// <param name="markdown">Exact Markdown without BOM.</param>
    /// <returns>Uppercase SHA-256.</returns>
    private static string Hash(string markdown) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));

    /// <summary>Verifies byte-exact file contents before any destructive step.</summary>
    /// <param name="path">Existing file.</param>
    /// <param name="expected">Approved SHA-256.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Hash check completion.</returns>
    private static async Task RequireHashAsync(
        string path, string expected, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open,
            FileAccess.Read, FileShare.Read | FileShare.Delete, 65536,
            useAsync: true);
        if (stream.Length > 1024 * 1024 ||
            !string.Equals(Convert.ToHexString(
                await SHA256.HashDataAsync(stream, token)), expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Un document EXI diffère du snapshot approuvé : aucune écriture.");
        }
    }
}
