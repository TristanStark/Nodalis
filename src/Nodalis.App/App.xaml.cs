using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Compatibility;
using Nodalis.Core.Migrations;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Compatibility;
using Nodalis.Infrastructure.Migrations;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Settings;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.App;

public partial class App : Application
{
    private ReadOnlyWorkspaceSnapshot? _readOnlyWorkspaceSnapshot;

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

            (string EffectiveWorkspacePath, bool IsReadOnly, string DisplayName, string? AccessMessage)? access =
                await PrepareWorkspaceAccessAsync(
                    resolved.Value.WorkspacePath);

            if (access is null)
            {
                Shutdown();
                return;
            }

            global::Nodalis.Infrastructure.Templates.FileSystemTemplateStore templateStore = new FileSystemTemplateStore(
                access.Value.EffectiveWorkspacePath);

            if (!access.Value.IsReadOnly)
            {
                await templateStore.InitializeDefaultsAsync();
            }

            global::Nodalis.Core.Navigation.WorkspaceNavigationNode root;

            if (access.Value.IsReadOnly)
            {
                global::Nodalis.Infrastructure.Compatibility.ReadOnlyWorkspaceNavigationBuilder navigationBuilder =
                    new ReadOnlyWorkspaceNavigationBuilder();
                root = await navigationBuilder.BuildAsync(
                    access.Value.EffectiveWorkspacePath,
                    access.Value.DisplayName);
            }
            else
            {
                global::Nodalis.Infrastructure.Navigation.WorkspaceNavigationBuilder navigationBuilder =
                    new WorkspaceNavigationBuilder();
                root = await navigationBuilder.BuildAsync(
                    access.Value.EffectiveWorkspacePath);
            }

            global::Nodalis.App.MainWindow window = new MainWindow(
                root,
                preferences,
                preferencesStore,
                templateStore,
                access.Value.IsReadOnly,
                access.Value.AccessMessage);

            if (_readOnlyWorkspaceSnapshot is not null)
            {
                window.Closed += (_, _) =>
                {
                    _readOnlyWorkspaceSnapshot?.Dispose();
                    _readOnlyWorkspaceSnapshot = null;
                };
            }

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
    /// Evaluates compatibility and prepares either normal access, migration or isolated fallback reading.
    /// </summary>
    /// <param name="workspacePath">The selected source workspace.</param>
    /// <returns>The effective workspace access, or null when the user cancels.</returns>
    private async Task<(string EffectiveWorkspacePath, bool IsReadOnly, string DisplayName, string? AccessMessage)?>
            PrepareWorkspaceAccessAsync(
                string workspacePath)
    {
        global::Nodalis.Infrastructure.Compatibility.WorkspaceCompatibilityService compatibilityService =
            new WorkspaceCompatibilityService(
                workspacePath);
        global::Nodalis.Core.Compatibility.WorkspaceCompatibilityDecision compatibility =
            await compatibilityService.EvaluateAsync();
        string displayName = new DirectoryInfo(
            workspacePath).Name;

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            displayName = "Nodalis";
        }

        if (compatibility.AccessMode == WorkspaceAccessMode.ReadWrite)
        {
            return (
                workspacePath,
                false,
                displayName,
                null);
        }

        if (compatibility.AccessMode == WorkspaceAccessMode.MigrationRequired)
        {
            global::Nodalis.Infrastructure.Migrations.WorkspaceMigrationService migrationService =
                new WorkspaceMigrationService(
                    workspacePath);
            global::Nodalis.Core.Migrations.WorkspaceMigrationPreflight preflight =
                await migrationService.PreflightAsync();

            if (!preflight.CanMigrate)
            {
                throw new InvalidDataException(
                    "Le workspace doit être migré mais le préflight a échoué.\n\n" +
                    string.Join(
                        Environment.NewLine,
                        preflight.Errors));
            }

            string stepText = string.Join(
                Environment.NewLine,
                preflight.Steps.Select(
                    step =>
                        $"• schéma {step.FromVersion} → {step.ToVersion} : {step.Description}"));

            global::System.Windows.MessageBoxResult migrationAnswer = MessageBox.Show(
                $"Ce workspace utilise le schéma {compatibility.WorkspaceSchemaVersion} et Nodalis écrit le schéma {compatibility.ApplicationSchemaVersion}.\n\n" +
                $"{stepText}\n\n" +
                "Oui : sauvegarder puis migrer.\n" +
                "Non : ouvrir uniquement les fichiers Markdown en lecture seule.\n" +
                "Annuler : ne pas ouvrir le workspace.",
                "Compatibilité du workspace",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (migrationAnswer == MessageBoxResult.Cancel)
            {
                return null;
            }

            if (migrationAnswer == MessageBoxResult.Yes)
            {
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

                return (
                    workspacePath,
                    false,
                    displayName,
                    null);
            }

            return await CreateReadOnlyAccessAsync(
                compatibilityService,
                compatibility,
                displayName);
        }

        if (compatibility.AccessMode == WorkspaceAccessMode.ReadOnlyFallback)
        {
            global::System.Windows.MessageBoxResult answer = MessageBox.Show(
                $"Le workspace utilise le schéma {compatibility.WorkspaceSchemaVersion}, " +
                $"plus récent que le schéma {compatibility.ApplicationSchemaVersion} compris par cette version de Nodalis.\n\n" +
                "Nodalis ne modifiera pas le workspace. Seuls les fichiers Markdown seront consultables depuis une copie temporaire isolée. " +
                "Les métadonnées, index, dashboards et fonctions d'écriture seront désactivés.\n\n" +
                $"{compatibility.RecommendedAction}\n\nOuvrir en lecture seule ?",
                "Workspace plus récent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return null;
            }

            return await CreateReadOnlyAccessAsync(
                compatibilityService,
                compatibility,
                displayName);
        }

        throw new InvalidDataException(
            compatibility.Message + "\n\n" +
            compatibility.RecommendedAction);
    }

    /// <summary>
    /// Creates the isolated fallback snapshot and returns the effective read-only access tuple.
    /// </summary>
    /// <param name="compatibilityService">The compatibility service.</param>
    /// <param name="compatibility">The evaluated compatibility decision.</param>
    /// <param name="displayName">The source workspace display name.</param>
    /// <returns>The prepared fallback access.</returns>
    private async Task<(string EffectiveWorkspacePath, bool IsReadOnly, string DisplayName, string? AccessMessage)>
            CreateReadOnlyAccessAsync(
                WorkspaceCompatibilityService compatibilityService,
                WorkspaceCompatibilityDecision compatibility,
                string displayName)
    {
        _readOnlyWorkspaceSnapshot?.Dispose();
        _readOnlyWorkspaceSnapshot =
            await compatibilityService.CreateReadOnlySnapshotAsync();

        string message =
            $"LECTURE SEULE — workspace source schéma {compatibility.WorkspaceSchemaVersion}, " +
            $"Nodalis schéma {compatibility.ApplicationSchemaVersion}. " +
            "Consultation Markdown sur copie temporaire ; aucune écriture n'est faite dans le workspace source.";

        return (
            _readOnlyWorkspaceSnapshot.SnapshotRoot,
            true,
            displayName,
            message);
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
