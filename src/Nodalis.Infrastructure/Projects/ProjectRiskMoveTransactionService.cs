using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable A relocation of exactly one RIS file. Its risk GUID and every
/// human history event are preserved; source ownership changes only after
/// an explicit, crash-recoverable transaction.
/// </summary>
public sealed record ProjectRiskMoveJournal
{
    /// <summary>Immutable transaction identifier.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Existing reviewed human handover intention.</summary>
    public required Guid IntentId { get; init; }

    /// <summary>Original active project.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Receiving active project.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Unchanged original risk identity.</summary>
    public required Guid RiskId { get; init; }

    /// <summary>Canonical original risk source.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Canonical new owner source.</summary>
    public required string TargetRelativePath { get; init; }

    /// <summary>Original local reference.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Target local reference after explicit renumbering.</summary>
    public required string TargetReference { get; init; }

    /// <summary>Exact original source SHA-256.</summary>
    public required string OriginalSha256 { get; init; }

    /// <summary>Exact proposed recipient SHA-256.</summary>
    public required string DestinationSha256 { get; init; }

    /// <summary>prepared, completed, rollback-prepared, or rolled-back.</summary>
    public required string Phase { get; init; }

    /// <summary>Durable time of first preparation.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A-mode risk relocation with two immutable Markdown snapshots, a durable
/// journal and an exclusive operation lock. Moves the source aside BEFORE
/// publishing its original GUID at the target; it never creates two owners.
/// </summary>
public sealed partial class ProjectRiskMoveTransactionService
{
    private const string Folder = ".ris-move-transactions";
    private const string JournalName = "journal.json";
    private const string Original = "original.md";
    private const string Proposed = "destination.md";
    private const string Parked = "parked-original.md";
    private const string Withdrawn = "withdrawn-destination.md";
    private readonly string _root;
    private readonly string _transactions;
    private readonly ProjectRiskHandoverPreviewService _previews;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectRiskMoveReferenceGuard _guard;
    private readonly Action<string>? _checkpoint;

    /// <summary>Creates the confined RIS-A journal owner.</summary>
    /// <param name="workspaceRoot">Existing Nodalis workspace.</param>
    /// <param name="checkpoint">Optional deterministic test-only interruption.</param>
    public ProjectRiskMoveTransactionService(
        string workspaceRoot, Action<string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _transactions = Path.Combine(_root, WorkspaceLayout.ImportsDirectoryName,
            Folder);
        _previews = new ProjectRiskHandoverPreviewService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _sources = new ProjectJourneySourceService(_root);
        _guard = new ProjectRiskMoveReferenceGuard(_root);
        _checkpoint = checkpoint;
    }

    /// <summary>Read-only list of retained journal records, newest first.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>All completed, pending and rolled-back A journals.</returns>
    public async Task<IReadOnlyList<ProjectRiskMoveJournal>> ListHistoryAsync(
        CancellationToken token = default)
    {
        IReadOnlyList<(string Directory, ProjectRiskMoveJournal Journal)> found =
            await ReadJournalsAsync(token);
        return found.Select(entry => entry.Journal)
            .OrderByDescending(entry => entry.CreatedUtc).ToArray();
    }

    /// <summary>Read-only list of interrupted RIS A work.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Transactions requiring explicit human action.</returns>
    public async Task<IReadOnlyList<ProjectRiskMoveJournal>> ListPendingAsync(
        CancellationToken token = default)
    {
        IReadOnlyList<ProjectRiskMoveJournal> journals =
            await ListHistoryAsync(token);
        return journals.Where(entry => entry.Phase is not
            ("completed" or "rolled-back")).ToArray();
    }

    /// <summary>
    /// Rechecks the exact A intention, files and incoming links under a lock.
    /// Both approved snapshots and the prepared journal are durable before
    /// moving the original, to allow recovery after either file operation.
    /// </summary>
    /// <param name="reviewed">Displayed SHA-pinned A plan.</param>
    /// <param name="approved">Distinct human approval for real relocation.</param>
    /// <param name="token">Cancellation before snapshot publication.</param>
    /// <returns>Verified completed journal or recoverable interruption.</returns>
    public async Task<ProjectRiskMoveJournal> ExecuteAsync(
        ProjectRiskHandoverPreview reviewed, bool approved,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!approved || !reviewed.CanPreview ||
            reviewed.Intent.Method != ProjectHandoverMethod.MoveOriginal ||
            reviewed.MoveMarkdown is null ||
            reviewed.TargetId != reviewed.Intent.SourceObjectId)
        {
            throw new InvalidOperationException(
                "Le déplacement RIS A exige préflight et approbation humaine " +
                "distincte. Le mode B n'est pas traité par cet exécuteur.");
        }

