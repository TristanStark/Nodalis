using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Domain;
using Nodalis.Core.Updates;
using Nodalis.Infrastructure.Updates;

namespace Nodalis.App;

public partial class MainWindow
{
    private const string UpdaterExecutableName =
        "Nodalis.Updater.exe";
    private const string ApplicationExecutableName =
        "Nodalis.exe";

    /// <summary>
    /// Opens the local release archive picker and starts an offline update after full validation.
    /// </summary>
    /// <param name="sender">The update button.</param>
    /// <param name="e">The routed event.</param>
    private async void OpenUpdate_Click(
            object sender,
            RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Mise à jour",
            OpenUpdateAsync);
    }

    /// <summary>
    /// Executes the local update workflow behind the common UI error boundary.
    /// </summary>
    /// <returns>A task representing the update workflow.</returns>
    private async Task OpenUpdateAsync()
    {
        if (!EnsureApplicationWorkspaceSeparation())
        {
            return;
        }

        OpenFileDialog dialog = new OpenFileDialog
        {
            Title =
                "Sélectionner une archive de release Nodalis",
            Filter =
                "Archive Nodalis (*.zip)|*.zip",
            CheckFileExists =
                true,
            Multiselect =
                false
        };

        if (dialog.ShowDialog(
                this) != true)
        {
            return;
        }

        string installationDirectory =
            GetInstallationDirectory();

        LocalReleasePackageService service =
            new LocalReleasePackageService();

        StatusText.Text =
            "Validation de la release locale…";

        StagedReleasePackage package;

        try
        {
            package =
                await service.ValidateAndStageAsync(
                    dialog.FileName,
                    installationDirectory,
                    GetCurrentApplicationVersion(),
                    WorkspaceManifest.CurrentSchemaVersion);
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinue(
                exception))
        {
            ReportRecoverableUiError(
                "Mise à jour · validation de l'archive",
                exception);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Release {package.Manifest.Version} validée.\n\n" +
            "Produit, compatibilité du workspace, liste des fichiers et SHA-256 sont conformes. " +
            "Nodalis va enregistrer les documents ouverts, se fermer, installer la release puis se relancer.\n\n" +
            "Continuer ?",
            "Installer la mise à jour",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            TryDeleteStagingDirectory(
                package.StagedApplicationDirectory);

            StatusText.Text =
                "Mise à jour annulée";
            return;
        }

        try
        {
            await FlushAllDocumentTabsAsync();

            string launcherPath =
                PrepareUpdaterLauncher(
                    installationDirectory);

            StartUpdater(
                launcherPath,
                "apply",
                installationDirectory,
                package.StagedApplicationDirectory);

            Application.Current.Shutdown();
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinue(
                exception))
        {
            TryDeleteStagingDirectory(
                package.StagedApplicationDirectory);

            ReportRecoverableUiError(
                "Mise à jour · lancement",
                exception);
        }
    }

    /// <summary>
    /// Starts a rollback to the application version preserved by the previous successful update.
    /// </summary>
    /// <param name="sender">The rollback button.</param>
    /// <param name="e">The routed event.</param>
    private async void RollbackUpdate_Click(
            object sender,
            RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Rollback",
            RollbackUpdateAsync);
    }

    /// <summary>
    /// Executes the rollback workflow behind the common UI error boundary.
    /// </summary>
    /// <returns>A task representing the rollback workflow.</returns>
    private async Task RollbackUpdateAsync()
    {
        if (!EnsureApplicationWorkspaceSeparation())
        {
            return;
        }

        string installationDirectory =
            GetInstallationDirectory();

        if (!LocalReleasePackageService.CanRollback(
                installationDirectory))
        {
            MessageBox.Show(
                this,
                "Aucune version précédente exécutable n'est disponible.",
                "Rollback Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "Nodalis va enregistrer les documents ouverts, se fermer, restaurer la version précédente puis se relancer.\n\nLe workspace ne sera pas modifié. Continuer ?",
            "Rollback Nodalis",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await FlushAllDocumentTabsAsync();

            string launcherPath =
                PrepareUpdaterLauncher(
                    installationDirectory);

            StartUpdater(
                launcherPath,
                "rollback",
                installationDirectory,
                stagingDirectory: null);

            Application.Current.Shutdown();
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinue(
                exception))
        {
            ReportRecoverableUiError(
                "Rollback · lancement",
                exception);
        }
    }

    /// <summary>
    /// Refuses binary replacement when the application directory and workspace overlap.
    /// </summary>
    /// <returns><see langword="true"/> when updating cannot affect workspace files.</returns>
    private bool EnsureApplicationWorkspaceSeparation()
    {
        string installationDirectory =
            GetInstallationDirectory();

        if (!LocalReleasePackageService.PathsOverlap(
                installationDirectory,
                _root.FullPath))
        {
            return true;
        }

        MessageBox.Show(
            this,
            "Le dossier de Nodalis et le workspace se chevauchent. " +
            "La mise à jour et le rollback sont désactivés dans cette configuration afin de garantir qu'aucun fichier du workspace ne soit déplacé ou remplacé.\n\n" +
            "Déplacez l'application hors du workspace puis relancez Nodalis.",
            "Sécurité de mise à jour",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    /// <summary>
    /// Copies the bundled standalone updater to a local launcher directory outside the application tree.
    /// </summary>
    /// <param name="installationDirectory">The current application directory.</param>
    /// <returns>The path of the launcher copy.</returns>
    private static string PrepareUpdaterLauncher(
            string installationDirectory)
    {
        string sourcePath = Path.Combine(
            installationDirectory,
            UpdaterExecutableName);

        if (!File.Exists(
                sourcePath))
        {
            throw new InvalidOperationException(
                "Nodalis.Updater.exe est absent de cette installation.");
        }

        string localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(
                localApplicationData))
        {
            throw new InvalidOperationException(
                "Windows n'a pas fourni de dossier LocalApplicationData.");
        }

        string launcherDirectory = Path.Combine(
            localApplicationData,
            "Nodalis",
            "Updater",
            "Launcher");

        Directory.CreateDirectory(
            launcherDirectory);

        string launcherPath = Path.Combine(
            launcherDirectory,
            $"Nodalis.Updater-{Guid.NewGuid():N}.exe");

        File.Copy(
            sourcePath,
            launcherPath,
            overwrite: false);

        return launcherPath;
    }

    /// <summary>
    /// Starts the standalone updater and instructs it to wait for the current Nodalis process.
    /// </summary>
    /// <param name="launcherPath">The external updater executable.</param>
    /// <param name="command">The updater command.</param>
    /// <param name="installationDirectory">The current application directory.</param>
    /// <param name="stagingDirectory">The staged release directory for an apply command.</param>
    private static void StartUpdater(
            string launcherPath,
            string command,
            string installationDirectory,
            string? stagingDirectory)
    {
        ProcessStartInfo startInfo =
            new ProcessStartInfo(
                launcherPath)
            {
                UseShellExecute =
                    false,
                WorkingDirectory =
                    Path.GetDirectoryName(
                        launcherPath) ??
                    Environment.CurrentDirectory
            };

        startInfo.ArgumentList.Add(
            command);
        startInfo.ArgumentList.Add(
            "--wait-pid");
        startInfo.ArgumentList.Add(
            Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(
            "--install-dir");
        startInfo.ArgumentList.Add(
            installationDirectory);
        startInfo.ArgumentList.Add(
            "--launch");
        startInfo.ArgumentList.Add(
            ApplicationExecutableName);

        if (!string.IsNullOrWhiteSpace(
                stagingDirectory))
        {
            startInfo.ArgumentList.Add(
                "--staging-dir");
            startInfo.ArgumentList.Add(
                stagingDirectory);
        }

        Process? process =
            Process.Start(
                startInfo);

        if (process is null)
        {
            throw new InvalidOperationException(
                "Windows n'a pas pu démarrer Nodalis.Updater.exe.");
        }
    }

    /// <summary>
    /// Gets the normalized current application directory.
    /// </summary>
    /// <returns>The application directory.</returns>
    private static string GetInstallationDirectory() =>
        Path.GetFullPath(
            AppContext.BaseDirectory)
        .TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Gets the running assembly version used to reject non-upgrade release packages.
    /// </summary>
    /// <returns>The running application version.</returns>
    private static string GetCurrentApplicationVersion() =>
        Assembly.GetEntryAssembly()
            ?.GetName()
            .Version
            ?.ToString() ??
        "0.0.0";

    /// <summary>
    /// Removes a staged release when the user cancels before application shutdown.
    /// </summary>
    /// <param name="stagingDirectory">The staging directory.</param>
    private static void TryDeleteStagingDirectory(
            string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(
                    stagingDirectory))
            {
                Directory.Delete(
                    stagingDirectory,
                    recursive: true);
            }
        }
        catch
        {
        }
    }
}
