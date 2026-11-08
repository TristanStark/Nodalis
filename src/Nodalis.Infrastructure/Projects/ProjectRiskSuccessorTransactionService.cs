using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable B succession of one canonical risk: new GUID, no inherited rating
/// or history. An immutable journal allows explicit replay or human rollback.
/// </summary>
public sealed record ProjectRiskSuccessorJournal
{
    /// <summary>Immutable local transaction identifier.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Existing human-signed B intention.</summary>
    public required Guid IntentId { get; init; }

    /// <summary>Original owner, never changed by the successor transaction.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Verified recipient project.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Original risk identifier, never reused.</summary>
    public required Guid SourceRiskId { get; init; }

    /// <summary>New, independently identified risk.</summary>
    public required Guid SuccessorRiskId { get; init; }

    /// <summary>Canonical source path at the human review.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Reviewed destination path inside Analyse/Risques.</summary>
    public required string TargetRelativePath { get; init; }

    /// <summary>Reviewed local RIS-xxxx code.</summary>
    public required string TargetReference { get; init; }

    /// <summary>Exact SHA of the original RIS Markdown.</summary>
    public required string SourceSha256 { get; init; }

    /// <summary>Exact SHA of the fresh successor Markdown.</summary>
    public required string SuccessorSha256 { get; init; }

    /// <summary>prepared, completed, rollback-prepared, or rolled-back.</summary>
    public required string Phase { get; init; }

    /// <summary>Recorded time, never a risk assessment.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Explicit and bounded RIS mode-B transaction. Publication is CreateNew;
/// recovery recognizes exact bytes, and rollback archives the exact successor.
/// A mode-A move remains unsupported and can never use this executor.
/// </summary>
public sealed class ProjectRiskSuccessorTransactionService
{
    private const string Folder = ".ris-successor-transactions";
    private const string JournalName = "journal.json";
    private const string StagedName = "successor.md";
    private const string WithdrawnName = "withdrawn-successor.md";
    private const int MaxBytes = 1024 * 1024;
    private readonly string _root;
    private readonly string _transactions;
    private readonly ProjectRiskHandoverPreviewService _previews;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectJourneySourceService _sources;
    private readonly Action<string>? _checkpoint;

    /// <summary>Creates a single-workspace and optional failure-hook executor.</summary>
    /// <param name="workspaceRoot">Existing workspace root.</param>
    /// <param name="checkpoint">Test-only deterministic fault injection.</param>
    public ProjectRiskSuccessorTransactionService(
        string workspaceRoot, Action<string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _transactions = Path.Combine(_root, WorkspaceLayout.ImportsDirectoryName,
            Folder);
        _previews = new ProjectRiskHandoverPreviewService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _sources = new ProjectJourneySourceService(_root);
        _checkpoint = checkpoint;
    }

    /// <summary>Lists previous approved journals without creating directories.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Newest first, including rolled back and incomplete.</returns>
    public async Task<IReadOnlyList<ProjectRiskSuccessorJournal>>
        ListHistoryAsync(CancellationToken token = default)
    {
        IReadOnlyList<(string Directory, ProjectRiskSuccessorJournal Journal)>
            entries = await ReadJournalsAsync(token);
        return entries.Select(entry => entry.Journal)
            .OrderByDescending(entry => entry.CreatedUtc).ToArray();
    }

    /// <summary>Finds unfinished explicit B operations.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Pending journals only.</returns>
    public async Task<IReadOnlyList<ProjectRiskSuccessorJournal>>
        ListPendingAsync(CancellationToken token = default)
    {
        IReadOnlyList<ProjectRiskSuccessorJournal> history =
            await ListHistoryAsync(token);
        return history.Where(item => item.Phase is not
            ("completed" or "rolled-back")).ToArray();
    }

