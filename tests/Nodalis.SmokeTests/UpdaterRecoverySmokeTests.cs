using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Updates;
using Nodalis.Updater;

internal static class UpdaterRecoverySmokeTests
{
    /// <summary>
    /// Simulates interruption after every durable apply stage and verifies deterministic recovery.
    /// </summary>
    /// <param name="root">The main smoke-test root.</param>
    public static async Task RunAsync(
            string root)
    {
        UpdateTransactionStage[] interruptionStages =
        [
            UpdateTransactionStage.Prepared,
            UpdateTransactionStage.ExistingPreviousRemoved,
            UpdateTransactionStage.CurrentMovedToPrevious,
            UpdateTransactionStage.CandidateMovedToCurrent,
            UpdateTransactionStage.CandidateFilesValidated,
            UpdateTransactionStage.CandidateStartupValidated
        ];

        foreach (UpdateTransactionStage stage in interruptionStages)
        {
            await VerifyInterruptedApplyRecoveryAsync(
                root,
                stage);
        }

        await VerifySuccessfulApplyAsync(
            root);
        await VerifyStartupFailureRollbackAsync(
            root);
    }

    /// <summary>
    /// Verifies one injected interruption and a subsequent journal-driven recovery pass.
    /// </summary>
    private static async Task VerifyInterruptedApplyRecoveryAsync(
            string root,
            UpdateTransactionStage stage)
    {
        string caseRoot = Path.Combine(
            root + "-UpdaterRecovery",
            stage.ToString());
        string installation = Path.Combine(
            caseRoot,
            "Nodalis");
        string staging = Path.Combine(
            caseRoot,
            "Staging");

        DeleteIfPresent(caseRoot);
        Directory.CreateDirectory(caseRoot);

        await CreateInstallationAsync(
            installation,
            "old");
        await CreateStagingAsync(
            staging,
            "new");

        UpdateTransactionEngine engine = CreateTestEngine(
            startupResult: true);
        bool interrupted = false;

        try
        {
            engine.Apply(
                installation,
                staging,
                "Nodalis.exe",
                stage);
        }
        catch (UpdateSimulationInterruptionException exception)
        {
            interrupted =
                exception.Stage ==
                stage;
        }

        Assert(
            interrupted,
            $"Updater must expose a deterministic interruption after {stage}.");

        UpdateTransactionEngine recoveryEngine = CreateTestEngine(
            startupResult: true);
        bool recovered = recoveryEngine.RecoverInterruptedTransaction(
            installation);

        Assert(
            recovered,
            $"Updater must recover a journal left at {stage}.");
        Assert(
            File.Exists(
                Path.Combine(
                    installation,
                    "Nodalis.exe")),
            $"Recovery after {stage} must restore an executable at the canonical installation path.");

        string currentText = await File.ReadAllTextAsync(
            Path.Combine(
                installation,
                "Nodalis.exe"));

        Assert(
            currentText ==
            "old",
            $"Recovery after {stage} must conservatively restore the last known-good version.");

        DeleteIfPresent(caseRoot);
    }

    /// <summary>
    /// Verifies a successful update retains the old executable for manual rollback.
    /// </summary>
    private static async Task VerifySuccessfulApplyAsync(
            string root)
    {
        string caseRoot =
            root + "-UpdaterSuccess";
        string installation = Path.Combine(
            caseRoot,
            "Nodalis");
        string staging = Path.Combine(
            caseRoot,
            "Staging");
        bool launched = false;

        DeleteIfPresent(caseRoot);
        Directory.CreateDirectory(caseRoot);

        await CreateInstallationAsync(
            installation,
            "old");
        await CreateStagingAsync(
            staging,
            "new");

        UpdateTransactionEngine engine =
            new UpdateTransactionEngine(
                (_, _) => true,
                (_, _) => launched = true,
                _ => { });

        int result = engine.Apply(
            installation,
            staging,
            "Nodalis.exe");

        Assert(
            result == 0 &&
            launched &&
            await File.ReadAllTextAsync(
                Path.Combine(
                    installation,
                    "Nodalis.exe")) ==
                "new",
            "A validated update must install and launch the new version.");

        Assert(
            File.Exists(
                Path.Combine(
                    installation + ".previous",
                    "Nodalis.exe")) &&
            await File.ReadAllTextAsync(
                Path.Combine(
                    installation + ".previous",
                    "Nodalis.exe")) ==
                "old",
            "A successful update must retain one known-good previous executable for rollback.");

        UpdateOperationJournal journal =
            JsonSerializer.Deserialize<UpdateOperationJournal>(
                await File.ReadAllTextAsync(
                    UpdateTransactionEngine.GetJournalPath(
                        installation))) ??
            throw new InvalidOperationException(
                "Updater journal was not readable.");

        Assert(
            journal.Stage ==
            UpdateTransactionStage.Completed,
            "A successful update must leave a completed durable journal.");

        DeleteIfPresent(caseRoot);
    }

