using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Durable checkpoint for one user-approved Complex v2 migration.</summary>
public sealed record ComplexProjectMigrationJournal
{
    /// <summary>Stable transaction identity.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Canonical workspace-relative project location.</summary>
    public required string ProjectRelativePath { get; init; }

    /// <summary>Fingerprint of the original project snapshot.</summary>
    public required string OriginalFingerprint { get; init; }

    /// <summary>Fingerprint of the staged proposed project.</summary>
    public required string StagedFingerprint { get; init; }

    /// <summary>Last durable transaction checkpoint.</summary>
    public required string Phase { get; init; }

    /// <summary>Time this approved journal was first recorded.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Outcome of one approved, durable, project-scoped migration.</summary>
public sealed record ComplexProjectMigrationResult
{
    /// <summary>Original project UUID, unchanged.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Absolute active directory after successful publication.</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>Kept, byte-exact original directory for explicit rollback.</summary>
    public required string BackupDirectory { get; init; }

    /// <summary>Durable journal path.</summary>
    public required string JournalPath { get; init; }

    /// <summary>Number of existing section sources relocated.</summary>
    public required int MovedSections { get; init; }
}

/// <summary>
/// Applies only link-free, fingerprinted Complex v2 migrations via staging and
/// two same-volume directory renames. The original is retained as an immutable
/// recovery source; an interrupted publish is never silently discarded.
/// </summary>
public sealed class ComplexProjectMigrationTransactionService
{
    private const string TransactionDirectoryName = ".complex-v2-migrations";
    private const string JournalFileName = "journal.json";
    private const string StagingDirectoryName = "staging";
    private const string BackupDirectoryName = "original";
    private const string PublishedDirectoryName = "previously-published";

    private readonly string _workspaceRoot;
    private readonly string _transactionsRoot;
    private readonly ComplexProjectMigrationPlanService _planner;
    private readonly Action<string>? _checkpoint;

    /// <summary>Creates a project-scoped migration executor and recovery owner.</summary>
    /// <param name="workspaceRoot">Active workspace.</param>
    /// <param name="checkpoint">Optional deterministic test fault injection.</param>
    public ComplexProjectMigrationTransactionService(
        string workspaceRoot,
        Action<string>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _transactionsRoot = Path.Combine(_workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName, TransactionDirectoryName);
        _planner = new ComplexProjectMigrationPlanService(_workspaceRoot);
        _checkpoint = checkpoint;
    }

    /// <summary>
    /// Returns project paths for transactions that need a manual recovery
    /// decision. Never changes the workspace or journal.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Absolute project roots with incomplete transactions.</returns>
    public async Task<IReadOnlyList<string>> ListPendingProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> pending = [];
        foreach ((string directory, ComplexProjectMigrationJournal journal) in
                 await ReadJournalsAsync(cancellationToken))
        {
            if (journal.Phase is "completed" or "recovered" or "aborted" or "rolled-back")
            {
                continue;
            }

            pending.Add(ResolveJournalProject(journal));
        }

        return pending.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Publishes only an explicitly approved, still-identical read-only plan.
    /// A concurrent edit or a pending earlier transaction fails closed.
    /// </summary>
    /// <param name="plan">The preflight plan the user reviewed.</param>
    /// <param name="approvedFingerprint">Expected user-approved fingerprint.</param>
    /// <param name="approved">Explicit human decision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Retained backup and transaction report.</returns>
    public async Task<ComplexProjectMigrationResult> ApplyAsync(
        ComplexProjectMigrationPlan plan,
        string approvedFingerprint,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!approved)
        {
            throw new InvalidOperationException(
                "La migration Complexe requiert une confirmation humaine explicite.");
        }

        if (!plan.CanExecute ||
            string.IsNullOrWhiteSpace(approvedFingerprint) ||
            !string.Equals(approvedFingerprint, plan.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le plan approuvé comporte des blocages ou une empreinte incorrecte.");
        }

        if (!await ValidateFreshPlanAsync(plan, cancellationToken))
        {
            throw new InvalidDataException(
                "Le projet ou ses références ont changé : générez un nouveau plan.");
        }

        Directory.CreateDirectory(_transactionsRoot);
        string lockPath = Path.Combine(_transactionsRoot, ".operation.lock");
        await using FileStream operationLock = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, useAsync: true);