    /// <summary>
    /// Obtains a fresh preview under an exclusive lock, snapshots the new
    /// Markdown in the journal and then publishes it without overwriting.
    /// </summary>
    /// <param name="reviewed">Previously displayed SHA-pinned B preview.</param>
    /// <param name="approved">Separate human approval of real creation.</param>
    /// <param name="token">Cancellation until journaling.</param>
    /// <returns>Durable completed journal; incomplete steps are recoverable.</returns>
    public async Task<ProjectRiskSuccessorJournal> ExecuteAsync(
        ProjectRiskHandoverPreview reviewed, bool approved,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!approved || !reviewed.CanPreview ||
            reviewed.Intent.Method != ProjectHandoverMethod.LinkedSuccessor ||
            reviewed.SuccessorMarkdown is null ||
            reviewed.TargetId == reviewed.Intent.SourceObjectId)
        {
            throw new InvalidOperationException(
                "La création RIS B exige un aperçu sûr et une deuxième " +
                "confirmation humaine. Le mode A n'est pas exécutable ici.");
        }

        await using FileStream operationLock = AcquireLock(token);
        IReadOnlyList<ProjectRiskSuccessorJournal> history =
            await ListHistoryAsync(token);
        if (history.Any(j => j.Phase is not ("completed" or "rolled-back")))
        {
            throw new InvalidOperationException(
                "Une transaction RIS doit être reprise avant la suivante.");
        }

        if (history.Any(j => j.IntentId == reviewed.Intent.Id))
        {
            throw new InvalidOperationException(
                "Cette intention RIS a déjà été exécutée ou restaurée.");
        }

        string sourceProject = ProjectDirectory(reviewed.SourceRelativePath);
        ProjectRiskHandoverPreview fresh = await _previews.PrepareAsync(
            sourceProject, reviewed.Intent.Id, token);
        if (!fresh.CanPreview || fresh.SuccessorMarkdown is null ||
            fresh.Intent != reviewed.Intent ||
            fresh.SourceSha256 != reviewed.SourceSha256 ||
            fresh.SourceRelativePath != reviewed.SourceRelativePath ||
            fresh.TargetReference != reviewed.TargetReference ||
            fresh.TargetRelativePath != reviewed.TargetRelativePath ||
            fresh.TargetId != reviewed.TargetId ||
            fresh.SuccessorMarkdown != reviewed.SuccessorMarkdown)
        {
            throw new InvalidDataException(
                "Le risque, le projet destinataire ou son code libre a changé : " +
                "renouveler la revue et la confirmation.");
        }

        string target = CombineRelative(fresh.TargetRelativePath!);
        if (Path.Exists(target))
        {
            throw new InvalidDataException("Une fiche RIS cible existe déjà.");
        }

        Guid transactionId = Guid.NewGuid();
        string directory = Path.Combine(_transactions,
            transactionId.ToString("N"));
        Directory.CreateDirectory(directory);
        RequirePlain(directory);
        string staged = Path.Combine(directory, StagedName);
        await AtomicFileWriter.WriteNewAllTextAsync(
            staged, fresh.SuccessorMarkdown, token);
        ProjectRiskSuccessorJournal journal = new()
        {
            TransactionId = transactionId,
            IntentId = fresh.Intent.Id,
            SourceProjectId = fresh.Intent.SourceProjectId,
            TargetProjectId = fresh.Intent.TargetProjectId,
            SourceRiskId = fresh.Intent.SourceObjectId,
            SuccessorRiskId = fresh.TargetId,
            SourceRelativePath = fresh.SourceRelativePath,
            TargetRelativePath = fresh.TargetRelativePath!,
            TargetReference = fresh.TargetReference!,
            SourceSha256 = fresh.SourceSha256,
            SuccessorSha256 = Hash(fresh.SuccessorMarkdown),
            Phase = "prepared"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, token);
        _checkpoint?.Invoke("prepared");
        return await PublishAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Explicit replay of previously human-approved journals only.</summary>
    /// <param name="approved">Fresh human decision.</param>
    /// <param name="token">Cancellation before each replay.</param>
    /// <returns>Number of completed recoveries.</returns>
    public async Task<int> RecoverPendingAsync(
        bool approved, CancellationToken token = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "Reprise RIS impossible sans confirmation humaine.");
        }

