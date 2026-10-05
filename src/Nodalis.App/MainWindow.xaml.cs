using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Nodalis.App.Commands;
using Nodalis.App.Dashboard;
using Nodalis.App.Dialogs;
using Nodalis.App.Editor;
using Nodalis.App.Glossary;
using Nodalis.App.Markdown;
using Nodalis.App.Navigation;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Decisions;
using Nodalis.Core.Glossary;
using Nodalis.Core.Importing;
using Nodalis.Core.Links;
using Nodalis.Core.Meetings;
using Nodalis.Core.Milestones;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Core.Settings;
using Nodalis.Core.Tasks;
using Nodalis.Core.Templates;
using Nodalis.Core.Trash;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Attachments;
using Nodalis.Infrastructure.Backups;
using Nodalis.Infrastructure.Decisions;
using Nodalis.Infrastructure.Documents;
using Nodalis.Infrastructure.Glossary;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Meetings;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Notes;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Tasks;
using Nodalis.Infrastructure.Trash;

namespace Nodalis.App;

public partial class MainWindow : Window
{
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly ITemplateStore _templateStore;
    private readonly WorkspaceNavigationBuilder _navigationBuilder = new();
    private readonly GlossaryService _glossaryService = new();
    private readonly WorkspaceLinkIndexService _linkIndexService;
    private readonly WorkspaceTaskService _taskService;
    private readonly WorkspaceMilestoneService _milestoneService;
    private readonly WorkspaceMeetingService _meetingService;
    private readonly WorkspaceDecisionService _decisionService;
    private readonly WorkspaceDocxImportService _docxImportService;
    private readonly WorkspaceTrashService _trashService;
    private readonly WorkspaceBackupService _backupService;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _backupTimer;

    private NavigationNodeViewModel _root;
    private NavigationNodeViewModel? _selectedNode;
    private TextDocumentSession? _documentSession;
    private DocumentAutosaveController? _autosave;
    private UserPreferences _preferences;
    private bool _allowClose;
    private bool _contextPanelOpen;
    private bool _previewVisible;
    private bool _suppressEditorChanges;
    private bool _documentDirty;
    private bool _restoringSelection;
    private bool _conflictWarningShown;
    private bool _backupInProgress;
    private double _lastContextWidth;
    private LinkIndexCatalog _linkIndex = new();
    private int _linkSuggestionStart;
    private int _linkSuggestionLength;
    private string? _linkSuggestionAlias;
    private bool _suppressLinkAutocomplete;
    private GlossaryTextBoxAdorner? _glossaryAdorner;
    private IReadOnlyList<GlossaryScope> _glossaryScopes = [];
    private IReadOnlyList<GlossaryEntry> _glossaryEntries = [];
    private IReadOnlyList<GlossaryTextMatch> _glossaryMatches = [];

    /// <summary>
    /// Initializes a new instance of <see cref="MainWindow"/>.
    /// </summary>
    /// <param name="root">The <c>root</c> value.</param>
    /// <param name="preferences">The <c>preferences</c> value.</param>
    /// <param name="preferencesStore">The <c>preferencesStore</c> value.</param>
    /// <param name="templateStore">The <c>templateStore</c> value.</param>
    public MainWindow(
            WorkspaceNavigationNode root,
            UserPreferences preferences,
            IUserPreferencesStore preferencesStore,
            ITemplateStore templateStore)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(preferencesStore);
        ArgumentNullException.ThrowIfNull(templateStore);

        InitializeComponent();
        InitializeDocumentTabs();
        InitializeDocumentOutline();

        _preferences = preferences;
        _preferencesStore = preferencesStore;
        _templateStore = templateStore;
        _linkIndexService = new WorkspaceLinkIndexService(
            root.FullPath);
        _taskService = new WorkspaceTaskService(
            root.FullPath);
        _milestoneService = new WorkspaceMilestoneService(
            root.FullPath);
        _meetingService = new WorkspaceMeetingService(
            root.FullPath);
        _decisionService = new WorkspaceDecisionService(
            root.FullPath);
        _docxImportService = new WorkspaceDocxImportService(
            root.FullPath);
        _trashService = new WorkspaceTrashService(
            root.FullPath);
        _backupService = new WorkspaceBackupService(
            root.FullPath);
        _contextPanelOpen = preferences.IsContextPanelOpen;
        _previewVisible = preferences.Editor.LivePreview;
        _lastContextWidth = preferences.ContextPanelWidth;

