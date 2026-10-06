using System.Diagnostics;
using System.Text;

namespace Nodalis.Updater;

/// <summary>
/// Applies or rolls back a staged Nodalis installation after the GUI process exits.
/// </summary>
internal static class Program
{
    private const string ApplicationExecutableName = "Nodalis.exe";

    /// <summary>
    /// Executes the updater command.
    /// </summary>
    /// <param name="args">The updater command-line arguments.</param>
    /// <returns>Zero on success; a non-zero value when recovery was required or the command was invalid.</returns>
    public static int Main(
            string[] args)
    {
        try
        {
            UpdaterArguments parsed =
                UpdaterArguments.Parse(
                    args);

            WaitForProcessExit(
                parsed.WaitProcessId);

            return parsed.Command switch
            {
                "apply" => ApplyUpdate(
                    parsed),
                "rollback" => Rollback(
                    parsed),
                "recover" => Recover(
                    parsed),
                _ => throw new InvalidDataException(
                    $"Commande updater inconnue : {parsed.Command}.")
            };
        }
        catch (Exception exception)
        {
            TryWriteLog(
                $"FATAL {exception}");

            return 2;
        }
    }

    /// <summary>
    /// Applies a validated staged release through the journaled transaction engine.
    /// </summary>
    /// <param name="arguments">The validated command arguments.</param>
    /// <returns>Zero on success or one when automatic recovery was required.</returns>
    private static int ApplyUpdate(
            UpdaterArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(
                arguments.StagingDirectory))
        {
            throw new InvalidDataException(
                "Le dossier de staging est obligatoire pour appliquer une mise à jour.");
        }

        UpdateTransactionEngine engine = CreateTransactionEngine();

