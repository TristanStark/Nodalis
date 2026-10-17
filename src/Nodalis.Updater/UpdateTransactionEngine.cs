using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Updates;

namespace Nodalis.Updater;

/// <summary>
/// Applies update directory swaps as a journaled transaction with deterministic recovery.
/// </summary>
public sealed class UpdateTransactionEngine
{
    private const string ApplicationExecutableName = "Nodalis.exe";
    private const string ReleaseManifestFileName = "release-manifest.json";
    private const string JournalSuffix = ".update-transaction.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    private readonly Func<string, string, bool> _startupValidator;
    private readonly Action<string, string> _launcher;
    private readonly Action<string> _log;

    /// <summary>
    /// Initializes the production update engine.
    /// </summary>
    public UpdateTransactionEngine()
        : this(
            ValidateStartupProcess,
            LaunchProcess,
            _ => { })
    {
    }

    /// <summary>
    /// Initializes an update engine with injectable process operations for deterministic recovery tests.
    /// </summary>
    /// <param name="startupValidator">Validates that an executable can start and exit cleanly in smoke-test mode.</param>
    /// <param name="launcher">Launches the selected application after a successful transaction.</param>
    /// <param name="log">Receives human-readable diagnostics.</param>
    public UpdateTransactionEngine(
            Func<string, string, bool> startupValidator,
            Action<string, string> launcher,
            Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(startupValidator);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(log);

        _startupValidator = startupValidator;
        _launcher = launcher;
        _log = log;
    }

    /// <summary>
    /// Applies a staged release and automatically rolls back ordinary validation or startup failures.
    /// </summary>
    /// <param name="installationDirectory">The current application directory.</param>
    /// <param name="stagingDirectory">The previously staged release directory.</param>
    /// <param name="launchExecutable">The executable to launch after validation.</param>
    /// <param name="interruptAfterStage">Optional deterministic interruption point used by recovery tests.</param>
    /// <returns>Zero on success and one when rollback was required.</returns>
    public int Apply(
            string installationDirectory,
            string stagingDirectory,
            string launchExecutable,
            UpdateTransactionStage? interruptAfterStage = null)
    {
        string installation = NormalizeDirectory(installationDirectory);
        string staging = NormalizeDirectory(stagingDirectory);
        string previous = installation + ".previous";

        RecoverInterruptedTransaction(installation);
        ValidatePackageDirectory(staging);
        EnsureApplicationPayload(installation);

        if (Directory.Exists(previous))
        {
            Directory.Delete(previous, recursive: true);
        }

        UpdateOperationJournal journal = CreateJournal(
            UpdateTransactionCommand.Apply,
            installation,
            staging,
            previous,
            swapDirectory: null,
            UpdateTransactionStage.Prepared,
            "Staging validé ; aucune bascule effectuée.");

        WriteJournal(journal);
        Checkpoint(journal.Stage, interruptAfterStage);

        try
        {
            journal = Advance(
                journal,
                UpdateTransactionStage.ExistingPreviousRemoved,
                "Ancienne version précédente nettoyée ; la version courante reste exécutable.");
            Checkpoint(journal.Stage, interruptAfterStage);

            Directory.Move(installation, previous);
            journal = Advance(
                journal,
                UpdateTransactionStage.CurrentMovedToPrevious,
                "Version courante déplacée vers le dossier de rollback.");
            Checkpoint(journal.Stage, interruptAfterStage);

            Directory.Move(staging, installation);
            journal = Advance(
                journal,
                UpdateTransactionStage.CandidateMovedToCurrent,
                "Version candidate installée ; rollback encore disponible.");
            Checkpoint(journal.Stage, interruptAfterStage);

            ValidatePackageDirectory(installation);
            journal = Advance(
                journal,
                UpdateTransactionStage.CandidateFilesValidated,
                "Fichiers de la version candidate revérifiés après bascule.");
            Checkpoint(journal.Stage, interruptAfterStage);

            if (!_startupValidator(installation, launchExecutable))
            {
                throw new InvalidDataException(
                    "Le nouveau binaire n'a pas réussi son test de démarrage ; rollback automatique.");
            }

            journal = Advance(
                journal,
                UpdateTransactionStage.CandidateStartupValidated,
                "Le nouveau binaire a réussi son test de démarrage.");
            Checkpoint(journal.Stage, interruptAfterStage);

            _launcher(installation, launchExecutable);

            Advance(
                journal,
                UpdateTransactionStage.Completed,
                "Mise à jour terminée ; la version précédente reste disponible pour rollback.");
            CleanupOldArtifacts(installation);
            _log($"APPLY OK {installation}");
            return 0;
        }
        catch (UpdateSimulationInterruptionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryMarkFailed(journal, exception.Message);
            _log($"APPLY FAILED {exception}");

            try
            {
                RecoverInterruptedTransaction(installation);
            }
            catch (Exception recoveryException)
            {
                _log($"APPLY RECOVERY FAILED {recoveryException}");
            }

            return 1;
        }
    }