    /// <summary>
    /// Verifies a candidate that cannot start is automatically rolled back.
    /// </summary>
    private static async Task VerifyStartupFailureRollbackAsync(
            string root)
    {
        string caseRoot =
            root + "-UpdaterStartupFailure";
        string installation = Path.Combine(
            caseRoot,
            "Nodalis");
        string staging = Path.Combine(
            caseRoot,
            "Staging");

        DeleteIfPresent(caseRoot);
        Directory.CreateDirectory(caseRoot);

        await CreateInstallationAsync(
            installation,
            "old");
        await CreateStagingAsync(
            staging,
            "new");

        UpdateTransactionEngine engine = CreateTestEngine(
            startupResult: false);

        int result = engine.Apply(
            installation,
            staging,
            "Nodalis.exe");

        Assert(
            result == 1 &&
            File.Exists(
                Path.Combine(
                    installation,
                    "Nodalis.exe")) &&
            await File.ReadAllTextAsync(
                Path.Combine(
                    installation,
                    "Nodalis.exe")) ==
                "old",
            "A candidate that fails startup validation must automatically restore the old executable.");

        DeleteIfPresent(caseRoot);
    }

    /// <summary>
    /// Creates a minimal currently installed version.
    /// </summary>
    private static async Task CreateInstallationAsync(
            string directory,
            string marker)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(
                directory,
                "Nodalis.exe"),
            marker);
        await File.WriteAllTextAsync(
            Path.Combine(
                directory,
                "Nodalis.Updater.exe"),
            "updater-" +
            marker);
    }

    /// <summary>
    /// Creates a staged candidate with a release manifest and exact hashes.
    /// </summary>
    private static async Task CreateStagingAsync(
            string directory,
            string marker)
    {
        Directory.CreateDirectory(directory);

        byte[] appBytes = Encoding.UTF8.GetBytes(marker);
        byte[] updaterBytes = Encoding.UTF8.GetBytes(
            "updater-" +
            marker);

        await File.WriteAllBytesAsync(
            Path.Combine(
                directory,
                "Nodalis.exe"),
            appBytes);
        await File.WriteAllBytesAsync(
            Path.Combine(
                directory,
                "Nodalis.Updater.exe"),
            updaterBytes);

        ReleaseManifest manifest =
            new ReleaseManifest
            {
                Product =
                    "Nodalis",
                Version =
                    "9.9.9",
                TargetRid =
                    "win-x64",
                MinimumWorkspaceSchemaVersion =
                    1,
                MaximumWorkspaceSchemaVersion =
                    1,
                PayloadDirectory =
                    "Nodalis-win-x64",
                Files =
                [
                    CreateEntry(
                        "Nodalis.exe",
                        appBytes),
                    CreateEntry(
                        "Nodalis.Updater.exe",
                        updaterBytes)
                ]
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                directory,
                "release-manifest.json"),
            JsonSerializer.Serialize(
                manifest));
    }

    /// <summary>
    /// Creates one exact release file entry.
    /// </summary>
    private static ReleaseFileEntry CreateEntry(
            string path,
            byte[] bytes) =>
        new ReleaseFileEntry
        {
            Path =
                path,
            Length =
                bytes.LongLength,
            Sha256 =
                Convert.ToHexString(
                        SHA256.HashData(
                            bytes))
                    .ToLowerInvariant()
        };

    /// <summary>
    /// Creates an updater engine whose process behavior is deterministic.
    /// </summary>
    private static UpdateTransactionEngine CreateTestEngine(
            bool startupResult) =>
        new UpdateTransactionEngine(
            (_, _) => startupResult,
            (_, _) => { },
            _ => { });

    /// <summary>
    /// Deletes a test directory when present.
    /// </summary>
    private static void DeleteIfPresent(
            string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }

    /// <summary>
    /// Throws when a smoke-test condition is false.
    /// </summary>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
