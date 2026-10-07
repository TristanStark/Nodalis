using Nodalis.Infrastructure.Reliability;


/// <summary>
/// Smoke tests for local crash diagnostics and safe temporary-file recovery.
/// </summary>
internal static class CrashDiagnosticsSmokeTests
{
    /// <summary>
    /// Verifies abnormal-session detection, log retention, technical reporting and safe quarantine.
    /// </summary>
    /// <param name="root">The smoke-test root.</param>
    public static void Run(
            string root)
    {
        string testRoot = Path.Combine(
            root,
            "CrashDiagnostics");
        string diagnosticsRoot = Path.Combine(
            testRoot,
            "Diagnostics");
        string workspaceRoot = Path.Combine(
            testRoot,
            "Workspace");
        string documentDirectory = Path.Combine(
            workspaceRoot,
            "Documents");

        Directory.CreateDirectory(documentDirectory);

        string documentPath = Path.Combine(
            documentDirectory,
            "note.md");
        File.WriteAllText(
            documentPath,
            "# Valide\nSECRET-DOCUMENT-BODY\n");

        global::Nodalis.Infrastructure.Reliability.LocalDiagnosticsService firstSession =
            new LocalDiagnosticsService(
                diagnosticsRoot,
                maximumLogBytes: 1024,
                retainedLogFileCount: 3);

        firstSession.StartSession();

        Assert(
            !firstSession.PreviousSessionEndedUnexpectedly,
            "A fresh diagnostics directory must not report an abnormal previous shutdown.");

        for (int index = 0;
             index < 20;
             index++)
        {
            firstSession.LogInformation(
                $"rotation-{index:D2}-{new string('x', 180)}");
        }

        string orphanedTemporaryPath = Path.Combine(
            documentDirectory,
            $".note.md.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(
            orphanedTemporaryPath,
            "RECOVERABLE-TEMPORARY-CONTENT");

        global::Nodalis.Infrastructure.Reliability.LocalDiagnosticsService secondSession =
            new LocalDiagnosticsService(
                diagnosticsRoot,
                maximumLogBytes: 1024,
                retainedLogFileCount: 3);

        secondSession.StartSession();

        Assert(
            secondSession.PreviousSessionEndedUnexpectedly,
            "An existing active-session marker must be detected as an abnormal previous shutdown.");

        IReadOnlyList<string> recovered =
            secondSession.RecoverOrphanedAtomicWriteFiles(
                workspaceRoot);

        Assert(
            recovered.Count == 1 &&
            !File.Exists(orphanedTemporaryPath) &&
            File.Exists(recovered[0]),
            "An orphaned atomic-write file must be quarantined outside the workspace.");

        Assert(
            File.ReadAllText(documentPath).Contains(
                "SECRET-DOCUMENT-BODY",
                StringComparison.Ordinal),
            "Temporary-file recovery must never overwrite the canonical document.");

        string report = secondSession.BuildTechnicalReport(
            workspaceRoot);

        Assert(
            report.Contains(
                "Arrêt précédent anormal détecté : oui",
                StringComparison.Ordinal) &&
            report.Contains(
                diagnosticsRoot,
                StringComparison.OrdinalIgnoreCase),
            "The technical report must expose abnormal-session state and diagnostics location.");

        Assert(
            !report.Contains(
                "SECRET-DOCUMENT-BODY",
                StringComparison.Ordinal),
            "The technical report must never read or include document contents.");

        Assert(
            RecoverableExceptionPolicy.CanContinue(
                new ArgumentOutOfRangeException(
                    "lineIndex")) &&
            RecoverableExceptionPolicy.CanContinue(
                new NullReferenceException(
                    "synthetic UI state")) &&
            RecoverableExceptionPolicy.CanContinue(
                new IOException(
                    "synthetic file failure")),
            "Common user-action failures must be classified as recoverable.");

        Assert(
            !RecoverableExceptionPolicy.CanContinue(
                new OutOfMemoryException()) &&
            !RecoverableExceptionPolicy.CanContinue(
                new AccessViolationException()) &&
            !RecoverableExceptionPolicy.CanContinue(
                new Exception(
                    "unclassified failure")),
            "Fatal or unknown process failures must never be silently marked safe to continue.");

        string errorId =
            secondSession.LogRecoverableException(
                "Smoke UI action · stale line",
                new ArgumentOutOfRangeException(
                    "lineIndex",
                    "synthetic stale source"));

        Assert(
            errorId.StartsWith(
                "NOD-",
                StringComparison.Ordinal) &&
            string.Equals(
                secondSession.ActiveLogPath,
                Path.Combine(
                    diagnosticsRoot,
                    "nodalis.log"),
                StringComparison.OrdinalIgnoreCase),
            "Recoverable failures must expose a stable error identifier and active local log path.");

        string retainedLogs =
            string.Join(
                Environment.NewLine,
                Directory
                    .EnumerateFiles(
                        diagnosticsRoot,
                        "nodalis*.log",
                        SearchOption.TopDirectoryOnly)
                    .Select(
                        File.ReadAllText));

        Assert(
            retainedLogs.Contains(
                errorId,
                StringComparison.Ordinal) &&
            retainedLogs.Contains(
                "Smoke UI action · stale line",
                StringComparison.Ordinal) &&
            retainedLogs.Contains(
                "ArgumentOutOfRangeException",
                StringComparison.Ordinal),
            "Recoverable UI failures must journal identifier, context, exception type, message and stack details.");

        int logFileCount = Directory
            .EnumerateFiles(
                diagnosticsRoot,
                "nodalis*.log",
                SearchOption.TopDirectoryOnly)
            .Count();

        Assert(
            logFileCount <= 3,
            "Local diagnostics logs must obey the configured retention bound.");

        secondSession.MarkCleanShutdown();

        global::Nodalis.Infrastructure.Reliability.LocalDiagnosticsService thirdSession =
            new LocalDiagnosticsService(
                diagnosticsRoot,
                maximumLogBytes: 1024,
                retainedLogFileCount: 3);

        thirdSession.StartSession();

        Assert(
            !thirdSession.PreviousSessionEndedUnexpectedly,
            "A clean shutdown must remove the active-session marker.");

        thirdSession.MarkCleanShutdown();
    }

    /// <summary>
    /// Throws when a smoke-test assertion fails.
    /// </summary>
    /// <param name="condition">The condition to verify.</param>
    /// <param name="message">The failure message.</param>
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
