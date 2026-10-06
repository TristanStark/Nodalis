using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Nodalis.Infrastructure.Reliability;

/// <summary>
/// Provides bounded local diagnostics, abnormal-shutdown detection and safe recovery
/// of orphaned atomic-write temporary files.
/// </summary>
public sealed class LocalDiagnosticsService
{
    private const long DefaultMaximumLogBytes = 512 * 1024;
    private const int DefaultRetainedLogFileCount = 5;
    private const int TechnicalReportLogLineCount = 200;
    private const string ActiveLogFileName = "nodalis.log";
    private const string SessionMarkerFileName = "session.active";

    private readonly object _gate = new();
    private readonly long _maximumLogBytes;
    private readonly int _retainedLogFileCount;
    private readonly global::System.Collections.Generic.List<string> _recoveredTemporaryFiles = [];
    private bool _sessionStarted;

    /// <summary>
    /// Initializes a new instance of <see cref="LocalDiagnosticsService"/>.
    /// </summary>
    /// <param name="diagnosticsDirectory">Optional diagnostics directory override, primarily for tests.</param>
    /// <param name="maximumLogBytes">Maximum size of the active log before rotation.</param>
    /// <param name="retainedLogFileCount">Maximum number of retained log files, including the active log.</param>
    public LocalDiagnosticsService(
            string? diagnosticsDirectory = null,
            long maximumLogBytes = DefaultMaximumLogBytes,
            int retainedLogFileCount = DefaultRetainedLogFileCount)
    {
        if (maximumLogBytes < 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLogBytes),
                "The maximum log size must be at least 1024 bytes.");
        }

        if (retainedLogFileCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedLogFileCount),
                "At least one log file must be retained.");
        }

        DiagnosticsDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(diagnosticsDirectory)
                ? BuildDefaultDiagnosticsDirectory()
                : diagnosticsDirectory);
        _maximumLogBytes = maximumLogBytes;
        _retainedLogFileCount = retainedLogFileCount;
    }

    /// <summary>
    /// Gets the local diagnostics directory.
    /// </summary>
    public string DiagnosticsDirectory { get; }

    /// <summary>
    /// Gets a value indicating whether the previous Nodalis process did not complete a clean shutdown.
    /// </summary>
    public bool PreviousSessionEndedUnexpectedly { get; private set; }

    /// <summary>
    /// Gets a snapshot of temporary files recovered during the current session.
    /// </summary>
    public IReadOnlyList<string> RecoveredTemporaryFiles
    {
        get
        {
            lock (_gate)
            {
                return _recoveredTemporaryFiles.ToArray();
            }
        }
    }

    /// <summary>
    /// Starts the diagnostics session and creates the clean-shutdown marker.
    /// </summary>
    public void StartSession()
    {
        lock (_gate)
        {
            if (_sessionStarted)
            {
                return;
            }

            _sessionStarted = true;

            try
            {
                Directory.CreateDirectory(DiagnosticsDirectory);

                string markerPath = GetSessionMarkerPath();
                PreviousSessionEndedUnexpectedly = File.Exists(markerPath);

                if (PreviousSessionEndedUnexpectedly)
                {
                    WriteEventUnsafe(
                        "WARN",
                        "Un marqueur de session existant indique un arrêt anormal précédent.",
                        exception: null);
                }

                string marker =
                    $"startedUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}" +
                    $"processId={Environment.ProcessId}{Environment.NewLine}" +
                    $"version={GetApplicationVersion()}{Environment.NewLine}";

                File.WriteAllText(
                    markerPath,
                    marker,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                WriteEventUnsafe(
                    "INFO",
                    "Session Nodalis démarrée.",
                    exception: null);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                PreviousSessionEndedUnexpectedly = false;
                WriteEventUnsafe(
                    "WARN",
                    "Le service de diagnostics local n'a pas pu initialiser son stockage.",
                    exception);
            }
        }
    }

    /// <summary>
    /// Marks the current process as cleanly stopped and removes the session marker.
    /// </summary>
    public void MarkCleanShutdown()
    {
        lock (_gate)
        {
            if (!_sessionStarted)
            {
                return;
            }

            WriteEventUnsafe(
                "INFO",
                "Session Nodalis arrêtée proprement.",
                exception: null);

            try
            {
                string markerPath = GetSessionMarkerPath();

                if (File.Exists(markerPath))
                {
                    File.Delete(markerPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                WriteEventUnsafe(
                    "WARN",
                    "Le marqueur de session n'a pas pu être supprimé.",
                    exception);
            }

            _sessionStarted = false;
        }
    }

    /// <summary>
    /// Writes an informational entry to the bounded local journal.
    /// </summary>
    /// <param name="message">The message to record.</param>
    public void LogInformation(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_gate)
        {
            WriteEventUnsafe(
                "INFO",
                message,
                exception: null);
        }
    }

    /// <summary>
    /// Writes an exception entry to the bounded local journal.
    /// </summary>
    /// <param name="source">The component or global exception source.</param>
    /// <param name="exception">The captured exception.</param>
    public void LogException(
            string source,
            Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);

        lock (_gate)
        {
            WriteEventUnsafe(
                "ERROR",
                source,
                exception);
        }
    }

    /// <summary>
    /// Moves orphaned Nodalis atomic-write temporary files outside the workspace for manual recovery.
    /// Existing canonical files are never replaced.
    /// </summary>
    /// <param name="workspaceRoot">The writable workspace root to inspect.</param>
    /// <returns>The quarantine paths created during this call.</returns>
    public IReadOnlyList<string> RecoverOrphanedAtomicWriteFiles(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);

        if (!Directory.Exists(fullWorkspaceRoot))
        {
            return [];
        }

        global::System.Collections.Generic.List<string> recovered = [];

        try
        {
            string[] temporaryFiles = Directory
                .EnumerateFiles(
                    fullWorkspaceRoot,
                    "*.tmp",
                    SearchOption.AllDirectories)
                .Where(IsAtomicWriteTemporaryFile)
                .ToArray();

            if (temporaryFiles.Length == 0)
            {
                return recovered;
            }

            string recoveryBatchDirectory = Path.Combine(
                DiagnosticsDirectory,
                "Recovery",
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));

            foreach (string temporaryFile in temporaryFiles)
            {
                string relativePath = Path.GetRelativePath(
                    fullWorkspaceRoot,
                    temporaryFile);
                string destinationPath = Path.Combine(
                    recoveryBatchDirectory,
                    relativePath);
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                if (File.Exists(destinationPath))
                {
                    destinationPath += $".{Guid.NewGuid():N}";
                }

                File.Move(
                    temporaryFile,
                    destinationPath);

                recovered.Add(destinationPath);
            }

            lock (_gate)
            {
                _recoveredTemporaryFiles.AddRange(recovered);
                WriteEventUnsafe(
                    "WARN",
                    $"{recovered.Count} fichier(s) temporaire(s) d'écriture atomique ont été mis en quarantaine.",
                    exception: null);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            LogException(
                "Récupération des fichiers temporaires du workspace",
                exception);
        }

        return recovered;
    }

    /// <summary>
    /// Builds a local technical report suitable for explicit user copy/paste.
    /// Document contents are never read by this operation.
    /// </summary>
    /// <param name="workspaceRoot">Optional workspace root for context.</param>
    /// <returns>The technical report text.</returns>
    public string BuildTechnicalReport(
            string? workspaceRoot = null)
    {
        global::System.Text.StringBuilder builder = new StringBuilder();

        builder.AppendLine("Nodalis — rapport technique local");
        builder.AppendLine($"Généré UTC : {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Version : {GetApplicationVersion()}");
        builder.AppendLine($"OS : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Runtime : {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Architecture processus : {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Processus : {Environment.ProcessId}");
        builder.AppendLine(
            $"Arrêt précédent anormal détecté : {(PreviousSessionEndedUnexpectedly ? "oui" : "non")}");
        builder.AppendLine($"Dossier diagnostics : {DiagnosticsDirectory}");

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            builder.AppendLine($"Workspace : {Path.GetFullPath(workspaceRoot)}");
        }

        IReadOnlyList<string> recovered = RecoveredTemporaryFiles;
        builder.AppendLine($"Fichiers temporaires récupérés : {recovered.Count}");

        foreach (string recoveredPath in recovered)
        {
            builder.AppendLine($"- {recoveredPath}");
        }

        builder.AppendLine();
        builder.AppendLine($"Dernières lignes de journal (max {TechnicalReportLogLineCount}) :");

        string[] logLines = ReadRecentLogLines(
            TechnicalReportLogLineCount);

        if (logLines.Length == 0)
        {
            builder.AppendLine("(journal vide)");
        }
        else
        {
            foreach (string line in logLines)
            {
                builder.AppendLine(line);
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Ce rapport est généré localement. Nodalis ne l'envoie à aucun service.");

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether a file name matches the private naming pattern used by <see cref="AtomicFileWriter"/>.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <returns>True when the file is a Nodalis atomic-write temporary file.</returns>
    internal static bool IsAtomicWriteTemporaryFile(
            string path)
    {
        string fileName = Path.GetFileName(path);

        if (!fileName.StartsWith(
                ".",
                StringComparison.Ordinal) ||
            !fileName.EndsWith(
                ".tmp",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string core = fileName[1..^4];
        int separatorIndex = core.LastIndexOf('.');

        if (separatorIndex <= 0 ||
            separatorIndex == core.Length - 1)
        {
            return false;
        }

        string token = core[(separatorIndex + 1)..];

        return Guid.TryParseExact(
            token,
            "N",
            out _);
    }

    /// <summary>
    /// Builds the default per-user diagnostics directory.
    /// </summary>
    /// <returns>The default diagnostics directory.</returns>
    private static string BuildDefaultDiagnosticsDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData = Path.GetTempPath();
        }

        return Path.Combine(
            localApplicationData,
            "Nodalis",
            "Diagnostics");
    }

    /// <summary>
    /// Gets the clean-shutdown marker path.
    /// </summary>
    /// <returns>The marker path.</returns>
    private string GetSessionMarkerPath() =>
        Path.Combine(
            DiagnosticsDirectory,
            SessionMarkerFileName);

    /// <summary>
    /// Writes one journal entry while the caller holds the service lock.
    /// This method intentionally swallows diagnostics failures so logging cannot crash Nodalis.
    /// </summary>
    /// <param name="level">The event level.</param>
    /// <param name="message">The event message.</param>
    /// <param name="exception">Optional captured exception.</param>
    private void WriteEventUnsafe(
            string level,
            string message,
            Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            RotateIfRequiredUnsafe();

            global::System.Text.StringBuilder builder = new StringBuilder();
            builder.Append(
                $"[{DateTimeOffset.UtcNow:O}] [{level}] {message}");

            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            builder.AppendLine();

            File.AppendAllText(
                Path.Combine(
                    DiagnosticsDirectory,
                    ActiveLogFileName),
                builder.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Diagnostics must never become a second crash source.
        }
    }

    /// <summary>
    /// Rotates the active log when it reaches its configured size.
    /// </summary>
    private void RotateIfRequiredUnsafe()
    {
        string activePath = Path.Combine(
            DiagnosticsDirectory,
            ActiveLogFileName);

        if (!File.Exists(activePath) ||
            new FileInfo(activePath).Length < _maximumLogBytes)
        {
            return;
        }

        if (_retainedLogFileCount == 1)
        {
            File.Delete(activePath);
            return;
        }

        int maximumArchiveIndex = _retainedLogFileCount - 1;
        string oldestPath = GetArchivedLogPath(
            maximumArchiveIndex);

        if (File.Exists(oldestPath))
        {
            File.Delete(oldestPath);
        }

        for (int index = maximumArchiveIndex - 1;
             index >= 1;
             index--)
        {
            string sourcePath = GetArchivedLogPath(index);

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            File.Move(
                sourcePath,
                GetArchivedLogPath(index + 1));
        }

        File.Move(
            activePath,
            GetArchivedLogPath(1));
    }

    /// <summary>
    /// Gets the path of a rotated log archive.
    /// </summary>
    /// <param name="index">The one-based archive index.</param>
    /// <returns>The archived log path.</returns>
    private string GetArchivedLogPath(
            int index) =>
        Path.Combine(
            DiagnosticsDirectory,
            $"nodalis.{index}.log");

    /// <summary>
    /// Reads the newest journal lines across retained log files.
    /// </summary>
    /// <param name="maximumLineCount">Maximum number of lines to return.</param>
    /// <returns>The newest journal lines in chronological order.</returns>
    private string[] ReadRecentLogLines(
            int maximumLineCount)
    {
        try
        {
            string[] logPaths = Directory
                .EnumerateFiles(
                    DiagnosticsDirectory,
                    "nodalis*.log",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path =>
                    File.GetLastWriteTimeUtc(path))
                .ToArray();

            global::System.Collections.Generic.List<string> lines = [];

            foreach (string logPath in logPaths)
            {
                lines.AddRange(
                    File.ReadAllLines(logPath));
            }

            return lines
                .TakeLast(maximumLineCount)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Returns the informational version of the running Nodalis assembly.
    /// </summary>
    /// <returns>The application version.</returns>
    private static string GetApplicationVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ??
            Assembly.GetExecutingAssembly();

        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "inconnue";
    }
}
