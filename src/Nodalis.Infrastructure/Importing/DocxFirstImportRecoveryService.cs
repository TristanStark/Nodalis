using System.Security.Cryptography;
using System.Text.Json;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Importing;

/// <summary>Immutable checksum and workspace-relative path for an output owned by one initial DOCX import.</summary>
public sealed record DocxFirstImportFile
{
    public required string RelativePath { get; init; }

    public required string Sha256 { get; init; }
}

/// <summary>Durable write-ahead record for the first import, independent of project creation.</summary>
public sealed record DocxFirstImportJournal
{
    public required Guid ImportId { get; init; }

    public required string ProjectRelativePath { get; init; }

    public required string PlanFingerprint { get; init; }

    public Guid? ExistingProjectId { get; init; }

    public string? OriginalProjectManifestSha256 { get; init; }

    public string? UpdatedProjectManifestSha256 { get; init; }

    public string? ProvenanceRelativePath { get; init; }

    public string State { get; init; } = "Prepared";

    public List<DocxFirstImportFile> Files { get; init; } = [];
}

/// <summary>Holds the workspace import lock until commit or rollback finishes.</summary>
public sealed class DocxFirstImportSession : IDisposable
{
    internal readonly FileStream LockStream;

    internal readonly string DirectoryPath;

    internal DocxFirstImportJournal Journal;

    /// <summary>Creates a lock-owning import session.</summary>
    /// <param name="lockStream">Workspace-exclusive file lock.</param>
    /// <param name="directoryPath">Transaction directory.</param>
    /// <param name="journal">Prepared journal.</param>
    internal DocxFirstImportSession(FileStream lockStream, string directoryPath,
        DocxFirstImportJournal journal)
    {
        LockStream = lockStream;
        DirectoryPath = directoryPath;
        Journal = journal;
    }

    /// <summary>Releases the workspace lock even when the importer fails.</summary>
    public void Dispose() => LockStream.Dispose();
}

/// <summary>
/// Crash-recoverable, local write-ahead journal for an initial DOCX import.
/// Existing project manifests have byte-for-byte backups. Recovery refuses to
/// delete external edits, and never recursively removes a newly created project.
/// </summary>
public sealed class DocxFirstImportRecoveryService
{
    public const string DirectoryName = ".nodalis-first-imports";

    private const string JournalName = "journal.json";
    private const string BackupName = "project-manifest.bak";
    private readonly string _root;
    private readonly DocxImportManifestStore _paths;

