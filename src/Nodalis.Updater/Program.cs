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
    /// Replaces the current installation with the previously validated staging directory.
    /// </summary>
    /// <param name="arguments">The validated command arguments.</param>
    /// <returns>Zero on success or one when the previous version had to be restored.</returns>
    private static int ApplyUpdate(
            UpdaterArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(
                arguments.StagingDirectory))
        {
            throw new InvalidDataException(
                "Le dossier de staging est obligatoire pour appliquer une mise à jour.");
        }

        string installationDirectory =
            NormalizeDirectory(
                arguments.InstallationDirectory);
        string stagingDirectory =
            NormalizeDirectory(
                arguments.StagingDirectory);

        EnsureApplicationPayload(
            stagingDirectory);

        string previousDirectory =
            installationDirectory +
            ".previous";

        bool currentMoved = false;

        try
        {
            if (Directory.Exists(
                    previousDirectory))
            {
                Directory.Delete(
                    previousDirectory,
                    recursive: true);
            }

            Directory.Move(
                installationDirectory,
                previousDirectory);
            currentMoved = true;

            Directory.Move(
                stagingDirectory,
                installationDirectory);

            EnsureApplicationPayload(
                installationDirectory);

            LaunchApplication(
                installationDirectory,
                arguments.LaunchExecutable);

            TryWriteLog(
                $"APPLY OK {installationDirectory}");

            return 0;
        }
        catch (Exception exception)
        {
            TryWriteLog(
                $"APPLY FAILED {exception}");

            RecoverPreviousInstallation(
                installationDirectory,
                previousDirectory,
                currentMoved);

            return 1;
        }
    }

    /// <summary>
    /// Swaps the current installation with the preserved previous version.
    /// </summary>
    /// <param name="arguments">The validated command arguments.</param>
    /// <returns>Zero on success or one when the rollback could not be completed.</returns>
    private static int Rollback(
            UpdaterArguments arguments)
    {
        string installationDirectory =
            NormalizeDirectory(
                arguments.InstallationDirectory);
        string previousDirectory =
            installationDirectory +
            ".previous";

        EnsureApplicationPayload(
            previousDirectory);

        string swapDirectory =
            installationDirectory +
            $".rollback-{Guid.NewGuid():N}";

        bool currentMoved = false;
        bool previousMoved = false;

        try
        {
            Directory.Move(
                installationDirectory,
                swapDirectory);
            currentMoved = true;

            Directory.Move(
                previousDirectory,
                installationDirectory);
            previousMoved = true;

            EnsureApplicationPayload(
                installationDirectory);

            Directory.Move(
                swapDirectory,
                previousDirectory);

            LaunchApplication(
                installationDirectory,
                arguments.LaunchExecutable);

            TryWriteLog(
                $"ROLLBACK OK {installationDirectory}");

            return 0;
        }
        catch (Exception exception)
        {
            TryWriteLog(
                $"ROLLBACK FAILED {exception}");

            RecoverRollbackSwap(
                installationDirectory,
                previousDirectory,
                swapDirectory,
                currentMoved,
                previousMoved);

            return 1;
        }
    }

    /// <summary>
    /// Restores the previous installation when an apply operation fails after the current directory has moved.
    /// </summary>
    /// <param name="installationDirectory">The intended application directory.</param>
    /// <param name="previousDirectory">The backup directory.</param>
    /// <param name="currentMoved">Whether the original installation was already moved.</param>
    private static void RecoverPreviousInstallation(
            string installationDirectory,
            string previousDirectory,
            bool currentMoved)
    {
        if (!currentMoved)
        {
            return;
        }

        try
        {
            if (Directory.Exists(
                    installationDirectory))
            {
                string failedDirectory =
                    installationDirectory +
                    $".failed-{Guid.NewGuid():N}";

                Directory.Move(
                    installationDirectory,
                    failedDirectory);
            }

            if (!Directory.Exists(
                    installationDirectory) &&
                Directory.Exists(
                    previousDirectory))
            {
                Directory.Move(
                    previousDirectory,
                    installationDirectory);

                if (File.Exists(
                        Path.Combine(
                            installationDirectory,
                            ApplicationExecutableName)))
                {
                    LaunchApplication(
                        installationDirectory,
                        ApplicationExecutableName);
                }
            }
        }
        catch (Exception recoveryException)
        {
            TryWriteLog(
                $"RECOVERY FAILED {recoveryException}");
        }
    }

    /// <summary>
    /// Restores the original directory layout when a rollback swap fails.
    /// </summary>
    /// <param name="installationDirectory">The intended application directory.</param>
    /// <param name="previousDirectory">The previous-version directory.</param>
    /// <param name="swapDirectory">The temporary current-version directory.</param>
    /// <param name="currentMoved">Whether the current version moved to the swap directory.</param>
    /// <param name="previousMoved">Whether the previous version moved into the application directory.</param>
    private static void RecoverRollbackSwap(
            string installationDirectory,
            string previousDirectory,
            string swapDirectory,
            bool currentMoved,
            bool previousMoved)
    {
        try
        {
            if (previousMoved &&
                Directory.Exists(
                    installationDirectory) &&
                !Directory.Exists(
                    previousDirectory))
            {
                Directory.Move(
                    installationDirectory,
                    previousDirectory);
            }

            if (currentMoved &&
                Directory.Exists(
                    swapDirectory) &&
                !Directory.Exists(
                    installationDirectory))
            {
                Directory.Move(
                    swapDirectory,
                    installationDirectory);
            }

            if (File.Exists(
                    Path.Combine(
                        installationDirectory,
                        ApplicationExecutableName)))
            {
                LaunchApplication(
                    installationDirectory,
                    ApplicationExecutableName);
            }
        }
        catch (Exception recoveryException)
        {
            TryWriteLog(
                $"ROLLBACK RECOVERY FAILED {recoveryException}");
        }
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