    /// <summary>
    /// Swaps the current version with the retained previous version using the same journal and startup validation.
    /// </summary>
    /// <param name="installationDirectory">The current application directory.</param>
    /// <param name="launchExecutable">The executable to relaunch.</param>
    /// <param name="interruptAfterStage">Optional deterministic interruption point used by recovery tests.</param>
    /// <returns>Zero on success and one when rollback recovery was required.</returns>
    public int Rollback(
            string installationDirectory,
            string launchExecutable,
            UpdateTransactionStage? interruptAfterStage = null)
    {
        string installation = NormalizeDirectory(installationDirectory);
        RecoverInterruptedTransaction(installation);

        string previous = installation + ".previous";
        EnsureApplicationPayload(installation);
        EnsureApplicationPayload(previous);

        string swap = installation + $".rollback-{Guid.NewGuid():N}";
        UpdateOperationJournal journal = CreateJournal(
            UpdateTransactionCommand.Rollback,
            installation,
            stagingDirectory: null,
            previous,
            swap,
            UpdateTransactionStage.Prepared,
            "Rollback préparé ; versions courante et précédente validées.");

        WriteJournal(journal);
        Checkpoint(journal.Stage, interruptAfterStage);

        try
        {
            Directory.Move(installation, swap);
            journal = Advance(
                journal,
                UpdateTransactionStage.RollbackCurrentMovedToSwap,
                "Version courante déplacée dans le dossier de swap.");
            Checkpoint(journal.Stage, interruptAfterStage);

            Directory.Move(previous, installation);
            journal = Advance(
                journal,
                UpdateTransactionStage.RollbackPreviousMovedToCurrent,
                "Version précédente restaurée à l'emplacement courant.");
            Checkpoint(journal.Stage, interruptAfterStage);

            if (!_startupValidator(installation, launchExecutable))
            {
                throw new InvalidDataException(
                    "La version restaurée n'a pas réussi son test de démarrage.");
            }

            journal = Advance(
                journal,
                UpdateTransactionStage.RollbackStartupValidated,
                "La version restaurée a réussi son test de démarrage.");
            Checkpoint(journal.Stage, interruptAfterStage);

            Directory.Move(swap, previous);
            journal = Advance(
                journal,
                UpdateTransactionStage.RollbackSwapMovedToPrevious,
                "La version remplacée est conservée comme version précédente.");
            Checkpoint(journal.Stage, interruptAfterStage);

            _launcher(installation, launchExecutable);
            Advance(
                journal,
                UpdateTransactionStage.Completed,
                "Rollback terminé avec une version exécutable conservée de chaque côté.");
            CleanupOldArtifacts(installation);
            _log($"ROLLBACK OK {installation}");
            return 0;
        }
        catch (UpdateSimulationInterruptionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryMarkFailed(journal, exception.Message);
            _log($"ROLLBACK FAILED {exception}");

            try
            {
                RecoverInterruptedTransaction(installation);
            }
            catch (Exception recoveryException)
            {
                _log($"ROLLBACK RECOVERY FAILED {recoveryException}");
            }

            return 1;
        }
    }

