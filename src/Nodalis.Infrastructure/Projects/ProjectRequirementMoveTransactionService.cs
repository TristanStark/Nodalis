using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Explicitly human-reviewed acceptance criterion carried by an EXI A
/// transaction. Reference is local; GUID remains the durable identity.
/// </summary>
/// <param name="Reference">Original CA-xx reference kept in the moved card.</param>
/// <param name="Id">Original stable Markdown block GUID.</param>
public sealed record ProjectRequirementReviewedCriterion(
    string Reference, Guid Id);

/// <summary>Durable A move of exactly one standalone EXI and its original GUID.</summary>
public sealed record ProjectRequirementMoveJournal
{
    /// <summary>Immutable transaction identity.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Human-reviewed, source-owned A intention.</summary>
    public required Guid IntentId { get; init; }

    /// <summary>Original project identity.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Recipient project identity.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Unchanged original EXI identity.</summary>
    public required Guid RequirementId { get; init; }

    /// <summary>Canonical source EXI Markdown path.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Canonical recipient EXI Markdown path.</summary>
    public required string TargetRelativePath { get; init; }

    /// <summary>Local source reference before renumbering.</summary>
    public required string SourceReference { get; init; }

    /// <summary>New local recipient reference.</summary>
    public required string TargetReference { get; init; }

    /// <summary>Exact source SHA before any changes.</summary>
    public required string OriginalSha256 { get; init; }

    /// <summary>Exact proposed recipient source SHA.</summary>
    public required string DestinationSha256 { get; init; }

    /// <summary>Individually reviewed, SHA-pinned CA IDs retained by A.</summary>
    public IReadOnlyList<ProjectRequirementReviewedCriterion> ReviewedCriteria
    { get; init; } = [];

    /// <summary>Written reason for retaining the approved CA identities.</summary>
    public string CriteriaReviewRationale { get; init; } = string.Empty;

    /// <summary>Exact time of explicit human CA acknowledgement.</summary>
    public DateTimeOffset? CriteriaReviewedUtc { get; init; }

    /// <summary>Prepared, completed, rollback-prepared or rolled-back.</summary>
    public required string Phase { get; init; }

    /// <summary>Durable human execution time.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Explicit, crash-recoverable EXI mode-A move. A journal and two immutable
/// snapshots precede any source removal. The source is parked BEFORE
/// publishing its original GUID at the target, preventing double ownership.
/// </summary>
public sealed partial class ProjectRequirementMoveTransactionService
{
    private const string Folder = ".exi-move-transactions";
    private const string JournalName = "journal.json";
    private const string Original = "original.md";
    private const string Proposed = "destination.md";
    private const string Parked = "parked-original.md";
    private const string Withdrawn = "withdrawn-destination.md";
    private readonly string _root;
    private readonly string _transactions;
    private readonly ProjectRequirementHandoverPreviewService _previews;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectRequirementMoveReferenceGuard _guard;
    private readonly Action<string>? _checkpoint;

    /// <summary>Creates a one-workspace EXI A transactional owner.</summary>
    /// <param name="workspaceRoot">Canonical workspace root.</param>
    /// <param name="checkpoint">Optional deterministic test interruption.</param>
    public ProjectRequirementMoveTransactionService(
        string workspaceRoot, Action<string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _transactions = Path.Combine(_root, WorkspaceLayout.ImportsDirectoryName,
            Folder);
        _previews = new ProjectRequirementHandoverPreviewService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _sources = new ProjectJourneySourceService(_root);
        _guard = new ProjectRequirementMoveReferenceGuard(_root);
        _checkpoint = checkpoint;
    }