        if ((await ListPendingProjectsAsync(cancellationToken)).Count > 0)
        {
            throw new InvalidOperationException(
                "Une migration Complexe précédente doit être récupérée avant une nouvelle opération.");
        }

        if (!await ValidateFreshPlanAsync(plan, cancellationToken))
        {
            throw new InvalidDataException(
                "Le projet a changé pendant la préparation : approbation périmée.");
        }

        Guid transactionId = Guid.NewGuid();
        string transactionDirectory = Path.Combine(_transactionsRoot,
            transactionId.ToString("N"));
        string stage = Path.Combine(transactionDirectory, StagingDirectoryName);
        string backup = Path.Combine(transactionDirectory, BackupDirectoryName);
        string journalPath = Path.Combine(transactionDirectory, JournalFileName);
        string projectRoot = Path.GetFullPath(plan.ProjectDirectory);
        string projectRelative = Path.GetRelativePath(
            _workspaceRoot, projectRoot).Replace('\\', '/');
        Directory.CreateDirectory(transactionDirectory);

        ComplexProjectMigrationJournal? journal = null;
        try
        {
            await CopyProjectAsync(projectRoot, stage, cancellationToken);
            await ApplyStagedLayoutAsync(stage, plan, cancellationToken);
            string stagedHash =
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    stage, cancellationToken);

