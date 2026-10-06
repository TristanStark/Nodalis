using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Nodalis.App.Commands;
using Nodalis.Core.Compatibility;
using Nodalis.Core.Migrations;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Compatibility;
using Nodalis.Infrastructure.Migrations;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Settings;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.App;

public partial class App : Application
{
    private readonly LocalDiagnosticsService _diagnosticsService = new();
    private ReadOnlyWorkspaceSnapshot? _readOnlyWorkspaceSnapshot;
    private bool _fatalExceptionObserved;

    /// <summary>
    /// Performs the <c>OnStartup</c> operation.
    /// </summary>
    /// <param name="e">The <c>e</c> value.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _diagnosticsService.StartSession();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyNodalisWindowStyle));

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            ShortcutCatalog.ValidateUniqueBindings();

            string? smokeTestWorkspacePath =
                GetOptionalArgumentValue(
                    e.Args,
                    "--smoke-test-workspace");

            bool smokeTestRequested =
                e.Args.Any(argument =>
                    string.Equals(
                        argument,
                        "--smoke-test",
                        StringComparison.OrdinalIgnoreCase));

            if (smokeTestRequested ||
                smokeTestWorkspacePath is not null)
            {
                if (TryFindResource(
                        "NodalisWindowStyle") is not Style ||
                    TryFindResource(
                        "NodalisKeyboardFocusVisual") is not Style)
                {
                    throw new InvalidOperationException(
                        "Le thème WPF Nodalis ou ses styles d'accessibilité n'ont pas pu être chargés.");
                }

                if (smokeTestWorkspacePath is not null)
                {
                    await VerifySmokeTestWorkspaceAsync(
                        smokeTestWorkspacePath);
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
                if (_diagnosticsService.PreviousSessionEndedUnexpectedly)
                {
                    _diagnosticsService.RecoverOrphanedAtomicWriteFiles(
                        access.Value.EffectiveWorkspacePath);
                }

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
                _diagnosticsService,
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
            _fatalExceptionObserved = true;
            _diagnosticsService.LogException(
                "Démarrage de Nodalis",
                exception);

            MessageBox.Show(
                $"Nodalis n'a pas pu démarrer.\n\n{exception.Message}",
                "Erreur Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    /// <summary>
    /// Returns the value that follows an optional command-line argument.
    /// </summary>
    /// <param name="arguments">Application startup arguments.</param>
    /// <param name="argumentName">Argument name whose value should be returned.</param>
    /// <returns>The argument value, or <see langword="null"/> when the argument is absent.</returns>
    private static string? GetOptionalArgumentValue(
            string[] arguments,
            string argumentName)
    {
        int argumentIndex =
            Array.FindIndex(
                arguments,
                argument =>
                    string.Equals(
                        argument,
                        argumentName,
                        StringComparison.OrdinalIgnoreCase));

        if (argumentIndex < 0)
        {
            return null;
        }

        int valueIndex =
            argumentIndex + 1;

        if (valueIndex >= arguments.Length ||
            string.IsNullOrWhiteSpace(
                arguments[valueIndex]) ||
            arguments[valueIndex].StartsWith(
                "--",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"L'argument {argumentName} nécessite une valeur.");
        }

        return arguments[valueIndex];
    }

    /// <summary>
    /// Exercises the packaged application against a representative release-candidate workspace.
    /// </summary>
    /// <param name="workspacePath">Workspace copied to an isolated temporary directory by the release workflow.</param>
    /// <returns>A task representing the validation.</returns>
    private static async Task VerifySmokeTestWorkspaceAsync(
            string workspacePath)
    {
        string workspaceRoot =
            Path.GetFullPath(
                workspacePath);

        if (!Directory.Exists(
                workspaceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Le workspace de smoke test est introuvable : {workspaceRoot}");
        }

        global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore workspaceStore =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        global::Nodalis.Core.Domain.WorkspaceManifest workspace =
            await workspaceStore.LoadAsync();

        int expectedSchemaVersion =
            global::Nodalis.Core.Domain.WorkspaceManifest.CurrentSchemaVersion;

        if (workspace.SchemaVersion !=
            expectedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Le workspace RC utilise le schéma {workspace.SchemaVersion} au lieu du schéma attendu {expectedSchemaVersion}.");
        }

        global::Nodalis.Infrastructure.Templates.FileSystemTemplateStore templateStore =
            new FileSystemTemplateStore(
                workspaceRoot);

        await templateStore.InitializeDefaultsAsync();

        global::Nodalis.Infrastructure.Navigation.WorkspaceNavigationBuilder navigationBuilder =
            new WorkspaceNavigationBuilder();

        global::Nodalis.Core.Navigation.WorkspaceNavigationNode navigation =
            await navigationBuilder.BuildAsync(
                workspaceRoot);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> nodes =
            DescendantsAndSelf(
                    navigation)
                .ToArray();

        if (!nodes.Any(node =>
                node.Kind ==
                    global::Nodalis.Core.Navigation.WorkspaceNodeKind.Project))
        {
            throw new InvalidDataException(
                "Le workspace RC ne contient aucun projet navigable.");
        }

        global::Nodalis.Core.Navigation.WorkspaceNavigationNode? sentinelDocument =
            nodes.FirstOrDefault(node =>
                node.Kind ==
                    global::Nodalis.Core.Navigation.WorkspaceNodeKind.Document &&
                string.Equals(
                    Path.GetFileName(
                        node.FullPath),
                    "Architecture.md",
                    StringComparison.OrdinalIgnoreCase));

        if (sentinelDocument is null)
        {
            throw new InvalidDataException(
                "Le document sentinelle Architecture.md du workspace RC est introuvable.");
        }

        global::Nodalis.Infrastructure.Search.WorkspaceSearchService searchService =
            new global::Nodalis.Infrastructure.Search.WorkspaceSearchService();

        global::Nodalis.Core.Search.SearchResultSet searchResults =
            await searchService.SearchAsync(
                workspaceRoot,
                sentinelDocument.FullPath,
                "release-candidate-sentinel",
                global::Nodalis.Core.Search.SearchMatchMode.Exact);

        bool sentinelFound =
            searchResults.Project
                .Concat(
                    searchResults.Application)
                .Concat(
                    searchResults.Global)
                .Any(result =>
                    string.Equals(
                        result.FilePath,
                        sentinelDocument.FullPath,
                        StringComparison.OrdinalIgnoreCase));

        if (!sentinelFound)
        {
            throw new InvalidDataException(
                "La recherche du binaire publié ne retrouve pas le document sentinelle du workspace RC.");
        }

        global::Nodalis.Infrastructure.Links.WorkspaceLinkIndexService linkIndexService =
            new global::Nodalis.Infrastructure.Links.WorkspaceLinkIndexService(
                workspaceRoot);

        global::Nodalis.Core.Links.LinkIndexCatalog links =
            await linkIndexService.RefreshAsync();

        if (!links.References.Any(reference =>
                string.Equals(
                    reference.RawTarget,
                    "Architecture",
                    StringComparison.OrdinalIgnoreCase) &&
                reference.TargetId is not null))
        {
            throw new InvalidDataException(
                "Le binaire publié ne résout pas le lien interne sentinelle du workspace RC.");
        }
    }

    /// <summary>
    /// Enumerates a navigation node and all of its descendants.
    /// </summary>
    /// <param name="node">Navigation node to enumerate.</param>
    /// <returns>The node followed by all descendants.</returns>
    private static IEnumerable<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> DescendantsAndSelf(
            global::Nodalis.Core.Navigation.WorkspaceNavigationNode node)
    {
        yield return node;

        foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode child in node.Children)
        {
            foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode descendant in DescendantsAndSelf(
                         child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Marks a clean application exit when no fatal exception was observed.
    /// </summary>
    /// <param name="e">The exit event arguments.</param>
    protected override void OnExit(
            ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

        if (!_fatalExceptionObserved)
        {
            _diagnosticsService.MarkCleanShutdown();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Journals an unhandled WPF dispatcher exception without masking the fatal error.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The unhandled exception arguments.</param>
    private void App_DispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
    {
        _fatalExceptionObserved = true;
        _diagnosticsService.LogException(
            "Exception WPF non gérée",
            e.Exception);
    }

    /// <summary>
    /// Journals an unhandled .NET domain exception.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The unhandled exception arguments.</param>
    private void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
    {
        _fatalExceptionObserved = true;

        if (e.ExceptionObject is Exception exception)
        {
            _diagnosticsService.LogException(
                "Exception .NET non gérée",
                exception);
            return;
        }

        _diagnosticsService.LogInformation(
            $"Exception .NET non gérée non typée : {e.ExceptionObject}");
    }

    /// <summary>
    /// Journals an unobserved task exception and marks it observed after capture.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The task exception arguments.</param>
    private void TaskScheduler_UnobservedTaskException(
            object? sender,
            UnobservedTaskExceptionEventArgs e)
    {
        _diagnosticsService.LogException(
            "Exception de tâche non observée",
            e.Exception);
        e.SetObserved();
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