        return engine.Apply(
            arguments.InstallationDirectory,
            arguments.StagingDirectory,
            arguments.LaunchExecutable);
    }

    /// <summary>
    /// Rolls back to the retained previous version through the journaled transaction engine.
    /// </summary>
    /// <param name="arguments">The validated command arguments.</param>
    /// <returns>Zero on success or one when recovery was required.</returns>
    private static int Rollback(
            UpdaterArguments arguments)
    {
        UpdateTransactionEngine engine = CreateTransactionEngine();

        return engine.Rollback(
            arguments.InstallationDirectory,
            arguments.LaunchExecutable);
    }

    /// <summary>
    /// Recovers an interrupted transaction without attempting another update.
    /// </summary>
    /// <param name="arguments">The validated command arguments.</param>
    /// <returns>Zero when recovery succeeds or no transaction is pending.</returns>
    private static int Recover(
            UpdaterArguments arguments)
    {
        UpdateTransactionEngine engine = CreateTransactionEngine();
        engine.RecoverInterruptedTransaction(
            arguments.InstallationDirectory);

        return 0;
    }

    /// <summary>
    /// Creates the production transaction engine with updater diagnostics.
    /// </summary>
    /// <returns>The configured transaction engine.</returns>
    private static UpdateTransactionEngine CreateTransactionEngine() =>
        new UpdateTransactionEngine(
            ValidateApplicationStartup,
            LaunchApplication,
            TryWriteLog);

    /// <summary>
    /// Validates an application process with the same smoke-test contract used by CI.
    /// </summary>
    /// <param name="installationDirectory">The candidate installation directory.</param>
    /// <param name="launchExecutable">The executable to validate.</param>
    /// <returns>True when the process exits successfully within the startup timeout.</returns>
    private static bool ValidateApplicationStartup(
            string installationDirectory,
            string launchExecutable)
    {
        string executableName = Path.GetFileName(
            launchExecutable);

        if (!string.Equals(
                executableName,
                launchExecutable,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(
                executableName))
        {
            return false;
        }

        string executablePath = Path.Combine(
            installationDirectory,
            executableName);

        if (!File.Exists(
                executablePath))
        {
            return false;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo(
            executablePath)
        {
            WorkingDirectory =
                installationDirectory,
            UseShellExecute =
                false
        };
        startInfo.ArgumentList.Add(
            "--smoke-test");

        using Process? process = Process.Start(
            startInfo);

        if (process is null)
        {
            return false;
        }

        if (!process.WaitForExit(
                milliseconds: 30_000))
        {
            try
            {
                process.Kill(
                    entireProcessTree: true);
            }
            catch
            {
            }

            return false;
        }

        return process.ExitCode ==
            0;
    }

    /// <summary>
    /// Waits for the Nodalis GUI process to terminate before modifying application files.
    /// </summary>
    /// <param name="processId">The process identifier to wait for.</param>
    private static void WaitForProcessExit(
            int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using Process process =
                Process.GetProcessById(
                    processId);

            process.WaitForExit();
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>
    /// Starts the requested Nodalis executable from the restored or updated application directory.
    /// </summary>
    /// <param name="installationDirectory">The application directory.</param>
    /// <param name="launchExecutable">The executable file name.</param>
    private static void LaunchApplication(
            string installationDirectory,
            string launchExecutable)
    {
        string executableName =
            Path.GetFileName(
                launchExecutable);

        if (!string.Equals(
                executableName,
                launchExecutable,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(
                executableName))
        {
            throw new InvalidDataException(
                "Le nom de l'exécutable à relancer est invalide.");
        }

        string executablePath =
            Path.Combine(
                installationDirectory,
                executableName);

        if (!File.Exists(
                executablePath))
        {
            throw new FileNotFoundException(
                "L'exécutable Nodalis à relancer est introuvable.",
                executablePath);
        }

        Process.Start(
            new ProcessStartInfo(
                executablePath)
            {
                WorkingDirectory =
                    installationDirectory,
                UseShellExecute =
                    true
            });
    }

    /// <summary>
    /// Verifies that a directory contains a runnable Nodalis application.
    /// </summary>
    /// <param name="directory">The candidate application directory.</param>
    private static void EnsureApplicationPayload(
            string directory)
    {
        if (!Directory.Exists(
                directory) ||
            !File.Exists(
                Path.Combine(
                    directory,
                    ApplicationExecutableName)))
        {
            throw new InvalidDataException(
                $"Le dossier « {directory} » ne contient pas une installation Nodalis exécutable.");
        }
    }

    /// <summary>
    /// Normalizes an updater directory argument.
    /// </summary>
    /// <param name="directory">The source directory.</param>
    /// <returns>The absolute directory path without a trailing separator.</returns>
    private static string NormalizeDirectory(
            string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            directory);

        return Path.GetFullPath(
                directory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Writes updater diagnostics to the local user profile without touching the workspace.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    private static void TryWriteLog(
            string message)
    {
        try
        {
            string localApplicationData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(
                    localApplicationData))
            {
                return;
            }

            string directory = Path.Combine(
                localApplicationData,
                "Nodalis",
                "Updater");

            Directory.CreateDirectory(
                directory);

            File.AppendAllText(
                Path.Combine(
                    directory,
                    "updater.log"),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Holds updater command-line arguments.
    /// </summary>
    private sealed record UpdaterArguments
    {
        public required string Command { get; init; }

        public required string InstallationDirectory { get; init; }

        public string? StagingDirectory { get; init; }

        public required string LaunchExecutable { get; init; }

        public int WaitProcessId { get; init; }

        /// <summary>
        /// Parses and validates updater command-line arguments.
        /// </summary>
        /// <param name="args">The raw arguments.</param>
        /// <returns>The parsed arguments.</returns>
        public static UpdaterArguments Parse(
                string[] args)
        {
            if (args.Length == 0)
            {
                throw new InvalidDataException(
                    "Commande updater manquante.");
            }

            string command =
                args[0].Trim()
                    .ToLowerInvariant();

            Dictionary<string, string> values =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            for (int index = 1;
                 index < args.Length;
                 index += 2)
            {
                if (index + 1 >=
                    args.Length)
                {
                    throw new InvalidDataException(
                        $"Valeur manquante pour « {args[index]} ».");
                }

                values[args[index]] =
                    args[index + 1];
            }

            if (!values.TryGetValue(
                    "--install-dir",
                    out string? installationDirectory) ||
                string.IsNullOrWhiteSpace(
                    installationDirectory))
            {
                throw new InvalidDataException(
                    "--install-dir est obligatoire.");
            }

            values.TryGetValue(
                "--staging-dir",
                out string? stagingDirectory);

            string launchExecutable =
                values.TryGetValue(
                    "--launch",
                    out string? launchValue) &&
                !string.IsNullOrWhiteSpace(
                    launchValue)
                    ? launchValue
                    : ApplicationExecutableName;

            int waitProcessId = 0;

            if (values.TryGetValue(
                    "--wait-pid",
                    out string? waitValue) &&
                !int.TryParse(
                    waitValue,
                    out waitProcessId))
            {
                throw new InvalidDataException(
                    "--wait-pid doit être un identifiant de processus entier.");
            }

            return new UpdaterArguments
            {
                Command =
                    command,
                InstallationDirectory =
                    installationDirectory,
                StagingDirectory =
                    stagingDirectory,
                LaunchExecutable =
                    launchExecutable,
                WaitProcessId =
                    waitProcessId
            };
        }
    }
}
