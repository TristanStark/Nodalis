using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Settings;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.App;

public partial class App : Application
{
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
            var preferencesStore = new UserPreferencesStore();
            var preferences = await preferencesStore.LoadAsync();

            var resolved = await ResolveWorkspaceAsync(
                preferences,
                preferencesStore);

            if (resolved is null)
            {
                Shutdown();
                return;
            }

            preferences = resolved.Value.Preferences;

            var templateStore = new FileSystemTemplateStore(
                resolved.Value.WorkspacePath);
            await templateStore.InitializeDefaultsAsync();

            var navigationBuilder = new WorkspaceNavigationBuilder();
            var root = await navigationBuilder.BuildAsync(
                resolved.Value.WorkspacePath);

            var window = new MainWindow(
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

    private void ApplyNodalisWindowStyle(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Window window ||
            window.ReadLocalValue(FrameworkElement.StyleProperty) !=
            DependencyProperty.UnsetValue ||
            Resources["NodalisWindowStyle"] is not Style style)
        {
            return;
        }

        // WPF resolves an implicit Window style against the concrete runtime
        // type. Nodalis dialogs are derived Window classes, so without this
        // explicit application their surface can fall back to the light
        // system background while child controls still use the dark theme.
        window.Style = style;
    }

    private static async Task<(string WorkspacePath, UserPreferences Preferences)?>
        ResolveWorkspaceAsync(
            UserPreferences preferences,
            UserPreferencesStore preferencesStore)
    {
        if (!string.IsNullOrWhiteSpace(preferences.WorkspaceRootPath))
        {
            var configuredPath = Path.GetFullPath(
                preferences.WorkspaceRootPath);

            if (Directory.Exists(configuredPath) &&
                File.Exists(Path.Combine(
                    configuredPath,
                    WorkspaceLayout.WorkspaceManifestFileName)))
            {
                return (configuredPath, preferences);
            }
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier du workspace Nodalis",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var workspacePath = Path.GetFullPath(dialog.FolderName);
        var manifestPath = Path.Combine(
            workspacePath,
            WorkspaceLayout.WorkspaceManifestFileName);

        if (!File.Exists(manifestPath))
        {
            var containsFiles = Directory
                .EnumerateFileSystemEntries(workspacePath)
                .Any();

            if (containsFiles)
            {
                var answer = MessageBox.Show(
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

            var directoryName = new DirectoryInfo(workspacePath).Name;
            var workspaceName = string.IsNullOrWhiteSpace(directoryName)
                ? "Nodalis"
                : directoryName;

            var workspaceStore = new FileSystemWorkspaceStore(
                workspacePath);

            await workspaceStore.InitializeAsync(workspaceName);
        }

        var updatedPreferences = preferences with
        {
            WorkspaceRootPath = workspacePath
        };

        await preferencesStore.SaveAsync(updatedPreferences);

        return (workspacePath, updatedPreferences);
    }
}
