using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
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
    public Task<DocxReimportTransactionResult> CommitAsync(
        string projectDirectory,
        DocxReimportDecisionPlan plan,
        Guid operationId,
        string approvedFingerprint,
        CancellationToken cancellationToken = default) =>
        CommitCoreAsync(projectDirectory, plan, operationId, approvedFingerprint,
            null, cancellationToken);

    /// <summary>
    /// Includes a new immutable Word copy and chained provenance in the same
    /// recoverable operation as Markdown changes. No UI calls this until the
    /// end-to-end workflow has been qualified.
    /// </summary>
    public Task<DocxReimportTransactionResult> CommitWithProvenanceAsync(
        string projectDirectory,
        DocxReimportDecisionPlan plan,
        Guid operationId,
        string approvedFingerprint,
        DocxReimportProvenancePackage provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        return CommitCoreAsync(projectDirectory, plan, operationId,
            approvedFingerprint, provenance, cancellationToken);
    }

    /// <summary>Runs the shared validated and journaled local commit workflow.</summary>
    private async Task<DocxReimportTransactionResult> CommitCoreAsync(
        string projectDirectory,
        DocxReimportDecisionPlan plan,
        Guid operationId,
        string approvedFingerprint,
        DocxReimportProvenancePackage? provenance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (operationId == Guid.Empty ||
            !string.Equals(approvedFingerprint, Fingerprint(plan),
                StringComparison.OrdinalIgnoreCase) ||
            !plan.ReviewComplete || plan.Lines.Count == 0 ||
            (plan.Writes.Count == 0 && provenance is null) ||
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
        PreparedProvenance? preparedProvenance = provenance is null
            ? null : await PrepareProvenanceAsync(project, plan,
                operationId, provenance, cancellationToken);
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
            if (existing.Entries.Count != plan.Writes.Count ||
                existing.CreatedFiles.Count != (provenance is null ? 0 : 2))
            {
                throw new InvalidDataException("Journal DOCX incompatible avec le plan.");
            }

            if (preparedProvenance is not null)
            {
                foreach (DocxReimportCreatedFile created in existing.CreatedFiles)
                {
                    string actual = CreatedDestination(project, operationId,
                        created.RelativePath);
                    string expected = string.Equals(actual,
                        preparedProvenance.SourcePath, StringComparison.OrdinalIgnoreCase)
                        ? preparedProvenance.SourceSha256
                        : preparedProvenance.ManifestSha256;
                    if (!string.Equals(created.Sha256, expected,
                            StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(actual) ||
                        !string.Equals(await DocxImportManifestStore.ComputeFileSha256Async(
                                actual, cancellationToken), created.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Un fichier du réimport déjà finalisé est absent ou modifié.");
                    }
                }
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
                WrittenFiles = existing.Entries.Count,
                ManifestPath = preparedProvenance?.ManifestPath
            };
        }

        if (preparedProvenance is not null &&
            (File.Exists(preparedProvenance.SourcePath) ||
             File.Exists(preparedProvenance.ManifestPath)))
        {
            throw new IOException(
                "Un historique DOCX de cette opération existe déjà : aucune écriture.");
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
                Entries = entries,
                CreatedFiles = preparedProvenance is null ? [] :
                [
                    new DocxReimportCreatedFile
                    {
                        RelativePath = _paths.ToWorkspaceRelativePath(
                            preparedProvenance.SourcePath),
                        Sha256 = preparedProvenance.SourceSha256
                    },
                    new DocxReimportCreatedFile
                    {
                        RelativePath = _paths.ToWorkspaceRelativePath(
                            preparedProvenance.ManifestPath),
                        Sha256 = preparedProvenance.ManifestSha256
                    }
                ]
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

            if (preparedProvenance is not null)
            {
                // Files are tracked in Prepared before either can be created,
                // so a crash between DOCX and manifest creation is recoverable.
                await WriteNewFileAsync(preparedProvenance.SourcePath,
                    preparedProvenance.SourceBytes, cancellationToken);
                await _paths.SaveNewAsync(project,
                    preparedProvenance.Manifest, cancellationToken);
                if (!string.Equals(
                        await DocxImportManifestStore.ComputeFileSha256Async(
                            preparedProvenance.ManifestPath, cancellationToken),
                        preparedProvenance.ManifestSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Le manifeste DOCX enregistré diffère de la version approuvée.");
                }

                _afterWriteCheckpoint?.Invoke(prepared.Count + 1);
            }

            await AtomicJsonFile.WriteAsync(journalPath,
                journal with { State = DocxReimportTransactionState.Completed },
                cancellationToken);
            return new DocxReimportTransactionResult
            {
                OperationId = operationId,
                WrittenFiles = entries.Count,
                ManifestPath = preparedProvenance?.ManifestPath
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

        List<string> createdToRemove = [];
        HashSet<string> createdTargets =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (journal.CreatedFiles.Count is not (0 or 2))
        {
            throw new InvalidDataException(
                "Le journal de réimport contient un nombre anormal de créations.");
        }

        foreach (DocxReimportCreatedFile created in journal.CreatedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string full = CreatedDestination(project, journal.OperationId,
                created.RelativePath);
            if (!createdTargets.Add(full))
            {
                throw new InvalidDataException(
                    "Créations du journal DOCX en doublon.");
            }

            if (File.Exists(full))
            {
                if (!string.Equals(
                        await DocxImportManifestStore.ComputeFileSha256Async(
                            full, cancellationToken),
                        created.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Fichier créé puis modifié hors du réimport : reprise manuelle.");
                }

                createdToRemove.Add(full);
            }
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

        foreach (string created in createdToRemove)
        {
            DocxReimportCreatedFile recorded = journal.CreatedFiles.Single(file =>
                string.Equals(CreatedDestination(project, journal.OperationId,
                    file.RelativePath), created, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(created))
            {
                if (!string.Equals(
                        await DocxImportManifestStore.ComputeFileSha256Async(
                            created, cancellationToken),
                        recorded.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Copie ou manifeste créé modifié avant restauration : reprise manuelle.");
                }

                File.Delete(created);
            }
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

    /// <summary>
    /// Validates the versioned source and provenance package before any local
    /// changes and caches exactly the bytes that will be committed.
    /// </summary>
    private async Task<PreparedProvenance> PrepareProvenanceAsync(
        string project,
        DocxReimportDecisionPlan plan,
        Guid operationId,
        DocxReimportProvenancePackage package,
        CancellationToken cancellationToken)
    {
        DocxImportManifest manifest = package.Manifest;
        _paths.Validate(manifest);
        if (manifest.ProjectId != plan.ProjectId ||
            manifest.PreviousImportId != plan.PreviousImportId ||
            manifest.ReimportOperationId != operationId ||
            manifest.ImportId != operationId ||
            !string.Equals(manifest.ApprovedPlanFingerprint,
                Fingerprint(plan), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Provenance incompatible avec l'approbation de réimport.");
        }

        string sourcePath = CreatedDestination(project, operationId,
            manifest.SourceCopyRelativePath);
        string manifestPath = _paths.GetManifestPath(project, operationId);
        EnsureNoLinks(sourcePath);
        EnsureNoLinks(manifestPath);

        string staged = Path.GetFullPath(package.StagedDocxPath);
        EnsureNoLinks(staged);
        if (!File.Exists(staged) && File.Exists(sourcePath))
        {
            // Replaying a completed transaction remains possible even after
            // the temporary DOCX staging area has been cleaned up.
            staged = sourcePath;
        }

        if (!File.Exists(staged) || new FileInfo(staged).Length > 128L * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Copie DOCX temporaire absente ou trop volumineuse.");
        }

        byte[] sourceBytes = await File.ReadAllBytesAsync(staged, cancellationToken);
        string sourceSha = ShaBytes(sourceBytes);
        if (!string.Equals(manifest.SourceSha256, sourceSha,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le Word temporaire ne correspond plus à sa provenance.");
        }

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            manifest, JsonDefaults.Options);
        if (manifestBytes.Length > 64L * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Manifeste de provenance du réimport trop volumineux.");
        }

        foreach (DocxImportManifestFragment fragment in manifest.Fragments)
        {
            DocxReimportProposedWrite? write = plan.Writes.FirstOrDefault(item =>
                item.FragmentId == fragment.FragmentId);
            if (write is not null &&
                (!string.Equals(write.DestinationRelativePath,
                    fragment.DestinationRelativePath,
                    StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(write.ProjectedSha256,
                    fragment.DestinationSha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "La provenance ne correspond pas aux fichiers projetés.");
            }

            // Retained local destinations are checked in the same preflight.
            if (write is null)
            {
                string path = Destination(project, fragment.DestinationRelativePath);
                if (!File.Exists(path) ||
                    !string.Equals(DocxImportManifestStore.ComputeSha256(
                            await File.ReadAllTextAsync(path, cancellationToken)),
                        fragment.DestinationSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "La destination conservée a changé depuis la prévisualisation.");
                }
            }
        }

        return new PreparedProvenance(manifest, sourcePath, manifestPath,
            sourceBytes, sourceSha, ShaBytes(manifestBytes));
    }

    /// <summary>
    /// Allows only the operation-scoped DOCX and its immutable project manifest
    /// as newly created transaction artifacts. Never resolves arbitrary files.
    /// </summary>
    private string CreatedDestination(string project, Guid operationId,
        string relativePath)
    {
        string resolved = _paths.ResolveWorkspaceRelativePath(relativePath);
        string expectedDocx = Path.Combine(_workspaceRoot, "Imports", "Sources",
            "reimport-" + operationId.ToString("N") + ".docx");
        string expectedManifest = _paths.GetManifestPath(project, operationId);
        if (!string.Equals(resolved, expectedDocx,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolved, expectedManifest,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fichier généré inconnu pour la transaction de réimport.");
        }

        EnsureNoLinks(resolved);
        return resolved;
    }

    /// <summary>
    /// Creates an immutable source copy from already verified package bytes
    /// without allowing an existing copy to be overwritten.
    /// </summary>
    private static async Task WriteNewFileAsync(string destination,
        byte[] content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory,
            "." + Path.GetFileName(destination) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new FileStream(temporary,
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
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
            journal.Entries is not { Count: <= 200 } ||
            journal.CreatedFiles is not { Count: <= 2 } ||
            (journal.Entries.Count == 0 && journal.CreatedFiles.Count == 0) ||
            journal.CreatedFiles.Any(file =>
                file is null ||
                !IsSha256(file.Sha256) ||
                string.IsNullOrWhiteSpace(file.RelativePath)) ||
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

    /// <summary>Prepared immutable provenance and the expected artifact hashes.</summary>
    private sealed record PreparedProvenance(
        DocxImportManifest Manifest,
        string SourcePath,
        string ManifestPath,
        byte[] SourceBytes,
        string SourceSha256,
        string ManifestSha256);

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

    /// <summary>New immutable files to remove when recovery rolls back.</summary>
    public List<DocxReimportCreatedFile> CreatedFiles { get; init; } = [];
}

/// <summary>One newly created file tracked by its exact byte checksum.</summary>
public sealed record DocxReimportCreatedFile
{
    public required string RelativePath { get; init; }

    public required string Sha256 { get; init; }
}

/// <summary>Optional provenance artifacts committed with the Markdown changes.</summary>
public sealed record DocxReimportProvenancePackage
{
    public required DocxImportManifest Manifest { get; init; }

    public required string StagedDocxPath { get; init; }
}

/// <summary>Result of a fully committed or already recorded operation.</summary>
public sealed record DocxReimportTransactionResult
{
    public required Guid OperationId { get; init; }

    public int WrittenFiles { get; init; }

    public bool AlreadyApplied { get; init; }

    /// <summary>New chained provenance manifest, when committed with the source.</summary>
    public string? ManifestPath { get; init; }
}