    /// <summary>
    /// Recovers any incomplete transaction using the journal and executable copies remaining on disk.
    /// </summary>
    /// <param name="installationDirectory">The canonical installation directory.</param>
    /// <returns>True when an incomplete transaction was detected and normalized.</returns>
    public bool RecoverInterruptedTransaction(
            string installationDirectory)
    {
        string installation = NormalizeDirectory(installationDirectory);
        string journalPath = GetJournalPath(installation);

        if (!File.Exists(journalPath))
        {
            return false;
        }

        UpdateOperationJournal journal = ReadJournal(journalPath);

        if (journal.Stage is UpdateTransactionStage.Completed or UpdateTransactionStage.Recovered)
        {
            return false;
        }

        string previous = journal.PreviousDirectory;
        string? swap = journal.SwapDirectory;
        bool installationRunnable = HasApplicationPayload(installation);
        bool previousRunnable = HasApplicationPayload(previous);
        bool swapRunnable =
            !string.IsNullOrWhiteSpace(swap) &&
            HasApplicationPayload(swap);

        if (journal.Command == UpdateTransactionCommand.Rollback && swapRunnable)
        {
            if (Directory.Exists(installation))
            {
                MoveAsideFailedDirectory(installation);
            }

            Directory.Move(swap!, installation);
            installationRunnable = true;
        }
        else if (previousRunnable)
        {
            if (Directory.Exists(installation))
            {
                MoveAsideFailedDirectory(installation);
            }

            Directory.Move(previous, installation);
            installationRunnable = true;
        }

        if (!installationRunnable &&
            !HasApplicationPayload(installation))
        {
            throw new InvalidDataException(
                "Récupération impossible automatiquement : aucune version exécutable n'a été trouvée dans l'installation, le rollback ou le swap.");
        }

        Advance(
            journal,
            UpdateTransactionStage.Recovered,
            "Transaction interrompue récupérée ; une version exécutable est restaurée à l'emplacement principal.");
        _log($"RECOVERY OK {installation}");
        return true;
    }

    /// <summary>
    /// Validates the staged manifest and every declared file immediately before or after a directory swap.
    /// </summary>
    /// <param name="directory">The candidate application directory.</param>
    public static void ValidatePackageDirectory(
            string directory)
    {
        string root = NormalizeDirectory(directory);
        string manifestPath = Path.Combine(root, ReleaseManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException(
                $"Le staging ne contient pas {ReleaseManifestFileName}.");
        }

        ReleaseManifest? manifest = JsonSerializer.Deserialize<ReleaseManifest>(
            File.ReadAllText(manifestPath, Encoding.UTF8),
            JsonOptions);

        if (manifest is null ||
            manifest.ManifestSchemaVersion != ReleaseManifest.CurrentManifestSchemaVersion ||
            !string.Equals(manifest.Product, "Nodalis", StringComparison.Ordinal) ||
            !string.Equals(manifest.TargetRid, "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le manifeste du staging est invalide ou incompatible.");
        }

        foreach (ReleaseFileEntry file in manifest.Files)
        {
            string relativePath = NormalizeRelativePath(file.Path);
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!IsSameOrChildPath(root, fullPath) ||
                !File.Exists(fullPath))
            {
                throw new InvalidDataException(
                    $"Le fichier de release '{relativePath}' est absent du staging.");
            }

            FileInfo info = new FileInfo(fullPath);

            if (info.Length != file.Length)
            {
                throw new InvalidDataException(
                    $"La taille du fichier '{relativePath}' a changé depuis le staging.");
            }

            using FileStream stream = File.OpenRead(fullPath);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();

            if (!string.Equals(
                    actualHash,
                    file.Sha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Le SHA-256 du fichier '{relativePath}' a changé depuis le staging.");
            }
        }

        EnsureApplicationPayload(root);
    }