        await using FileStream locked = AcquireLock(token);
        IReadOnlyList<ProjectRiskMoveJournal> history =
            await ListHistoryAsync(token);
        if (history.Any(entry => entry.Phase is not
            ("completed" or "rolled-back")))
        {
            throw new InvalidOperationException(
                "Une passation RIS A interrompue doit être récupérée d'abord.");
        }

        if (history.Any(entry => entry.IntentId == reviewed.Intent.Id))
        {
            throw new InvalidOperationException(
                "L'intention RIS A a déjà été consommée, même après rollback.");
        }

        string sourceProject = ProjectRoot(reviewed.SourceRelativePath);
        ProjectRiskHandoverPreview fresh = await _previews.PrepareAsync(
            sourceProject, reviewed.Intent.Id, token);
        if (!fresh.CanPreview || fresh.MoveMarkdown is null ||
            fresh.Intent != reviewed.Intent ||
            fresh.SourceRelativePath != reviewed.SourceRelativePath ||
            fresh.SourceSha256 != reviewed.SourceSha256 ||
            fresh.TargetRelativePath != reviewed.TargetRelativePath ||
            fresh.TargetReference != reviewed.TargetReference ||
            fresh.TargetId != reviewed.TargetId ||
            fresh.MoveMarkdown != reviewed.MoveMarkdown)
        {
            throw new InvalidDataException(
                "Risque, destinataire ou code RIS A modifié : nouvelle revue.");
        }

        string source = _sources.ResolveExistingFile(
            fresh.SourceRelativePath);
        string target = CombineRelative(fresh.TargetRelativePath!);
        if (Path.Exists(target))
        {
            throw new InvalidDataException(
                "Référence RIS A déjà occupée dans le destinataire.");
        }

        await _guard.AssertNoIncomingAsync(fresh.TargetId,
            fresh.Intent.SourceReference, fresh.SourceRelativePath,
            fresh.TargetRelativePath!, token);
        await _guard.AssertNoIncomingAsync(fresh.TargetId,
            fresh.TargetReference!, fresh.SourceRelativePath,
            fresh.TargetRelativePath!, token);
        (string original, string originalSha) =
            await ProjectHandoverTaskExecutionPlanner
                .ReadExactTaskDocumentAsync(source, token);
        if (originalSha != fresh.SourceSha256 ||
            ProjectRiskMoveDraft.Prepare(original,
                fresh.Intent.TargetProjectId, fresh.TargetReference!) !=
            fresh.MoveMarkdown)
        {
            throw new InvalidDataException(
                "Le texte RIS a changé depuis la revue humaine.");
        }

