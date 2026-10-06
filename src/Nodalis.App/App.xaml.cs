using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Migrations;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Migrations;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Settings;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.App;

public partial class App : Application
{
    /// <summary>
    /// Performs the <c>OnStartup</c> operation.
    /// </summary>
    /// <param name="e">The <c>e</c> value.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyNodalisWindowStyle));

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            if (e.Args.Any(argument =>
                    string.Equals(
                        argument,
                        "--smoke-test",
                        StringComparison.OrdinalIgnoreCase)))
            {
                if (TryFindResource(
                        "NodalisWindowStyle") is not Style)
                {
                    throw new InvalidOperationException(
                        "Le thème WPF Nodalis n'a pas pu être chargé.");
                }

                Shutdown(0);
                return;
            }

            global::Nodalis.Infrastructure.Settings.UserPreferencesStore preferencesStore = new UserPreferencesStore();
            global::Nodalis.Core.Settings.UserPreferences preferences = await preferencesStore.LoadAsync();

            (string WorkspacePath, global::Nodalis.Core.Settings.UserPreferences Preferences)? resolved = await ResolveWorkspaceAsync(
                preferences,
                preferencesStore);

            if (resolved is null)
            {
                Shutdown();
                return;
            }

            preferences = resolved.Value.Preferences;

            bool workspaceReady = await EnsureWorkspaceSchemaAsync(
                resolved.Value.WorkspacePath);

            if (!workspaceReady)
            {
                Shutdown();
                return;
            }

            global::Nodalis.Infrastructure.Templates.FileSystemTemplateStore templateStore = new FileSystemTemplateStore(
                resolved.Value.WorkspacePath);
            await templateStore.InitializeDefaultsAsync();

            global::Nodalis.Infrastructure.Navigation.WorkspaceNavigationBuilder navigationBuilder = new WorkspaceNavigationBuilder();
            global::Nodalis.Core.Navigation.WorkspaceNavigationNode root = await navigationBuilder.BuildAsync(
                resolved.Value.WorkspacePath);

            global::Nodalis.App.MainWindow window = new MainWindow(
                root,
                preferences,
                preferencesStore,
                templateStore);

            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Nodalis n'a pas pu démarrer.\n\n{exception.Message}",
                "Erreur Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    /// <summary>
    /// Performs the <c>ApplyNodalisWindowStyle</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void ApplyNodalisWindowStyle(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not Window window ||
            window.ReadLocalValue(FrameworkElement.StyleProperty) !=
            DependencyProperty.UnsetValue ||
            TryFindResource("NodalisWindowStyle") is not Style style)
        {
            return;
        }

        // WPF resolves an implicit Window style against the concrete runtime
        // type. Nodalis dialogs are derived Window classes, so without this
        // explicit application their surface can fall back to the light
        // system background while child controls still use the dark theme.
        window.Style = style;
    }


    /// <summary>
    /// Ensures that the selected workspace uses the current schema before normal services open it.
    /// </summary>
    /// <param name="workspacePath">The selected workspace path.</param>
    /// <returns>True when startup may continue.</returns>
    private static async Task<bool> EnsureWorkspaceSchemaAsync(
            string workspacePath)
    {
        global::Nodalis.Infrastructure.Migrations.WorkspaceMigrationService migrationService =
            new WorkspaceMigrationService(
                workspacePath);
        global::Nodalis.Core.Migrations.WorkspaceMigrationPreflight preflight =
            await migrationService.PreflightAsync();

        if (!preflight.CanMigrate)
        {
            throw new InvalidDataException(
                "Le workspace ne peut pas être ouvert avec cette version de Nodalis.\n\n" +
                string.Join(
                    Environment.NewLine,
                    preflight.Errors));
        }

        if (!preflight.MigrationRequired)
        {
            return true;
        }

        string stepText = string.Join(
            Environment.NewLine,
            preflight.Steps.Select(
                step =>
                    $"• schéma {step.FromVersion} → {step.ToVersion} : {step.Description}"));

        string backupText = preflight.BackupRequired
            ? "Une sauvegarde ZIP vérifiée sera créée hors du workspace avant toute modification."
            : "Une sauvegarde de migration est recommandée.";

        global::System.Windows.MessageBoxResult answer = MessageBox.Show(
            $"Ce workspace utilise le schéma {preflight.DetectedSchemaVersion} et Nodalis utilise le schéma {preflight.TargetSchemaVersion}.\n\n" +
            $"{stepText}\n\n{backupText}\n\nContinuer la migration ?",
            "Migration du workspace",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        global::Nodalis.Core.Migrations.WorkspaceMigrationReport report =
            await migrationService.MigrateAsync(
                migrationApproved: true);

        string reportText = report.ReportPath is null
            ? "Rapport non persisté, mais disponible pour cette session."
            : $"Rapport : {report.ReportPath}";

        MessageBox.Show(
            "Migration terminée avec succès.\n\n" +
            $"Sauvegarde : {report.BackupArchivePath}\n" +
            reportText,
            "Migration terminée",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return true;
    }

    /// <summary>
    /// Performs the <c>ResolveWorkspaceAsync</c> operation.
    /// </summary>
    /// <param name="preferences">The <c>preferences</c> value.</param>
    /// <param name="preferencesStore">The <c>preferencesStore</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<(string WorkspacePath, UserPreferences Preferences)?>
            ResolveWorkspaceAsync(
                UserPreferences preferences,
                UserPreferencesStore preferencesStore)
    {
        if (!string.IsNullOrWhiteSpace(preferences.WorkspaceRootPath))
        {
            string configuredPath = Path.GetFullPath(
                preferences.WorkspaceRootPath);

            if (Directory.Exists(configuredPath) &&
                File.Exists(Path.Combine(
                    configuredPath,
                    WorkspaceLayout.WorkspaceManifestFileName)))
            {
                return (configuredPath, preferences);
            }
        }

        global::Microsoft.Win32.OpenFolderDialog dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier du workspace Nodalis",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string workspacePath = Path.GetFullPath(dialog.FolderName);
        string manifestPath = Path.Combine(
            workspacePath,
            WorkspaceLayout.WorkspaceManifestFileName);

        if (!File.Exists(manifestPath))
        {
            bool containsFiles = Directory
                .EnumerateFileSystemEntries(workspacePath)
                .Any();

            if (containsFiles)
            {
                global::System.Windows.MessageBoxResult answer = MessageBox.Show(
                    "Ce dossier n'est pas encore un workspace Nodalis. " +
                    "Nodalis peut l'initialiser sans supprimer ni modifier " +
                    "les fichiers déjà présents. Continuer ?",
                    "Initialiser le workspace",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                {
                    return null;
                }
            }

            string directoryName = new DirectoryInfo(workspacePath).Name;
            string workspaceName = string.IsNullOrWhiteSpace(directoryName)
                ? "Nodalis"
                : directoryName;

            global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore workspaceStore = new FileSystemWorkspaceStore(
                workspacePath);

            await workspaceStore.InitializeAsync(workspaceName);
        }

        global::Nodalis.Core.Settings.UserPreferences updatedPreferences = preferences with
        {
            WorkspaceRootPath = workspacePath
        };

        await preferencesStore.SaveAsync(updatedPreferences);

        return (workspacePath, updatedPreferences);
    }
}