        _previewTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Background,
            PreviewTimer_Tick,
            Dispatcher)
        {
            IsEnabled = false
        };

        _backupTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(1),
            DispatcherPriority.Background,
            BackupTimer_Tick,
            Dispatcher)
        {
            IsEnabled = false
        };

        InitializeEditorSplit();

        MarkdownEditorTextBox.FontSize = preferences.Editor.FontSize;
        MarkdownEditorTextBox.TextWrapping =
            preferences.Editor.WordWrap
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap;

        _root = CreateRootViewModel(
            root,
            preferences.ExpandedNodeIds);

        NavigationTree.ItemsSource =
            new[] { _root };

        WorkspaceNameText.Text = $" / {root.DisplayName}";
        Title = $"{root.DisplayName} — Nodalis";

        NavigationColumn.Width = new GridLength(
            preferences.NavigationPanelWidth);

        ApplyContextPanelState();
        ApplyPreviewState();

        Loaded += async (_, _) =>
        {
            try
            {
                AttachGlossaryAdorner();
                await RefreshLinkIndexAndContextAsync();
                await CleanupRecentHistoryAsync();
                await RefreshGlossaryContextAsync(
                    _selectedNode?.FullPath ?? _root.FullPath);
                UpdateGlossaryAnnotations();
                await RefreshDashboardTasksAsync();
                await RefreshDashboardMilestonesAsync();
                await RestoreDocumentTabsAsync();
                await RestoreEditorSplitAsync();
                _backupTimer.Start();
                await TryRunAutomaticBackupAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                StatusText.Text =
                    $"Index de liens indisponible : {exception.Message}";
            }
        };
    }

    /// <summary>
    /// Performs the <c>OnClosing</c> operation.
    /// </summary>
    /// <param name="e">The <c>e</c> value.</param>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (!await TryFlushAllDocumentTabsForShutdownAsync())
        {
            return;
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Settings.OpenDocumentTabReference> openDocumentTabs =
            BuildOpenDocumentTabReferences();

        Guid? activeDocumentTabId =
            _activeDocumentTab?.DocumentId;

        try
        {
            _preferences = _preferences with
            {
                ExpandedNodeIds = GetExpandedNodeIds(),
                IsContextPanelOpen = _contextPanelOpen,
                NavigationPanelWidth = Math.Max(
                    180,
                    NavigationColumn.ActualWidth),
                ContextPanelWidth = Math.Max(
                    180,
                    _contextPanelOpen
                        ? ContextColumn.ActualWidth
                        : _lastContextWidth),
                Editor = _preferences.Editor with
                {
                    LivePreview = _previewVisible,
                    WordWrap =
                        MarkdownEditorTextBox.TextWrapping ==
                        TextWrapping.Wrap,
                    FontSize = (int)Math.Round(
                        MarkdownEditorTextBox.FontSize),
                    SplitMode = _editorSplitMode,
                    SplitRatio = CaptureEditorSplitRatio(),
                    SecondaryDocumentTabId =
                        _secondaryDocumentTab?.DocumentId
                },
                OpenDocumentTabs = openDocumentTabs,
                ActiveDocumentTabId = activeDocumentTabId
            };

            await _preferencesStore.SaveAsync(_preferences);
            await DisposeAllDocumentTabsAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Les préférences d'interface n'ont pas pu être enregistrées.\n\n" +
                exception.Message,
                "Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _previewTimer.Stop();
        _backupTimer.Stop();
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// Performs the <c>NavigationTree_PreviewMouseRightButtonDown</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void NavigationTree_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        global::System.Windows.Controls.TreeViewItem? item = FindVisualParent<TreeViewItem>(
            e.OriginalSource as DependencyObject);

        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    /// <summary>
    /// Performs the <c>NavigationTree_ContextMenuOpening</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void NavigationTree_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e)
    {
        if (_selectedNode is null)
        {
            e.Handled = true;
            return;
        }

        global::System.Windows.Controls.ContextMenu menu = new ContextMenu();

        void AddItem(
            string header,
            RoutedEventHandler handler)
        {
            global::System.Windows.Controls.MenuItem item = new MenuItem
            {
                Header = header
            };

            item.Click += handler;
            menu.Items.Add(item);
        }

        switch (_selectedNode.Kind)
        {
            case WorkspaceNodeKind.ApplicationsRoot:
                AddItem(
                    "Nouvelle application",
                    async (_, _) => await CreateApplicationAsync());
                break;

            case WorkspaceNodeKind.Application:
                AddItem(
                    "Nouveau module",
                    async (_, _) => await CreateModuleAsync(_selectedNode));
                menu.Items.Add(new Separator());
                AddItem(
                    "Renommer",
                    async (_, _) => await RenameApplicationOrModuleAsync(_selectedNode));
                AddItem(
                    "Mettre à la corbeille",
                    async (_, _) => await MoveNodeToTrashAsync(_selectedNode));
                break;

            case WorkspaceNodeKind.Module:
                AddItem(
                    "Nouveau sous-module",
                    async (_, _) => await CreateModuleAsync(_selectedNode));
                AddItem(
                    "Déplacer le module",
                    async (_, _) => await MoveModuleAsync(_selectedNode));
                menu.Items.Add(new Separator());
                AddItem(
                    "Renommer",
                    async (_, _) => await RenameApplicationOrModuleAsync(_selectedNode));
                AddItem(
                    "Mettre à la corbeille",
                    async (_, _) => await MoveNodeToTrashAsync(_selectedNode));
                break;

            case WorkspaceNodeKind.Project:
                AddItem(
                    IsFavorite(_selectedNode)
                        ? "Retirer des favoris"
                        : "Ajouter aux favoris",
                    async (_, _) => await ToggleFavoriteAsync(_selectedNode));
                menu.Items.Add(new Separator());
                AddItem(
                    "Mettre à la corbeille",
                    async (_, _) => await MoveNodeToTrashAsync(_selectedNode));
                break;

            case WorkspaceNodeKind.Document:
                AddItem(
                    IsFavorite(_selectedNode)
                        ? "Retirer des favoris"
                        : "Ajouter aux favoris",
                    async (_, _) => await ToggleFavoriteAsync(_selectedNode));
                menu.Items.Add(new Separator());
                AddItem(
                    "Renommer le document",
                    async (_, _) => await RenameDocumentAsync(_selectedNode));
                AddItem(
                    "Mettre à la corbeille",
                    async (_, _) => await MoveNodeToTrashAsync(_selectedNode));
                break;

            default:
                e.Handled = true;
                return;
        }

        NavigationTree.ContextMenu = menu;
    }

    /// <summary>
    /// Opens the backup manager.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OpenBackups_Click(
            object sender,
            RoutedEventArgs e) =>
            await ShowBackupsAsync();

    /// <summary>
    /// Opens the backup manager and persists its local scheduling preferences.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task ShowBackupsAsync()
    {
        await FlushAllDocumentTabsAsync();

        _backupTimer.Stop();

        try
        {
            global::Nodalis.App.Dialogs.BackupDialog dialog = new BackupDialog(
                _backupService,
                _preferences.Backup)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true ||
                dialog.Preferences is null)
            {
                return;
            }

            _preferences = _preferences with
            {
                Backup = dialog.Preferences
            };

            await _preferencesStore.SaveAsync(
                _preferences);

            StatusText.Text =
                "Préférences de sauvegarde enregistrées localement";
        }
        finally
        {
            _backupTimer.Start();
        }

        await TryRunAutomaticBackupAsync();
    }

    /// <summary>
    /// Checks the configured cadence when the automatic backup timer ticks.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private async void BackupTimer_Tick(
            object? sender,
            EventArgs e) =>
            await TryRunAutomaticBackupAsync();

    /// <summary>
    /// Creates a due automatic backup without blocking the editor thread with ZIP I/O.
    /// </summary>
    /// <returns>A task representing the scheduling check.</returns>
    private async Task TryRunAutomaticBackupAsync()
    {
        if (_backupInProgress)
        {
            return;
        }

        global::Nodalis.Core.Settings.BackupPreferences settings =
            _preferences.Backup;

        if (!settings.AutomaticEnabled ||
            string.IsNullOrWhiteSpace(
                settings.DestinationDirectory))
        {
            return;
        }

        _backupInProgress = true;

        try
        {
            bool due = await _backupService.IsBackupDueAsync(
                settings.DestinationDirectory,
                settings.IntervalMinutes,
                DateTimeOffset.UtcNow);

            if (!due)
            {
                return;
            }

            await FlushAllDocumentTabsAsync();

            global::Nodalis.Core.Backups.WorkspaceBackupInfo backup = await _backupService.CreateBackupAsync(
                settings.DestinationDirectory,
                settings.RetentionCount);

            StatusText.Text =
                $"Sauvegarde auto · {backup.FileName} · {backup.DisplaySize}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            StatusText.Text =
                $"Sauvegarde auto impossible · {exception.Message}";
        }
        finally
        {
            _backupInProgress = false;
        }
    }

    /// <summary>
    /// Opens the workspace trash and refreshes navigation when its contents change.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OpenTrash_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Nodalis.App.Dialogs.TrashDialog dialog = new TrashDialog(
            _trashService)
        {
            Owner = this
        };

        dialog.ShowDialog();

        if (!dialog.WorkspaceChanged)
        {
            return;
        }

        try
        {
            await RefreshNavigationAsync();
            StatusText.Text = "Corbeille mise à jour";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ShowStructureError(
                "Corbeille",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>CreateApplicationAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task CreateApplicationAsync()
    {
        global::Nodalis.App.Dialogs.TextPromptDialog dialog = new TextPromptDialog(
            "Nouvelle application",
            "Nom de l'application :")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            global::Nodalis.Infrastructure.Applications.ApplicationStructureService service = new ApplicationStructureService(
                _root.FullPath);

            string path = await service.CreateApplicationAsync(
                dialog.Value);

            await RefreshNavigationAsync(path);
            StatusText.Text = $"Application créée · {dialog.Value}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ShowStructureError(
                "Nouvelle application",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>CreateModuleAsync</c> operation.
    /// </summary>
    /// <param name="parent">The <c>parent</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task CreateModuleAsync(
            NavigationNodeViewModel parent)
    {
        global::Nodalis.App.Dialogs.TextPromptDialog dialog = new TextPromptDialog(
            "Nouveau module",
            parent.Kind == WorkspaceNodeKind.Application
                ? $"Nom du module dans {parent.DisplayName} :"
                : $"Nom du sous-module dans {parent.DisplayName} :")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Guid applicationId;
            Guid? parentModuleId;

            if (parent.Kind == WorkspaceNodeKind.Application)
            {
                applicationId = parent.Id;
                parentModuleId = null;
            }
            else
            {
                global::Nodalis.Infrastructure.Applications.ApplicationStructureService manifestReader = new ApplicationStructureService(
                    _root.FullPath);

                global::Nodalis.Core.Domain.ModuleManifest manifest = await manifestReader.LoadModuleAsync(
                    parent.FullPath);

                applicationId = manifest.ApplicationId;
                parentModuleId = manifest.Id;
            }

            global::Nodalis.Infrastructure.Applications.ApplicationStructureService service = new ApplicationStructureService(
                _root.FullPath);

            string path = await service.CreateModuleAsync(
                parent.FullPath,
                applicationId,
                parentModuleId,
                dialog.Value);

            await RefreshNavigationAsync(path);
            StatusText.Text = $"Module créé · {dialog.Value}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ShowStructureError(
                "Nouveau module",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>RenameDocumentAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task RenameDocumentAsync(
            NavigationNodeViewModel node)
    {
        global::Nodalis.App.Dialogs.TextPromptDialog dialog = new TextPromptDialog(
            "Renommer le document",
            $"Nouveau nom pour {node.DisplayName} :",
            node.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            string.Equals(
                dialog.Value,
                node.DisplayName,
                StringComparison.CurrentCulture))
        {
            return;
        }

        try
        {
            if (!await TryCloseCurrentDocumentAsync(
                    "renommer ce document"))
            {
                return;
            }

            global::Nodalis.Infrastructure.Documents.DocumentStructureService service = new DocumentStructureService(
                _root.FullPath);

            string path = await service.RenameAsync(
                node.FullPath,
                dialog.Value);

            await RefreshNavigationAsync(path);
            StatusText.Text = $"Document renommé · {dialog.Value}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            ShowStructureError(
                "Renommer le document",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>RenameApplicationOrModuleAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task RenameApplicationOrModuleAsync(
            NavigationNodeViewModel node)
    {
        global::Nodalis.App.Dialogs.TextPromptDialog dialog = new TextPromptDialog(
            "Renommer",
            $"Nouveau nom pour {node.DisplayName} :",
            node.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            string.Equals(
                dialog.Value,
                node.DisplayName,
                StringComparison.CurrentCulture))
        {
            return;
        }

        try
        {
            if (!await TryCloseCurrentDocumentAsync(
                    "renommer cet élément"))
            {
                return;
            }

            global::Nodalis.Infrastructure.Applications.ApplicationStructureService service = new ApplicationStructureService(
                _root.FullPath);

            string path = node.Kind == WorkspaceNodeKind.Application
                ? await service.RenameApplicationAsync(
                    node.FullPath,
                    dialog.Value)
                : await service.RenameModuleAsync(
                    node.FullPath,
                    dialog.Value);

            await RefreshNavigationAsync(path);
            StatusText.Text = $"Renommé · {dialog.Value}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ShowStructureError(
                "Renommer",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>MoveModuleAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task MoveModuleAsync(
            NavigationNodeViewModel node)
    {
        try
        {
            global::Nodalis.Infrastructure.Applications.ApplicationStructureService service = new ApplicationStructureService(
                _root.FullPath);

            global::Nodalis.Core.Domain.ModuleManifest manifest = await service.LoadModuleAsync(
                node.FullPath);

            global::Nodalis.Infrastructure.Projects.ProjectCreationTargetDiscovery discovery = new ProjectCreationTargetDiscovery();
            global::Nodalis.Core.Projects.ProjectCreationTarget[] targets = (await discovery.DiscoverAsync(
                    _root.FullPath))
                .Where(target =>
                    target.ApplicationId == manifest.ApplicationId &&
                    target.ParentProjectId is null &&
                    target.ModuleId != manifest.Id &&
                    !(manifest.ParentModuleId is null && target.ModuleId is null) &&
                    !(manifest.ParentModuleId is Guid currentParentId &&
                      target.ModuleId == currentParentId))
                .ToArray();

            if (targets.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Aucune autre destination n'est disponible dans cette application.",
                    "Déplacer le module",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            global::Nodalis.App.Dialogs.MoveModuleDialog dialog = new MoveModuleDialog(targets)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (!await TryCloseCurrentDocumentAsync(
                    "déplacer ce module"))
            {
                return;
            }

            string path = await service.MoveModuleAsync(
                node.FullPath,
                dialog.SelectedTarget.ParentDirectory,
                dialog.SelectedTarget.ModuleId);

            await RefreshNavigationAsync(path);
            StatusText.Text = $"Module déplacé · {node.DisplayName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            Nodalis.Core.Validation.DomainValidationException)
        {
            ShowStructureError(
                "Déplacer le module",
                exception);
        }
    }

    /// <summary>
    /// Moves a supported navigation item to the recoverable workspace trash.
    /// </summary>
    /// <param name="node">The navigation item to move.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task MoveNodeToTrashAsync(
            NavigationNodeViewModel node)
    {
        TrashItemKind kind = node.Kind switch
        {
            WorkspaceNodeKind.Document => TrashItemKind.Document,
            WorkspaceNodeKind.Project => TrashItemKind.Project,
            WorkspaceNodeKind.Module => TrashItemKind.Module,
            WorkspaceNodeKind.Application => TrashItemKind.Application,
            _ => throw new InvalidOperationException(
                "This navigation item cannot be moved to the trash.")
        };

        string scopeWarning = kind == TrashItemKind.Document
            ? "Le document sera déplacé dans la corbeille Nodalis et pourra être restauré."
            : "L'élément et tout son contenu seront déplacés dans la corbeille Nodalis et pourront être restaurés.";

        global::System.Windows.MessageBoxResult answer = MessageBox.Show(
            this,
            $"Mettre « {node.DisplayName} » à la corbeille ?\n\n{scopeWarning}",
            "Corbeille",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!await TryCloseCurrentDocumentAsync(
                    "mettre cet élément à la corbeille"))
            {
                return;
            }

            await _trashService.MoveToTrashAsync(
                node.FullPath,
                kind,
                node.Id);

            await RefreshNavigationAsync();
            StatusText.Text = $"Corbeille · {node.DisplayName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            ShowStructureError(
                "Corbeille",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>ShowStructureError</c> operation.
    /// </summary>
    /// <param name="title">The <c>title</c> value.</param>
    /// <param name="exception">The <c>exception</c> value.</param>
    private void ShowStructureError(
            string title,
            Exception exception)
    {
        MessageBox.Show(
            this,
            $"L'opération n'a pas pu être effectuée.\n\n{exception.Message}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Performs the <c>FindVisualParent</c> operation.
    /// </summary>
    /// <typeparam name="T">The <c>T</c> type.</typeparam>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static T? FindVisualParent<T>(
            DependencyObject? child)
            where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T target)
            {
                return target;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    /// <summary>
    /// Performs the <c>NavigationTree_SelectedItemChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void NavigationTree_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
    {
        if (_restoringSelection ||
            e.NewValue is not NavigationNodeViewModel node)
        {
            return;
        }

        _selectedNode = node;
        await DisplayNodeAsync(node);
    }

    /// <summary>
    /// Performs the <c>DisplayNodeAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task DisplayNodeAsync(NavigationNodeViewModel node)
    {
        DocumentTitleText.Text = node.DisplayName;

        string relativePath = Path.GetRelativePath(
            _root.FullPath,
            node.FullPath);

        DocumentPathText.Text =
            relativePath == "."
                ? _root.FullPath
                : relativePath;

        ContextTitleText.Text = node.DisplayName;
        ContextKindText.Text = GetKindLabel(node.Kind);
        ContextPathText.Text = node.FullPath;

        if (node.Kind != WorkspaceNodeKind.Document)
        {
            ClearDocumentPropertiesContext();
        }

        if (node.Kind == WorkspaceNodeKind.Workspace)
        {
            ShowDashboard();
            return;
        }

        if (node.Kind == WorkspaceNodeKind.Document)
        {
            await OpenDocumentAsync(node);
            return;
        }

        if (node.Kind == WorkspaceNodeKind.Project)
        {
            await TrackRecentContextAsync(node);
        }

        ShowNodeSummary(node);
    }

    /// <summary>
    /// Performs the <c>OpenDocumentAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task OpenDocumentAsync(
            NavigationNodeViewModel node)
    {
        try
        {
            await OpenOrActivateDocumentTabAsync(
                node);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException)
        {
            NodeSummaryHost.Visibility = Visibility.Visible;
            EditorToolbar.Visibility = Visibility.Collapsed;
            DocumentEditorHost.Visibility = Visibility.Collapsed;

            NodeSummaryText.Text =
                $"Impossible de lire le document.\n\n{exception.Message}";

            StatusText.Text = "Erreur de lecture";
        }
    }

    /// <summary>
    /// Performs the <c>ShowNodeSummary</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    private void ShowNodeSummary(
            NavigationNodeViewModel node)
    {
        DeactivateDocumentTabView();

        DashboardHost.Visibility = Visibility.Collapsed;
        EditorToolbar.Visibility = Visibility.Collapsed;
        DocumentEditorHost.Visibility = Visibility.Collapsed;
        NodeSummaryHost.Visibility = Visibility.Visible;

        NodeSummaryText.Text =
            $"{GetKindLabel(node.Kind)}\n\n" +
            $"{node.Children.Count} élément(s) enfant(s)\n\n" +
            $"{node.FullPath}";

        BacklinksList.ItemsSource = null;
        ContextBrokenLinksText.Text = "Liens internes : —";
        _glossaryMatches = [];
        _glossaryAdorner?.SetMatches([]);
        MarkdownEditorTextBox.ToolTip = null;

        StatusText.Text =
            $"{GetKindLabel(node.Kind)} · {node.Children.Count} élément(s)";
    }

    /// <summary>
    /// Performs the <c>TryCloseCurrentDocumentAsync</c> operation.
    /// </summary>
    /// <param name="actionDescription">The <c>actionDescription</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private Task<bool> TryCloseCurrentDocumentAsync(
            string actionDescription) =>
            TryCloseActiveDocumentTabAsync(
                actionDescription);

    /// <summary>
    /// Performs the <c>MarkdownEditorTextBox_TextChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void MarkdownEditorTextBox_TextChanged(
            object sender,
            System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressEditorChanges ||
            _autosave is null)
        {
            return;
        }

        _documentDirty = true;

        if (_activeDocumentTab is not null)
        {
            _activeDocumentTab.Content =
                MarkdownEditorTextBox.Text;
            _activeDocumentTab.IsDirty = true;
        }

        RefreshDocumentPropertiesContext(
            MarkdownEditorTextBox.Text);

        SaveStateText.Text = "Modification…";

        _autosave.Schedule(
            MarkdownEditorTextBox.Text);

        _previewTimer.Stop();
        _previewTimer.Start();

        if (!_suppressLinkAutocomplete)
        {
            InternalLinkPopup.IsOpen = false;
            _ = RefreshInternalLinkSuggestionsSafelyAsync();
        }
    }

    /// <summary>
    /// Performs the <c>PreviewTimer_Tick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void PreviewTimer_Tick(
            object? sender,
            EventArgs e)
    {
        _previewTimer.Stop();

        try
        {
            RenderPreview();
            UpdateGlossaryAnnotations();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException)
        {
            InternalLinkPopup.IsOpen = false;
            StatusText.Text =
                $"Aperçu Markdown temporairement indisponible : {exception.Message}";
        }
    }

    /// <summary>
    /// Performs the <c>RenderPreview</c> operation.
    /// </summary>
    private void RenderPreview()
    {
        if (_previewVisible &&
            _activeDocumentTab is not null)
        {
            string? baseDirectory = Path.GetDirectoryName(
                _activeDocumentTab.FullPath);

            MarkdownPreview.Document =
                MarkdownFlowDocumentRenderer.Render(
                    MarkdownEditorTextBox.Text,
                    baseDirectory,
                    OnInternalLinkClicked,
                    OnMarkdownLinkClicked);
        }

        RenderSecondaryPreview();
    }

    /// <summary>
    /// Performs the <c>Autosave_Saved</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Autosave_Saved(
            object? sender,
            EventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
            await HandleDocumentTabAutosaveSavedAsync(
                sender));
    }

    /// <summary>
    /// Performs the <c>Autosave_ConflictDetected</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Autosave_ConflictDetected(
            object? sender,
            AutosaveConflictEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
            HandleDocumentTabAutosaveConflict(
                sender));
    }

    /// <summary>
    /// Performs the <c>Autosave_SaveFailed</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Autosave_SaveFailed(
            object? sender,
            AutosaveFailureEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
            HandleDocumentTabAutosaveFailure(
                sender,
                e.Exception));
    }

    /// <summary>
    /// Performs the <c>NewNote_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void NewNote_Click(
            object sender,
            RoutedEventArgs e)
    {
        await CreateNoteAsync();
    }

    /// <summary>
    /// Performs the <c>NewProject_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void NewProject_Click(
            object sender,
            RoutedEventArgs e)
    {
        await CreateProjectAsync();
    }

    /// <summary>
    /// Performs the <c>CaptureQuickNote_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void CaptureQuickNote_Click(
            object sender,
            RoutedEventArgs e)
    {
        await CaptureQuickNoteAsync();
    }

    /// <summary>
    /// Performs the <c>ShowQuickNotes_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void ShowQuickNotes_Click(
            object sender,
            RoutedEventArgs e)
    {
        await ShowQuickNotesAsync();
    }

    /// <summary>
    /// Performs the <c>CommandPalette_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void CommandPalette_Click(
            object sender,
            RoutedEventArgs e)
    {
        await ShowCommandPaletteAsync();
    }

    /// <summary>
    /// Performs the <c>AttachFile_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void AttachFile_Click(
            object sender,
            RoutedEventArgs e)
    {
        await AttachFileAsync();
    }

    /// <summary>
    /// Performs the <c>ImportDocx_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void ImportDocx_Click(
            object sender,
            RoutedEventArgs e)
    {
        await ImportDocxAsync();
    }

    /// <summary>
    /// Performs the <c>ShowAllTasks_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void ShowAllTasks_Click(
            object sender,
            RoutedEventArgs e)
    {
        await ShowTasksAsync(
            global: true);
    }

    /// <summary>
    /// Performs the <c>MainWindow_PreviewKeyDown</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void MainWindow_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
    {
        bool controlPressed =
            (Keyboard.Modifiers & ModifierKeys.Control) ==
            ModifierKeys.Control;

        if (controlPressed &&
            e.Key == Key.Tab)
        {
            e.Handled = true;

            int delta =
                (Keyboard.Modifiers & ModifierKeys.Shift) ==
                ModifierKeys.Shift
                    ? -1
                    : 1;

            await CycleFocusedDocumentTabAsync(
                delta);
            return;
        }

        DocumentTabViewModel? focusedTab =
            GetFocusedDocumentTab();

        if (controlPressed &&
            e.Key == Key.W &&
            focusedTab is not null)
        {
            e.Handled = true;
            await CloseDocumentTabAsync(
                focusedTab,
                "fermer cet onglet",
                activateNeighbor: true);
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.MoveToPrimaryPane))
        {
            e.Handled = true;
            await MoveFocusedTabToPaneAsync(
                EditorPaneSlot.Primary);
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.MoveToSecondaryPane))
        {
            e.Handled = true;
            await MoveFocusedTabToPaneAsync(
                EditorPaneSlot.Secondary);
            return;
        }

        if (InternalLinkPopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                MoveInternalLinkSelection(1);
                return;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = true;
                MoveInternalLinkSelection(-1);
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CompleteInternalLinkSuggestion();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                InternalLinkPopup.IsOpen = false;
                return;
            }
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.QuickOpen))
        {
            e.Handled = true;
            await ShowQuickOpenAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.InternalLink))
        {
            e.Handled = true;
            await OpenInternalLinkPickerAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.Search))
        {
            e.Handled = true;
            await SearchAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.CommandPalette))
        {
            e.Handled = true;
            await ShowCommandPaletteAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.QuickNote))
        {
            e.Handled = true;
            await CaptureQuickNoteAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.QuickNotesOverview))
        {
            e.Handled = true;
            await ShowQuickNotesAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.NewProject))
        {
            e.Handled = true;
            await CreateProjectAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.NewNote))
        {
            e.Handled = true;
            await CreateNoteAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.Save))
        {
            e.Handled = true;
            await SaveCurrentDocumentAsync();
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.Bold) &&
            GetFocusedDocumentTab() is not null)
        {
            e.Handled = true;
            WrapSelection("**", "**");
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.Italic) &&
            GetFocusedDocumentTab() is not null)
        {
            e.Handled = true;
            WrapSelection("*", "*");
            return;
        }

        if (ShortcutCatalog.Matches(
                e,
                ShortcutCatalog.InlineCode) &&
            GetFocusedDocumentTab() is not null)
        {
            e.Handled = true;
            string marker = ((char)96).ToString();
            WrapSelection(marker, marker);
        }
    }

    /// <summary>
    /// Performs the <c>SaveCurrentDocumentAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task SaveCurrentDocumentAsync()
    {
        await SaveFocusedDocumentAsync();
    }

    /// <summary>
    /// Performs the <c>Bold_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Bold_Click(
            object sender,
            RoutedEventArgs e) =>
            WrapSelection("**", "**");

    /// <summary>
    /// Performs the <c>Italic_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Italic_Click(
            object sender,
            RoutedEventArgs e) =>
            WrapSelection("*", "*");

    /// <summary>
    /// Performs the <c>InlineCode_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void InlineCode_Click(
            object sender,
            RoutedEventArgs e)
    {
        string marker = ((char)96).ToString();
        WrapSelection(marker, marker);
    }

    /// <summary>
    /// Performs the <c>WrapSelection</c> operation.
    /// </summary>
    /// <param name="prefix">The <c>prefix</c> value.</param>
    /// <param name="suffix">The <c>suffix</c> value.</param>
    private void WrapSelection(
            string prefix,
            string suffix)
    {
        TextBox editor =
            GetFocusedEditorTextBox();

        if (GetFocusedDocumentTab() is null)
        {
            return;
        }

        int start = editor.SelectionStart;
        int length = editor.SelectionLength;
        string selectedText = editor.SelectedText;

        editor.SelectedText =
            prefix + selectedText + suffix;

        if (length == 0)
        {
            editor.CaretIndex =
                start + prefix.Length;
        }
        else
        {
            editor.Select(
                start + prefix.Length,
                length);
        }

        editor.Focus();
    }

    /// <summary>
    /// Performs the <c>TogglePreview_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void TogglePreview_Click(
            object sender,
            RoutedEventArgs e)
    {
        _previewVisible = !_previewVisible;
        ApplyPreviewState();

        if (_previewVisible)
        {
            RenderPreview();
        }
    }

    /// <summary>
    /// Performs the <c>ApplyPreviewState</c> operation.
    /// </summary>
    private void ApplyPreviewState()
    {
        if (_previewVisible)
        {
            PreviewColumn.Width = new GridLength(
                1,
                GridUnitType.Star);
            PreviewSplitterColumn.Width = new GridLength(5);
            MarkdownPreview.Visibility = Visibility.Visible;
            PreviewSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewColumn.Width = new GridLength(0);
            PreviewSplitterColumn.Width = new GridLength(0);
            MarkdownPreview.Visibility = Visibility.Collapsed;
            PreviewSplitter.Visibility = Visibility.Collapsed;
        }

        ApplySecondaryPreviewState();
    }

    /// <summary>
    /// Performs the <c>OnInternalLinkClicked</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    private async void OnInternalLinkClicked(string target)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        global::Nodalis.Core.Links.LinkResolution resolution = WorkspaceLinkIndexService.Resolve(
            _linkIndex,
            target);

        if (resolution.Status == LinkResolutionStatus.Resolved)
        {
            await NavigateToLinkTargetAsync(
                resolution.Target!);
            return;
        }

        StatusText.Text =
            resolution.Status == LinkResolutionStatus.Missing
                ? $"Lien interne introuvable : {target}"
                : $"Lien interne ambigu : {target} ({resolution.Candidates.Count} cibles)";
    }

    /// <summary>
    /// Performs the <c>ShowDashboard</c> operation.
    /// </summary>
    private void ShowDashboard()
    {
        DeactivateDocumentTabView();

        EditorToolbar.Visibility = Visibility.Collapsed;
        DocumentEditorHost.Visibility = Visibility.Collapsed;
        NodeSummaryHost.Visibility = Visibility.Collapsed;
        DashboardHost.Visibility = Visibility.Visible;

        DocumentTitleText.Text = "Accueil";
        DocumentPathText.Text = _root.FullPath;

        BacklinksList.ItemsSource = null;
        ContextBrokenLinksText.Text = "Liens internes : —";
        _glossaryMatches = [];
        _glossaryAdorner?.SetMatches([]);

        RefreshDashboard();
        _ = RefreshDashboardTasksAsync();
        _ = RefreshDashboardMilestonesAsync();

        StatusText.Text =
            "Accueil · favoris et éléments récents locaux";
    }

    /// <summary>
    /// Performs the <c>RefreshDashboard</c> operation.
    /// </summary>
    private void RefreshDashboard()
    {
        FavoritesList.ItemsSource = _preferences.Favorites
            .Select(reference =>
                CreateDashboardItem(
                    reference,
                    lastOpenedUtc: null))
            .Where(item => item is not null)
            .Cast<DashboardItemViewModel>()
            .ToArray();

        global::Nodalis.App.Dashboard.DashboardItemViewModel[] recent = _preferences.RecentItems
            .OrderByDescending(item => item.LastOpenedUtc)
            .Select(item =>
                CreateDashboardItem(
                    item.Item,
                    item.LastOpenedUtc))
            .Where(item => item is not null)
            .Cast<DashboardItemViewModel>()
            .ToArray();

        RecentProjectsList.ItemsSource = recent
            .Where(item =>
                string.Equals(
                    item.KindLabel,
                    "Projet",
                    StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();

        global::Nodalis.App.Dashboard.DashboardItemViewModel[] recentDocuments = recent
            .Where(item =>
                string.Equals(
                    item.KindLabel,
                    "Document",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        RecentDocumentsList.ItemsSource = recentDocuments
            .Take(12)
            .ToArray();

        RecentMeetingsList.ItemsSource = recentDocuments
            .Where(IsMeetingDashboardItem)
            .Take(8)
            .ToArray();

        RefreshRecentSearchesDashboard();
    }

    /// <summary>
    /// Performs the <c>CreateDashboardItem</c> operation.
    /// </summary>
    /// <param name="reference">The <c>reference</c> value.</param>
    /// <param name="lastOpenedUtc">The <c>lastOpenedUtc</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private DashboardItemViewModel? CreateDashboardItem(
            UserItemReference reference,
            DateTimeOffset? lastOpenedUtc)
    {
        if (!Guid.TryParse(
                reference.Key,
                out global::System.Guid targetId))
        {
            return null;
        }

        global::Nodalis.Core.Links.LinkTargetEntry? target = _linkIndex.Targets.FirstOrDefault(candidate =>
            candidate.Id == targetId);

        if (target is null)
        {
            return new DashboardItemViewModel
            {
                TargetId = targetId,
                DisplayName =
                    reference.DisplayName ??
                    "Élément introuvable",
                KindLabel = ToDashboardKindLabel(
                    reference.Kind),
                Context = "Cible actuellement introuvable",
                LastOpenedUtc = lastOpenedUtc
            };
        }

        return new DashboardItemViewModel
        {
            TargetId = target.Id,
            DisplayName = target.DisplayName,
            KindLabel = ToDashboardKindLabel(
                target.Kind.ToString()),
            Context = target.QualifiedName,
            LastOpenedUtc = lastOpenedUtc
        };
    }

    /// <summary>
    /// Performs the <c>ToDashboardKindLabel</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string ToDashboardKindLabel(
            string kind) =>
            kind.Equals(
                "project",
                StringComparison.OrdinalIgnoreCase)
            || kind.Equals(
                "Project",
                StringComparison.OrdinalIgnoreCase)
                ? "Projet"
                : kind.Equals(
                    "document",
                    StringComparison.OrdinalIgnoreCase)
                  || kind.Equals(
                      "Document",
                      StringComparison.OrdinalIgnoreCase)
                    ? "Document"
                    : kind;

    /// <summary>
    /// Performs the <c>IsMeetingDashboardItem</c> operation.
    /// </summary>
    /// <param name="item">The <c>item</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsMeetingDashboardItem(
            DashboardItemViewModel item) =>
            item.Context?.Contains(
                "Réunions",
                StringComparison.CurrentCultureIgnoreCase) == true ||
            item.Context?.Contains(
                "Reunions",
                StringComparison.CurrentCultureIgnoreCase) == true ||
            item.DisplayName.StartsWith(
                "Réunion",
                StringComparison.CurrentCultureIgnoreCase) ||
            item.DisplayName.StartsWith(
                "Reunion",
                StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Performs the <c>DashboardList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void DashboardList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (sender is not ListBox list ||
            list.SelectedItem is not DashboardItemViewModel item)
        {
            return;
        }

        global::Nodalis.Core.Links.LinkTargetEntry? target = _linkIndex.Targets.FirstOrDefault(candidate =>
            candidate.Id == item.TargetId);

        if (target is null)
        {
            StatusText.Text =
                $"Favori/récent introuvable : {item.DisplayName}";
            return;
        }

        await NavigateToLinkTargetAsync(
            target);
    }

    /// <summary>
    /// Performs the <c>IsFavorite</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private bool IsFavorite(
            NavigationNodeViewModel node)
    {
        global::Nodalis.Core.Links.LinkTargetEntry? target = FindIndexedTarget(
            node);

        return target is not null &&
               _preferences.Favorites.Any(reference =>
                   string.Equals(
                       reference.Key,
                       target.Id.ToString("D"),
                       StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the <c>ToggleFavoriteAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task ToggleFavoriteAsync(
            NavigationNodeViewModel node)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        global::Nodalis.Core.Links.LinkTargetEntry? target = FindIndexedTarget(
            node);

        if (target is null)
        {
            await RefreshLinkIndexAsync();
            target = FindIndexedTarget(
                node);
        }

        if (target is null)
        {
            StatusText.Text =
                $"Impossible d'indexer le favori : {node.DisplayName}";
            return;
        }

        string key = target.Id.ToString("D");

        global::System.Collections.Generic.List<global::Nodalis.Core.Settings.UserItemReference> favorites = _preferences.Favorites
            .Where(reference =>
                !string.Equals(
                    reference.Key,
                    key,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool removed =
            favorites.Count !=
            _preferences.Favorites.Count;

        if (!removed)
        {
            favorites.Add(
                CreateUserItemReference(
                    target));
        }

        _preferences = _preferences with
        {
            Favorites = favorites
        };

        await _preferencesStore.SaveAsync(
            _preferences);

        RefreshDashboard();

        StatusText.Text = removed
            ? $"Retiré des favoris · {target.DisplayName}"
            : $"Ajouté aux favoris · {target.DisplayName}";
    }

    /// <summary>
    /// Performs the <c>TrackRecentContextAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task TrackRecentContextAsync(
            NavigationNodeViewModel node)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkTargetEntry> targets = new List<LinkTargetEntry>();

        global::Nodalis.Core.Links.LinkTargetEntry? primary = FindIndexedTarget(
            node);

        if (primary is not null &&
            primary.Kind is
                LinkTargetKind.Project or
                LinkTargetKind.Document)
        {
            targets.Add(
                primary);
        }

        if (primary?.Kind == LinkTargetKind.Document)
        {
            global::Nodalis.Core.Links.LinkTargetEntry? parentProject = _linkIndex.Targets
                .Where(candidate =>
                    candidate.Kind == LinkTargetKind.Project &&
                    IsRelativeAncestor(
                        candidate.RelativePath,
                        primary.RelativePath))
                .OrderByDescending(candidate =>
                    candidate.RelativePath.Length)
                .FirstOrDefault();

            if (parentProject is not null)
            {
                targets.Add(
                    parentProject);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Settings.RecentItemReference> recent = _preferences.RecentItems
            .ToList();

        global::System.DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (global::Nodalis.Core.Links.LinkTargetEntry target in targets
                     .DistinctBy(target => target.Id))
        {
            string key = target.Id.ToString("D");

            recent.RemoveAll(item =>
                string.Equals(
                    item.Item.Key,
                    key,
                    StringComparison.OrdinalIgnoreCase));

            recent.Insert(
                0,
                new RecentItemReference
                {
                    Item = CreateUserItemReference(
                        target),
                    LastOpenedUtc = now
                });
        }

        recent = recent
            .OrderByDescending(item => item.LastOpenedUtc)
            .Take(40)
            .ToList();

        _preferences = _preferences with
        {
            RecentItems = recent
        };

        await _preferencesStore.SaveAsync(
            _preferences);

        RefreshDashboard();
    }

    /// <summary>
    /// Performs the <c>FindIndexedTarget</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private LinkTargetEntry? FindIndexedTarget(
            NavigationNodeViewModel node)
    {
        if (node.Kind is
            WorkspaceNodeKind.Application or
            WorkspaceNodeKind.Module or
            WorkspaceNodeKind.Project)
        {
            return _linkIndex.Targets.FirstOrDefault(target =>
                target.Id == node.Id);
        }

        if (node.Kind != WorkspaceNodeKind.Document)
        {
            return null;
        }

        string relativePath = Path.GetRelativePath(
                _root.FullPath,
                node.FullPath)
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

        return _linkIndex.Targets.FirstOrDefault(target =>
            target.Kind == LinkTargetKind.Document &&
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the <c>CreateUserItemReference</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static UserItemReference CreateUserItemReference(
            LinkTargetEntry target) =>
            new()
            {
                Kind = target.Kind == LinkTargetKind.Project
                    ? "project"
                    : "document",
                Key = target.Id.ToString("D"),
                DisplayName = target.DisplayName
            };

    /// <summary>
    /// Performs the <c>IsRelativeAncestor</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsRelativeAncestor(
            string candidateParent,
            string child)
    {
        string parent = candidateParent
            .TrimEnd('/') + "/";

        return child.StartsWith(
            parent,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>RefreshLinkIndexAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshLinkIndexAsync()
    {
        _linkIndex = await _linkIndexService.RefreshAsync();
        RefreshDashboard();
    }

    /// <summary>
    /// Performs the <c>RefreshLinkIndexAndContextAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshLinkIndexAndContextAsync()
    {
        await RefreshLinkIndexAsync();

        if (_selectedNode?.Kind == WorkspaceNodeKind.Document)
        {
            UpdateLinkContext(
                _selectedNode.FullPath);
            RenderPreview();
        }
    }

    /// <summary>
    /// Performs the <c>UpdateLinkContext</c> operation.
    /// </summary>
    /// <param name="documentPath">The <c>documentPath</c> value.</param>
    private void UpdateLinkContext(string documentPath)
    {
        string relativePath = Path.GetRelativePath(
                _root.FullPath,
                Path.GetFullPath(documentPath))
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

        global::Nodalis.Core.Links.LinkTargetEntry? target = _linkIndex.Targets.FirstOrDefault(candidate =>
            candidate.Kind == LinkTargetKind.Document &&
            string.Equals(
                candidate.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            BacklinksList.ItemsSource = null;
            ContextBrokenLinksText.Text =
                "Liens internes : indexation en attente";
            return;
        }

        global::Nodalis.Core.Links.BacklinkEntry[] backlinks = _linkIndex.References
            .Where(reference =>
                reference.TargetId == target.Id)
            .Select(reference =>
            {
                global::Nodalis.Core.Links.LinkTargetEntry? source = _linkIndex.Targets.FirstOrDefault(candidate =>
                    candidate.Id == reference.SourceId);

                return source is null
                    ? null
                    : new BacklinkEntry
                    {
                        Source = source,
                        LineNumber = reference.LineNumber,
                        Excerpt = reference.Excerpt,
                        RawTarget = reference.RawTarget
                    };
            })
            .Where(backlink => backlink is not null)
            .Cast<BacklinkEntry>()
            .OrderBy(
                backlink => backlink.Source.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(backlink => backlink.LineNumber)
            .ToArray();

        BacklinksList.ItemsSource = backlinks;

        global::Nodalis.Core.Links.LinkReferenceEntry[] unresolved = _linkIndex.References
            .Where(reference =>
                reference.SourceId == target.Id &&
                reference.TargetId is null)
            .ToArray();

        int missing = unresolved.Count(reference =>
            WorkspaceLinkIndexService.Resolve(
                _linkIndex,
                reference.RawTarget).Status ==
            LinkResolutionStatus.Missing);

        int ambiguous = unresolved.Length - missing;

        ContextBrokenLinksText.Text =
            $"Liens internes : {backlinks.Length} backlink(s) · " +
            $"{missing} cassé(s) · {ambiguous} ambigu(s)";
    }

    /// <summary>
    /// Performs the <c>NavigateToLinkTargetAsync</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    /// <param name="lineNumber">The <c>lineNumber</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task NavigateToLinkTargetAsync(
            LinkTargetEntry target,
            int? lineNumber = null)
    {
        string fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                target.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        global::Nodalis.App.Navigation.NavigationNodeViewModel? node = FindAndExpand(
            _root,
            fullPath);

        if (node is null)
        {
            await RefreshNavigationAsync();
            node = FindAndExpand(
                _root,
                fullPath);
        }

        if (node is null)
        {
            StatusText.Text =
                $"Cible indexée mais introuvable : {target.DisplayName}";
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir le lien interne"))
        {
            return;
        }

        _restoringSelection = true;
        node.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = node;
        await DisplayNodeAsync(node);

        if (lineNumber is int line &&
            node.Kind == WorkspaceNodeKind.Document)
        {
            MoveCaretToLine(line);
        }
    }

    /// <summary>
    /// Performs the <c>OpenInternalLinkPickerAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task OpenInternalLinkPickerAsync()
    {
        if (_documentSession is null)
        {
            return;
        }

        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        string alias = MarkdownEditorTextBox.SelectedText;
        int start = MarkdownEditorTextBox.SelectionStart;
        int length = MarkdownEditorTextBox.SelectionLength;

        await OpenInternalLinkSuggestionsAsync(
            alias,
            start,
            length,
            string.IsNullOrWhiteSpace(alias)
                ? null
                : alias);
    }

    /// <summary>
    /// Refreshes internal-link suggestions without allowing a transient editor
    /// index or visual-state error to terminate the application.
    /// </summary>
    /// <returns>A task representing the refresh operation.</returns>
    private async Task RefreshInternalLinkSuggestionsSafelyAsync()
    {
        try
        {
            await RefreshInternalLinkSuggestionsAsync();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException)
        {
            InternalLinkPopup.IsOpen = false;
        }
    }

    /// <summary>
    /// Performs the <c>RefreshInternalLinkSuggestionsAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshInternalLinkSuggestionsAsync()
    {
        if (_documentSession is null ||
            !TryGetOpenInternalLinkToken(
                out int start,
                out int length,
                out string? query))
        {
            InternalLinkPopup.IsOpen = false;
            return;
        }

        await OpenInternalLinkSuggestionsAsync(
            query,
            start,
            length,
            alias: null);
    }

    /// <summary>
    /// Performs the <c>OpenInternalLinkSuggestionsAsync</c> operation.
    /// </summary>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="start">The <c>start</c> value.</param>
    /// <param name="length">The <c>length</c> value.</param>
    /// <param name="alias">The <c>alias</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task OpenInternalLinkSuggestionsAsync(
            string query,
            int start,
            int length,
            string? alias)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        string normalized = query.Trim();

        global::Nodalis.Core.Links.LinkTargetEntry[] suggestions = _linkIndex.Targets
            .Where(target =>
                string.IsNullOrWhiteSpace(normalized) ||
                target.DisplayName.Contains(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase) ||
                target.QualifiedName.Contains(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(target =>
                target.DisplayName.StartsWith(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(
                target => target.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                target => target.QualifiedName,
                StringComparer.CurrentCultureIgnoreCase)
            .Take(40)
            .ToArray();

        if (suggestions.Length == 0 ||
            !IsEditorRangeValid(
                start,
                length))
        {
            InternalLinkPopup.IsOpen = false;
            return;
        }

        _linkSuggestionStart = start;
        _linkSuggestionLength = length;
        _linkSuggestionAlias = alias;

        InternalLinkSuggestions.ItemsSource = suggestions;
        InternalLinkSuggestions.SelectedIndex = 0;

        Rect caretRect =
            GetSafeEditorCaretRect();

        InternalLinkPopup.HorizontalOffset =
            caretRect.IsEmpty
                ? 8
                : Math.Max(
                    8,
                    caretRect.X);

        InternalLinkPopup.VerticalOffset =
            caretRect.IsEmpty
                ? 8
                : Math.Max(
                    8,
                    caretRect.Bottom + 4);

        InternalLinkPopup.IsOpen = true;
    }

    /// <summary>
    /// Gets a caret rectangle only when the current editor content contains a
    /// valid character index. This protects asynchronous popup refreshes after
    /// deleting the last character in a document or line.
    /// </summary>
    /// <returns>The caret rectangle, or <see cref="Rect.Empty"/> for empty text.</returns>
    private Rect GetSafeEditorCaretRect()
    {
        string text = MarkdownEditorTextBox.Text;

        if (text.Length == 0)
        {
            return Rect.Empty;
        }

        int caret = Math.Clamp(
            MarkdownEditorTextBox.CaretIndex,
            0,
            text.Length);

        if (caret == text.Length)
        {
            return MarkdownEditorTextBox.GetRectFromCharacterIndex(
                text.Length - 1,
                trailingEdge: true);
        }

        return MarkdownEditorTextBox.GetRectFromCharacterIndex(
            caret,
            trailingEdge: false);
    }

    /// <summary>
    /// Determines whether an editor range is still valid after an asynchronous
    /// suggestion refresh.
    /// </summary>
    /// <param name="start">The range start.</param>
    /// <param name="length">The range length.</param>
    /// <returns><see langword="true"/> when the range fits the current text.</returns>
    private bool IsEditorRangeValid(
            int start,
            int length)
    {
        int textLength =
            MarkdownEditorTextBox.Text.Length;

        return start >= 0 &&
               length >= 0 &&
               start <= textLength &&
               length <= textLength - start;
    }

    /// <summary>
    /// Performs the <c>TryGetOpenInternalLinkToken</c> operation.
    /// </summary>
    /// <param name="start">The <c>start</c> value.</param>
    /// <param name="length">The <c>length</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private bool TryGetOpenInternalLinkToken(
            out int start,
            out int length,
            out string query)
    {
        start = 0;
        length = 0;
        query = string.Empty;

        int caret = MarkdownEditorTextBox.CaretIndex;
        string text = MarkdownEditorTextBox.Text;

        if (caret < 2 ||
            caret > text.Length)
        {
            return false;
        }

        string beforeCaret = text[..caret];
        int open = beforeCaret.LastIndexOf(
            "[[",
            StringComparison.Ordinal);

        if (open < 0)
        {
            return false;
        }

        int close = beforeCaret.LastIndexOf(
            "]]",
            StringComparison.Ordinal);

        if (close > open)
        {
            return false;
        }

        string rawQuery = beforeCaret[(open + 2)..];

        if (rawQuery.Contains('\n') ||
            rawQuery.Contains('\r') ||
            rawQuery.Contains('|'))
        {
            return false;
        }

        start = open;
        length = caret - open;
        query = rawQuery;
        return true;
    }

    /// <summary>
    /// Performs the <c>MoveInternalLinkSelection</c> operation.
    /// </summary>
    /// <param name="delta">The <c>delta</c> value.</param>
    private void MoveInternalLinkSelection(int delta)
    {
        if (InternalLinkSuggestions.Items.Count == 0)
        {
            return;
        }

        int current = InternalLinkSuggestions.SelectedIndex;
        int next = Math.Clamp(
            current + delta,
            0,
            InternalLinkSuggestions.Items.Count - 1);

        InternalLinkSuggestions.SelectedIndex = next;
        InternalLinkSuggestions.ScrollIntoView(
            InternalLinkSuggestions.SelectedItem);
    }

    /// <summary>
    /// Performs the <c>CompleteInternalLinkSuggestion</c> operation.
    /// </summary>
    private void CompleteInternalLinkSuggestion()
    {
        if (InternalLinkSuggestions.SelectedItem is not LinkTargetEntry target ||
            !IsEditorRangeValid(
                _linkSuggestionStart,
                _linkSuggestionLength))
        {
            InternalLinkPopup.IsOpen = false;
            return;
        }

        int duplicateDisplayNames = _linkIndex.Targets.Count(candidate =>
            string.Equals(
                candidate.DisplayName,
                target.DisplayName,
                StringComparison.CurrentCultureIgnoreCase));

        string linkTarget = duplicateDisplayNames == 1
            ? target.DisplayName
            : target.QualifiedName;

        string syntax = string.IsNullOrWhiteSpace(_linkSuggestionAlias)
            ? $"[[{linkTarget}]]"
            : $"[[{linkTarget}|{_linkSuggestionAlias.Trim()}]]";

        _suppressLinkAutocomplete = true;

        try
        {
            MarkdownEditorTextBox.Select(
                _linkSuggestionStart,
                _linkSuggestionLength);

            MarkdownEditorTextBox.SelectedText = syntax;
            MarkdownEditorTextBox.CaretIndex =
                _linkSuggestionStart + syntax.Length;
        }
        finally
        {
            _suppressLinkAutocomplete = false;
            InternalLinkPopup.IsOpen = false;
        }

        MarkdownEditorTextBox.Focus();
    }

    /// <summary>
    /// Performs the <c>InternalLinkSuggestions_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void InternalLinkSuggestions_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            CompleteInternalLinkSuggestion();

    /// <summary>
    /// Performs the <c>BacklinksList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void BacklinksList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (BacklinksList.SelectedItem is not BacklinkEntry backlink)
        {
            return;
        }

        await NavigateToLinkTargetAsync(
            backlink.Source,
            backlink.LineNumber);
    }

    /// <summary>
    /// Performs the <c>OnMarkdownLinkClicked</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    private async void OnMarkdownLinkClicked(string target)
    {
        if (_selectedNode?.Kind == WorkspaceNodeKind.Document)
        {
            string? baseDirectory = Path.GetDirectoryName(
                _selectedNode.FullPath);

            if (!string.IsNullOrWhiteSpace(baseDirectory) &&
                !Uri.TryCreate(
                    target,
                    UriKind.Absolute,
                    out global::System.Uri? absoluteUri))
            {
                string localPath = Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        target.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

                global::Nodalis.App.Navigation.NavigationNodeViewModel? match = _root
                    .DescendantsAndSelf()
                    .FirstOrDefault(node =>
                        string.Equals(
                            Path.GetFullPath(node.FullPath),
                            localPath,
                            StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    if (_selectedNode is not null &&
                        !ReferenceEquals(
                            _selectedNode,
                            match) &&
                        !await TryCloseCurrentDocumentAsync(
                            "ouvrir le document lié"))
                    {
                        return;
                    }

                    _restoringSelection = true;
                    match.IsSelected = true;
                    _restoringSelection = false;
                    _selectedNode = match;
                    await DisplayNodeAsync(match);
                    return;
                }
            }

            if (Uri.TryCreate(
                    target,
                    UriKind.Absolute,
                    out global::System.Uri? uri) &&
                !uri.IsFile)
            {
                CopyExternalTargetToClipboard(
                    target);
                return;
            }

            global::Nodalis.Infrastructure.Attachments.AttachmentService attachmentService = new AttachmentService(
                _root.FullPath);

            global::Nodalis.Core.Attachments.AttachmentReference reference = attachmentService.Resolve(
                target,
                _selectedNode.FullPath);

            if (reference.Exists)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(
                        reference.FullPath)
                    {
                        UseShellExecute = true
                    });

                    StatusText.Text =
                        $"Ouvert avec Windows · {reference.DisplayName}";
                }
                catch (Exception exception) when (
                    exception is Win32Exception or
                    InvalidOperationException)
                {
                    MessageBox.Show(
                        this,
                        $"Windows n'a pas pu ouvrir ce fichier.\n\n{exception.Message}",
                        "Ouvrir le fichier",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            MessageBox.Show(
                this,
                $"Le fichier lié est introuvable.\n\n{reference.FullPath}",
                "Fichier manquant",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            StatusText.Text =
                $"Fichier manquant · {reference.DisplayName}";
            return;
        }

        CopyExternalTargetToClipboard(
            target);
    }

    /// <summary>
    /// Performs the <c>CopyExternalTargetToClipboard</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    private void CopyExternalTargetToClipboard(
            string target)
    {
        try
        {
            Clipboard.SetText(target);
            StatusText.Text =
                "Lien externe copié dans le presse-papiers (aucun appel réseau effectué).";
        }
        catch
        {
            StatusText.Text = target;
        }
    }

    /// <summary>
    /// Opens search using the current navigation context.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private Task SearchAsync() =>
        SearchAsync(
            initialQuery: null,
            contextPath: _selectedNode?.FullPath);

    /// <summary>
    /// Opens search with an optional restored query and local context.
    /// </summary>
    /// <param name="initialQuery">The query to prefill, when any.</param>
    /// <param name="contextPath">The local scope path, when any.</param>
    /// <returns>The result of the operation.</returns>
    private async Task SearchAsync(
            string? initialQuery,
            string? contextPath)
    {
        global::Nodalis.App.Dialogs.SearchDialog dialog = new SearchDialog(
            _root.FullPath,
            contextPath,
            initialQuery)
        {
            Owner = this
        };

        bool? accepted =
            dialog.ShowDialog();

        if (!string.IsNullOrWhiteSpace(
                dialog.Query))
        {
            await TrackRecentSearchAsync(
                dialog.Query,
                contextPath);
        }

        if (accepted != true ||
            dialog.SelectedResult is null)
        {
            return;
        }

        global::Nodalis.App.Navigation.NavigationNodeViewModel? target = _root
            .DescendantsAndSelf()
            .FirstOrDefault(node =>
                node.Kind == WorkspaceNodeKind.Document &&
                string.Equals(
                    Path.GetFullPath(node.FullPath),
                    Path.GetFullPath(dialog.SelectedResult.FilePath),
                    StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            StatusText.Text =
                "Le document trouvé n'est plus présent dans la navigation.";
            return;
        }

        if (_selectedNode is not null &&
            !ReferenceEquals(_selectedNode, target) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir le résultat de recherche"))
        {
            return;
        }

        _restoringSelection = true;
        target.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = target;
        await DisplayNodeAsync(target);

        MoveCaretToLine(
            dialog.SelectedResult.LineNumber);

        StatusText.Text =
            $"Résultat · {target.DisplayName} · ligne {dialog.SelectedResult.LineNumber}";
    }

    /// <summary>
    /// Performs the <c>MoveCaretToLine</c> operation.
    /// </summary>
    /// <param name="lineNumber">The <c>lineNumber</c> value.</param>
    private void MoveCaretToLine(int lineNumber)
    {
        if (_documentSession is null ||
            lineNumber <= 1)
        {
            MarkdownEditorTextBox.CaretIndex = 0;
            return;
        }

        string text = MarkdownEditorTextBox.Text;
        int currentLine = 1;
        int index = 0;

        while (index < text.Length &&
               currentLine < lineNumber)
        {
            if (text[index] == '\n')
            {
                currentLine++;
            }

            index++;
        }

        MarkdownEditorTextBox.CaretIndex =
            Math.Min(index, text.Length);
        MarkdownEditorTextBox.Focus();
        MarkdownEditorTextBox.ScrollToLine(
            Math.Max(0, lineNumber - 1));
    }

    /// <summary>
    /// Performs the <c>ShowCommandPaletteAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ShowCommandPaletteAsync()
    {
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.App.Commands.PaletteCommand> commands = BuildPaletteCommands();

        global::Nodalis.App.Dialogs.CommandPaletteDialog dialog = new CommandPaletteDialog(commands)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.SelectedCommand is null)
        {
            return;
        }

        await dialog.SelectedCommand.ExecuteAsync();
    }

    /// <summary>
    /// Performs the <c>ShowPreferencesAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ShowPreferencesAsync()
    {
        global::Nodalis.App.Dialogs.PreferencesDialog dialog = new PreferencesDialog(
            _preferences.Editor,
            _contextPanelOpen)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.Editor is null)
        {
            return;
        }

        global::Nodalis.Core.Settings.EditorPreferences editor = dialog.Editor;
        bool autosaveDelayChanged =
            editor.AutosaveDelayMilliseconds !=
            _preferences.Editor.AutosaveDelayMilliseconds;

        if (_contextPanelOpen &&
            !dialog.ContextPanelOpen)
        {
            _lastContextWidth = Math.Max(
                180,
                ContextColumn.ActualWidth);
        }

        _contextPanelOpen = dialog.ContextPanelOpen;
        _previewVisible = editor.LivePreview;

        MarkdownEditorTextBox.FontSize =
            editor.FontSize;
        MarkdownEditorTextBox.TextWrapping =
            editor.WordWrap
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap;

        ApplyContextPanelState();
        ApplyPreviewState();

        if (_previewVisible)
        {
            RenderPreview();
        }

        if (autosaveDelayChanged &&
            _documentTabs.Count > 0)
        {
            await RecreateDocumentTabAutosavesAsync(
                editor.AutosaveDelayMilliseconds);
        }

        _preferences = _preferences with
        {
            IsContextPanelOpen = _contextPanelOpen,
            Editor = editor
        };

        await _preferencesStore.SaveAsync(
            _preferences);

        StatusText.Text =
            "Préférences enregistrées localement";
    }

    /// <summary>
    /// Performs the <c>BuildPaletteCommands</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private IReadOnlyList<PaletteCommand> BuildPaletteCommands()
    {
        global::System.Collections.Generic.List<global::Nodalis.App.Commands.PaletteCommand> commands = new List<PaletteCommand>
        {
            new()
            {
                Id = "note.new",
                Title = "Nouvelle note",
                Subtitle = "Créer une note depuis un template ou en mode libre",
                Keywords = ["note", "template", "créer"],
                ExecuteAsync = CreateNoteAsync
            },
            new()
            {
                Id = "project.new",
                Title = "Nouveau projet",
                Subtitle = "Créer un projet ou sous-projet depuis un profil",
                Keywords = ["projet", "simple", "moyen", "complexe"],
                ExecuteAsync = CreateProjectAsync
            },
            new()
            {
                Id = "meeting.new",
                Title = "Nouveau compte-rendu de réunion",
                Subtitle = "Créer une réunion structurée dans le projet ou l'application",
                Keywords = ["réunion", "meeting", "compte-rendu", "participants", "actions", "décisions"],
                ExecuteAsync = CreateMeetingAsync
            },
            new()
            {
                Id = "decision.new",
                Title = "Nouvelle décision",
                Subtitle = "Créer un Decision Record, éventuellement depuis le document courant",
                Keywords = ["décision", "decision", "record", "adr", "réunion"],
                ExecuteAsync = CreateDecisionAsync
            },
            new()
            {
                Id = "decisions.list",
                Title = "Décisions du contexte",
                Subtitle = "Lister et rechercher les Decision Records du projet ou de l'application",
                Keywords = ["décisions", "decision", "recherche", "historique"],
                ExecuteAsync = ShowDecisionsAsync
            },
            new()
            {
                Id = "application.new",
                Title = "Nouvelle application",
                Subtitle = "Créer une application dans le workspace",
                Keywords = ["application", "créer"],
                ExecuteAsync = CreateApplicationAsync
            },
            new()
            {
                Id = "quick-open",
                Title = "Quick Open",
                Subtitle = "Ouvrir document, projet, module ou application · Ctrl+T",
                Keywords = ["ouvrir", "navigation", "document", "projet", "module", "application", "ctrl+t"],
                ExecuteAsync = ShowQuickOpenAsync
            },
            new()
            {
                Id = "search",
                Title = "Rechercher",
                Subtitle = "Résultats Projet / Application / Global",
                Keywords = ["recherche", "chercher", "trouver", "ctrl+f"],
                ExecuteAsync = SearchAsync
            },
            new()
            {
                Id = "attachment.add",
                Title = "Ajouter une pièce jointe",
                Subtitle = "Copier dans le workspace ou référencer un fichier local",
                Keywords = ["pièce jointe", "fichier", "word", "pdf", "excel", "image"],
                ExecuteAsync = AttachFileAsync
            },
            new()
            {
                Id = "docx.import",
                Title = "Importer un DOCX",
                Subtitle = "Prévisualiser, remapper puis valider un document Word local",
                Keywords = ["docx", "word", "import", "prévisualisation", "mapping"],
                ExecuteAsync = ImportDocxAsync
            },
            new()
            {
                Id = "tasks.context",
                Title = "Tâches du contexte",
                Subtitle = "Projet / Application / Global selon la sélection",
                Keywords = ["tâches", "actions", "checkbox", "projet"],
                ExecuteAsync = () => ShowTasksAsync(global: false)
            },
            new()
            {
                Id = "tasks.global",
                Title = "Toutes les tâches",
                Subtitle = "Vue consolidée de toutes les tâches Markdown",
                Keywords = ["tâches", "actions", "global", "checkbox"],
                ExecuteAsync = () => ShowTasksAsync(global: true)
            },
            new()
            {
                Id = "milestones.context",
                Title = "Jalons du projet",
                Subtitle = "Liste, timeline et édition locale des jalons",
                Keywords = ["jalons", "timeline", "planning", "projet", "date"],
                ExecuteAsync = ShowMilestonesAsync
            },
            new()
            {
                Id = "glossary.lookup",
                Title = "Consulter le glossaire",
                Subtitle = "Projet → Application → Global",
                Keywords = ["glossaire", "définition", "terme", "acronyme", "synonyme"],
                ExecuteAsync = ShowGlossaryAsync
            },
            new()
            {
                Id = "quick-note.capture",
                Title = "Ajouter une note rapide",
                Subtitle = "Projet / Application / Global",
                Keywords = ["rapide", "capture", "idée"],
                ExecuteAsync = CaptureQuickNoteAsync
            },
            new()
            {
                Id = "quick-note.overview",
                Title = "Voir les notes rapides agrégées",
                Subtitle = "Projet → Application → Global",
                Keywords = ["rapide", "notes", "agrégé"],
                ExecuteAsync = ShowQuickNotesAsync
            },
            new()
            {
                Id = "backups",
                Title = "Sauvegardes",
                Subtitle = "Créer, vérifier ou restaurer un ZIP du workspace",
                Keywords = ["sauvegarde", "backup", "zip", "restauration", "sécurité"],
                ExecuteAsync = ShowBackupsAsync
            },
            new()
            {
                Id = "preferences",
                Title = "Préférences",
                Subtitle = "Configurer l'éditeur et l'interface locale",
                Keywords = ["préférences", "settings", "éditeur", "autosave", "police", "aperçu"],
                ExecuteAsync = ShowPreferencesAsync
            }
        };

        foreach (global::Nodalis.App.Navigation.NavigationNodeViewModel node in _root
                     .DescendantsAndSelf()
                     .Where(node => node.Kind is
                         WorkspaceNodeKind.Application or
                         WorkspaceNodeKind.Module or
                         WorkspaceNodeKind.Project or
                         WorkspaceNodeKind.Document))
        {
            global::Nodalis.App.Navigation.NavigationNodeViewModel capturedNode = node;
            commands.Add(new PaletteCommand
            {
                Id = $"open:{capturedNode.Id:D}",
                Title = $"Ouvrir · {capturedNode.DisplayName}",
                Subtitle = GetKindLabel(capturedNode.Kind),
                Keywords =
                [
                    "ouvrir",
                    capturedNode.DisplayName,
                    GetKindLabel(capturedNode.Kind)
                ],
                ExecuteAsync = () =>
                {
                    capturedNode.IsSelected = true;
                    return Task.CompletedTask;
                }
            });
        }

        return commands;
    }

    /// <summary>
    /// Performs the <c>AttachGlossaryAdorner</c> operation.
    /// </summary>
    private void AttachGlossaryAdorner()
    {
        if (_glossaryAdorner is not null)
        {
            return;
        }

        global::System.Windows.Documents.AdornerLayer layer = AdornerLayer.GetAdornerLayer(
            MarkdownEditorTextBox);

        if (layer is null)
        {
            return;
        }

        _glossaryAdorner = new GlossaryTextBoxAdorner(
            MarkdownEditorTextBox);

        layer.Add(
            _glossaryAdorner);
    }

    /// <summary>
    /// Performs the <c>RefreshGlossaryContextAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshGlossaryContextAsync(
            string? contextPath)
    {
        _glossaryScopes = await _glossaryService.ResolveScopesAsync(
            _root.FullPath,
            contextPath);

        global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryEntry> entries = new List<GlossaryEntry>();

        foreach (global::Nodalis.Core.Glossary.GlossaryScope scope in _glossaryScopes)
        {
            entries.AddRange(
                await _glossaryService.LoadEntriesAsync(
                    scope));
        }

        _glossaryEntries = entries;
    }

    /// <summary>
    /// Performs the <c>UpdateGlossaryAnnotations</c> operation.
    /// </summary>
    private void UpdateGlossaryAnnotations()
    {
        if (_documentSession is null ||
            _glossaryAdorner is null)
        {
            _glossaryMatches = [];
            _glossaryAdorner?.SetMatches([]);
            return;
        }

        _glossaryMatches = GlossaryTextMatcher.Match(
            MarkdownEditorTextBox.Text,
            _glossaryEntries);

        _glossaryAdorner.SetMatches(
            _glossaryMatches);
    }

    /// <summary>
    /// Performs the <c>MarkdownEditorTextBox_PreviewMouseMove</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void MarkdownEditorTextBox_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
    {
        global::Nodalis.Core.Glossary.GlossaryTextMatch? match = FindGlossaryMatchAtPoint(
            e.GetPosition(MarkdownEditorTextBox));

        MarkdownEditorTextBox.ToolTip =
            match is null
                ? null
                : $"{match.Entry.Term}\n{match.Entry.Definition}\n\n{match.Entry.Scope.DisplayName}\nDouble-cliquer pour ouvrir.";
    }

    /// <summary>
    /// Performs the <c>MarkdownEditorTextBox_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void MarkdownEditorTextBox_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        global::Nodalis.Core.Glossary.GlossaryTextMatch? match = FindGlossaryMatchAtPoint(
            e.GetPosition(MarkdownEditorTextBox));

        if (match is null)
        {
            return;
        }

        e.Handled = true;
        await OpenGlossaryEntryAsync(
            match.Entry);
    }

    /// <summary>
    /// Moves the editor caret to the word that was right-clicked unless the
    /// click happened inside the current selection.
    /// </summary>
    /// <param name="sender">The editor text box.</param>
    /// <param name="e">The mouse event.</param>
    private void MarkdownEditorTextBox_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        int index = MarkdownEditorTextBox.GetCharacterIndexFromPoint(
            e.GetPosition(MarkdownEditorTextBox),
            snapToText: true);

        if (index < 0)
        {
            return;
        }

        int selectionStart = MarkdownEditorTextBox.SelectionStart;
        int selectionEnd =
            selectionStart + MarkdownEditorTextBox.SelectionLength;

        bool clickedInsideSelection =
            MarkdownEditorTextBox.SelectionLength > 0 &&
            index >= selectionStart &&
            index < selectionEnd;

        if (clickedInsideSelection)
        {
            return;
        }

        MarkdownEditorTextBox.CaretIndex = index;
        MarkdownEditorTextBox.Select(
            index,
            0);
    }

    /// <summary>
    /// Performs the <c>MarkdownEditorTextBox_ContextMenuOpening</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void MarkdownEditorTextBox_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e)
    {
        global::System.Windows.Controls.ContextMenu menu = new ContextMenu();

        menu.Items.Add(new MenuItem
        {
            Header = "Couper",
            Command = ApplicationCommands.Cut,
            CommandTarget = MarkdownEditorTextBox
        });

        menu.Items.Add(new MenuItem
        {
            Header = "Copier",
            Command = ApplicationCommands.Copy,
            CommandTarget = MarkdownEditorTextBox
        });

        menu.Items.Add(new MenuItem
        {
            Header = "Coller",
            Command = ApplicationCommands.Paste,
            CommandTarget = MarkdownEditorTextBox
        });

        string glossaryTerm =
            GetGlossaryCandidateTerm();

        if (_glossaryScopes.Count > 0)
        {
            menu.Items.Add(
                new Separator());

            string glossaryHeader =
                string.IsNullOrWhiteSpace(glossaryTerm)
                    ? "Ajouter au glossaire…"
                    : $"Ajouter « {FormatGlossaryMenuTerm(glossaryTerm)} » au glossaire";

            global::System.Windows.Controls.MenuItem addToGlossary = new MenuItem
            {
                Header = glossaryHeader
            };

            foreach (global::Nodalis.Core.Glossary.GlossaryScope scope in _glossaryScopes)
            {
                global::Nodalis.Core.Glossary.GlossaryScope capturedScope = scope;

                global::System.Windows.Controls.MenuItem scopeItem = new MenuItem
                {
                    Header = scope.DisplayName,
                    FontWeight =
                        scope.Kind == GlossaryScopeKind.Project
                            ? FontWeights.SemiBold
                            : FontWeights.Normal
                };

                scopeItem.Click += async (_, _) =>
                    await AddGlossaryEntryAsync(
                        capturedScope,
                        glossaryTerm);

                addToGlossary.Items.Add(
                    scopeItem);
            }

            menu.Items.Add(
                addToGlossary);
        }

        menu.Items.Add(
            new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = "Tout sélectionner",
            Command = ApplicationCommands.SelectAll,
            CommandTarget = MarkdownEditorTextBox
        });

        MarkdownEditorTextBox.ContextMenu = menu;
    }

    /// <summary>
    /// Gets the selected text or, when there is no selection, the word at the
    /// current editor caret for use as a glossary term.
    /// </summary>
    /// <returns>The normalized glossary term, or an empty string.</returns>
    private string GetGlossaryCandidateTerm()
    {
        string selected = NormalizeGlossarySelection(
            MarkdownEditorTextBox.SelectedText);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            return selected;
        }

        return GetGlossaryWordAtPosition(
            MarkdownEditorTextBox.Text,
            MarkdownEditorTextBox.CaretIndex);
    }

    /// <summary>
    /// Extracts a glossary-friendly word around a character position.
    /// Letters, digits, apostrophes, underscores and hyphens are preserved.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="position">The caret position in the source text.</param>
    /// <returns>The word around the position, or an empty string.</returns>
    private static string GetGlossaryWordAtPosition(
            string text,
            int position)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int index = Math.Clamp(
            position,
            0,
            text.Length - 1);

        if (!IsGlossaryWordCharacter(text[index]) &&
            index > 0 &&
            IsGlossaryWordCharacter(text[index - 1]))
        {
            index--;
        }

        if (!IsGlossaryWordCharacter(text[index]))
        {
            return string.Empty;
        }

        int start = index;
        int end = index + 1;

        while (start > 0 &&
               IsGlossaryWordCharacter(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length &&
               IsGlossaryWordCharacter(text[end]))
        {
            end++;
        }

        return NormalizeGlossarySelection(
            text[start..end]);
    }

    /// <summary>
    /// Determines whether a character can belong to a glossary term detected
    /// directly under the editor caret.
    /// </summary>
    /// <param name="value">The character to inspect.</param>
    /// <returns><see langword="true"/> when the character belongs to a word.</returns>
    private static bool IsGlossaryWordCharacter(
            char value) =>
            char.IsLetterOrDigit(value) ||
            value == '-' ||
            value == '_' ||
            value == '\'' ||
            value == '’';

    /// <summary>
    /// Produces a compact term label for the context-menu header.
    /// </summary>
    /// <param name="term">The glossary term.</param>
    /// <returns>A term shortened for display when necessary.</returns>
    private static string FormatGlossaryMenuTerm(
            string term)
    {
        const int maximumLength = 48;

        return term.Length <= maximumLength
            ? term
            : term[..(maximumLength - 1)] + "…";
    }

    /// <summary>
    /// Performs the <c>FindGlossaryMatchAtPoint</c> operation.
    /// </summary>
    /// <param name="point">The <c>point</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private GlossaryTextMatch? FindGlossaryMatchAtPoint(
            Point point)
    {
        if (_glossaryMatches.Count == 0)
        {
            return null;
        }

        int index = MarkdownEditorTextBox.GetCharacterIndexFromPoint(
            point,
            snapToText: false);

        if (index < 0)
        {
            return null;
        }

        return _glossaryMatches.FirstOrDefault(match =>
            index >= match.Start &&
            index < match.Start + match.Length);
    }

    /// <summary>
    /// Performs the <c>AddGlossaryEntryAsync</c> operation.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="term">The <c>term</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task AddGlossaryEntryAsync(
            GlossaryScope scope,
            string term)
    {
        global::Nodalis.App.Dialogs.AddGlossaryEntryDialog dialog = new AddGlossaryEntryDialog(
            scope,
            term)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string? currentPath = _documentSession?.Path;
            string targetPath = Path.GetFullPath(
                scope.FilePath);

            bool isCurrentDocument =
                currentPath is not null &&
                string.Equals(
                    Path.GetFullPath(currentPath),
                    targetPath,
                    StringComparison.OrdinalIgnoreCase);

            if (isCurrentDocument &&
                _autosave is not null)
            {
                await _autosave.FlushAsync();

                if (_documentDirty)
                {
                    MessageBox.Show(
                        this,
                        "Le glossaire contient des modifications non enregistrées. " +
                        "Enregistrez ou résolvez le conflit avant d'ajouter l'entrée.",
                        "Ajouter au glossaire",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            await _glossaryService.AppendAsync(
                scope,
                dialog.Draft);

            if (isCurrentDocument &&
                _documentSession is not null)
            {
                await _documentSession.ReloadAsync();

                _suppressEditorChanges = true;
                MarkdownEditorTextBox.Text =
                    _documentSession.Content;
                MarkdownEditorTextBox.CaretIndex =
                    MarkdownEditorTextBox.Text.Length;
                _suppressEditorChanges = false;

                _documentDirty = false;
                SaveStateText.Text = "Enregistré";
                RenderPreview();
            }

            await RefreshGlossaryContextAsync(
                _selectedNode?.FullPath ?? _root.FullPath);

            UpdateGlossaryAnnotations();

            StatusText.Text =
                $"Ajouté au glossaire · {dialog.Draft.Term} · {scope.DisplayName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ExternalModificationException)
        {
            MessageBox.Show(
                this,
                $"L'entrée n'a pas pu être ajoutée.\n\n{exception.Message}",
                "Ajouter au glossaire",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>OpenGlossaryEntryAsync</c> operation.
    /// </summary>
    /// <param name="entry">The <c>entry</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task OpenGlossaryEntryAsync(
            GlossaryEntry entry)
    {
        string path = Path.GetFullPath(
            entry.Scope.FilePath);

        global::Nodalis.App.Navigation.NavigationNodeViewModel? node = FindAndExpand(
            _root,
            path);

        if (node is null)
        {
            await RefreshNavigationAsync();
            node = FindAndExpand(
                _root,
                path);
        }

        if (node is null)
        {
            StatusText.Text =
                $"Entrée trouvée mais fichier introuvable dans la navigation : {entry.Term}";
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir l'entrée de glossaire"))
        {
            return;
        }

        _restoringSelection = true;
        node.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = node;
        await DisplayNodeAsync(node);
        MoveCaretToLine(
            entry.LineNumber);

        StatusText.Text =
            $"Glossaire · {entry.Term} · {entry.Scope.DisplayName}";
    }

    /// <summary>
    /// Performs the <c>NormalizeGlossarySelection</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeGlossarySelection(
            string value) =>
            string.Join(
                " ",
                value.Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Performs the <c>RefreshDashboardTasksAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshDashboardTasksAsync()
    {
        try
        {
            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> tasks = await _taskService.GetTasksAsync(
                _root.FullPath,
                includeCompleted: false);

            OpenTasksList.ItemsSource = tasks
                .OrderBy(task =>
                    task.DueDate ?? DateOnly.MaxValue)
                .ThenBy(
                    task => task.Text,
                    StringComparer.CurrentCultureIgnoreCase)
                .Take(12)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            OpenTasksList.ItemsSource = null;

            if (DashboardHost.Visibility == Visibility.Visible)
            {
                StatusText.Text =
                    $"Tâches indisponibles : {exception.Message}";
            }
        }
    }

    /// <summary>
    /// Performs the <c>RefreshDashboardMilestonesAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshDashboardMilestonesAsync()
    {
        try
        {
            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> milestones = await _milestoneService.GetUpcomingAsync(
                DateOnly.FromDateTime(DateTime.Today),
                forwardDays: 60);

            UpcomingMilestonesList.ItemsSource = milestones
                .Take(12)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            UpcomingMilestonesList.ItemsSource = null;

            if (DashboardHost.Visibility == Visibility.Visible)
            {
                StatusText.Text =
                    $"Jalons indisponibles : {exception.Message}";
            }
        }
    }

    /// <summary>
    /// Performs the <c>DashboardMilestoneList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void DashboardMilestoneList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (UpcomingMilestonesList.SelectedItem is not MilestoneItem milestone)
        {
            return;
        }

        await NavigateToMilestoneAsync(
            milestone);
    }

    /// <summary>
    /// Performs the <c>NavigateToMilestoneAsync</c> operation.
    /// </summary>
    /// <param name="milestone">The <c>milestone</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task NavigateToMilestoneAsync(
            MilestoneItem milestone)
    {
        string fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                milestone.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        global::Nodalis.App.Navigation.NavigationNodeViewModel? node = FindAndExpand(
            _root,
            fullPath);

        if (node is null)
        {
            await RefreshNavigationAsync();
            node = FindAndExpand(
                _root,
                fullPath);
        }

        if (node is null)
        {
            StatusText.Text =
                $"Document de jalons introuvable : {milestone.SourceRelativePath}";
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir le jalon"))
        {
            return;
        }

        _restoringSelection = true;
        node.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = node;
        await DisplayNodeAsync(
            node);

        MoveCaretToLine(
            milestone.LineNumber);

        StatusText.Text =
            $"Jalon · {milestone.Name} · {milestone.ProjectName} · ligne {milestone.LineNumber}";
    }

    /// <summary>
    /// Performs the <c>ShowMilestonesAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ShowMilestonesAsync()
    {
        string contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            string? projectDirectory =
                await _milestoneService.GetProjectDirectoryForContextAsync(
                    contextPath);

            if (projectDirectory is null)
            {
                MessageBox.Show(
                    this,
                    "Sélectionnez un projet, un sous-projet ou un document de projet pour ouvrir ses jalons.",
                    "Jalons",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            global::Nodalis.App.Dialogs.MilestoneListDialog dialog = new MilestoneListDialog(
                _root.FullPath,
                projectDirectory)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true &&
                dialog.SelectedMilestone is not null)
            {
                await NavigateToMilestoneAsync(
                    dialog.SelectedMilestone);
            }

            await RefreshDashboardMilestonesAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Jalons",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Performs the <c>ShowTasksAsync</c> operation.
    /// </summary>
    /// <param name="global">The <c>global</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task ShowTasksAsync(bool global)
    {
        string contextPath = global
            ? _root.FullPath
            : _selectedNode?.FullPath ?? _root.FullPath;

        string scopeLabel = global
            ? "Global · toutes les applications et tous les projets"
            : $"Contexte · {_selectedNode?.DisplayName ?? _root.DisplayName}";

        global::Nodalis.App.Dialogs.TaskListDialog dialog = new TaskListDialog(
            _root.FullPath,
            contextPath,
            scopeLabel,
            ToggleTaskFromViewAsync,
            UpdateTaskMetadataFromViewAsync)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true &&
            dialog.SelectedTask is not null)
        {
            await NavigateToTaskAsync(
                dialog.SelectedTask);
        }
    }

    /// <summary>
    /// Performs the <c>DashboardTaskCheckBox_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void DashboardTaskCheckBox_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox ||
            checkBox.DataContext is not TaskItem task)
        {
            return;
        }

        try
        {
            await ToggleTaskFromViewAsync(
                task,
                checkBox.IsChecked == true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            TaskSourceConflictException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Mettre à jour la tâche",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            await RefreshDashboardTasksAsync();
        }
    }

    /// <summary>
    /// Performs the <c>DashboardTaskList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void DashboardTaskList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (OpenTasksList.SelectedItem is not TaskItem task)
        {
            return;
        }

        await NavigateToTaskAsync(
            task);
    }

    /// <summary>
    /// Performs the <c>ToggleTaskFromViewAsync</c> operation.
    /// </summary>
    /// <param name="task">The <c>task</c> value.</param>
    /// <param name="completed">The <c>completed</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task ToggleTaskFromViewAsync(
            TaskItem task,
            bool completed)
    {
        string sourcePath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                task.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        bool isCurrentDocument =
            _documentSession is not null &&
            string.Equals(
                Path.GetFullPath(_documentSession.Path),
                sourcePath,
                StringComparison.OrdinalIgnoreCase);

        int caret = MarkdownEditorTextBox.CaretIndex;

        if (isCurrentDocument &&
            _autosave is not null)
        {
            await _autosave.FlushAsync();

            if (_documentDirty)
            {
                MessageBox.Show(
                    this,
                    "Le document source contient des modifications non enregistrées. " +
                    "Résolvez d'abord le conflit avant de modifier cette tâche depuis la vue consolidée.",
                    "Tâche",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
        }

        await _taskService.SetCompletedAsync(
            task,
            completed);

        if (isCurrentDocument &&
            _documentSession is not null)
        {
            await _documentSession.ReloadAsync();

            _suppressEditorChanges = true;
            MarkdownEditorTextBox.Text =
                _documentSession.Content;
            MarkdownEditorTextBox.CaretIndex =
                Math.Min(
                    caret,
                    MarkdownEditorTextBox.Text.Length);
            _suppressEditorChanges = false;

            _documentDirty = false;
            SaveStateText.Text = "Enregistré";

            await RefreshLinkIndexAndContextAsync();
            await RefreshGlossaryContextAsync(
                sourcePath);
            UpdateGlossaryAnnotations();
            RenderPreview();
        }

        await RefreshDashboardTasksAsync();

        StatusText.Text = completed
            ? $"Tâche terminée · {task.Text}"
            : $"Tâche rouverte · {task.Text}";
    }

    /// <summary>
    /// Updates readable task metadata from a consolidated view while keeping an open editor synchronized.
    /// </summary>
    /// <param name="task">The task to update.</param>
    /// <param name="metadata">The complete metadata set to write.</param>
    /// <returns>A task representing the local Markdown update.</returns>
    private async Task UpdateTaskMetadataFromViewAsync(
            TaskItem task,
            TaskMetadataUpdate metadata)
    {
        string sourcePath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                task.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        bool isCurrentDocument =
            _documentSession is not null &&
            string.Equals(
                Path.GetFullPath(
                    _documentSession.Path),
                sourcePath,
                StringComparison.OrdinalIgnoreCase);

        int caret =
            MarkdownEditorTextBox.CaretIndex;

        if (isCurrentDocument &&
            _autosave is not null)
        {
            await _autosave.FlushAsync();

            if (_documentDirty)
            {
                MessageBox.Show(
                    this,
                    "Le document source contient des modifications non enregistrées. " +
                    "Résolvez d'abord le conflit avant de modifier les métadonnées depuis la vue consolidée.",
                    "Tâche",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
        }

        await _taskService.UpdateMetadataAsync(
            task,
            metadata);

        if (isCurrentDocument &&
            _documentSession is not null)
        {
            await _documentSession.ReloadAsync();

            _suppressEditorChanges =
                true;
            MarkdownEditorTextBox.Text =
                _documentSession.Content;
            MarkdownEditorTextBox.CaretIndex =
                Math.Min(
                    caret,
                    MarkdownEditorTextBox.Text.Length);
            _suppressEditorChanges =
                false;

            _documentDirty =
                false;
            SaveStateText.Text =
                "Enregistré";

            await RefreshLinkIndexAndContextAsync();
            await RefreshGlossaryContextAsync(
                sourcePath);
            UpdateGlossaryAnnotations();
            RefreshDocumentPropertiesContext(
                MarkdownEditorTextBox.Text);
            RenderPreview();
        }

        await RefreshDashboardTasksAsync();

        StatusText.Text =
            $"Métadonnées de tâche mises à jour · {task.Text}";
    }

    /// <summary>
    /// Performs the <c>NavigateToTaskAsync</c> operation.
    /// </summary>
    /// <param name="task">The <c>task</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task NavigateToTaskAsync(
            TaskItem task)
    {
        string fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                task.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        global::Nodalis.App.Navigation.NavigationNodeViewModel? node = FindAndExpand(
            _root,
            fullPath);

        if (node is null)
        {
            await RefreshNavigationAsync();
            node = FindAndExpand(
                _root,
                fullPath);
        }

        if (node is null)
        {
            StatusText.Text =
                $"Document source introuvable : {task.SourceRelativePath}";
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir la tâche"))
        {
            return;
        }

        _restoringSelection = true;
        node.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = node;
        await DisplayNodeAsync(
            node);

        MoveCaretToLine(
            task.LineNumber);

        StatusText.Text =
            $"Tâche · {task.Text} · ligne {task.LineNumber}";
    }

    /// <summary>
    /// Performs the <c>ImportDocxAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ImportDocxAsync()
    {
        global::Microsoft.Win32.OpenFileDialog picker = new OpenFileDialog
        {
            Title = "Importer un document Word",
            Filter = "Documents Word (*.docx)|*.docx",
            Multiselect = false,
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        DocxImportPreview? preview = null;

        try
        {
            StatusText.Text =
                "Import DOCX · analyse du document…";

            preview = await _docxImportService.PreparePreviewAsync(
                picker.FileName);

            global::Nodalis.App.Dialogs.DocxImportPreviewDialog dialog = new DocxImportPreviewDialog(
                preview,
                request => _docxImportService.BuildPlanAsync(
                    preview,
                    request))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true ||
                dialog.CommitRequest is null)
            {
                _docxImportService.DiscardStagedCopy(
                    preview.StagedImport);

                preview = null;
                StatusText.Text =
                    "Import DOCX annulé";
                return;
            }

            StatusText.Text =
                "Import DOCX · écriture dans le workspace…";

            global::Nodalis.Core.Importing.DocxImportCommitResult result = await _docxImportService.CommitAsync(
                preview,
                dialog.CommitRequest);

            preview = null;

            try
            {
                await RefreshNavigationAsync(
                    result.ProjectDirectory);
                await RefreshLinkIndexAndContextAsync();
                await RefreshDashboardTasksAsync();
                await RefreshDashboardMilestonesAsync();
            }
            catch (Exception refreshException) when (
                refreshException is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                MessageBox.Show(
                    this,
                    "L'import DOCX est terminé, mais l'interface n'a pas pu être " +
                    $"rafraîchie complètement.\n\n{refreshException.Message}",
                    "Import DOCX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                StatusText.Text =
                    $"Import DOCX terminé · rafraîchissement incomplet · {result.GeneratedFiles.Count} fichier(s) créé(s)";
                return;
            }

            StatusText.Text =
                $"Import DOCX terminé · {result.GeneratedFiles.Count} fichier(s) Markdown créé(s)";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Le document Word n'a pas pu être importé.\n\n{exception.Message}",
                "Import DOCX",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Import DOCX en échec";
        }
        finally
        {
            if (preview is not null &&
                File.Exists(
                    preview.StagedImport.StagedCopyPath))
            {
                _docxImportService.DiscardStagedCopy(
                    preview.StagedImport);
            }
        }
    }

    /// <summary>
    /// Performs the <c>AttachFileAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task AttachFileAsync()
    {
        if (_documentSession is null ||
            _selectedNode?.Kind != WorkspaceNodeKind.Document)
        {
            MessageBox.Show(
                this,
                "Ouvrez d'abord une note Markdown pour y associer une pièce jointe.",
                "Pièce jointe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        global::Microsoft.Win32.OpenFileDialog picker = new OpenFileDialog
        {
            Title = "Choisir une pièce jointe",
            Multiselect = false,
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        global::System.Windows.MessageBoxResult mode = MessageBox.Show(
            this,
            "Voulez-vous copier ce fichier dans le workspace Nodalis ?\n\n" +
            "Oui : copie locale dans Attachments.\n" +
            "Non : conserver une référence vers le fichier original.\n" +
            "Annuler : ne rien faire.",
            "Mode de pièce jointe",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (mode == MessageBoxResult.Cancel)
        {
            return;
        }

        try
        {
            global::Nodalis.Infrastructure.Attachments.AttachmentService service = new AttachmentService(
                _root.FullPath);

            global::Nodalis.Core.Attachments.AttachmentReference attachment = mode == MessageBoxResult.Yes
                ? await service.CopyIntoWorkspaceAsync(
                    picker.FileName,
                    _selectedNode.FullPath)
                : service.CreateExternalReference(
                    picker.FileName,
                    _selectedNode.FullPath);

            string label = string.IsNullOrWhiteSpace(
                    MarkdownEditorTextBox.SelectedText)
                ? attachment.DisplayName
                : MarkdownEditorTextBox.SelectedText.Trim();

            bool isImage = IsImageAttachment(
                attachment.FullPath);

            string syntax = isImage
                ? $"![{label}]({attachment.MarkdownTarget})"
                : $"[{label}]({attachment.MarkdownTarget})";

            int start = MarkdownEditorTextBox.SelectionStart;
            MarkdownEditorTextBox.SelectedText = syntax;
            MarkdownEditorTextBox.CaretIndex =
                start + syntax.Length;
            MarkdownEditorTextBox.Focus();

            StatusText.Text =
                attachment.StorageMode ==
                Nodalis.Core.Attachments.AttachmentStorageMode.CopiedIntoWorkspace
                    ? $"Pièce jointe copiée · {attachment.DisplayName}"
                    : $"Référence externe ajoutée · {attachment.DisplayName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            MessageBox.Show(
                this,
                $"La pièce jointe n'a pas pu être ajoutée.\n\n{exception.Message}",
                "Pièce jointe",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>IsImageAttachment</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsImageAttachment(
            string path)
    {
        string extension = Path.GetExtension(path);

        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>ShowGlossaryAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ShowGlossaryAsync()
    {
        global::Nodalis.App.Dialogs.GlossaryLookupDialog dialog = new GlossaryLookupDialog(
            _root.FullPath,
            _selectedNode?.FullPath)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.SelectedEntry is null)
        {
            return;
        }

        await OpenGlossaryEntryAsync(
            dialog.SelectedEntry);
    }

    /// <summary>
    /// Performs the <c>CaptureQuickNoteAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task CaptureQuickNoteAsync()
    {
        try
        {
            global::Nodalis.Infrastructure.Notes.QuickNotesService service = new QuickNotesService();
            string contextPath =
                _selectedNode?.FullPath ??
                _root.FullPath;

            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Notes.QuickNoteScope> scopes = await service.ResolveScopesAsync(
                _root.FullPath,
                contextPath);

            global::Nodalis.App.Dialogs.QuickNoteDialog dialog = new QuickNoteDialog(scopes)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string targetPath = Path.GetFullPath(
                dialog.SelectedScope.FilePath);

            string? currentPath = _documentSession?.Path;

            if (currentPath is not null &&
                string.Equals(
                    Path.GetFullPath(currentPath),
                    targetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_autosave is not null)
                {
                    await _autosave.FlushAsync();
                }

                if (_documentDirty)
                {
                    MessageBox.Show(
                        this,
                        "La note rapide actuellement ouverte contient des modifications " +
                        "non enregistrées. Enregistrez ou résolvez le conflit avant " +
                        "d'ajouter une nouvelle entrée.",
                        "Note rapide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            await service.AppendAsync(
                dialog.SelectedScope,
                dialog.NoteText,
                DateTimeOffset.Now);

            if (_documentSession is not null &&
                string.Equals(
                    Path.GetFullPath(_documentSession.Path),
                    targetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                await _documentSession.ReloadAsync();

                _suppressEditorChanges = true;
                MarkdownEditorTextBox.Text =
                    _documentSession.Content;
                MarkdownEditorTextBox.CaretIndex =
                    MarkdownEditorTextBox.Text.Length;
                _suppressEditorChanges = false;

                _documentDirty = false;
                SaveStateText.Text = "Enregistré";
                RenderPreview();
            }

            StatusText.Text =
                $"Note rapide ajoutée · {dialog.SelectedScope.DisplayName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ExternalModificationException)
        {
            MessageBox.Show(
                this,
                $"La note rapide n'a pas pu être ajoutée.\n\n{exception.Message}",
                "Note rapide",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>ShowQuickNotesAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ShowQuickNotesAsync()
    {
        try
        {
            global::Nodalis.Infrastructure.Notes.QuickNotesService service = new QuickNotesService();
            string contextPath =
                _selectedNode?.FullPath ??
                _root.FullPath;

            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Notes.QuickNotesSnapshot> snapshots = await service.ReadAggregateAsync(
                _root.FullPath,
                contextPath);

            global::Nodalis.App.Dialogs.QuickNotesOverviewDialog dialog = new QuickNotesOverviewDialog(
                snapshots,
                _root.FullPath)
            {
                Owner = this
            };

            dialog.ShowDialog();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            MessageBox.Show(
                this,
                $"Les notes rapides n'ont pas pu être chargées.\n\n{exception.Message}",
                "Notes rapides",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>CreateProjectAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task CreateProjectAsync()
    {
        try
        {
            global::Nodalis.Infrastructure.Projects.ProjectCreationTargetDiscovery targetDiscovery = new ProjectCreationTargetDiscovery();
            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Projects.ProjectCreationTarget> targets = await targetDiscovery.DiscoverAsync(
                _root.FullPath);

            if (targets.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Aucune application Nodalis n'existe encore dans le workspace. " +
                    "Créez d'abord une application avant de créer un projet.",
                    "Nouveau projet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            global::Nodalis.Core.Templates.ProjectProfileCatalog profiles = await _templateStore.LoadProjectProfilesAsync();

            global::Nodalis.App.Dialogs.NewProjectDialog dialog = new NewProjectDialog(
                targets,
                profiles.Profiles)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (!await TryCloseCurrentDocumentAsync(
                    "créer le nouveau projet"))
            {
                return;
            }

            global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(
                _root.FullPath);

            global::Nodalis.Core.Projects.ProjectCreationResult result = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = dialog.ProjectName,
                    Complexity = dialog.SelectedComplexity,
                    Target = dialog.SelectedTarget,
                    BusinessLinks = dialog.BusinessLinks.ToList()
                });

            await RefreshNavigationAsync(
                result.OverviewFilePath);

            StatusText.Text =
                $"Projet créé · {Path.GetRelativePath(_root.FullPath, result.ProjectDirectory)}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            TemplateRenderException)
        {
            MessageBox.Show(
                this,
                $"Le projet n'a pas pu être créé.\n\n{exception.Message}",
                "Nouveau projet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>CreateDecisionAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task CreateDecisionAsync()
    {
        string contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            (string ScopeKind, string ScopeName)? scope = await _decisionService.ResolveScopeAsync(
                contextPath);

            if (scope is null)
            {
                MessageBox.Show(
                    this,
                    "Sélectionnez une application, un projet, un sous-projet ou un document rattaché avant de créer une décision.",
                    "Decision Record",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string? sourcePath = null;
            string? sourceDisplayName = null;
            IReadOnlyList<string> candidates = [];

            if (_selectedNode?.Kind == WorkspaceNodeKind.Document)
            {
                sourcePath = _selectedNode.FullPath;
                sourceDisplayName = _selectedNode.DisplayName;
                candidates = await _decisionService.ExtractDecisionCandidatesAsync(
                    sourcePath);
            }

            global::Nodalis.App.Dialogs.DecisionDialog dialog = new DecisionDialog(
                scope.Value.ScopeKind,
                scope.Value.ScopeName,
                sourceDisplayName,
                candidates)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true ||
                dialog.Draft is null)
            {
                return;
            }

            global::Nodalis.Core.Decisions.DecisionCreationResult result = await _decisionService.CreateAsync(
                contextPath,
                dialog.Draft,
                sourcePath);

            if (!await TryCloseCurrentDocumentAsync(
                    "ouvrir le Decision Record créé"))
            {
                await RefreshNavigationAsync();

                StatusText.Text =
                    $"Décision créée · {Path.GetRelativePath(_root.FullPath, result.FilePath)}";
                return;
            }

            await RefreshNavigationAsync(
                result.FilePath);

            await RefreshLinkIndexAndContextAsync();

            StatusText.Text =
                $"Décision créée · {result.ScopeKind} {result.ScopeName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            TemplateRenderException)
        {
            MessageBox.Show(
                this,
                $"La décision n'a pas pu être créée.\n\n{exception.Message}",
                "Decision Record",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>ShowDecisionsAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task ShowDecisionsAsync()
    {
        string contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            (string ScopeKind, string ScopeName)? scope = await _decisionService.ResolveScopeAsync(
                contextPath);

            if (scope is null)
            {
                MessageBox.Show(
                    this,
                    "Sélectionnez une application ou un projet pour consulter ses décisions.",
                    "Décisions",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            global::Nodalis.App.Dialogs.DecisionListDialog dialog = new DecisionListDialog(
                _root.FullPath,
                contextPath,
                $"{scope.Value.ScopeKind} · {scope.Value.ScopeName}")
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true &&
                dialog.SelectedDecision is not null)
            {
                await NavigateToDecisionAsync(
                    dialog.SelectedDecision);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Décisions",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Performs the <c>NavigateToDecisionAsync</c> operation.
    /// </summary>
    /// <param name="decision">The <c>decision</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task NavigateToDecisionAsync(
            DecisionRecord decision)
    {
        string fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                decision.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        global::Nodalis.App.Navigation.NavigationNodeViewModel? node = FindAndExpand(
            _root,
            fullPath);

        if (node is null)
        {
            await RefreshNavigationAsync();
            node = FindAndExpand(
                _root,
                fullPath);
        }

        if (node is null)
        {
            StatusText.Text =
                $"Decision Record introuvable : {decision.SourceRelativePath}";
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir la décision"))
        {
            return;
        }

        _restoringSelection = true;
        node.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = node;
        await DisplayNodeAsync(
            node);

        StatusText.Text =
            $"Décision · {decision.Title}";
    }

    /// <summary>
    /// Performs the <c>CreateMeetingAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task CreateMeetingAsync()
    {
        string contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            (string ScopeKind, string ScopeName)? scope = await _meetingService.ResolveScopeAsync(
                contextPath);

            if (scope is null)
            {
                MessageBox.Show(
                    this,
                    "Sélectionnez une application, un projet, un sous-projet ou un document rattaché avant de créer un compte-rendu.",
                    "Compte-rendu de réunion",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            global::Nodalis.App.Dialogs.MeetingDialog dialog = new MeetingDialog(
                scope.Value.ScopeKind,
                scope.Value.ScopeName)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true ||
                dialog.Draft is null)
            {
                return;
            }

            global::Nodalis.Core.Meetings.MeetingCreationResult result = await _meetingService.CreateAsync(
                contextPath,
                dialog.Draft);

            if (!await TryCloseCurrentDocumentAsync(
                    "ouvrir le compte-rendu créé"))
            {
                await RefreshNavigationAsync();

                StatusText.Text =
                    $"Réunion créée · {Path.GetRelativePath(_root.FullPath, result.FilePath)}";
                return;
            }

            await RefreshNavigationAsync(
                result.FilePath);

            await RefreshDashboardTasksAsync();

            StatusText.Text =
                $"Réunion créée · {result.ScopeKind} {result.ScopeName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            TemplateRenderException)
        {
            MessageBox.Show(
                this,
                $"Le compte-rendu n'a pas pu être créé.\n\n{exception.Message}",
                "Compte-rendu de réunion",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>CreateNoteAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task CreateNoteAsync()
    {
        string? targetDirectory = ResolveNewNoteDirectory();

        if (targetDirectory is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez Global, une application, un module, un projet " +
                "ou une section avant de créer la note.",
                "Nouvelle note",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            global::Nodalis.Core.Templates.TemplateCatalog catalog = await _templateStore.LoadTemplateCatalogAsync();

            global::Nodalis.App.Dialogs.NewNoteDialog dialog = new NewNoteDialog(catalog.Templates)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            global::System.Guid documentId = Guid.NewGuid();
            global::System.Collections.Generic.Dictionary<string, string> variables = MarkdownTemplateRenderer.CreateStandardVariables(
                dialog.NoteTitle,
                documentId,
                DateTimeOffset.Now,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["workspace.name"] = _root.DisplayName,
                    ["context.name"] =
                        _selectedNode?.DisplayName ?? _root.DisplayName,
                    ["context.kind"] =
                        GetKindLabel(
                            _selectedNode?.Kind ??
                            WorkspaceNodeKind.Workspace)
                });

            string content;
            string desiredFileName;

            if (dialog.SelectedTemplateKey is null)
            {
                content = string.Empty;
                desiredFileName = $"{dialog.NoteTitle}.md";
            }
            else
            {
                global::Nodalis.Core.Templates.MarkdownTemplateDefinition definition = catalog.Templates.Single(
                    template => string.Equals(
                        template.Key,
                        dialog.SelectedTemplateKey,
                        StringComparison.OrdinalIgnoreCase));

                content = await _templateStore.RenderAsync(
                    definition.Key,
                    variables);

                desiredFileName = MarkdownTemplateRenderer.Render(
                    definition.DefaultFileName,
                    variables);

                if (!desiredFileName.EndsWith(
                        ".md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    desiredFileName += ".md";
                }
            }

            string filePath = WindowsPathRules.GetUniqueFilePath(
                targetDirectory,
                desiredFileName);

            await AtomicFileWriter.WriteAllTextAsync(
                filePath,
                content);

            if (!await TryCloseCurrentDocumentAsync(
                    "ouvrir la nouvelle note"))
            {
                StatusText.Text =
                    "Nouvelle note créée, document actuel conservé.";
                return;
            }

            await RefreshNavigationAsync(filePath);

            StatusText.Text =
                $"Note créée · {Path.GetRelativePath(_root.FullPath, filePath)}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            TemplateRenderException)
        {
            MessageBox.Show(
                this,
                $"La note n'a pas pu être créée.\n\n{exception.Message}",
                "Nouvelle note",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Performs the <c>ResolveNewNoteDirectory</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private string? ResolveNewNoteDirectory()
    {
        if (_selectedNode is null)
        {
            return _root.FullPath;
        }

        if (_selectedNode.Kind is
            WorkspaceNodeKind.ApplicationsRoot or
            WorkspaceNodeKind.ProjectsRoot)
        {
            return null;
        }

        if (_selectedNode.Kind == WorkspaceNodeKind.Document)
        {
            return Path.GetDirectoryName(
                _selectedNode.FullPath);
        }

        return Directory.Exists(_selectedNode.FullPath)
            ? _selectedNode.FullPath
            : null;
    }

    /// <summary>
    /// Performs the <c>RefreshNavigationAsync</c> operation.
    /// </summary>
    /// <param name="openPath">The <c>openPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshNavigationAsync(
            string? openPath = null)
    {
        global::System.Collections.Generic.HashSet<global::System.Guid> expandedIds = GetExpandedNodeIds();

        global::Nodalis.Core.Navigation.WorkspaceNavigationNode root = await _navigationBuilder.BuildAsync(
            _root.FullPath);

        _root = CreateRootViewModel(
            root,
            expandedIds);

        NavigationTree.ItemsSource =
            new[] { _root };

        RefreshDocumentTabsFromNavigation();
        await RefreshLinkIndexAsync();

        if (openPath is null)
        {
            return;
        }

        global::Nodalis.App.Navigation.NavigationNodeViewModel? target = FindAndExpand(
            _root,
            Path.GetFullPath(openPath));

        if (target is null)
        {
            return;
        }

        _selectedNode = target;
        target.IsSelected = true;
        await DisplayNodeAsync(target);
    }

    /// <summary>
    /// Performs the <c>GetExpandedNodeIds</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private HashSet<Guid> GetExpandedNodeIds() =>
            _root
                .DescendantsAndSelf()
                .Where(node => node.IsExpanded)
                .Select(node => node.Id)
                .ToHashSet();

    /// <summary>
    /// Performs the <c>CreateRootViewModel</c> operation.
    /// </summary>
    /// <param name="root">The <c>root</c> value.</param>
    /// <param name="expandedIds">The <c>expandedIds</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static NavigationNodeViewModel CreateRootViewModel(
            WorkspaceNavigationNode root,
            IReadOnlySet<Guid> expandedIds)
    {
        global::Nodalis.App.Navigation.NavigationNodeViewModel viewModel = new NavigationNodeViewModel(
            root,
            expandedIds);

        viewModel.IsExpanded = true;
        return viewModel;
    }

    /// <summary>
    /// Performs the <c>FindAndExpand</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <param name="targetPath">The <c>targetPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static NavigationNodeViewModel? FindAndExpand(
            NavigationNodeViewModel node,
            string targetPath)
    {
        if (string.Equals(
                Path.GetFullPath(node.FullPath),
                targetPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (global::Nodalis.App.Navigation.NavigationNodeViewModel child in node.Children)
        {
            global::Nodalis.App.Navigation.NavigationNodeViewModel? found = FindAndExpand(
                child,
                targetPath);

            if (found is null)
            {
                continue;
            }

            node.IsExpanded = true;
            return found;
        }

        return null;
    }

    /// <summary>
    /// Performs the <c>ToggleContextPanel_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void ToggleContextPanel_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (_contextPanelOpen)
        {
            _lastContextWidth = Math.Max(
                180,
                ContextColumn.ActualWidth);
        }

        _contextPanelOpen = !_contextPanelOpen;
        ApplyContextPanelState();
    }

    /// <summary>
    /// Performs the <c>ApplyContextPanelState</c> operation.
    /// </summary>
    private void ApplyContextPanelState()
    {
        if (_contextPanelOpen)
        {
            ContextColumn.Width = new GridLength(
                Math.Max(180, _lastContextWidth));

            ContextSplitterColumn.Width = new GridLength(5);
            ContextPanel.Visibility = Visibility.Visible;
            ContextSplitter.Visibility = Visibility.Visible;
            return;
        }

        ContextColumn.Width = new GridLength(0);
        ContextSplitterColumn.Width = new GridLength(0);
        ContextPanel.Visibility = Visibility.Collapsed;
        ContextSplitter.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Performs the <c>GetKindLabel</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string GetKindLabel(WorkspaceNodeKind kind) =>
            kind switch
            {
                WorkspaceNodeKind.Workspace => "Workspace",
                WorkspaceNodeKind.Global => "Global",
                WorkspaceNodeKind.ApplicationsRoot => "Applications",
                WorkspaceNodeKind.Application => "Application",
                WorkspaceNodeKind.Module => "Module",
                WorkspaceNodeKind.ProjectsRoot => "Projets",
                WorkspaceNodeKind.Project => "Projet",
                WorkspaceNodeKind.Section => "Section",
                WorkspaceNodeKind.Document => "Document Markdown",
                WorkspaceNodeKind.Folder => "Dossier",
                _ => kind.ToString()
            };
}