            if (!string.Equals(
                    await ComplexProjectMigrationPlanService.FingerprintAsync(
                        projectRoot, cancellationToken),
                    approvedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Modification externe du projet pendant le staging : aucun changement publié.");
            }

            journal = new ComplexProjectMigrationJournal
            {
                TransactionId = transactionId,
                ProjectRelativePath = projectRelative,
                OriginalFingerprint = approvedFingerprint,
                StagedFingerprint = stagedHash,
                Phase = "prepared"
            };
            await AtomicJsonFile.WriteAsync(
                journalPath, journal, cancellationToken);
            _checkpoint?.Invoke("prepared");

            Directory.Move(projectRoot, backup);
            _checkpoint?.Invoke("original-moved");

            // A last-minute external write before the first rename must not
            // be lost when the prepared copy is published.
            if (!string.Equals(
                    await ComplexProjectMigrationPlanService.FingerprintAsync(
                        backup, cancellationToken),
                    approvedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(backup, projectRoot);
                await AtomicJsonFile.WriteAsync(
                    journalPath, journal with { Phase = "aborted" },
                    CancellationToken.None);
                throw new InvalidDataException(
                    "L'original a changé pendant la bascule : restauration sans publication.");
            }

            journal = journal with { Phase = "backed-up" };
            await AtomicJsonFile.WriteAsync(
                journalPath, journal, CancellationToken.None);
            _checkpoint?.Invoke("backed-up");

            Directory.Move(stage, projectRoot);
            _checkpoint?.Invoke("published");

            journal = journal with { Phase = "published" };
            await AtomicJsonFile.WriteAsync(
                journalPath, journal, CancellationToken.None);

            string resultHash =
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot, CancellationToken.None);
            if (!string.Equals(resultHash, stagedHash,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "L'empreinte publiée diffère du staging : récupération manuelle nécessaire.");
            }

            journal = journal with { Phase = "completed" };
            await AtomicJsonFile.WriteAsync(
                journalPath, journal, CancellationToken.None);
            return new ComplexProjectMigrationResult
            {
                ProjectId = plan.ProjectId,
                ProjectDirectory = projectRoot,
                BackupDirectory = backup,
                JournalPath = journalPath,
                MovedSections = plan.Moves.Count(move =>
                    move.State == ComplexMigrationMoveState.Candidate)
            };
        }
        catch
        {
            // A failed publish that left the original directory absent must
            // restore it immediately. All other states are retained for
            // explicit, fingerprint-checked recovery.
            if (!Directory.Exists(projectRoot) && Directory.Exists(backup))
            {
                Directory.Move(backup, projectRoot);
                if (journal is not null)
                {
                    await AtomicJsonFile.WriteAsync(
                        journalPath, journal with { Phase = "recovered" },
                        CancellationToken.None);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Explicitly reconciles incomplete journals. Never overwrites a modified
    /// published project or a newer unrelated directory.
    /// </summary>
    /// <param name="approved">Human approval for the recovery operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of reconciled transactions.</returns>
    public async Task<int> RecoverPendingAsync(
        bool approved,
        CancellationToken cancellationToken = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "La récupération d'une migration exige une confirmation explicite.");
        }

        if (!Directory.Exists(_transactionsRoot))
        {
            return 0;
        }

        await using FileStream operationLock = new(
            Path.Combine(_transactionsRoot, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            1, useAsync: true);

        int count = 0;
        foreach ((string directory, ComplexProjectMigrationJournal journal) in
                 await ReadJournalsAsync(cancellationToken))
        {
            if (journal.Phase is "completed" or "recovered" or "aborted" or "rolled-back")
            {
                continue;
            }

            string projectRoot = ResolveJournalProject(journal);
            string backup = Path.Combine(directory, BackupDirectoryName);
            string stage = Path.Combine(directory, StagingDirectoryName);
            string journalPath = Path.Combine(directory, JournalFileName);

            if (Directory.Exists(backup))
            {
                string backupHash =
                    await ComplexProjectMigrationPlanService.FingerprintAsync(
                        backup, cancellationToken);
                if (!string.Equals(backupHash, journal.OriginalFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "La sauvegarde a changé : récupération automatique interdite.");
                }
            }

            string nextPhase;
            if (Directory.Exists(backup) && !Directory.Exists(projectRoot))
            {
                Directory.Move(backup, projectRoot);
                nextPhase = journal.Phase == "rollback-prepared"
                    ? "rolled-back" : "recovered";
            }
            else if (Directory.Exists(backup) && Directory.Exists(projectRoot))
            {
                string publishedHash =
                    await ComplexProjectMigrationPlanService.FingerprintAsync(
                        projectRoot, cancellationToken);
                if (!string.Equals(publishedHash, journal.StagedFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Le projet publié contient des changements externes : aucune restauration automatique.");
                }

                nextPhase = "completed";
            }
            else if (Directory.Exists(projectRoot) && !Directory.Exists(backup))
            {
                string originalHash =
                    await ComplexProjectMigrationPlanService.FingerprintAsync(
                        projectRoot, cancellationToken);
                if (!string.Equals(originalHash, journal.OriginalFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "La source initiale a changé : réconciliation manuelle requise.");
                }

                nextPhase = journal.Phase == "rollback-prepared"
                    ? "rolled-back" : "aborted";
            }
            else
            {
                throw new InvalidDataException(
                    "Ni le projet ni sa sauvegarde ne sont présents : récupération manuelle requise.");
            }

            await AtomicJsonFile.WriteAsync(journalPath,
                journal with { Phase = nextPhase }, CancellationToken.None);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Lists successful migrations with retained originals, newest first.
    /// </summary>
    /// <param name="projectDirectory">Active project root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completed rollback candidates only, without writes.</returns>
    public async Task<IReadOnlyList<ComplexProjectMigrationJournal>>
        GetRollbackCandidatesAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(projectDirectory);
        List<ComplexProjectMigrationJournal> candidates = [];
        foreach ((string directory, ComplexProjectMigrationJournal journal) in
                 await ReadJournalsAsync(cancellationToken))
        {
            if (journal.Phase == "completed" &&
                string.Equals(ResolveJournalProject(journal), root,
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(Path.Combine(directory, BackupDirectoryName)))
            {
                candidates.Add(journal);
            }
        }

        return candidates.OrderByDescending(item => item.CreatedUtc).ToArray();
    }

    /// <summary>
    /// Restores the original project only after explicitly approving its
    /// precise journal and verifying both original and published fingerprints.
    /// Preserves the displaced published project for manual inspection.
    /// </summary>
    /// <param name="transactionId">Journal selected for rollback.</param>
    /// <param name="approved">Explicit human confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Restored project path.</returns>
    public async Task<string> RollbackAsync(
        Guid transactionId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        if (!approved)
        {
            throw new InvalidOperationException(
                "Une restauration exige une approbation humaine explicite.");
        }

        if (transactionId == Guid.Empty || !Directory.Exists(_transactionsRoot))
        {
            throw new InvalidDataException("Transaction de restauration inconnue.");
        }

        string directory = Path.Combine(_transactionsRoot, transactionId.ToString("N"));
        string journalPath = Path.Combine(directory, JournalFileName);
        if (!File.Exists(journalPath))
        {
            throw new FileNotFoundException("Journal de migration introuvable.", journalPath);
        }

        await using FileStream operationLock = new(
            Path.Combine(_transactionsRoot, ".operation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            1, useAsync: true);

        ComplexProjectMigrationJournal journal =
            await AtomicJsonFile.ReadAsync<ComplexProjectMigrationJournal>(
                journalPath, cancellationToken);
        if (journal.TransactionId != transactionId || journal.Phase != "completed")
        {
            throw new InvalidOperationException(
                "La restauration exige une migration achevée et non encore restaurée.");
        }

        string source = ResolveJournalProject(journal);
        string original = Path.Combine(directory, BackupDirectoryName);
        string previouslyPublished = Path.Combine(directory, PublishedDirectoryName);
        if (!Directory.Exists(source) || !Directory.Exists(original) ||
            Directory.Exists(previouslyPublished) ||
            (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(original) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Une source de restauration manque ou ne peut pas être vérifiée.");
        }

        string currentHash =
            await ComplexProjectMigrationPlanService.FingerprintAsync(
                source, cancellationToken);
        string backupHash =
            await ComplexProjectMigrationPlanService.FingerprintAsync(
                original, cancellationToken);
        if (!string.Equals(currentHash, journal.StagedFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(backupHash, journal.OriginalFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le projet ou sa sauvegarde ont changé : restauration interdite sans vérification manuelle.");
        }

        journal = journal with { Phase = "rollback-prepared" };
        await AtomicJsonFile.WriteAsync(journalPath, journal, CancellationToken.None);
        try
        {
            Directory.Move(source, previouslyPublished);
            _checkpoint?.Invoke("rollback-source-moved");
            Directory.Move(original, source);
            await AtomicJsonFile.WriteAsync(
                journalPath, journal with { Phase = "rolled-back" },
                CancellationToken.None);
            return source;
        }
        catch
        {
            if (!Directory.Exists(source) && Directory.Exists(original))
            {
                Directory.Move(original, source);
                await AtomicJsonFile.WriteAsync(
                    journalPath, journal with { Phase = "rolled-back" },
                    CancellationToken.None);
            }

            throw;
        }
    }

    /// <summary>
    /// Rebuilds a source snapshot under the transaction directory without
    /// traversing symbolic links. File bytes are copied, not re-rendered.
    /// </summary>
    /// <param name="source">Original project root.</param>
    /// <param name="stage">Nonexistent staging root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task CopyProjectAsync(
        string source,
        string stage,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stage);
        foreach (string entry in ComplexProjectMigrationPlanService.EnumerateTree(
                     source, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Copie refusée : lien symbolique.");
            }

            string relative = Path.GetRelativePath(source, entry);
            string destination = Path.Combine(stage, relative);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using FileStream input = new(entry, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            await using FileStream output = new(destination, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    /// <summary>
    /// Changes staging only, preserving GUIDs and all source content while
    /// introducing the four structural parent/child sections.
    /// </summary>
    /// <param name="stage">Verified staging project directory.</param>
    /// <param name="plan">Approved frozen move inventory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task ApplyStagedLayoutAsync(
        string stage,
        ComplexProjectMigrationPlan plan,
        CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(stage,
            WorkspaceLayout.ProjectManifestFileName);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != plan.ProjectId)
        {
            throw new InvalidDataException(
                "Le projet de staging ne possède plus son identité attendue.");
        }

        foreach (ComplexMigrationMove move in plan.Moves.Where(move =>
                     move.State == ComplexMigrationMoveState.Candidate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveUnderStage(stage, move.From);
            string destination = ResolveUnderStage(stage, move.To);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new IOException("La destination est déjà occupée dans le staging.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (move.To.EndsWith("/Jalons.md", StringComparison.OrdinalIgnoreCase))
            {
                File.Move(source, destination);
            }
            else
            {
                Directory.Move(source, destination);
            }
        }

        foreach (string directory in plan.MissingDirectories)
        {
            Directory.CreateDirectory(ResolveUnderStage(stage, directory));
        }

        Dictionary<Guid, string> mapped = plan.Moves.ToDictionary(
            move => move.SectionId,
            move => move.To.EndsWith("/Jalons.md",
                StringComparison.OrdinalIgnoreCase)
                ? move.To[..^"/Jalons.md".Length]
                : move.To);

        List<SectionManifest> sections = manifest.Sections
            .Select(section => mapped.TryGetValue(section.Id, out string? directory)
                ? section with { RelativeDirectory = directory }
                : section)
            .ToList();

        (string Name, string Directory)[] containers =
        [
            ("Exigences", "Documentation/Exigences"),
            ("Architecture", "Documentation/Technique/Architecture"),
            ("Analyse", "Analyse"),
            ("Management", "Management")
        ];

        int nextOrder = sections.Count == 0
            ? 10 : sections.Max(section => section.Order) + 10;
        foreach ((string name, string relativeDirectory) in containers)
        {
            if (sections.Any(section => string.Equals(section.Name, name,
                StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            sections.Add(new SectionManifest
            {
                Id = Guid.NewGuid(),
                Name = name,
                Order = nextOrder,
                IsSingleton = false,
                RelativeDirectory = relativeDirectory
            });
            nextOrder += 10;
        }

        await AtomicJsonFile.WriteAsync(
            manifestPath, manifest with { Sections = sections },
            cancellationToken);
    }

    /// <summary>
    /// Rejects traversal and rooted paths in the already reviewed relative
    /// file/folder plan before applying its proposed staging changes.
    /// </summary>
    /// <param name="stage">Staging project root.</param>
    /// <param name="relative">Portable project-relative file or folder.</param>
    /// <returns>Absolute validated staging path.</returns>
    private static string ResolveUnderStage(string stage, string relative)
    {
        string[] parts = relative.Split('/');
        if (parts.Length == 0 ||
            parts.Any(part => part.Length == 0 || part is "." or ".." ||
                part.Contains('\\') || part.Contains(':')))
        {
            throw new InvalidDataException("Chemin de migration non portable.");
        }

        string path = Path.GetFullPath(Path.Combine(stage,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(stage) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Déplacement hors staging refusé.");
        }

        return path;
    }

    /// <summary>
    /// Revalidates the entire approved plan rather than only trusting its
    /// fingerprint or a UI callback captured minutes earlier.
    /// </summary>
    /// <param name="approved">Approved read-only plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True only if the rebuilt plan is still identical.</returns>
    private async Task<bool> ValidateFreshPlanAsync(
        ComplexProjectMigrationPlan approved,
        CancellationToken cancellationToken)
    {
        ComplexProjectMigrationPlan latest = await _planner.PrepareAsync(
            approved.ProjectDirectory, cancellationToken);
        return latest.CanExecute &&
            latest.ProjectId == approved.ProjectId &&
            latest.Moves.SequenceEqual(approved.Moves) &&
            string.Equals(latest.SourceFingerprint,
                approved.SourceFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads only transaction directories with a durable journal.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validated journal-directory pairs.</returns>
    private async Task<IReadOnlyList<(string Directory,
        ComplexProjectMigrationJournal Journal)>> ReadJournalsAsync(
        CancellationToken cancellationToken)
    {
        List<(string Directory, ComplexProjectMigrationJournal Journal)> journals = [];
        if (!Directory.Exists(_transactionsRoot))
        {
            return journals;
        }

        foreach (string directory in Directory.EnumerateDirectories(
                     _transactionsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, JournalFileName);
            if (!File.Exists(path))
            {
                continue;
            }

            ComplexProjectMigrationJournal journal =
                await AtomicJsonFile.ReadAsync<ComplexProjectMigrationJournal>(
                    path, cancellationToken);
            if (journal.TransactionId.ToString("N") != Path.GetFileName(directory))
            {
                throw new InvalidDataException(
                    "Journal de migration corrompu : identité incohérente.");
            }

            journals.Add((directory, journal));
        }

        return journals;
    }

    /// <summary>Maps journal-relative project paths strictly inside workspace.</summary>
    /// <param name="journal">Durable migration journal.</param>
    /// <returns>Canonical existing or temporarily missing project location.</returns>
    private string ResolveJournalProject(ComplexProjectMigrationJournal journal)
    {
        string path = Path.GetFullPath(Path.Combine(_workspaceRoot,
            journal.ProjectRelativePath.Replace('/',
                Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_workspaceRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Journal rejeté : chemin projet hors workspace.");
        }

        return path;
    }
}
