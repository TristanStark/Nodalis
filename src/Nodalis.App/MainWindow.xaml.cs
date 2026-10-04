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
using Nodalis.App.Glossary;
using Nodalis.App.Markdown;
using Nodalis.App.Navigation;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Decisions;
using Nodalis.Core.Glossary;
using Nodalis.Core.Links;
using Nodalis.Core.Meetings;
using Nodalis.Core.Milestones;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Core.Settings;
using Nodalis.Core.Tasks;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Attachments;
using Nodalis.Infrastructure.Decisions;
using Nodalis.Infrastructure.Documents;
using Nodalis.Infrastructure.Glossary;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Meetings;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Notes;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Tasks;

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
    private readonly DispatcherTimer _previewTimer;

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
                await RefreshGlossaryContextAsync(
                    _selectedNode?.FullPath ?? _root.FullPath);
                UpdateGlossaryAnnotations();
                await RefreshDashboardTasksAsync();
                await RefreshDashboardMilestonesAsync();
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

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (!await TryCloseCurrentDocumentAsync(
                "fermer Nodalis"))
        {
            return;
        }

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
                        MarkdownEditorTextBox.FontSize)
                }
            };

            await _preferencesStore.SaveAsync(_preferences);
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
        _allowClose = true;
        Close();
    }

    private void NavigationTree_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var item = FindVisualParent<TreeViewItem>(
            e.OriginalSource as DependencyObject);

        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void NavigationTree_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        if (_selectedNode is null)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();

        void AddItem(
            string header,
            RoutedEventHandler handler)
        {
            var item = new MenuItem
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
                    "Supprimer",
                    async (_, _) => await DeleteApplicationOrModuleAsync(_selectedNode));
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
                    "Supprimer",
                    async (_, _) => await DeleteApplicationOrModuleAsync(_selectedNode));
                break;

            case WorkspaceNodeKind.Project:
                AddItem(
                    IsFavorite(_selectedNode)
                        ? "Retirer des favoris"
                        : "Ajouter aux favoris",
                    async (_, _) => await ToggleFavoriteAsync(_selectedNode));
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
                break;

            default:
                e.Handled = true;
                return;
        }

        NavigationTree.ContextMenu = menu;
    }

    private async Task CreateApplicationAsync()
    {
        var dialog = new TextPromptDialog(
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
            var service = new ApplicationStructureService(
                _root.FullPath);

            var path = await service.CreateApplicationAsync(
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

    private async Task CreateModuleAsync(
        NavigationNodeViewModel parent)
    {
        var dialog = new TextPromptDialog(
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
                var manifestReader = new ApplicationStructureService(
                    _root.FullPath);

                var manifest = await manifestReader.LoadModuleAsync(
                    parent.FullPath);

                applicationId = manifest.ApplicationId;
                parentModuleId = manifest.Id;
            }

            var service = new ApplicationStructureService(
                _root.FullPath);

            var path = await service.CreateModuleAsync(
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

    private async Task RenameDocumentAsync(
        NavigationNodeViewModel node)
    {
        var dialog = new TextPromptDialog(
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

            var service = new DocumentStructureService(
                _root.FullPath);

            var path = await service.RenameAsync(
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

    private async Task RenameApplicationOrModuleAsync(
        NavigationNodeViewModel node)
    {
        var dialog = new TextPromptDialog(
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

            var service = new ApplicationStructureService(
                _root.FullPath);

            var path = node.Kind == WorkspaceNodeKind.Application
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

    private async Task MoveModuleAsync(
        NavigationNodeViewModel node)
    {
        try
        {
            var service = new ApplicationStructureService(
                _root.FullPath);

            var manifest = await service.LoadModuleAsync(
                node.FullPath);

            var discovery = new ProjectCreationTargetDiscovery();
            var targets = (await discovery.DiscoverAsync(
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

            var dialog = new MoveModuleDialog(targets)
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

            var path = await service.MoveModuleAsync(
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

    private async Task DeleteApplicationOrModuleAsync(
        NavigationNodeViewModel node)
    {
        var answer = MessageBox.Show(
            this,
            $"Supprimer « {node.DisplayName} » ?\n\n" +
            "La suppression sera refusée si l'élément contient des données.",
            "Supprimer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!await TryCloseCurrentDocumentAsync(
                    "supprimer cet élément"))
            {
                return;
            }

            var service = new ApplicationStructureService(
                _root.FullPath);

            if (node.Kind == WorkspaceNodeKind.Application)
            {
                await service.DeleteApplicationAsync(
                    node.FullPath);
            }
            else
            {
                await service.DeleteModuleAsync(
                    node.FullPath);
            }

            await RefreshNavigationAsync();
            StatusText.Text = $"Supprimé · {node.DisplayName}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            Nodalis.Core.Validation.DomainValidationException)
        {
            ShowStructureError(
                "Supprimer",
                exception);
        }
    }

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

    private async void NavigationTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_restoringSelection ||
            e.NewValue is not NavigationNodeViewModel node)
        {
            return;
        }

        var previous = _selectedNode;

        if (previous is not null &&
            previous.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(previous, node))
        {
            var canLeave = await TryCloseCurrentDocumentAsync(
                "changer de document");

            if (!canLeave)
            {
                _restoringSelection = true;
                previous.IsSelected = true;
                _restoringSelection = false;
                return;
            }
        }

        _selectedNode = node;
        await DisplayNodeAsync(node);
    }

    private async Task DisplayNodeAsync(NavigationNodeViewModel node)
    {
        DocumentTitleText.Text = node.DisplayName;

        var relativePath = Path.GetRelativePath(
            _root.FullPath,
            node.FullPath);

        DocumentPathText.Text =
            relativePath == "."
                ? _root.FullPath
                : relativePath;

        ContextTitleText.Text = node.DisplayName;
        ContextKindText.Text = GetKindLabel(node.Kind);
        ContextPathText.Text = node.FullPath;

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

    private async Task OpenDocumentAsync(
        NavigationNodeViewModel node)
    {
        try
        {
            var session = await TextDocumentSession.OpenAsync(
                node.FullPath);

            _documentSession = session;
            _documentDirty = false;
            _conflictWarningShown = false;

            _autosave = new DocumentAutosaveController(
                session,
                TimeSpan.FromMilliseconds(
                    _preferences.Editor.AutosaveDelayMilliseconds));

            _autosave.Saved += Autosave_Saved;
            _autosave.ConflictDetected += Autosave_ConflictDetected;
            _autosave.SaveFailed += Autosave_SaveFailed;

            _suppressEditorChanges = true;
            MarkdownEditorTextBox.Text = session.Content;
            MarkdownEditorTextBox.CaretIndex = 0;
            _suppressEditorChanges = false;

            DashboardHost.Visibility = Visibility.Collapsed;
            NodeSummaryHost.Visibility = Visibility.Collapsed;
            EditorToolbar.Visibility = Visibility.Visible;
            DocumentEditorHost.Visibility = Visibility.Visible;

            SaveStateText.Text = "Enregistré";
            StatusText.Text =
                $"Document · {Path.GetRelativePath(_root.FullPath, node.FullPath)}";

            if (_linkIndex.Targets.Count == 0)
            {
                await RefreshLinkIndexAsync();
            }

            UpdateLinkContext(node.FullPath);
            await TrackRecentContextAsync(node);
            await RefreshGlossaryContextAsync(
                node.FullPath);
            UpdateGlossaryAnnotations();
            RenderPreview();
            MarkdownEditorTextBox.Focus();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException)
        {
            _documentSession = null;
            _autosave = null;

            NodeSummaryHost.Visibility = Visibility.Visible;
            EditorToolbar.Visibility = Visibility.Collapsed;
            DocumentEditorHost.Visibility = Visibility.Collapsed;

            NodeSummaryText.Text =
                $"Impossible de lire le document.\n\n{exception.Message}";

            StatusText.Text = "Erreur de lecture";
        }
    }

    private void ShowNodeSummary(
        NavigationNodeViewModel node)
    {
        _documentSession = null;
        _autosave = null;
        _documentDirty = false;

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

    private async Task<bool> TryCloseCurrentDocumentAsync(
        string actionDescription)
    {
        if (_autosave is null)
        {
            return true;
        }

        await _autosave.FlushAsync();

        if (_documentDirty)
        {
            var answer = MessageBox.Show(
                this,
                "Les dernières modifications n'ont pas pu être enregistrées " +
                "(le fichier a peut-être été modifié ailleurs ou est verrouillé).\n\n" +
                $"Voulez-vous quand même {actionDescription} et abandonner " +
                "les modifications locales non enregistrées ?",
                "Modifications non enregistrées",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        await _autosave.DisposeAsync();
        _autosave = null;
        _documentSession = null;
        _documentDirty = false;
        _previewTimer.Stop();

        return true;
    }

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
        SaveStateText.Text = "Modification…";

        _autosave.Schedule(
            MarkdownEditorTextBox.Text);

        _previewTimer.Stop();
        _previewTimer.Start();

        if (!_suppressLinkAutocomplete)
        {
            _ = RefreshInternalLinkSuggestionsAsync();
        }
    }

    private void PreviewTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _previewTimer.Stop();
        RenderPreview();
        UpdateGlossaryAnnotations();
    }

    private void RenderPreview()
    {
        if (!_previewVisible ||
            _selectedNode?.Kind != WorkspaceNodeKind.Document)
        {
            return;
        }

        var baseDirectory = Path.GetDirectoryName(
            _selectedNode.FullPath);

        MarkdownPreview.Document =
            MarkdownFlowDocumentRenderer.Render(
                MarkdownEditorTextBox.Text,
                baseDirectory,
                OnInternalLinkClicked,
                OnMarkdownLinkClicked);
    }

    private void Autosave_Saved(
        object? sender,
        EventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_documentSession is null)
            {
                return;
            }

            _documentDirty =
                !string.Equals(
                    MarkdownEditorTextBox.Text,
                    _documentSession.Content,
                    StringComparison.Ordinal);

            SaveStateText.Text =
                _documentDirty
                    ? "Modification…"
                    : "Enregistré";

            if (!_documentDirty)
            {
                StatusText.Text = "Enregistré localement";

                try
                {
                    await RefreshLinkIndexAndContextAsync();
                    await RefreshGlossaryContextAsync(
                        _selectedNode?.FullPath ?? _root.FullPath);
                    UpdateGlossaryAnnotations();
                    await RefreshDashboardTasksAsync();
                    await RefreshDashboardMilestonesAsync();
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException)
                {
                    StatusText.Text =
                        $"Enregistré · index liens indisponible : {exception.Message}";
                }
            }
        });
    }

    private void Autosave_ConflictDetected(
        object? sender,
        AutosaveConflictEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _documentDirty = true;
            SaveStateText.Text = "Conflit externe";
            StatusText.Text =
                "Le fichier a été modifié en dehors de Nodalis.";

            if (_conflictWarningShown)
            {
                return;
            }

            _conflictWarningShown = true;

            MessageBox.Show(
                this,
                "Ce fichier a été modifié par un autre programme depuis son ouverture. " +
                "Nodalis n'écrasera pas cette version automatiquement.\n\n" +
                "Vos modifications restent visibles dans l'éditeur tant que vous " +
                "ne changez pas de document.",
                "Conflit de modification",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    private void Autosave_SaveFailed(
        object? sender,
        AutosaveFailureEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _documentDirty = true;
            SaveStateText.Text = "Erreur d'enregistrement";
            StatusText.Text = e.Exception.Message;
        });
    }

    private async void NewNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CreateNoteAsync();
    }

    private async void NewProject_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CreateProjectAsync();
    }

    private async void CaptureQuickNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CaptureQuickNoteAsync();
    }

    private async void ShowQuickNotes_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ShowQuickNotesAsync();
    }

    private async void CommandPalette_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ShowCommandPaletteAsync();
    }

    private async void AttachFile_Click(
        object sender,
        RoutedEventArgs e)
    {
        await AttachFileAsync();
    }

    private async void ShowAllTasks_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ShowTasksAsync(
            global: true);
    }

    private async void MainWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
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

        if (Keyboard.Modifiers == ModifierKeys.Control &&
            e.Key == Key.K)
        {
            e.Handled = true;
            await OpenInternalLinkPickerAsync();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control &&
            e.Key == Key.F)
        {
            e.Handled = true;
            await SearchAsync();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control &&
            e.Key == Key.P)
        {
            e.Handled = true;
            await ShowCommandPaletteAsync();
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) &&
            e.Key == Key.N)
        {
            e.Handled = true;
            await CaptureQuickNoteAsync();
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
            e.Key == Key.Q)
        {
            e.Handled = true;
            await ShowQuickNotesAsync();
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
            e.Key == Key.N)
        {
            e.Handled = true;
            await CreateProjectAsync();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.N)
            {
                e.Handled = true;
                await CreateNoteAsync();
                return;
            }

            if (e.Key == Key.S)
            {
                e.Handled = true;
                await SaveCurrentDocumentAsync();
                return;
            }

            if (e.Key == Key.B &&
                _documentSession is not null)
            {
                e.Handled = true;
                WrapSelection("**", "**");
                return;
            }

            if (e.Key == Key.I &&
                _documentSession is not null)
            {
                e.Handled = true;
                WrapSelection("*", "*");
                return;
            }

            if (e.Key == Key.Oem3 &&
                _documentSession is not null)
            {
                e.Handled = true;
                var marker = ((char)96).ToString();
                WrapSelection(marker, marker);
            }
        }
    }

    private async Task SaveCurrentDocumentAsync()
    {
        if (_autosave is null)
        {
            return;
        }

        await _autosave.FlushAsync();

        SaveStateText.Text =
            _documentDirty
                ? "Non enregistré"
                : "Enregistré";
    }

    private void Bold_Click(
        object sender,
        RoutedEventArgs e) =>
        WrapSelection("**", "**");

    private void Italic_Click(
        object sender,
        RoutedEventArgs e) =>
        WrapSelection("*", "*");

    private void InlineCode_Click(
        object sender,
        RoutedEventArgs e)
    {
        var marker = ((char)96).ToString();
        WrapSelection(marker, marker);
    }

    private void WrapSelection(
        string prefix,
        string suffix)
    {
        if (_documentSession is null)
        {
            return;
        }

        var start = MarkdownEditorTextBox.SelectionStart;
        var length = MarkdownEditorTextBox.SelectionLength;
        var selectedText = MarkdownEditorTextBox.SelectedText;

        MarkdownEditorTextBox.SelectedText =
            prefix + selectedText + suffix;

        if (length == 0)
        {
            MarkdownEditorTextBox.CaretIndex =
                start + prefix.Length;
        }
        else
        {
            MarkdownEditorTextBox.Select(
                start + prefix.Length,
                length);
        }

        MarkdownEditorTextBox.Focus();
    }

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
            return;
        }

        PreviewColumn.Width = new GridLength(0);
        PreviewSplitterColumn.Width = new GridLength(0);
        MarkdownPreview.Visibility = Visibility.Collapsed;
        PreviewSplitter.Visibility = Visibility.Collapsed;
    }

    private async void OnInternalLinkClicked(string target)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        var resolution = WorkspaceLinkIndexService.Resolve(
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

    private void ShowDashboard()
    {
        _documentSession = null;
        _autosave = null;
        _documentDirty = false;

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

        var recent = _preferences.RecentItems
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

        var recentDocuments = recent
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
    }

    private DashboardItemViewModel? CreateDashboardItem(
        UserItemReference reference,
        DateTimeOffset? lastOpenedUtc)
    {
        if (!Guid.TryParse(
                reference.Key,
                out var targetId))
        {
            return null;
        }

        var target = _linkIndex.Targets.FirstOrDefault(candidate =>
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

    private async void DashboardList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBox list ||
            list.SelectedItem is not DashboardItemViewModel item)
        {
            return;
        }

        var target = _linkIndex.Targets.FirstOrDefault(candidate =>
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

    private bool IsFavorite(
        NavigationNodeViewModel node)
    {
        var target = FindIndexedTarget(
            node);

        return target is not null &&
               _preferences.Favorites.Any(reference =>
                   string.Equals(
                       reference.Key,
                       target.Id.ToString("D"),
                       StringComparison.OrdinalIgnoreCase));
    }

    private async Task ToggleFavoriteAsync(
        NavigationNodeViewModel node)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        var target = FindIndexedTarget(
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

        var key = target.Id.ToString("D");

        var favorites = _preferences.Favorites
            .Where(reference =>
                !string.Equals(
                    reference.Key,
                    key,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removed =
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

    private async Task TrackRecentContextAsync(
        NavigationNodeViewModel node)
    {
        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        var targets = new List<LinkTargetEntry>();

        var primary = FindIndexedTarget(
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
            var parentProject = _linkIndex.Targets
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

        var recent = _preferences.RecentItems
            .ToList();

        var now = DateTimeOffset.UtcNow;

        foreach (var target in targets
                     .DistinctBy(target => target.Id))
        {
            var key = target.Id.ToString("D");

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

        var relativePath = Path.GetRelativePath(
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

    private static bool IsRelativeAncestor(
        string candidateParent,
        string child)
    {
        var parent = candidateParent
            .TrimEnd('/') + "/";

        return child.StartsWith(
            parent,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshLinkIndexAsync()
    {
        _linkIndex = await _linkIndexService.RefreshAsync();
        RefreshDashboard();
    }

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

    private void UpdateLinkContext(string documentPath)
    {
        var relativePath = Path.GetRelativePath(
                _root.FullPath,
                Path.GetFullPath(documentPath))
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

        var target = _linkIndex.Targets.FirstOrDefault(candidate =>
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

        var backlinks = _linkIndex.References
            .Where(reference =>
                reference.TargetId == target.Id)
            .Select(reference =>
            {
                var source = _linkIndex.Targets.FirstOrDefault(candidate =>
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

        var unresolved = _linkIndex.References
            .Where(reference =>
                reference.SourceId == target.Id &&
                reference.TargetId is null)
            .ToArray();

        var missing = unresolved.Count(reference =>
            WorkspaceLinkIndexService.Resolve(
                _linkIndex,
                reference.RawTarget).Status ==
            LinkResolutionStatus.Missing);

        var ambiguous = unresolved.Length - missing;

        ContextBrokenLinksText.Text =
            $"Liens internes : {backlinks.Length} backlink(s) · " +
            $"{missing} cassé(s) · {ambiguous} ambigu(s)";
    }

    private async Task NavigateToLinkTargetAsync(
        LinkTargetEntry target,
        int? lineNumber = null)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                target.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        var node = FindAndExpand(
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

        var alias = MarkdownEditorTextBox.SelectedText;
        var start = MarkdownEditorTextBox.SelectionStart;
        var length = MarkdownEditorTextBox.SelectionLength;

        await OpenInternalLinkSuggestionsAsync(
            alias,
            start,
            length,
            string.IsNullOrWhiteSpace(alias)
                ? null
                : alias);
    }

    private async Task RefreshInternalLinkSuggestionsAsync()
    {
        if (_documentSession is null ||
            !TryGetOpenInternalLinkToken(
                out var start,
                out var length,
                out var query))
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

        var normalized = query.Trim();

        var suggestions = _linkIndex.Targets
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

        if (suggestions.Length == 0)
        {
            InternalLinkPopup.IsOpen = false;
            return;
        }

        _linkSuggestionStart = start;
        _linkSuggestionLength = length;
        _linkSuggestionAlias = alias;

        InternalLinkSuggestions.ItemsSource = suggestions;
        InternalLinkSuggestions.SelectedIndex = 0;

        var caretRect = MarkdownEditorTextBox.GetRectFromCharacterIndex(
            MarkdownEditorTextBox.CaretIndex,
            trailingEdge: true);

        InternalLinkPopup.HorizontalOffset =
            Math.Max(8, caretRect.X);
        InternalLinkPopup.VerticalOffset =
            Math.Max(8, caretRect.Bottom + 4);
        InternalLinkPopup.IsOpen = true;
    }

    private bool TryGetOpenInternalLinkToken(
        out int start,
        out int length,
        out string query)
    {
        start = 0;
        length = 0;
        query = string.Empty;

        var caret = MarkdownEditorTextBox.CaretIndex;
        var text = MarkdownEditorTextBox.Text;

        if (caret < 2 ||
            caret > text.Length)
        {
            return false;
        }

        var beforeCaret = text[..caret];
        var open = beforeCaret.LastIndexOf(
            "[[",
            StringComparison.Ordinal);

        if (open < 0)
        {
            return false;
        }

        var close = beforeCaret.LastIndexOf(
            "]]",
            StringComparison.Ordinal);

        if (close > open)
        {
            return false;
        }

        var rawQuery = beforeCaret[(open + 2)..];

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

    private void MoveInternalLinkSelection(int delta)
    {
        if (InternalLinkSuggestions.Items.Count == 0)
        {
            return;
        }

        var current = InternalLinkSuggestions.SelectedIndex;
        var next = Math.Clamp(
            current + delta,
            0,
            InternalLinkSuggestions.Items.Count - 1);

        InternalLinkSuggestions.SelectedIndex = next;
        InternalLinkSuggestions.ScrollIntoView(
            InternalLinkSuggestions.SelectedItem);
    }

    private void CompleteInternalLinkSuggestion()
    {
        if (InternalLinkSuggestions.SelectedItem is not LinkTargetEntry target)
        {
            return;
        }

        var duplicateDisplayNames = _linkIndex.Targets.Count(candidate =>
            string.Equals(
                candidate.DisplayName,
                target.DisplayName,
                StringComparison.CurrentCultureIgnoreCase));

        var linkTarget = duplicateDisplayNames == 1
            ? target.DisplayName
            : target.QualifiedName;

        var syntax = string.IsNullOrWhiteSpace(_linkSuggestionAlias)
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

    private void InternalLinkSuggestions_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        CompleteInternalLinkSuggestion();

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

    private async void OnMarkdownLinkClicked(string target)
    {
        if (_selectedNode?.Kind == WorkspaceNodeKind.Document)
        {
            var baseDirectory = Path.GetDirectoryName(
                _selectedNode.FullPath);

            if (!string.IsNullOrWhiteSpace(baseDirectory) &&
                !Uri.TryCreate(
                    target,
                    UriKind.Absolute,
                    out var absoluteUri))
            {
                var localPath = Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        target.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

                var match = _root
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
                    out var uri) &&
                !uri.IsFile)
            {
                CopyExternalTargetToClipboard(
                    target);
                return;
            }

            var attachmentService = new AttachmentService(
                _root.FullPath);

            var reference = attachmentService.Resolve(
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

    private async Task SearchAsync()
    {
        var dialog = new SearchDialog(
            _root.FullPath,
            _selectedNode?.FullPath)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.SelectedResult is null)
        {
            return;
        }

        var target = _root
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

    private void MoveCaretToLine(int lineNumber)
    {
        if (_documentSession is null ||
            lineNumber <= 1)
        {
            MarkdownEditorTextBox.CaretIndex = 0;
            return;
        }

        var text = MarkdownEditorTextBox.Text;
        var currentLine = 1;
        var index = 0;

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

    private async Task ShowCommandPaletteAsync()
    {
        var commands = BuildPaletteCommands();

        var dialog = new CommandPaletteDialog(commands)
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

    private IReadOnlyList<PaletteCommand> BuildPaletteCommands()
    {
        var commands = new List<PaletteCommand>
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
            }
        };

        foreach (var node in _root
                     .DescendantsAndSelf()
                     .Where(node => node.Kind is
                         WorkspaceNodeKind.Application or
                         WorkspaceNodeKind.Module or
                         WorkspaceNodeKind.Project or
                         WorkspaceNodeKind.Document))
        {
            var capturedNode = node;
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

    private void AttachGlossaryAdorner()
    {
        if (_glossaryAdorner is not null)
        {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(
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

    private async Task RefreshGlossaryContextAsync(
        string? contextPath)
    {
        _glossaryScopes = await _glossaryService.ResolveScopesAsync(
            _root.FullPath,
            contextPath);

        var entries = new List<GlossaryEntry>();

        foreach (var scope in _glossaryScopes)
        {
            entries.AddRange(
                await _glossaryService.LoadEntriesAsync(
                    scope));
        }

        _glossaryEntries = entries;
    }

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

    private void MarkdownEditorTextBox_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        var match = FindGlossaryMatchAtPoint(
            e.GetPosition(MarkdownEditorTextBox));

        MarkdownEditorTextBox.ToolTip =
            match is null
                ? null
                : $"{match.Entry.Term}\n{match.Entry.Definition}\n\n{match.Entry.Scope.DisplayName}\nDouble-cliquer pour ouvrir.";
    }

    private async void MarkdownEditorTextBox_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        var match = FindGlossaryMatchAtPoint(
            e.GetPosition(MarkdownEditorTextBox));

        if (match is null)
        {
            return;
        }

        e.Handled = true;
        await OpenGlossaryEntryAsync(
            match.Entry);
    }

    private void MarkdownEditorTextBox_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        var menu = new ContextMenu();

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

        var selected = NormalizeGlossarySelection(
            MarkdownEditorTextBox.SelectedText);

        if (!string.IsNullOrWhiteSpace(selected) &&
            _glossaryScopes.Count > 0)
        {
            menu.Items.Add(
                new Separator());

            var addToGlossary = new MenuItem
            {
                Header = "Ajouter au glossaire"
            };

            foreach (var scope in _glossaryScopes)
            {
                var capturedScope = scope;

                var scopeItem = new MenuItem
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
                        selected);

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

    private GlossaryTextMatch? FindGlossaryMatchAtPoint(
        Point point)
    {
        if (_glossaryMatches.Count == 0)
        {
            return null;
        }

        var index = MarkdownEditorTextBox.GetCharacterIndexFromPoint(
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

    private async Task AddGlossaryEntryAsync(
        GlossaryScope scope,
        string term)
    {
        var dialog = new AddGlossaryEntryDialog(
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
            var currentPath = _documentSession?.Path;
            var targetPath = Path.GetFullPath(
                scope.FilePath);

            var isCurrentDocument =
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

    private async Task OpenGlossaryEntryAsync(
        GlossaryEntry entry)
    {
        var path = Path.GetFullPath(
            entry.Scope.FilePath);

        var node = FindAndExpand(
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

    private static string NormalizeGlossarySelection(
        string value) =>
        string.Join(
            " ",
            value.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries));

    private async Task RefreshDashboardTasksAsync()
    {
        try
        {
            var tasks = await _taskService.GetTasksAsync(
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

    private async Task RefreshDashboardMilestonesAsync()
    {
        try
        {
            var milestones = await _milestoneService.GetUpcomingAsync(
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

    private async Task NavigateToMilestoneAsync(
        MilestoneItem milestone)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                milestone.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        var node = FindAndExpand(
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

    private async Task ShowMilestonesAsync()
    {
        var contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            var projectDirectory =
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

            var dialog = new MilestoneListDialog(
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

    private async Task ShowTasksAsync(bool global)
    {
        var contextPath = global
            ? _root.FullPath
            : _selectedNode?.FullPath ?? _root.FullPath;

        var scopeLabel = global
            ? "Global · toutes les applications et tous les projets"
            : $"Contexte · {_selectedNode?.DisplayName ?? _root.DisplayName}";

        var dialog = new TaskListDialog(
            _root.FullPath,
            contextPath,
            scopeLabel,
            ToggleTaskFromViewAsync)
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

    private async Task ToggleTaskFromViewAsync(
        TaskItem task,
        bool completed)
    {
        var sourcePath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                task.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        var isCurrentDocument =
            _documentSession is not null &&
            string.Equals(
                Path.GetFullPath(_documentSession.Path),
                sourcePath,
                StringComparison.OrdinalIgnoreCase);

        var caret = MarkdownEditorTextBox.CaretIndex;

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

    private async Task NavigateToTaskAsync(
        TaskItem task)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                task.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        var node = FindAndExpand(
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

        var picker = new OpenFileDialog
        {
            Title = "Choisir une pièce jointe",
            Multiselect = false,
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        var mode = MessageBox.Show(
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
            var service = new AttachmentService(
                _root.FullPath);

            var attachment = mode == MessageBoxResult.Yes
                ? await service.CopyIntoWorkspaceAsync(
                    picker.FileName,
                    _selectedNode.FullPath)
                : service.CreateExternalReference(
                    picker.FileName,
                    _selectedNode.FullPath);

            var label = string.IsNullOrWhiteSpace(
                    MarkdownEditorTextBox.SelectedText)
                ? attachment.DisplayName
                : MarkdownEditorTextBox.SelectedText.Trim();

            var isImage = IsImageAttachment(
                attachment.FullPath);

            var syntax = isImage
                ? $"![{label}]({attachment.MarkdownTarget})"
                : $"[{label}]({attachment.MarkdownTarget})";

            var start = MarkdownEditorTextBox.SelectionStart;
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

    private static bool IsImageAttachment(
        string path)
    {
        var extension = Path.GetExtension(path);

        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ShowGlossaryAsync()
    {
        var dialog = new GlossaryLookupDialog(
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

    private async Task CaptureQuickNoteAsync()
    {
        try
        {
            var service = new QuickNotesService();
            var contextPath =
                _selectedNode?.FullPath ??
                _root.FullPath;

            var scopes = await service.ResolveScopesAsync(
                _root.FullPath,
                contextPath);

            var dialog = new QuickNoteDialog(scopes)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var targetPath = Path.GetFullPath(
                dialog.SelectedScope.FilePath);

            var currentPath = _documentSession?.Path;

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

    private async Task ShowQuickNotesAsync()
    {
        try
        {
            var service = new QuickNotesService();
            var contextPath =
                _selectedNode?.FullPath ??
                _root.FullPath;

            var snapshots = await service.ReadAggregateAsync(
                _root.FullPath,
                contextPath);

            var dialog = new QuickNotesOverviewDialog(
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

    private async Task CreateProjectAsync()
    {
        try
        {
            var targetDiscovery = new ProjectCreationTargetDiscovery();
            var targets = await targetDiscovery.DiscoverAsync(
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

            var profiles = await _templateStore.LoadProjectProfilesAsync();

            var dialog = new NewProjectDialog(
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

            var creator = new FileSystemProjectCreator(
                _root.FullPath);

            var result = await creator.CreateAsync(
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

    private async Task CreateDecisionAsync()
    {
        var contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            var scope = await _decisionService.ResolveScopeAsync(
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

            var dialog = new DecisionDialog(
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

            var result = await _decisionService.CreateAsync(
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

    private async Task ShowDecisionsAsync()
    {
        var contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            var scope = await _decisionService.ResolveScopeAsync(
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

            var dialog = new DecisionListDialog(
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

    private async Task NavigateToDecisionAsync(
        DecisionRecord decision)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                decision.SourceRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

        var node = FindAndExpand(
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

    private async Task CreateMeetingAsync()
    {
        var contextPath =
            _selectedNode?.FullPath ??
            _root.FullPath;

        try
        {
            var scope = await _meetingService.ResolveScopeAsync(
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

            var dialog = new MeetingDialog(
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

            var result = await _meetingService.CreateAsync(
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

    private async Task CreateNoteAsync()
    {
        var targetDirectory = ResolveNewNoteDirectory();

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
            var catalog = await _templateStore.LoadTemplateCatalogAsync();

            var dialog = new NewNoteDialog(catalog.Templates)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var documentId = Guid.NewGuid();
            var variables = MarkdownTemplateRenderer.CreateStandardVariables(
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
                var definition = catalog.Templates.Single(
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

            var filePath = WindowsPathRules.GetUniqueFilePath(
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

    private async Task RefreshNavigationAsync(
        string? openPath = null)
    {
        var expandedIds = GetExpandedNodeIds();

        var root = await _navigationBuilder.BuildAsync(
            _root.FullPath);

        _root = CreateRootViewModel(
            root,
            expandedIds);

        NavigationTree.ItemsSource =
            new[] { _root };

        await RefreshLinkIndexAsync();

        if (openPath is null)
        {
            return;
        }

        var target = FindAndExpand(
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

    private HashSet<Guid> GetExpandedNodeIds() =>
        _root
            .DescendantsAndSelf()
            .Where(node => node.IsExpanded)
            .Select(node => node.Id)
            .ToHashSet();

    private static NavigationNodeViewModel CreateRootViewModel(
        WorkspaceNavigationNode root,
        IReadOnlySet<Guid> expandedIds)
    {
        var viewModel = new NavigationNodeViewModel(
            root,
            expandedIds);

        viewModel.IsExpanded = true;
        return viewModel;
    }

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

        foreach (var child in node.Children)
        {
            var found = FindAndExpand(
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