    /// <summary>
    /// Returns the durable journal path beside the installation directory.
    /// </summary>
    /// <param name="installationDirectory">The canonical installation directory.</param>
    /// <returns>The journal path.</returns>
    public static string GetJournalPath(
            string installationDirectory) =>
        NormalizeDirectory(installationDirectory) + JournalSuffix;

    /// <summary>
    /// Creates the initial journal record.
    /// </summary>
    private static UpdateOperationJournal CreateJournal(
            UpdateTransactionCommand command,
            string installationDirectory,
            string? stagingDirectory,
            string previousDirectory,
            string? swapDirectory,
            UpdateTransactionStage stage,
            string message)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new UpdateOperationJournal
        {
            OperationId = Guid.NewGuid(),
            Command = command,
            Stage = stage,
            InstallationDirectory = installationDirectory,
            StagingDirectory = stagingDirectory,
            PreviousDirectory = previousDirectory,
            SwapDirectory = swapDirectory,
            StartedUtc = now,
            UpdatedUtc = now,
            Message = message
        };
    }

    /// <summary>
    /// Advances and atomically persists a transaction state.
    /// </summary>
    private static UpdateOperationJournal Advance(
            UpdateOperationJournal journal,
            UpdateTransactionStage stage,
            string message)
    {
        UpdateOperationJournal updated = journal with
        {
            Stage = stage,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Message = message
        };

        WriteJournal(updated);
        return updated;
    }

    /// <summary>
    /// Persists the journal with replace semantics so a partial JSON file is never authoritative.
    /// </summary>
    private static void WriteJournal(
            UpdateOperationJournal journal)
    {
        string path = GetJournalPath(journal.InstallationDirectory);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(journal, JsonOptions),
                Encoding.UTF8);

            if (File.Exists(path))
            {
                File.Replace(
                    temporaryPath,
                    path,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Reads and validates the durable operation journal.
    /// </summary>
    private static UpdateOperationJournal ReadJournal(
            string path)
    {
        UpdateOperationJournal? journal = JsonSerializer.Deserialize<UpdateOperationJournal>(
            File.ReadAllText(path, Encoding.UTF8),
            JsonOptions);

        if (journal is null ||
            journal.JournalSchemaVersion != UpdateOperationJournal.CurrentJournalSchemaVersion)
        {
            throw new InvalidDataException(
                "Le journal de mise à jour est vide ou utilise une version inconnue.");
        }

        return journal;
    }

    /// <summary>
    /// Marks an operation failed without hiding the original exception if journal persistence also fails.
    /// </summary>
    private static void TryMarkFailed(
            UpdateOperationJournal journal,
            string message)
    {
        try
        {
            Advance(journal, UpdateTransactionStage.Failed, message);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Throws a deterministic interruption after a persisted stage for recovery testing.
    /// </summary>
    private static void Checkpoint(
            UpdateTransactionStage stage,
            UpdateTransactionStage? interruptAfterStage)
    {
        if (interruptAfterStage == stage)
        {
            throw new UpdateSimulationInterruptionException(stage);
        }
    }

    /// <summary>
    /// Moves a failed or unvalidated installation aside without deleting its forensic contents.
    /// </summary>
    private static void MoveAsideFailedDirectory(
            string installationDirectory)
    {
        string failedDirectory =
            installationDirectory +
            $".failed-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";

        Directory.Move(installationDirectory, failedDirectory);
    }

    /// <summary>
    /// Removes only old failed/swap artifacts after a verified successful operation.
    /// </summary>
    private static void CleanupOldArtifacts(
            string installationDirectory)
    {
        string? parent = Path.GetDirectoryName(installationDirectory);

        if (string.IsNullOrWhiteSpace(parent) ||
            !Directory.Exists(parent))
        {
            return;
        }

        string installationName = Path.GetFileName(installationDirectory);
        DateTime threshold = DateTime.UtcNow.AddDays(-7);

        foreach (string directory in Directory.EnumerateDirectories(
                     parent,
                     installationName + ".failed-*",
                     SearchOption.TopDirectoryOnly)
                 .Concat(
                     Directory.EnumerateDirectories(
                         parent,
                         installationName + ".rollback-*",
                         SearchOption.TopDirectoryOnly)))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < threshold)
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Validates that the application executable exists.
    /// </summary>
    private static void EnsureApplicationPayload(
            string directory)
    {
        if (!HasApplicationPayload(directory))
        {
            throw new InvalidDataException(
                $"Le dossier '{directory}' ne contient pas une installation Nodalis exécutable.");
        }
    }

    /// <summary>
    /// Returns whether a directory contains the Nodalis application executable.
    /// </summary>
    private static bool HasApplicationPayload(
            string directory) =>
        Directory.Exists(directory) &&
        File.Exists(Path.Combine(directory, ApplicationExecutableName));

    /// <summary>
    /// Normalizes a directory path.
    /// </summary>
    private static string NormalizeDirectory(
            string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return Path.GetFullPath(directory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Normalizes and validates a manifest-relative file path.
    /// </summary>
    private static string NormalizeRelativePath(
            string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "Le manifeste contient un chemin de fichier invalide.");
        }

        string normalized = relativePath.Replace(
            '/',
            Path.DirectorySeparatorChar);

        if (normalized.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"Le manifeste contient un chemin dangereux : '{relativePath}'.");
        }

        return normalized;
    }

    /// <summary>
    /// Determines whether a path remains under a root.
    /// </summary>
    private static bool IsSameOrChildPath(
            string root,
            string candidate)
    {
        string normalizedRoot = NormalizeDirectory(root);
        string normalizedCandidate = Path.GetFullPath(candidate);

        return string.Equals(
                   normalizedRoot,
                   normalizedCandidate,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Starts a candidate with the smoke-test switch and requires a clean exit.
    /// </summary>
    private static bool ValidateStartupProcess(
            string installationDirectory,
            string executableName)
    {
        string executablePath = Path.Combine(
            installationDirectory,
            Path.GetFileName(executableName));

        if (!File.Exists(executablePath))
        {
            return false;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = installationDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--smoke-test");

        using Process? process = Process.Start(startInfo);

        if (process is null)
        {
            return false;
        }

        if (!process.WaitForExit(milliseconds: 30_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return false;
        }

        return process.ExitCode == 0;
    }

    /// <summary>
    /// Launches the requested executable normally after transaction validation.
    /// </summary>
    private static void LaunchProcess(
            string installationDirectory,
            string executableName)
    {
        string fileName = Path.GetFileName(executableName);

        if (!string.Equals(
                fileName,
                executableName,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException(
                "Le nom de l'exécutable à lancer est invalide.");
        }

        Process? process = Process.Start(
            new ProcessStartInfo(
                Path.Combine(installationDirectory, fileName))
            {
                WorkingDirectory = installationDirectory,
                UseShellExecute = true
            });

        if (process is null)
        {
            throw new InvalidOperationException(
                "Windows n'a pas pu relancer Nodalis.");
        }
    }
}

/// <summary>
/// Represents an intentional process interruption injected after a durable updater stage.
/// </summary>
public sealed class UpdateSimulationInterruptionException : Exception
{
    /// <summary>
    /// Initializes a deterministic interruption exception.
    /// </summary>
    /// <param name="stage">The stage after which interruption was simulated.</param>
    public UpdateSimulationInterruptionException(
            UpdateTransactionStage stage)
        : base(
            $"Interruption simulée après l'étape {stage}.")
    {
        Stage = stage;
    }

    public UpdateTransactionStage Stage { get; }
}