        await using FileStream operationLock = AcquireLock(token);
        IReadOnlyList<(string Directory, ProjectRiskSuccessorJournal Journal)>
            entries = await ReadJournalsAsync(token);
        int count = 0;
        foreach ((string directory, ProjectRiskSuccessorJournal journal)
            in entries)
        {
            token.ThrowIfCancellationRequested();
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
    /// Moves an unmodified and unreferenced B successor into the durable
    /// private journal. No original risk or human history is overwritten.
    /// </summary>
    /// <param name="transactionId">Completed B transaction.</param>
    /// <param name="approved">Second explicit human rollback approval.</param>
    /// <param name="token">Cancellation before preparation.</param>
    /// <returns>Durable rolled-back event.</returns>
    public async Task<ProjectRiskSuccessorJournal> RollbackAsync(
        Guid transactionId, bool approved, CancellationToken token = default)
    {
        if (!approved || transactionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Rollback RIS exige une transaction et une confirmation.");
        }

        await using FileStream operationLock = AcquireLock(token);
        string directory = Path.Combine(_transactions,
            transactionId.ToString("N"));
        ProjectRiskSuccessorJournal journal =
            await ReadJournalAsync(directory, token);
        if (journal.TransactionId != transactionId ||
            journal.Phase != "completed")
        {
            throw new InvalidOperationException(
                "Seul un successeur RIS B publié peut être restauré.");
        }

        // Verify both active project owners BEFORE transitioning the journal.
        // An archived/relocated target must never leave an orphaned
        // rollback-prepared event when no withdrawal was possible.
        await ResolveOwnersAsync(journal, token);
        string target = _sources.ResolveExistingFile(
            journal.TargetRelativePath);
        await RequireHashAsync(target, journal.SuccessorSha256, token);
        await AssertNoReferencesAsync(journal, token);
        journal = journal with { Phase = "rollback-prepared" };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), journal, token);
        return await WithdrawAsync(directory, journal, CancellationToken.None);
    }

    /// <summary>Publishes one journaled B file or recognizes an exact replay.</summary>
    /// <param name="directory">Existing bounded private transaction path.</param>
    /// <param name="journal">Immutable approved transaction.</param>
    /// <param name="token">Cancellation before publication.</param>
    /// <returns>Completed journal.</returns>
    private async Task<ProjectRiskSuccessorJournal> PublishAsync(
        string directory, ProjectRiskSuccessorJournal journal,
        CancellationToken token)
    {
        Validate(journal);
        RequirePlain(directory);
        if (journal.Phase != "prepared" ||
            await ReadJournalAsync(directory, token) != journal)
        {
            throw new InvalidDataException(
                "Journal RIS modifié ou phase incompatible avec la publication.");
        }

        string staged = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(Path.Combine(
                directory, StagedName)));
        await RequireHashAsync(staged, journal.SuccessorSha256, token);
        ProjectRiskCard draft = ProjectRiskMarkdownCodec.Parse(
            await File.ReadAllTextAsync(staged, token));
        if (draft.Id != journal.SuccessorRiskId ||
            draft.ProjectId != journal.TargetProjectId ||
            draft.Reference != journal.TargetReference ||
            draft.Qualification != ProjectRiskQualification.ToReview ||
            draft.Lifecycle != ProjectRiskLifecycle.Open ||
            draft.Initial != new ProjectRiskRating(null, null) ||
            draft.Residual != new ProjectRiskRating(null, null) ||
            draft.TreatmentHeadId is not null)
        {
            throw new InvalidDataException(
                "La fiche RIS préparée contient une décision héritée ou " +
                "une identité différente du journal.");
        }

        (string source, string target, string destination) =
            await ResolveOwnersAsync(journal, token);
        string original = _sources.ResolveExistingFile(
            journal.SourceRelativePath);
        await RequireHashAsync(original, journal.SourceSha256, token);

        if (Path.Exists(destination))
        {
            await RequireHashAsync(
                _sources.ResolveExistingFile(journal.TargetRelativePath),
                journal.SuccessorSha256, token);
        }
        else
        {
            ProjectRiskCatalog catalog = await new ProjectRiskRegistryService(
                _root).ListAsync(target, token);
            if (catalog.Diagnostics.Count > 0 ||
                catalog.NextReference != journal.TargetReference ||
                catalog.Risks.Any(item =>
                    item.Card.Id == journal.SuccessorRiskId))
            {
                throw new InvalidDataException(
                    "Catalogue RIS cible modifié : publication interdite.");
            }

            string analysis = Path.Combine(target, "Analyse");
            string risks = Path.Combine(analysis, "Risques");
            if (Path.Exists(analysis) && !Directory.Exists(analysis) ||
                Path.Exists(risks) && !Directory.Exists(risks))
            {
                throw new InvalidDataException(
                    "Dossier RIS destinataire occupé par un fichier.");
            }

            Directory.CreateDirectory(analysis);
            RequirePlain(analysis);
            Directory.CreateDirectory(risks);
            RequirePlain(risks);
            token.ThrowIfCancellationRequested();
            await AtomicFileWriter.WriteNewAllTextAsync(
                destination, await File.ReadAllTextAsync(staged, token), token);
            _checkpoint?.Invoke("target-created");
            await RequireHashAsync(
                _sources.ResolveExistingFile(journal.TargetRelativePath),
                journal.SuccessorSha256, CancellationToken.None);
        }

        ProjectRiskSuccessorJournal completed = journal with
        {
            Phase = "completed"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), completed,
            CancellationToken.None);
        _checkpoint?.Invoke("completed");
        return completed;
    }

    /// <summary>Withdraws a rollback-approved exact file, preserving bytes.</summary>
    /// <param name="directory">Existing transaction path.</param>
    /// <param name="journal">Immutable rollback-prepared journal.</param>
    /// <param name="token">Cancellation before moving the target.</param>
    /// <returns>Rolled-back durable journal.</returns>
    private async Task<ProjectRiskSuccessorJournal> WithdrawAsync(
        string directory, ProjectRiskSuccessorJournal journal,
        CancellationToken token)
    {
        Validate(journal);
        RequirePlain(directory);
        if (journal.Phase != "rollback-prepared" ||
            await ReadJournalAsync(directory, token) != journal)
        {
            throw new InvalidDataException(
                "Rollback RIS non préparé ou journal modifié.");
        }

        (string source, string target, string destination) =
            await ResolveOwnersAsync(journal, token);
        string withdrawn = Path.Combine(directory, WithdrawnName);
        if (Path.Exists(withdrawn))
        {
            if (Path.Exists(destination))
            {
                throw new InvalidDataException(
                    "RIS retiré mais référence cible à nouveau occupée.");
            }

            await RequireHashAsync(
                _sources.ResolveExistingFile(
                    _sources.GetReferenceForExistingFile(withdrawn)),
                journal.SuccessorSha256, token);
        }
        else
        {
            string current = _sources.ResolveExistingFile(
                journal.TargetRelativePath);
            await RequireHashAsync(current, journal.SuccessorSha256, token);
            await AssertNoReferencesAsync(journal, token);
            token.ThrowIfCancellationRequested();
            File.Move(current, withdrawn);
            _checkpoint?.Invoke("target-withdrawn");
            try
            {
                await RequireHashAsync(withdrawn, journal.SuccessorSha256,
                    CancellationToken.None);
            }
            catch
            {
                if (!Path.Exists(destination))
                {
                    File.Move(withdrawn, destination);
                }

                throw;
            }
        }

        ProjectRiskSuccessorJournal rolledBack = journal with
        {
            Phase = "rolled-back"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), rolledBack,
            CancellationToken.None);
        _checkpoint?.Invoke("rolled-back");
        return rolledBack;
    }

    /// <summary>Rejects links to the new RIS GUID or local reference.</summary>
    /// <param name="journal">Approved B file and source identities.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Completed bounded audit or explicit refusal.</returns>
    private async Task AssertNoReferencesAsync(
        ProjectRiskSuccessorJournal journal, CancellationToken token)
    {
        string target = _sources.ResolveExistingFile(
            journal.TargetRelativePath);
        IReadOnlyList<string> backlinks =
            await new ProjectHandoverTaskExecutionPlanner(_root)
                .FindOtherReferencesAsync(
                    journal.SuccessorRiskId, target, target, token);
        if (backlinks.Count > 0)
        {
            throw new InvalidDataException(
                "RIS B référencé par d'autres documents : rollback refusé.");
        }

        string owner = ProjectDirectory(journal.TargetRelativePath);
        Stack<string> folders = new();
        folders.Push(owner);
        int checkedFiles = 0;
        long checkedBytes = 0;
        while (folders.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string directory = folders.Pop();
            RequirePlain(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(
                directory))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Lien symbolique RIS : références non vérifiables.");
                }

                if ((attrs & FileAttributes.Directory) != 0)
                {
                    if (!File.Exists(Path.Combine(path,
                        WorkspaceLayout.ProjectManifestFileName)))
                    {
                        folders.Push(path);
                    }

                    continue;
                }

                if ((!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                     !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                checkedFiles++;
                long length = new FileInfo(path).Length;
                checkedBytes += length;
                if (checkedFiles > 4000 || checkedBytes > 64L * 1024 * 1024 ||
                    length > 2L * 1024 * 1024)
                {
                    throw new InvalidDataException(
                        "Inventaire des références RIS trop volumineux.");
                }

                string absolute = _sources.ResolveExistingFile(
                    _sources.GetReferenceForExistingFile(path));
                string body = new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(absolute, token));
                if (body.Contains(journal.TargetReference,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Référence RIS du successeur encore utilisée : " +
                        "requalification manuelle requise avant rollback.");
                }
            }
        }
    }

    /// <summary>Rechecks two active owners and canonical RIS paths.</summary>
    /// <param name="journal">Persisted read-only transaction metadata.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Verified physical roots and destination.</returns>
    private async Task<(string Source, string Target, string Destination)>
        ResolveOwnersAsync(ProjectRiskSuccessorJournal journal,
            CancellationToken token)
    {
        string sourceDirectory = ProjectDirectory(journal.SourceRelativePath);
        string targetDirectory = ProjectDirectory(journal.TargetRelativePath);
        (string source, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest sourceOwner) =
            await _preflight.ResolveActiveProjectAsync(sourceDirectory, token);
        (string target, string targetRelative,
            Nodalis.Core.Domain.ProjectManifest targetOwner) =
            await _preflight.ResolveActiveProjectAsync(targetDirectory, token);
        if (sourceOwner.Id != journal.SourceProjectId ||
            targetOwner.Id != journal.TargetProjectId ||
            sourceRelative == targetRelative ||
            journal.TargetRelativePath != targetRelative +
                "/Analyse/Risques/" + journal.TargetReference + ".md")
        {
            throw new InvalidDataException(
                "Propriétaire ou emplacement RIS différent du journal.");
        }

        return (source, target, CombineRelative(journal.TargetRelativePath));
    }

    /// <summary>Derives the exact owning project from a canonical RIS path.</summary>
    /// <param name="relative">Workspace-relative path with RIS folder.</param>
    /// <returns>Physical project directory.</returns>
    private string ProjectDirectory(string relative)
    {
        const string marker = "/Analyse/Risques/";
        if (!ProjectJourneySourcePath.IsSafe(relative) ||
            !relative.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fiche RIS hors chemin canonique.");
        }

        return CombineRelative(relative[
            ..relative.IndexOf(marker, StringComparison.Ordinal)]);
    }

    /// <summary>Ensures a computed path is workspace-relative and confined.</summary>
    /// <param name="relative">Validated portable reference.</param>
    /// <returns>Full physical location.</returns>
    private string CombineRelative(string relative)
    {
        if (!ProjectJourneySourcePath.IsSafe(relative))
        {
            throw new InvalidDataException("Chemin RIS non sûr.");
        }

        return Path.GetFullPath(Path.Combine(_root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Reads bounded and identity-checked transaction folders.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Existing journals in stable order.</returns>
    private async Task<IReadOnlyList<(string Directory,
        ProjectRiskSuccessorJournal Journal)>> ReadJournalsAsync(
        CancellationToken token)
    {
        if (!Directory.Exists(_transactions))
        {
            return [];
        }

        RequirePlain(_transactions);
        string[] directories = Directory.GetDirectories(_transactions);
        if (directories.Length > 2000)
        {
            throw new InvalidDataException(
                "Trop de journaux RIS pour une inspection sûre.");
        }

        List<(string Directory, ProjectRiskSuccessorJournal Journal)> all = [];
        foreach (string directory in directories.OrderBy(value => value,
            StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            RequirePlain(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N",
                out Guid id))
            {
                throw new InvalidDataException(
                    "Dossier de transaction RIS non identifié.");
            }

            if (!File.Exists(Path.Combine(directory, JournalName)))
            {
                continue;
            }

            ProjectRiskSuccessorJournal journal =
                await ReadJournalAsync(directory, token);
            if (journal.TransactionId != id)
            {
                throw new InvalidDataException(
                    "L'identité du journal RIS diffère du dossier.");
            }

            all.Add((directory, journal));
        }

        return all;
    }

    /// <summary>Reads one strictly validated bounded journal.</summary>
    /// <param name="directory">Existing transaction folder.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Typed and validated transaction.</returns>
    private async Task<ProjectRiskSuccessorJournal> ReadJournalAsync(
        string directory, CancellationToken token)
    {
        RequirePlain(directory);
        string full = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, JournalName)));
        if (new FileInfo(full).Length > 16384)
        {
            throw new InvalidDataException("Journal RIS trop volumineux.");
        }

        ProjectRiskSuccessorJournal journal =
            await AtomicJsonFile.ReadAsync<ProjectRiskSuccessorJournal>(
                full, token);
        Validate(journal);
        return journal;
    }

    /// <summary>Rejects forged, inconsistent or unknown journal metadata.</summary>
    /// <param name="journal">Untrusted persisted transaction.</param>
    private static void Validate(ProjectRiskSuccessorJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.TransactionId == Guid.Empty ||
            journal.IntentId == Guid.Empty ||
            journal.SourceRiskId == Guid.Empty ||
            journal.SuccessorRiskId == Guid.Empty ||
            journal.SourceRiskId == journal.SuccessorRiskId ||
            journal.SourceProjectId == Guid.Empty ||
            journal.TargetProjectId == Guid.Empty ||
            journal.SourceProjectId == journal.TargetProjectId ||
            !ProjectRiskMarkdownCodec.IsReference(journal.TargetReference) ||
            !ProjectJourneySourcePath.IsSafe(journal.SourceRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(journal.TargetRelativePath) ||
            !journal.SourceRelativePath.Contains("/Analyse/Risques/",
                StringComparison.Ordinal) ||
            !journal.SourceRelativePath.EndsWith(".md",
                StringComparison.Ordinal) ||
            !journal.TargetRelativePath.EndsWith("/Analyse/Risques/" +
                journal.TargetReference + ".md", StringComparison.Ordinal) ||
            !ValidHash(journal.SourceSha256) ||
            !ValidHash(journal.SuccessorSha256) ||
            journal.CreatedUtc == default ||
            journal.Phase is not ("prepared" or "completed" or
                "rollback-prepared" or "rolled-back"))
        {
            throw new InvalidDataException(
                "Journal de succession RIS invalide.");
        }
    }

    /// <summary>Opens a workspace-scoped exclusive handover lock.</summary>
    /// <param name="token">Cancellation before creating journal folder.</param>
    /// <returns>Locked file stream for the operation lifetime.</returns>
    private FileStream AcquireLock(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string imports = Path.GetDirectoryName(_transactions)!;
        Directory.CreateDirectory(imports);
        RequirePlain(imports);
        Directory.CreateDirectory(_transactions);
        RequirePlain(_transactions);
        return new FileStream(Path.Combine(_transactions, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    /// <summary>Rejects linked/junction transaction folders.</summary>
    /// <param name="directory">Existing directory.</param>
    private static void RequirePlain(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier RIS sous lien symbolique ou jonction.");
        }
    }

    /// <summary>Computes canonical UTF-8 SHA-256.</summary>
    /// <param name="markdown">Exact proposed Markdown string.</param>
    /// <returns>Uppercase digest.</returns>
    private static string Hash(string markdown) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));

    /// <summary>Reads and verifies original immutable Markdown bytes.</summary>
    /// <param name="path">Verified existing file.</param>
    /// <param name="expected">Previously approved SHA-256.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Success or explicit rejection.</returns>
    private static async Task RequireHashAsync(
        string path, string expected, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open,
            FileAccess.Read, FileShare.Read | FileShare.Delete, 65536,
            useAsync: true);
        if (stream.Length > MaxBytes ||
            !string.Equals(Convert.ToHexString(
                await SHA256.HashDataAsync(stream, token)), expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fiche RIS modifiée depuis le snapshot signé.");
        }
    }

    /// <summary>Checks canonical SHA-256 metadata before replay.</summary>
    /// <param name="hash">Untrusted persistent digest.</param>
    /// <returns>True only for exactly 64 hex characters.</returns>
    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
