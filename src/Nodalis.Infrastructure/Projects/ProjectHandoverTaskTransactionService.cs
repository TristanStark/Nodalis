using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Explicit, recoverable two-file task transfer. Only standalone Tâches.md
/// checkboxes are supported. Each journal retains both original and proposed
/// snapshots, even after success, for human rollback.
/// </summary>
public sealed class ProjectHandoverTaskTransactionService
{
    private const string FolderName = ".handover-task-transactions";
    private const string JournalName = "journal.json";
    private const string OriginalSource = "original-source.md";
    private const string OriginalTarget = "original-target.md";
    private const string ProposedSource = "proposed-source.md";
    private const string ProposedTarget = "proposed-target.md";
    private readonly string _root;
    private readonly string _transactions;
    private readonly ProjectHandoverTaskExecutionPlanner _planner;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectJourneySourceService _sources;
    private readonly Action<string>? _checkpoint;

    /// <summary>Constructs a workspace-scoped and checkpointed executor.</summary>
    /// <param name="workspaceRoot">Single workspace boundary.</param>
    /// <param name="checkpoint">Optional deterministic fault-injection hook.</param>
    public ProjectHandoverTaskTransactionService(
        string workspaceRoot, Action<string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _transactions = Path.Combine(_root, WorkspaceLayout.ImportsDirectoryName,
            FolderName);
        _planner = new ProjectHandoverTaskExecutionPlanner(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _sources = new ProjectJourneySourceService(_root);
        _checkpoint = checkpoint;
    }

    /// <summary>Returns durable incomplete journals without taking a write lock.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Transactions awaiting explicit recovery or rollback.</returns>
    public async Task<IReadOnlyList<ProjectHandoverTaskJournal>> ListPendingAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(string Directory, ProjectHandoverTaskJournal Journal)> all =
            await ReadJournalsAsync(cancellationToken);
        return all.Select(item => item.Journal)
            .Where(item => item.Phase is not ("completed" or "rolled-back"))
            .OrderBy(item => item.CreatedUtc).ToArray();
    }

    /// <summary>
    /// Revalidates an approved dry-run inside an exclusive operation lock,
    /// durably snapshots both files and publishes them in an order which
    /// never creates duplicate active GUID ownership for strategy A.
    /// </summary>
    /// <param name="reviewed">Previously displayed execution plan.</param>
    /// <param name="approved">Distinct human approval for actual mutation.</param>
    /// <param name="cancellationToken">Cancellation before staging.</param>
    /// <returns>Durable completed or recoverable transaction identity.</returns>
    public async Task<ProjectHandoverTaskJournal> ExecuteAsync(
        ProjectHandoverTaskExecutionPlan reviewed, bool approved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!approved || !reviewed.CanExecute)
        {
            throw new InvalidOperationException(
                "Un transfert réel requiert un plan sans blocage et " +
                "une confirmation humaine distincte.");
        }

        await using FileStream operationLock =
            await AcquireLockAsync(cancellationToken);
        if ((await ListPendingAsync(cancellationToken)).Count > 0)
        {
            throw new InvalidOperationException(
                "Une passation précédente est interrompue : " +
                "récupérez-la avant un nouveau transfert.");
        }

        string sourceProject = ResolveRelative(
            reviewed.Intent.SourceProjectId,
            reviewed.Intent.SourceRelativePath, parentDocument: true);
        ProjectHandoverTaskExecutionPlan current =
            await _planner.PrepareAsync(sourceProject, reviewed.Intent.Id,
                cancellationToken);
        if (!current.CanExecute ||
            current.Intent.Id != reviewed.Intent.Id ||
            current.SourceHash != reviewed.SourceHash ||
            current.TargetHash != reviewed.TargetHash ||
            current.ProposedSource != reviewed.ProposedSource ||
            current.ProposedTarget != reviewed.ProposedTarget ||
            current.Intent.ProposedTargetObjectId !=
                reviewed.Intent.ProposedTargetObjectId)
        {
            throw new InvalidDataException(
                "Source, destinataire, liens ou plan modifiés depuis la revue : " +
                "générez un nouveau dry-run.");
        }