    /// <summary>Initializes workspace-confined journal storage.</summary>
    /// <param name="workspaceRoot">Nodalis workspace root.</param>
    public DocxFirstImportRecoveryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.GetFullPath(workspaceRoot);
        _paths = new DocxImportManifestStore(_root);
    }

    /// <summary>
    /// Locks initial imports, backs up an existing manifest, and durably writes
    /// expected output checksums BEFORE creating or modifying any project file.
    /// </summary>
    /// <param name="plan">User-approved or revalidated dry-run.</param>
    /// <param name="importId">Stable DOCX preview identity.</param>
    /// <param name="existingProjectId">Existing project ID, null for new projects.</param>
    /// <param name="files">All currently absent output files with expected byte hashes, except provenance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exclusive session held for the entire first import.</returns>
    public async Task<DocxFirstImportSession> BeginAsync(
        DocxImportPlan plan, Guid importId, Guid? existingProjectId,
        IReadOnlyList<DocxFirstImportFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (importId == Guid.Empty || plan.Fingerprint.Length != 64 ||
            files.Count == 0 || files.Count > 10000)
        {
            throw new InvalidDataException("Plan de transaction DOCX initiale invalide.");
        }

        FileStream lockStream = AcquireLock();
        try
        {
            EnsureNoPending();
            string project = SafePath(plan.TargetProjectRelativePath);
            string projectRelative = _paths.ToWorkspaceRelativePath(project);
            if (existingProjectId is null && Directory.Exists(project))
            {
                throw new IOException("Le nom de projet prévu est désormais occupé.");
            }

            HashSet<string> unique = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (DocxFirstImportFile file in files)
            {
                string destination = SafePath(file.RelativePath);
                if (!unique.Add(destination) || !IsSha(file.Sha256) ||
                    File.Exists(destination))
                {
                    throw new InvalidDataException(
                        "Plan DOCX obsolète : chemin réservé, dupliqué ou empreinte invalide.");
                }
            }

            string directory = Path.Combine(JournalsDirectory, importId.ToString("N"));
            if (Directory.Exists(directory))
            {
                throw new InvalidDataException("Cet identifiant d'import DOCX possède déjà un journal.");
            }

            Directory.CreateDirectory(directory);
            string? originalHash = null;
            if (existingProjectId is not null)
            {
                string manifest = Path.Combine(project,
                    WorkspaceLayout.ProjectManifestFileName);
                EnsureNoLinks(manifest);
                byte[] backup = await File.ReadAllBytesAsync(manifest, cancellationToken);
                originalHash = Sha(backup);
                string backupPath = Path.Combine(directory, BackupName);
                await using (FileStream stream = new FileStream(backupPath,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(backup, cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
            }

            DocxFirstImportJournal journal = new DocxFirstImportJournal
            {
                ImportId = importId,
                ProjectRelativePath = projectRelative,
                PlanFingerprint = plan.Fingerprint,
                ExistingProjectId = existingProjectId,
                OriginalProjectManifestSha256 = originalHash,
                Files = files.ToList()
            };
            await SaveAsync(directory, journal, cancellationToken);
            return new DocxFirstImportSession(lockStream, directory, journal);
        }
        catch
        {
            lockStream.Dispose();
            throw;
        }
    }

    /// <summary>Records a project manifest's exact future bytes BEFORE replacing it.</summary>
    /// <param name="session">Active exclusive import session.</param>
    /// <param name="expectedSha256">SHA256 of JSON to be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable journal update.</returns>
    public async Task ExpectProjectManifestAsync(DocxFirstImportSession session,
        string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (session.Journal.ExistingProjectId is null || !IsSha(expectedSha256))
        {
            throw new InvalidDataException("Mise à jour du manifeste initial incompatible.");
        }

        await UpdateAsync(session, session.Journal with
        {
            UpdatedProjectManifestSha256 = expectedSha256
        }, cancellationToken);
    }

    /// <summary>Registers the exact provenance bytes BEFORE its immutable file is created.</summary>
    /// <param name="session">Active import session.</param>
    /// <param name="manifest">Provenance record that will be serialized.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable journal update.</returns>
    public async Task ExpectProvenanceAsync(DocxFirstImportSession session,
        DocxImportManifest manifest, CancellationToken cancellationToken = default)
    {
        if (manifest.ImportId != session.Journal.ImportId ||
            (session.Journal.ExistingProjectId is Guid projectId &&
             manifest.ProjectId != projectId) ||
            session.Journal.ProvenanceRelativePath is not null)
        {
            throw new InvalidDataException("Provenance initiale incompatible avec le journal.");
        }

        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(
            manifest, JsonDefaults.Options);
        string path = _paths.GetManifestPath(
            SafePath(session.Journal.ProjectRelativePath), manifest.ImportId);
        string relative = _paths.ToWorkspaceRelativePath(path);
        if (File.Exists(path))
        {
            throw new InvalidDataException("Manifeste de provenance déjà présent.");
        }

        List<DocxFirstImportFile> files = session.Journal.Files.ToList();
        files.Add(new DocxFirstImportFile
        {
            RelativePath = relative,
            Sha256 = Sha(serialized)
        });
        await UpdateAsync(session, session.Journal with
        {
            ProvenanceRelativePath = relative,
            Files = files
        }, cancellationToken);
    }

    /// <summary>Marks an import committed after all of its outputs have been verified.</summary>
    /// <param name="session">Active exclusive import session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable commit marker.</returns>
    public async Task CompleteAsync(DocxFirstImportSession session,
        CancellationToken cancellationToken = default)
    {
        await VerifyCompleteAsync(session.Journal, cancellationToken);
        await UpdateAsync(session, session.Journal with { State = "Completed" },
            cancellationToken);
    }

    /// <summary>
    /// Restores a prepared import after an exception, while retaining the lock.
    /// External edits block rollback instead of being overwritten.
    /// </summary>
    /// <param name="session">Active exclusive import session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable rollback marker.</returns>
    public Task RollbackAsync(DocxFirstImportSession session,
        CancellationToken cancellationToken = default) =>
        RecoverOneAsync(session.DirectoryPath, session.Journal,
            cancellationToken, allowCompletion: false);

    /// <summary>Lists the affected project paths without modifying any file.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project roots whose initial import is still pending.</returns>
    public async Task<IReadOnlyList<string>> ListPendingProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        using FileStream exclusive = AcquireLock();
        List<string> pending = [];
        foreach (string directory in Directory.EnumerateDirectories(JournalsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = Path.Combine(directory, JournalName);
            if (!File.Exists(file))
            {
                continue;
            }

            DocxFirstImportJournal journal =
                await AtomicJsonFile.ReadAsync<DocxFirstImportJournal>(
                    file, cancellationToken);
            ValidateJournal(directory, journal);
            if (journal.State == "Prepared")
            {
                pending.Add(SafePath(journal.ProjectRelativePath));
            }
        }

        return pending;
    }

    /// <summary>
    /// On startup or before another import, recovers interrupted transactions.
    /// A fully written and verified import is completed, not discarded.
    /// New project scaffolds are preserved for inspection after failed imports.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of interrupted transactions reconciled.</returns>
    public async Task<int> RecoverPendingAsync(CancellationToken cancellationToken = default)
    {
        using FileStream exclusive = AcquireLock();
        int recovered = 0;
        foreach (string directory in Directory.EnumerateDirectories(JournalsDirectory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = Path.Combine(directory, JournalName);
            if (!File.Exists(file))
            {
                // The backup can have been written before journal preparation.
                continue;
            }

            DocxFirstImportJournal journal =
                await AtomicJsonFile.ReadAsync<DocxFirstImportJournal>(
                    file, cancellationToken);
            ValidateJournal(directory, journal);
            if (journal.State == "Prepared")
            {
                await RecoverOneAsync(directory, journal, cancellationToken,
                    allowCompletion: true);
                recovered++;
            }
        }

        return recovered;
    }

    /// <summary>Rejects a new initial import while any old prepared journal exists.</summary>
    private void EnsureNoPending()
    {
        foreach (string directory in Directory.EnumerateDirectories(JournalsDirectory))
        {
            string path = Path.Combine(directory, JournalName);
            if (!File.Exists(path))
            {
                continue;
            }

            DocxFirstImportJournal journal = AtomicJsonFile
                .ReadAsync<DocxFirstImportJournal>(path).GetAwaiter().GetResult();
            ValidateJournal(directory, journal);
            if (journal.State == "Prepared")
            {
                throw new InvalidOperationException(
                    "Import DOCX interrompu : lancez d'abord la récupération des opérations initiales.");
            }
        }
    }

    /// <summary>Performs one fail-closed recovery with no user file overwrite.</summary>
    private async Task RecoverOneAsync(string directory,
        DocxFirstImportJournal journal, CancellationToken cancellationToken,
        bool allowCompletion)
    {
        ValidateJournal(directory, journal);
        if (journal.State != "Prepared")
        {
            return;
        }

        if (allowCompletion && journal.ProvenanceRelativePath is not null &&
            await AllFilesMatchAsync(journal.Files, cancellationToken) &&
            await ManifestMatchesExpectedAsync(journal, cancellationToken))
        {
            await SaveAsync(directory, journal with { State = "Completed" },
                cancellationToken);
            return;
        }

        // Validate the ENTIRE rollback before changing a single destination.
        foreach (DocxFirstImportFile file in journal.Files)
        {
            string destination = SafePath(file.RelativePath);
            if (File.Exists(destination) &&
                !string.Equals(
                    await DocxImportManifestStore.ComputeFileSha256Async(
                        destination, cancellationToken),
                    file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Import DOCX interrompu : un fichier a changé hors transaction. " +
                    "Récupération manuelle requise : " + file.RelativePath);
            }
        }

        string project = SafePath(journal.ProjectRelativePath);
        string projectManifest = Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName);
        byte[]? backupBytes = null;
        if (journal.ExistingProjectId is not null)
        {
            string backupPath = Path.Combine(directory, BackupName);
            EnsureNoLinks(backupPath);
            backupBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
            if (!string.Equals(Sha(backupBytes),
                    journal.OriginalProjectManifestSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !await ManifestMatchesExpectedAsync(journal, cancellationToken))
            {
                throw new InvalidDataException(
                    "Manifeste projet DOCX altéré : récupération manuelle requise.");
            }
        }

        foreach (DocxFirstImportFile file in journal.Files.Reverse<DocxFirstImportFile>())
        {
            string destination = SafePath(file.RelativePath);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        if (backupBytes is not null &&
            File.Exists(projectManifest) &&
            !string.Equals(
                await DocxImportManifestStore.ComputeFileSha256Async(
                    projectManifest, cancellationToken),
                journal.OriginalProjectManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            await AtomicFileWriter.WriteAsync(projectManifest,
                async (stream, token) =>
                    await stream.WriteAsync(backupBytes, token), cancellationToken);
        }

        await SaveAsync(directory, journal with { State = "RolledBack" },
            cancellationToken);
    }

    /// <summary>Checks whether all recorded created files retain expected bytes.</summary>
    private async Task<bool> AllFilesMatchAsync(IReadOnlyList<DocxFirstImportFile> files,
        CancellationToken cancellationToken)
    {
        foreach (DocxFirstImportFile file in files)
        {
            string path = SafePath(file.RelativePath);
            if (!File.Exists(path) ||
                !string.Equals(
                    await DocxImportManifestStore.ComputeFileSha256Async(
                        path, cancellationToken),
                    file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Allows only the original or the journal-approved project manifest.</summary>
    private async Task<bool> ManifestMatchesExpectedAsync(
        DocxFirstImportJournal journal, CancellationToken cancellationToken)
    {
        string path = Path.Combine(SafePath(journal.ProjectRelativePath),
            WorkspaceLayout.ProjectManifestFileName);
        if (!File.Exists(path))
        {
            return journal.ExistingProjectId is null;
        }

        if (journal.ExistingProjectId is null)
        {
            return true;
        }

        string actual = await DocxImportManifestStore.ComputeFileSha256Async(
            path, cancellationToken);
        return string.Equals(actual, journal.OriginalProjectManifestSha256,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(actual, journal.UpdatedProjectManifestSha256,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Confirms a committed import contains the exact expected output bytes.</summary>
    private async Task VerifyCompleteAsync(DocxFirstImportJournal journal,
        CancellationToken cancellationToken)
    {
        if (journal.ProvenanceRelativePath is null ||
            !await AllFilesMatchAsync(journal.Files, cancellationToken) ||
            !await ManifestMatchesExpectedAsync(journal, cancellationToken))
        {
            throw new InvalidDataException(
                "Import DOCX incomplet : provenance, document ou manifeste non conforme.");
        }
    }

    /// <summary>Persists an updated journal and synchronizes the current in-memory state.</summary>
    private async Task UpdateAsync(DocxFirstImportSession session,
        DocxFirstImportJournal updated, CancellationToken cancellationToken)
    {
        await SaveAsync(session.DirectoryPath, updated, cancellationToken);
        session.Journal = updated;
    }

    /// <summary>Atomically synchronizes a transaction journal.</summary>
    private static Task SaveAsync(string directory, DocxFirstImportJournal journal,
        CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(Path.Combine(directory, JournalName),
            journal, cancellationToken);

    /// <summary>Checks journal identity, state, every output hash and safe path.</summary>
    private void ValidateJournal(string directory, DocxFirstImportJournal journal)
    {
        if (!Guid.TryParseExact(Path.GetFileName(directory), "N",
                out Guid id) || journal.ImportId != id ||
            journal.State is not ("Prepared" or "Completed" or "RolledBack") ||
            journal.Files is null || journal.Files.Count is < 1 or > 10001 ||
            !IsSha(journal.PlanFingerprint) ||
            (journal.ExistingProjectId is not null &&
             !IsSha(journal.OriginalProjectManifestSha256)) ||
            (journal.UpdatedProjectManifestSha256 is not null &&
             !IsSha(journal.UpdatedProjectManifestSha256)))
        {
            throw new InvalidDataException("Journal de récupération DOCX invalide.");
        }

        string project = SafePath(journal.ProjectRelativePath);
        string expectedProvenance = Path.Combine(project,
            DocxImportManifestStore.DirectoryName,
            journal.ImportId.ToString("N") + ".json");
        if (journal.ProvenanceRelativePath is not null &&
            !string.Equals(SafePath(journal.ProvenanceRelativePath),
                expectedProvenance, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Emplacement du manifeste de provenance DOCX inattendu.");
        }

        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DocxFirstImportFile file in journal.Files)
        {
            string full = SafePath(file.RelativePath);
            if (!unique.Add(full) || !IsSha(file.Sha256) ||
                !IsAllowedOutput(project, full, expectedProvenance, file.Sha256))
            {
                throw new InvalidDataException("Fichiers de récupération DOCX incohérents.");
            }
        }

        if (journal.ProvenanceRelativePath is not null &&
            !unique.Contains(expectedProvenance))
        {
            throw new InvalidDataException("Provenance DOCX manquante au journal.");
        }
    }

    /// <summary>Checks a journal file can only be an import Markdown, image, preserved Word or its own provenance.</summary>
    private bool IsAllowedOutput(string project, string file, string provenance,
        string sha256)
    {
        if (string.Equals(file, provenance, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string sources = Path.Combine(_root, "Imports", "Sources");
        if (string.Equals(Path.GetDirectoryName(file), sources,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetExtension(file), ".docx",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string within = Path.GetRelativePath(project, file);
        if (Path.IsPathRooted(within) || within == ".." ||
            within.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(Path.GetExtension(file), ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string media = Path.Combine(project, DocxSelectedMediaAssets.MediaFolder);
        return string.Equals(Path.GetDirectoryName(file), media,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetFileNameWithoutExtension(file), sha256,
                   StringComparison.OrdinalIgnoreCase) &&
               Path.GetExtension(file).ToLowerInvariant() is
                   (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp");
    }

    /// <summary>Locks the workspace against concurrent initial import or recovery.</summary>
    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(JournalsDirectory);
        string lockPath = Path.Combine(JournalsDirectory, ".lock");
        EnsureNoLinks(lockPath);
        return new FileStream(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Computes a safe path, rejecting traversal or reparse ancestors.</summary>
    private string SafePath(string relative)
    {
        string full = _paths.ResolveWorkspaceRelativePath(relative);
        EnsureNoLinks(full);
        return full;
    }

    /// <summary>Rejects a symlink/junction in any existing workspace child path.</summary>
    private void EnsureNoLinks(string path)
    {
        string full = Path.GetFullPath(path);
        string within = Path.GetRelativePath(_root, full);
        if (Path.IsPathRooted(within) || within == ".." ||
            within.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Chemin de récupération DOCX hors workspace.");
        }

        string cursor = _root;
        foreach (string segment in within.Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, segment);
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Lien symbolique dans un chemin DOCX.");
            }
        }
    }

    /// <summary>Computes SHA256 from exact bytes (including optional UTF-8 BOM).</summary>
    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Validates a hexadecimal SHA256 string.</summary>
    private static bool IsSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private string JournalsDirectory => Path.Combine(_root, "Imports", DirectoryName);
}
