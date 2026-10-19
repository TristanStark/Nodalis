using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Explicitly approved local transaction for existing Markdown destinations.
/// Every write has a durable byte-for-byte backup and an interruption journal.
/// The normal DOCX preview does not call this service until #164 is qualified.
/// </summary>
public sealed class DocxReimportTransactionService
{
    private const string DirectoryName = ".nodalis-reimport-transactions";
    private const string JournalName = "journal.json";
    private const int MaxFileBytes = 16 * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly DocxImportManifestStore _paths;
    private readonly Action<int>? _afterWriteCheckpoint;

    /// <summary>
    /// Initializes the transaction engine. The optional checkpoint is a test
    /// hook for simulating disk-write failures after a committed file.
    /// </summary>
    public DocxReimportTransactionService(string workspaceRoot,
        Action<int>? afterWriteCheckpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _paths = new DocxImportManifestStore(_workspaceRoot);
        _afterWriteCheckpoint = afterWriteCheckpoint;
    }

    /// <summary>
    /// Computes an approval fingerprint over the COMPLETE dry-run, including
    /// decisions, destinations, projected text and source checksums.
    /// </summary>
    public static string Fingerprint(DocxReimportDecisionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string serialized = JsonSerializer.Serialize(plan, JsonDefaults.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Commits existing Markdown only after explicit approval, project identity
    /// verification and byte backups. Writes are atomic per file; if any step
    /// fails, all destinations are restored or retained in a recovery journal.
    /// No new Word sections, destinations or DOCX copies are created.
    /// </summary>
    public async Task<DocxReimportTransactionResult> CommitAsync(
        string projectDirectory,
        DocxReimportDecisionPlan plan,
        Guid operationId,
        string approvedFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (operationId == Guid.Empty ||
            !string.Equals(approvedFingerprint, Fingerprint(plan),
                StringComparison.OrdinalIgnoreCase) ||
            !plan.ReviewComplete || plan.Lines.Count == 0 ||
            plan.Writes.Count == 0 ||
            plan.Lines.Any(line => !line.Resolved) ||
            plan.Warnings.Count != 0 ||
            plan.UnmatchedNewSections.Count != 0 ||
            plan.AmbiguousNewSections.Count != 0)
        {
            throw new InvalidDataException(
                "Réimport non approuvé, incomplet ou sans modification.");
        }

        if (plan.Lines.Select(line => line.FragmentId).Distinct().Count() !=
                plan.Lines.Count ||
            plan.Writes.Select(write => write.FragmentId).Distinct().Count() !=
                plan.Writes.Count ||
            plan.Writes.Any(write => !plan.Lines.Any(line =>
                line.FragmentId == write.FragmentId &&
                (line.Action is DocxReimportResolutionAction.UseWord or
                    DocxReimportResolutionAction.ManualMerge) && line.Resolved)))
        {
            throw new InvalidDataException(
                "Le plan contient des fragments répétés ou non approuvés.");
        }

        string project = ProjectPath(projectDirectory);
        string transactions = TransactionsDirectory(project);
        Directory.CreateDirectory(transactions);
        EnsureNoLinks(transactions);
        await using FileStream guard = AcquireLock(transactions);
        await VerifyProjectAsync(project, plan.ProjectId, cancellationToken);
        foreach (string directory in Directory.EnumerateDirectories(transactions))
        {
            EnsureNoLinks(directory);
            DocxReimportTransactionJournal prior = await ReadJournalAsync(
                Path.Combine(directory, JournalName), cancellationToken);
            ValidateJournalDirectory(directory, prior);
            if (prior.State == DocxReimportTransactionState.Prepared)
            {
                throw new InvalidOperationException(
                    "Une transaction DOCX interrompue doit être restaurée avant tout nouvel import.");
            }
        }

        string operationDirectory = Path.Combine(transactions,
            operationId.ToString("N"));
        string journalPath = Path.Combine(operationDirectory, JournalName);
        if (Directory.Exists(operationDirectory))
        {
            DocxReimportTransactionJournal existing =
                await ReadJournalAsync(journalPath, cancellationToken);
            if (existing.OperationId != operationId ||
                existing.ProjectId != plan.ProjectId ||
                existing.PreviousImportId != plan.PreviousImportId ||
                !string.Equals(existing.Fingerprint, approvedFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Cet identifiant de transaction appartient à une autre opération.");
            }

            if (existing.State != DocxReimportTransactionState.Completed)
            {
                throw new InvalidOperationException(
                    "Transaction DOCX interrompue : lancez d'abord la récupération.");
            }

            ValidateJournalDirectory(operationDirectory, existing);
            if (existing.Entries.Count != plan.Writes.Count)
            {
                throw new InvalidDataException("Journal DOCX incompatible avec le plan.");
            }

            foreach (DocxReimportTransactionEntry entry in existing.Entries)
            {
                if (!plan.Writes.Any(write =>
                    string.Equals(write.DestinationRelativePath,
                        entry.DestinationRelativePath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(write.ProjectedSha256, entry.AfterSha256,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(write.ExpectedCurrentSha256, entry.BeforeSha256,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        "Journal DOCX incompatible avec les destinations approuvées.");
                }

                string path = Destination(project, entry.DestinationRelativePath);
                if (!File.Exists(path) ||
                    !string.Equals(DocxImportManifestStore.ComputeSha256(
                            await File.ReadAllTextAsync(path, cancellationToken)),
                        entry.AfterSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Réimport déjà enregistré mais destination modifiée : aucune nouvelle écriture.");
                }
            }

            return new DocxReimportTransactionResult
            {
                OperationId = operationId,
                AlreadyApplied = true,
                WrittenFiles = existing.Entries.Count
            };
        }

        List<PreparedWrite> prepared = [];
        HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DocxReimportProposedWrite write in plan.Writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Destination(project, write.DestinationRelativePath);
            if (!destinations.Add(path) ||
                string.IsNullOrWhiteSpace(write.ProjectedMarkdown) ||
                Encoding.UTF8.GetByteCount(write.ProjectedMarkdown) > MaxFileBytes ||
                !string.Equals(DocxImportManifestStore.ComputeSha256(
                        write.ProjectedMarkdown), write.ProjectedSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                throw new InvalidDataException(
                    "Destination ou empreinte de réimport invalide.");
            }

            FileInfo file = new FileInfo(path);
            if (file.Length > MaxFileBytes)
            {
                throw new InvalidDataException(
                    "Destination Markdown trop volumineuse pour la transaction.");
            }

            byte[] originalBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            string before = await File.ReadAllTextAsync(path, cancellationToken);
            if (!string.Equals(DocxImportManifestStore.ComputeSha256(before),
                    write.ExpectedCurrentSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Le Markdown a changé depuis l'aperçu : recalcul obligatoire.");
            }

            prepared.Add(new PreparedWrite(write, path, originalBytes));
        }

        Directory.CreateDirectory(operationDirectory);
        List<DocxReimportTransactionEntry> entries = [];
        bool journalWritten = false;
        try
        {
            for (int index = 0; index < prepared.Count; index++)
            {
                PreparedWrite item = prepared[index];
                string backupFile = index.ToString("D5") + ".bak";
                string backupPath = Path.Combine(operationDirectory, backupFile);
                await using (FileStream backup = new FileStream(backupPath,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await backup.WriteAsync(item.Bytes, cancellationToken);
                    await backup.FlushAsync(cancellationToken);
                    backup.Flush(flushToDisk: true);
                }

                entries.Add(new DocxReimportTransactionEntry
                {
                    DestinationRelativePath = item.Write.DestinationRelativePath,
                    BeforeSha256 = item.Write.ExpectedCurrentSha256,
                    AfterSha256 = item.Write.ProjectedSha256,
                    BackupFileName = backupFile,
                    BackupBytesSha256 = ShaBytes(item.Bytes)
                });
            }

            DocxReimportTransactionJournal journal = new DocxReimportTransactionJournal
            {
                OperationId = operationId,
                ProjectId = plan.ProjectId,
                PreviousImportId = plan.PreviousImportId,
                Fingerprint = Fingerprint(plan),
                State = DocxReimportTransactionState.Prepared,
                Entries = entries
            };
            await AtomicJsonFile.WriteAsync(journalPath, journal, cancellationToken);
            journalWritten = true;

            // Verify every original immediately before the first replacement.
            // This does not make third-party writers transactional; per-file
            // races after this check are documented and fail on recovery.
            foreach (PreparedWrite item in prepared)
            {
                if (!File.Exists(item.Path) ||
                    !string.Equals(DocxImportManifestStore.ComputeSha256(
                            await File.ReadAllTextAsync(item.Path, cancellationToken)),
                        item.Write.ExpectedCurrentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Le Markdown a changé durant la préparation de la transaction.");
                }
            }

            for (int index = 0; index < prepared.Count; index++)
            {
                PreparedWrite item = prepared[index];
                cancellationToken.ThrowIfCancellationRequested();
                // Check immediately before each write as well as before
                // beginning the operation, to detect intermediate edits.
                if (!File.Exists(item.Path) ||
                    !string.Equals(DocxImportManifestStore.ComputeSha256(
                            await File.ReadAllTextAsync(item.Path, cancellationToken)),
                        item.Write.ExpectedCurrentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Destination modifiée pendant le réimport : rollback requis.");
                }

                await AtomicFileWriter.WriteAllTextAsync(item.Path,
                    item.Write.ProjectedMarkdown, cancellationToken);
                _afterWriteCheckpoint?.Invoke(index + 1);
            }

            await AtomicJsonFile.WriteAsync(journalPath,
                journal with { State = DocxReimportTransactionState.Completed },
                cancellationToken);
            return new DocxReimportTransactionResult
            {
                OperationId = operationId,
                WrittenFiles = entries.Count
            };
        }
        catch
        {
            if (journalWritten)
            {
                // Recovery must run even on cancellation. If it cannot safely
                // recover, preserve the journal/backups and expose the failure.
                await RestoreJournalAsync(project, operationDirectory,
                    cancellationToken: CancellationToken.None);
            }
            else
            {
                // No destination has been modified before journaling.
                Directory.Delete(operationDirectory, recursive: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Restores every incomplete transaction for a project. After a crash,
    /// call before starting another reimport. Conflicting external changes
    /// are rejected and the original backup files are preserved.
    /// </summary>
    public async Task<int> RecoverPendingAsync(string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string project = ProjectPath(projectDirectory);
        string transactions = TransactionsDirectory(project);
        if (!Directory.Exists(transactions))
        {
            return 0;
        }

        EnsureNoLinks(transactions);
        await using FileStream guard = AcquireLock(transactions);
        int recovered = 0;
        foreach (string directory in Directory.EnumerateDirectories(transactions)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string journalPath = Path.Combine(directory, JournalName);
            EnsureNoLinks(directory);
            DocxReimportTransactionJournal journal =
                await ReadJournalAsync(journalPath, cancellationToken);
            ValidateJournalDirectory(directory, journal);
            await VerifyProjectAsync(project, journal.ProjectId, cancellationToken);
            if (journal.State == DocxReimportTransactionState.Prepared)
            {
                await RestoreJournalAsync(project, directory, cancellationToken);
                recovered++;
            }
        }

        return recovered;
    }

    /// <summary>Validates and restores all writes of one prepared journal.</summary>
    private async Task RestoreJournalAsync(string project, string directory,
        CancellationToken cancellationToken)
    {
        string journalPath = Path.Combine(directory, JournalName);
        DocxReimportTransactionJournal journal =
            await ReadJournalAsync(journalPath, cancellationToken);
        ValidateJournalDirectory(directory, journal);
        if (journal.State != DocxReimportTransactionState.Prepared)
        {
            return;
        }

        List<(string Destination, string Backup)> restore = [];
        HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DocxReimportTransactionEntry entry in journal.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Destination(project, entry.DestinationRelativePath);
            string backup = Path.Combine(directory, entry.BackupFileName);
            if (!destinations.Add(destination) ||
                entry.BackupFileName.Length != 9 ||
                !entry.BackupFileName[..5].All(char.IsAsciiDigit) ||
                !entry.BackupFileName.EndsWith(".bak", StringComparison.Ordinal) ||
                !File.Exists(backup) ||
                (File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0 ||
                !File.Exists(destination))
            {
                throw new InvalidDataException(
                    "Sauvegarde de récupération DOCX manquante ou corrompue.");
            }

            byte[] originalBytes = await File.ReadAllBytesAsync(backup, cancellationToken);
            using MemoryStream source = new MemoryStream(originalBytes);
            using StreamReader reader = new StreamReader(source, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            string restoredText = await reader.ReadToEndAsync(cancellationToken);
            if (!string.Equals(ShaBytes(originalBytes), entry.BackupBytesSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(DocxImportManifestStore.ComputeSha256(restoredText),
                    entry.BeforeSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "La sauvegarde DOCX ne correspond pas au snapshot d'origine.");
            }

            string current = DocxImportManifestStore.ComputeSha256(
                await File.ReadAllTextAsync(destination, cancellationToken));
            if (!string.Equals(current, entry.BeforeSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(current, entry.AfterSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Fichier modifié hors du réimport : récupération manuelle nécessaire.");
            }

            if (!string.Equals(current, entry.BeforeSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                restore.Add((destination, backup));
            }
        }

        foreach ((string destination, string backup) in restore)
        {
            await AtomicFileWriter.WriteAsync(destination,
                async (stream, token) =>
                {
                    await using FileStream input = new FileStream(backup,
                        FileMode.Open, FileAccess.Read, FileShare.Read);
                    await input.CopyToAsync(stream, token);
                }, cancellationToken);
        }

        await AtomicJsonFile.WriteAsync(journalPath,
            journal with { State = DocxReimportTransactionState.RolledBack },
            cancellationToken);
    }

    /// <summary>Rejects traversal or links on any existing destination ancestor.</summary>
    private string Destination(string project, string relative)
    {
        string destination = _paths.ResolveWorkspaceRelativePath(relative);
        string within = Path.GetRelativePath(project, destination);
        if (Path.IsPathRooted(within) || within == ".." ||
            within.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(destination), ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Destination de réimport hors du projet.");
        }

        EnsureNoLinks(destination);
        return destination;
    }

    /// <summary>Confines the selected project path to the existing workspace.</summary>
    private string ProjectPath(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string path = Path.GetFullPath(projectDirectory);
        string relative = _paths.ToWorkspaceRelativePath(path);
        if (relative == "." || relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal) ||
            !Directory.Exists(path))
        {
            throw new InvalidDataException("Projet de réimport inexistant ou hors du workspace.");
        }

        EnsureNoLinks(path);
        return path;
    }

    /// <summary>Rejects symlinks and junctions between the workspace and a file.</summary>
    private void EnsureNoLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Réimport interdit sur un lien symbolique ou une jonction.");
            }

            if (string.Equals(current, _workspaceRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>Validates the journal ID against its path.</summary>
    private static void ValidateJournalDirectory(string directory,
        DocxReimportTransactionJournal journal)
    {
        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out Guid id) ||
            id != journal.OperationId)
        {
            throw new InvalidDataException(
                "Journal DOCX déplacé ou identifiant de transaction invalide.");
        }
    }

    /// <summary>Returns the isolated journal root of a project.</summary>
    private static string TransactionsDirectory(string project) =>
        Path.Combine(project, DirectoryName);

    /// <summary>Creates an exclusive operation-wide lock shared by commit and recovery.</summary>
    private static FileStream AcquireLock(string directory)
    {
        string path = Path.Combine(directory, ".lock");
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Verrou de transaction DOCX sur lien symbolique interdit.");
        }

        return new FileStream(path, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Confirms the canonical persisted project identifier.</summary>
    private static async Task VerifyProjectAsync(string project, Guid projectId,
        CancellationToken cancellationToken)
    {
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            Path.Combine(project, WorkspaceLayout.ProjectManifestFileName),
            cancellationToken);
        if (projectId == Guid.Empty || manifest.Id != projectId)
        {
            throw new InvalidDataException("Historique DOCX d'un autre projet.");
        }
    }

    /// <summary>Reads a durable transaction journal and rejects invalid states.</summary>
    private static async Task<DocxReimportTransactionJournal> ReadJournalAsync(
        string journalPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(journalPath) || new FileInfo(journalPath).Length > 128_000)
        {
            throw new InvalidDataException("Journal de réimport manquant ou trop volumineux.");
        }

        DocxReimportTransactionJournal journal =
            await AtomicJsonFile.ReadAsync<DocxReimportTransactionJournal>(
                journalPath, cancellationToken);
        if (journal.OperationId == Guid.Empty || journal.ProjectId == Guid.Empty ||
            journal.PreviousImportId == Guid.Empty ||
            !IsSha256(journal.Fingerprint) ||
            journal.Entries is not { Count: > 0 and <= 200 } ||
            journal.Entries.Any(entry =>
                entry is null ||
                !IsSha256(entry.BeforeSha256) ||
                !IsSha256(entry.AfterSha256) ||
                !IsSha256(entry.BackupBytesSha256) ||
                string.IsNullOrWhiteSpace(entry.DestinationRelativePath) ||
                string.IsNullOrWhiteSpace(entry.BackupFileName)) ||
            !Enum.IsDefined(journal.State))
        {
            throw new InvalidDataException("Journal de réimport invalide.");
        }

        return journal;
    }

    /// <summary>Ensures journals contain real 64-digit SHA-256 hex strings.</summary>
    private static bool IsSha256(string? hash) =>
        hash is { Length: 64 } &&
        hash.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or
                >= 'A' and <= 'F');

    /// <summary>Returns a stable SHA-256 over the exact original backup bytes.</summary>
    private static string ShaBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>One complete local destination snapshot before journaling.</summary>
    private sealed record PreparedWrite(DocxReimportProposedWrite Write,
        string Path, byte[] Bytes);
}

/// <summary>Durable reimport state allowing explicit recovery after interruption.</summary>
public enum DocxReimportTransactionState
{
    Prepared,
    Completed,
    RolledBack
}

/// <summary>One byte-exact backed-up Markdown destination.</summary>
public sealed record DocxReimportTransactionEntry
{
    public required string DestinationRelativePath { get; init; }

    public required string BeforeSha256 { get; init; }

    public required string AfterSha256 { get; init; }

    public required string BackupFileName { get; init; }

    public required string BackupBytesSha256 { get; init; }
}

/// <summary>Local audit and recovery journal retained under the project.</summary>
public sealed record DocxReimportTransactionJournal
{
    public required Guid OperationId { get; init; }

    public required Guid ProjectId { get; init; }

    public required Guid PreviousImportId { get; init; }

    public required string Fingerprint { get; init; }

    public required DocxReimportTransactionState State { get; init; }

    public List<DocxReimportTransactionEntry> Entries { get; init; } = [];
}

/// <summary>Result of a fully committed or already recorded operation.</summary>
public sealed record DocxReimportTransactionResult
{
    public required Guid OperationId { get; init; }

    public int WrittenFiles { get; init; }

    public bool AlreadyApplied { get; init; }
}
