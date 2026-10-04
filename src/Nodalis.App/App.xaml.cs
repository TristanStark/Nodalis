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