        Guid id = Guid.NewGuid();
        string directory = Path.Combine(_transactions, id.ToString("N"));
        Directory.CreateDirectory(directory);
        Plain(directory);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, Original), original, token);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, Proposed), fresh.MoveMarkdown, token);
        ProjectRiskMoveJournal journal = new()
        {
            TransactionId = id,
            IntentId = fresh.Intent.Id,
            SourceProjectId = fresh.Intent.SourceProjectId,
            TargetProjectId = fresh.Intent.TargetProjectId,
            RiskId = fresh.TargetId,
            SourceRelativePath = fresh.SourceRelativePath,
            TargetRelativePath = fresh.TargetRelativePath!,
            SourceReference = fresh.Intent.SourceReference,
            TargetReference = fresh.TargetReference!,
            OriginalSha256 = originalSha,
            DestinationSha256 = Hash(fresh.MoveMarkdown),
            Phase = "prepared"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, token);
        _checkpoint?.Invoke("prepared");
        return await PublishAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Human-approved replay of already signed RIS A journals only.</summary>
    /// <param name="approved">Required explicit recovery decision.</param>
    /// <param name="token">Cancellation before each operation.</param>
    /// <returns>Number of resolved pending transaction records.</returns>
    public async Task<int> RecoverPendingAsync(bool approved,
        CancellationToken token = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "Reprise RIS A interdite sans consentement humain.");
        }

        await using FileStream locked = AcquireLock(token);
        IReadOnlyList<(string Directory, ProjectRiskMoveJournal Journal)> all =
            await ReadJournalsAsync(token);
        int count = 0;
        foreach ((string directory, ProjectRiskMoveJournal journal) in all)
        {
            token.ThrowIfCancellationRequested();
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
    /// After human confirmation, restores the original RIS source and
    /// withdraws the exact published recipient into durable history.
    /// Any new incoming link or edit fails closed, never overwriting it.
    /// </summary>
    /// <param name="transactionId">Completed A journal to undo.</param>
    /// <param name="approved">Distinct human rollback approval.</param>
    /// <param name="token">Cancellation before recording rollback.</param>
    /// <returns>Durable restored journal.</returns>
    public async Task<ProjectRiskMoveJournal> RollbackAsync(
        Guid transactionId, bool approved, CancellationToken token = default)
    {
        if (!approved || transactionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Rollback RIS A sans journal/confirmation interdit.");
        }

        await using FileStream locked = AcquireLock(token);
        string directory = Path.Combine(_transactions,
            transactionId.ToString("N"));
        ProjectRiskMoveJournal journal = await ReadJournalAsync(directory, token);
        if (journal.TransactionId != transactionId ||
            journal.Phase != "completed")
        {
            throw new InvalidDataException(
                "Seul un risque A publié peut être restauré explicitement.");
        }

        await ResolveOwnersAsync(journal, token);
        string target = _sources.ResolveExistingFile(
            journal.TargetRelativePath);
        await VerifyHashAsync(target, journal.DestinationSha256, token);
        await _guard.AssertNoIncomingAsync(journal.RiskId,
            journal.TargetReference, journal.TargetRelativePath,
            journal.SourceRelativePath, token);
        journal = journal with { Phase = "rollback-prepared" };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, token);
        return await UndoAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Obtains a workspace-local exclusive RIS A lock.</summary>
    /// <param name="token">Cancellation before creating transaction directory.</param>
    /// <returns>Exclusive stream until disposed.</returns>
    private FileStream AcquireLock(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string imports = Path.GetDirectoryName(_transactions)!;
        Directory.CreateDirectory(imports);
        Plain(imports);
        Directory.CreateDirectory(_transactions);
        Plain(_transactions);
        return new FileStream(Path.Combine(_transactions, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    /// <summary>Returns a physical path confined by the workspace root.</summary>
    /// <param name="relative">Canonical workspace-relative path.</param>
    /// <returns>Physical destination, not necessarily an existing file.</returns>
    private string CombineRelative(string relative)
    {
        if (!ProjectJourneySourcePath.IsSafe(relative))
        {
            throw new InvalidDataException("Chemin RIS A non sûr.");
        }

        return Path.GetFullPath(Path.Combine(_root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Extracts the canonical owning project of a RIS file.</summary>
    /// <param name="relative">Workspace-relative RIS source path.</param>
    /// <returns>Active project directory to revalidate.</returns>
    private string ProjectRoot(string relative)
    {
        const string marker = "/Analyse/Risques/";
        if (!ProjectJourneySourcePath.IsSafe(relative) ||
            !relative.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fiche RIS A hors dossier canonique.");
        }

        return CombineRelative(relative[
            ..relative.IndexOf(marker, StringComparison.Ordinal)]);
    }

    /// <summary>Rejects transaction folders under symbolic links.</summary>
    /// <param name="directory">Existing folder.</param>
    private static void Plain(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier transactionnel RIS A lié, accès interdit.");
        }
    }

    /// <summary>Hashes canonical UTF-8 Markdown with SHA-256.</summary>
    /// <param name="markdown">Exact source text.</param>
    /// <returns>Uppercase hexadecimal digest.</returns>
    private static string Hash(string markdown) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));

    /// <summary>Verifies exact file content before any move or rollback.</summary>
    /// <param name="path">Existing document file.</param>
    /// <param name="expected">Signed original/proposed SHA-256.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Success or explicit refusal.</returns>
    private static async Task VerifyHashAsync(
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
                "RIS modifié depuis le snapshot approuvé.");
        }
    }
}