        Guid transactionId = Guid.NewGuid();
        string directory = Path.Combine(_transactions,
            transactionId.ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = _sources.ResolveExistingFile(
            current.SourceDocumentRelativePath);
        string targetPath = _sources.ResolveExistingFile(
            current.TargetDocumentRelativePath);
        (string sourceText, string sourceHash) =
            await ProjectHandoverTaskExecutionPlanner.ReadExactTaskDocumentAsync(
                sourcePath, cancellationToken);
        (string targetText, string targetHash) =
            await ProjectHandoverTaskExecutionPlanner.ReadExactTaskDocumentAsync(
                targetPath, cancellationToken);
        if (sourceHash != current.SourceHash || targetHash != current.TargetHash)
        {
            throw new InvalidDataException(
                "Document de tâches changé pendant la préparation de sauvegarde.");
        }

        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, OriginalSource), sourceText,
            cancellationToken);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, OriginalTarget), targetText,
            cancellationToken);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, ProposedSource), current.ProposedSource,
            cancellationToken);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, ProposedTarget), current.ProposedTarget,
            cancellationToken);
        ProjectHandoverTaskJournal journal = new()
        {
            TransactionId = transactionId,
            IntentId = current.Intent.Id,
            SourceProjectId = current.Intent.SourceProjectId,
            TargetProjectId = current.Intent.TargetProjectId,
            TaskId = current.Intent.SourceObjectId,
            TargetTaskId = current.Intent.ProposedTargetObjectId,
            Method = current.Intent.Method,
            SourceProjectRelativePath = GetProjectRelative(
                current.Intent.SourceRelativePath),
            TargetProjectRelativePath = current.Intent.TargetProjectRelativePath,
            SourceDocumentRelativePath = current.SourceDocumentRelativePath,
            TargetDocumentRelativePath = current.TargetDocumentRelativePath,
            OriginalSourceHash = current.SourceHash,
            OriginalTargetHash = current.TargetHash,
            PublishedSourceHash = Hash(current.ProposedSource),
            PublishedTargetHash = Hash(current.ProposedTarget),
            Phase = "prepared"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, cancellationToken);
        _checkpoint?.Invoke("prepared");

        // An exception after this checkpoint leaves a durable, inspectable
        // journal. No catch block can silently overwrite an external edit.
        return await ReplayAsync(directory, journal, publish: true,
            CancellationToken.None);
    }

    /// <summary>
    /// Completes interrupted publications, or completes an explicitly begun
    /// rollback. Unknown hashes and missing owners remain blocked.
    /// </summary>
    /// <param name="approved">Human approval after inspecting pending journals.</param>
    /// <param name="cancellationToken">Cancellation before each recovery step.</param>
    /// <returns>Count of safely reconciled transaction journals.</returns>
    public async Task<int> RecoverPendingAsync(bool approved,
        CancellationToken cancellationToken = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "Reprise de passation non autorisée sans accord humain.");
        }

        await using FileStream operationLock =
            await AcquireLockAsync(cancellationToken);
        int recovered = 0;
        foreach ((string directory, ProjectHandoverTaskJournal journal) in
            await ReadJournalsAsync(cancellationToken))
        {
            if (journal.Phase is "completed" or "rolled-back")
            {
                continue;
            }

            await ReplayAsync(directory, journal,
                publish: journal.Phase != "rollback-prepared",
                cancellationToken);
            recovered++;
        }

        return recovered;
    }

    /// <summary>
    /// Reverts a completed or interrupted transaction using exact retained
    /// snapshots, in reverse publication order and only after human approval.
    /// </summary>
    /// <param name="transactionId">Exact verified transaction.</param>
    /// <param name="approved">Separate explicit rollback confirmation.</param>
    /// <param name="cancellationToken">Cancellation before rollback begins.</param>
    /// <returns>Verified restored journal.</returns>
    public async Task<ProjectHandoverTaskJournal> RollbackAsync(
        Guid transactionId, bool approved,
        CancellationToken cancellationToken = default)
    {
        if (!approved || transactionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Une restauration exige une transaction et une confirmation.");
        }

        await using FileStream operationLock =
            await AcquireLockAsync(cancellationToken);
        string directory = Path.Combine(_transactions,
            transactionId.ToString("N"));
        ProjectHandoverTaskJournal journal =
            await ReadJournalAsync(directory, cancellationToken);
        if (journal.TransactionId != transactionId ||
            journal.Phase == "rolled-back")
        {
            throw new InvalidOperationException(
                "Cette passation est absente ou déjà restaurée.");
        }

        if (journal.Phase != "rollback-prepared")
        {
            journal = journal with { Phase = "rollback-prepared" };
            await AtomicJsonFile.WriteAsync(
                Path.Combine(directory, JournalName), journal,
                cancellationToken);
        }

        return await ReplayAsync(directory, journal, publish: false,
            CancellationToken.None);
    }

    /// <summary>Exclusive lock with a durable workspace-owned parent.</summary>
    /// <param name="cancellationToken">Abort before lock acquisition.</param>
    /// <returns>Held stream that releases the lock on disposal.</returns>
    private Task<FileStream> AcquireLockAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string imports = Path.GetDirectoryName(_transactions)!;
        Directory.CreateDirectory(imports);
        EnsurePlain(imports);
        Directory.CreateDirectory(_transactions);
        EnsurePlain(_transactions);
        FileStream operationLock = new(
            Path.Combine(_transactions, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        return Task.FromResult(operationLock);
    }

    /// <summary>Rejects linked directories, including junctions.</summary>
    /// <param name="path">Existing directory.</param>
    private static void EnsurePlain(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier transactionnel sous lien symbolique interdit.");
        }
    }

    /// <summary>Computes the expected SHA-256 of UTF-8 canonical text.</summary>
    /// <param name="value">Exact Markdown to publish.</param>
    /// <returns>Uppercase SHA-256 hex.</returns>
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Extracts only the canonical project path from Tâches.md.</summary>
    /// <param name="relativeTaskDocument">Workspace-relative task file.</param>
    /// <returns>Relative original project path.</returns>
    private static string GetProjectRelative(string relativeTaskDocument)
    {
        if (!relativeTaskDocument.EndsWith("/Tâches.md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le transfert n'accepte que les fichiers Tâches.md.");
        }

        return relativeTaskDocument[..^"/Tâches.md".Length];
    }

    /// <summary>
    /// Resolves a path from a reviewed source identity; the GUID is
    /// independently reverified during preflight and replay.
    /// </summary>
    /// <param name="projectId">Expected owner.</param>
    /// <param name="relative">Canonical source doc or directory.</param>
    /// <param name="parentDocument">Whether relative identifies Tâches.md.</param>
    /// <returns>Workspace-confined project physical path.</returns>
    private string ResolveRelative(Guid projectId, string relative,
        bool parentDocument)
    {
        if (projectId == Guid.Empty ||
            !ProjectJourneySourcePath.IsSafe(relative))
        {
            throw new InvalidDataException("Propriétaire de passation invalide.");
        }

        string project = parentDocument ? GetProjectRelative(relative) : relative;
        return Path.GetFullPath(Path.Combine(_root,
            project.Replace('/', Path.DirectorySeparatorChar)));
    }
}