    /// <summary>Lists all retained A transaction records newest first.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Exact historical journal states.</returns>
    public async Task<IReadOnlyList<ProjectRequirementMoveJournal>> ListHistoryAsync(
        CancellationToken token = default)
    {
        IReadOnlyList<(string Directory, ProjectRequirementMoveJournal Journal)>
            found = await ReadJournalsAsync(token);
        return found.Select(item => item.Journal)
            .OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    /// <summary>Lists all interrupted A journals awaiting human recovery.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Unfinished durable operations.</returns>
    public async Task<IReadOnlyList<ProjectRequirementMoveJournal>> ListPendingAsync(
        CancellationToken token = default)
    {
        IReadOnlyList<ProjectRequirementMoveJournal> all =
            await ListHistoryAsync(token);
        return all.Where(item => item.Phase is not
            ("completed" or "rolled-back")).ToArray();
    }

    /// <summary>
    /// Uses an explicit A intention, fresh inventory and link scan, then
    /// durably snapshots the original and candidate before any move.
    /// </summary>
    /// <param name="reviewed">Displayed, unblocked preflight.</param>
    /// <param name="approved">Separate human execution approval.</param>
    /// <param name="token">Cancellation before committing a journal.</param>
    /// <param name="reviewedCriteriaIds">Explicitly acknowledged CA GUIDs.</param>
    /// <param name="criteriaReviewRationale">Required written review reason for CA moves.</param>
    /// <returns>Committed or recoverable transaction identity.</returns>
    public async Task<ProjectRequirementMoveJournal> ExecuteAsync(
        ProjectRequirementHandoverPreview reviewed, bool approved,
        CancellationToken token = default,
        IReadOnlyList<Guid>? reviewedCriteriaIds = null,
        string? criteriaReviewRationale = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!approved || !reviewed.CanPrepare ||
            reviewed.Intent.Method != ProjectHandoverMethod.MoveOriginal ||
            reviewed.ProposedId != reviewed.Intent.SourceObjectId)
        {
            throw new InvalidOperationException(
                "Exécution A exigeant une intention et une confirmation " +
                "humaine distinctes, avec conservation du GUID.");
        }

        ProjectRequirementReviewedCriterion[] criteria = reviewed
            .CriteriaForReview.Select(item => new
                ProjectRequirementReviewedCriterion(
                    item.Reference, item.Id ?? Guid.Empty)).ToArray();
        Guid[] approvedCriteria = reviewedCriteriaIds?.ToArray() ?? [];
        Guid[] expected = criteria.Select(item => item.Id)
            .OrderBy(item => item).ToArray();
        if (criteria.Any(item => item.Id == Guid.Empty) ||
            approvedCriteria.Length != approvedCriteria.Distinct().Count() ||
            !approvedCriteria.OrderBy(item => item).SequenceEqual(expected) ||
            criteria.Length > 0 &&
            (string.IsNullOrWhiteSpace(criteriaReviewRationale) ||
             criteriaReviewRationale.Trim().Length > 2000))
        {
            throw new InvalidOperationException(
                "Chaque critère CA doit être vérifié et approuvé " +
                "explicitement avant un déplacement EXI A.");
        }

        await using FileStream locked = await AcquireLockAsync(token);
        IReadOnlyList<ProjectRequirementMoveJournal> history =
            await ListHistoryAsync(token);
        if (history.Any(item => item.Phase is not
            ("completed" or "rolled-back")))
        {
            throw new InvalidOperationException(
                "Une passation EXI A doit être récupérée avant toute autre.");
        }

        if (history.Any(item => item.IntentId == reviewed.Intent.Id))
        {
            throw new InvalidOperationException(
                "Intention EXI A déjà consommée, même après rollback.");
        }

        string sourceProject = ProjectRootFromRelative(
            reviewed.SourceRelativePath);
        ProjectRequirementHandoverPreview fresh =
            await _previews.PrepareAsync(sourceProject,
                reviewed.Intent.Id, token);
        if (!fresh.CanPrepare || fresh.Intent != reviewed.Intent ||
            fresh.SourceRelativePath != reviewed.SourceRelativePath ||
            fresh.ProposedTargetRelativePath !=
                reviewed.ProposedTargetRelativePath ||
            fresh.ProposedReference != reviewed.ProposedReference ||
            fresh.ProposedId != reviewed.ProposedId ||
            fresh.SourceSha256 != reviewed.SourceSha256 ||
            fresh.ProposedMarkdown != reviewed.ProposedMarkdown ||
            !fresh.CriteriaForReview.Select(item =>
                new ProjectRequirementReviewedCriterion(
                    item.Reference, item.Id ?? Guid.Empty))
                .SequenceEqual(criteria))
        {
            throw new InvalidDataException(
                "Source, référence ou fiche EXI A modifiée : nouvelle revue.");
        }

        string source = _sources.ResolveExistingFile(fresh.SourceRelativePath);
        string target = CombineRelative(fresh.ProposedTargetRelativePath!);
        if (Path.Exists(target))
        {
            throw new InvalidDataException(
                "Référence EXI A déjà occupée par un autre fichier.");
        }

        await _guard.AssertNoIncomingAsync(fresh.ProposedId,
            fresh.Intent.SourceReference, fresh.SourceRelativePath, token);
        await _guard.AssertNoIncomingAsync(fresh.ProposedId,
            fresh.ProposedReference!, fresh.SourceRelativePath, token);
        await new ProjectRequirementMoveCriteriaGuard(_root)
            .AssertNoIncomingAsync(criteria, fresh.SourceRelativePath,
                fresh.ProposedTargetRelativePath!, token);
        (string original, string originalHash) =
            await ProjectHandoverTaskExecutionPlanner
                .ReadExactTaskDocumentAsync(source, token);
        if (originalHash != fresh.SourceSha256)
        {
            throw new InvalidDataException(
                "La fiche EXI A source a changé pendant l'audit.");
        }

        Guid id = Guid.NewGuid();
        string directory = Path.Combine(_transactions, id.ToString("N"));
        Directory.CreateDirectory(directory);
        Plain(directory);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, Original), original, token);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, Proposed), fresh.ProposedMarkdown!, token);
        ProjectRequirementMoveJournal journal = new()
        {
            TransactionId = id,
            IntentId = fresh.Intent.Id,
            SourceProjectId = fresh.Intent.SourceProjectId,
            TargetProjectId = fresh.Intent.TargetProjectId,
            RequirementId = fresh.ProposedId,
            SourceRelativePath = fresh.SourceRelativePath,
            TargetRelativePath = fresh.ProposedTargetRelativePath!,
            SourceReference = fresh.Intent.SourceReference,
            TargetReference = fresh.ProposedReference!,
            OriginalSha256 = originalHash,
            DestinationSha256 = Hash(fresh.ProposedMarkdown!),
            ReviewedCriteria = criteria,
            CriteriaReviewRationale = criteria.Length == 0 ? string.Empty :
                criteriaReviewRationale!.Trim(),
            CriteriaReviewedUtc = criteria.Length == 0 ? null :
                DateTimeOffset.UtcNow,
            Phase = "prepared"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, token);
        _checkpoint?.Invoke("prepared");
        return await PublishAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Recovers only after explicit human approval of pending journals.</summary>
    /// <param name="approved">Required human consent.</param>
    /// <param name="token">Cancellation before each operation.</param>
    /// <returns>Number of recovered transactions.</returns>
    public async Task<int> RecoverPendingAsync(bool approved,
        CancellationToken token = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "Reprise EXI A sans confirmation humaine interdite.");
        }

        await using FileStream locked = await AcquireLockAsync(token);
        IReadOnlyList<(string Directory, ProjectRequirementMoveJournal Journal)>
            found = await ReadJournalsAsync(token);
        int count = 0;
        foreach ((string directory, ProjectRequirementMoveJournal journal) in found)
        {
            if (journal.Phase is "completed" or "rolled-back")
            {
                continue;
            }

            if (journal.Phase == "rollback-prepared")
            {
                await UndoAsync(directory, journal, token);
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
    /// Human-approved restoration of the original SHA-identical EXI source.
    /// Any destination edit or incoming link blocks rollback.
    /// </summary>
    /// <param name="transactionId">One completed A transaction.</param>
    /// <param name="approved">Explicit human rollback choice.</param>
    /// <param name="token">Pre-mutation cancellation.</param>
    /// <returns>Durable rollback journal.</returns>
    public async Task<ProjectRequirementMoveJournal> RollbackAsync(
        Guid transactionId, bool approved, CancellationToken token = default)
    {
        if (!approved || transactionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Transaction EXI A non sélectionnée ou non approuvée.");
        }

        await using FileStream locked = await AcquireLockAsync(token);
        string directory = Path.Combine(_transactions, transactionId.ToString("N"));
        ProjectRequirementMoveJournal journal =
            await ReadJournalAsync(directory, token);
        if (journal.TransactionId != transactionId ||
            journal.Phase is "prepared" or "rolled-back")
        {
            throw new InvalidDataException(
                "Seule une EXI A publiée et non restaurée est réversible.");
        }

        if (journal.Phase != "rollback-prepared")
        {
            string target = _sources.ResolveExistingFile(journal.TargetRelativePath);
            await VerifyHashAsync(target, journal.DestinationSha256, token);
            await _guard.AssertNoIncomingAsync(journal.RequirementId,
                journal.TargetReference, journal.TargetRelativePath, token);
            await new ProjectRequirementMoveCriteriaGuard(_root)
                .AssertNoIncomingAsync(journal.ReviewedCriteria,
                    journal.SourceRelativePath,
                    journal.TargetRelativePath, token);
            journal = journal with { Phase = "rollback-prepared" };
            await AtomicJsonFile.WriteAsync(
                Path.Combine(directory, JournalName), journal, token);
        }

        return await UndoAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Acquires the single EXI A exclusive operation lock.</summary>
    /// <param name="token">Cancellation before the lock.</param>
    /// <returns>Dispose-to-unlock stream.</returns>
    private Task<FileStream> AcquireLockAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string imports = Path.GetDirectoryName(_transactions)!;
        Directory.CreateDirectory(imports);
        Plain(imports);
        Directory.CreateDirectory(_transactions);
        Plain(_transactions);
        FileStream file = new(Path.Combine(_transactions, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        return Task.FromResult(file);
    }

    /// <summary>Returns root-confined, canonical relative physical path.</summary>
    /// <param name="relative">Validated workspace-relative path.</param>
    /// <returns>Destination physical path.</returns>
    private string CombineRelative(string relative)
    {
        if (!ProjectJourneySourcePath.IsSafe(relative))
        {
            throw new InvalidDataException("Chemin EXI A non sûr.");
        }

        return Path.GetFullPath(Path.Combine(_root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Derives exactly one containing project root from an EXI file.</summary>
    /// <param name="relative">Workspace-relative EXI path.</param>
    /// <returns>Project physical directory.</returns>
    private string ProjectRootFromRelative(string relative)
    {
        const string marker = "/Documentation/Exigences/";
        if (!ProjectJourneySourcePath.IsSafe(relative) ||
            !relative.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Dossier projet EXI A invalide.");
        }

        return CombineRelative(relative[
            ..relative.IndexOf(marker, StringComparison.Ordinal)]);
    }

    /// <summary>Checks that a folder is not a symlink or junction.</summary>
    /// <param name="directory">Existing folder.</param>
    private static void Plain(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier lié refusé pour une passation EXI A.");
        }
    }

    /// <summary>Returns uppercase SHA256 for exact UTF8 snapshot contents.</summary>
    /// <param name="content">Losslessly preserved Markdown.</param>
    /// <returns>Canonical SHA256 digest.</returns>
    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>Requires exact SHA256 of a bounded existing Markdown file.</summary>
    /// <param name="file">Physical file to validate.</param>
    /// <param name="sha">Expected immutable original or proposed hash.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Completed integrity verification.</returns>
    private static async Task VerifyHashAsync(
        string file, string sha, CancellationToken token)
    {
        if (new FileInfo(file).Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("EXI A modifiée ou trop volumineuse.");
        }

        await using FileStream stream = new(file, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 65536, useAsync: true);
        if (!string.Equals(
            Convert.ToHexString(await SHA256.HashDataAsync(stream, token)),
            sha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Document EXI A modifié depuis le snapshot : arrêt sans écriture.");
        }
    }
}
