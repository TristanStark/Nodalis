using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodalis.App.Commands;
using Nodalis.App.Dialogs;
using Nodalis.App.Markdown;
using Nodalis.App.Navigation;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Core.Settings;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Notes;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App;

public partial class MainWindow : Window
{
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly ITemplateStore _templateStore;
    private readonly WorkspaceNavigationBuilder _navigationBuilder = new();
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

        if (node.Kind == WorkspaceNodeKind.Document)
        {
            await OpenDocumentAsync(node);
            return;
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

            NodeSummaryHost.Visibility = Visibility.Collapsed;
            EditorToolbar.Visibility = Visibility.Visible;
            DocumentEditorHost.Visibility = Visibility.Visible;

            SaveStateText.Text = "Enregistré";
            StatusText.Text =
                $"Document · {Path.GetRelativePath(_root.FullPath, node.FullPath)}";

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

        EditorToolbar.Visibility = Visibility.Collapsed;
        DocumentEditorHost.Visibility = Visibility.Collapsed;
        NodeSummaryHost.Visibility = Visibility.Visible;

        NodeSummaryText.Text =
            $"{GetKindLabel(node.Kind)}\n\n" +
            $"{node.Children.Count} élément(s) enfant(s)\n\n" +
            $"{node.FullPath}";

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
    }

    private void PreviewTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _previewTimer.Stop();
        RenderPreview();
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
        Dispatcher.BeginInvoke(() =>
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

    private async void MainWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
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

    private void OnInternalLinkClicked(string target)
    {
        var matches = _root
            .DescendantsAndSelf()
            .Where(node =>
                string.Equals(
                    node.DisplayName,
                    target,
                    StringComparison.CurrentCultureIgnoreCase))
            .Take(2)
            .ToArray();

        if (matches.Length == 1)
        {
            matches[0].IsSelected = true;
            return;
        }

        StatusText.Text = matches.Length == 0
            ? $"Lien interne introuvable : {target}"
            : $"Lien interne ambigu : {target}";
    }

    private void OnMarkdownLinkClicked(string target)
    {
        if (_selectedNode?.Kind == WorkspaceNodeKind.Document)
        {
            var baseDirectory = Path.GetDirectoryName(
                _selectedNode.FullPath);

            if (!string.IsNullOrWhiteSpace(baseDirectory) &&
                !Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                var localPath = Path.GetFullPath(
                    Path.Combine(baseDirectory, target));

                var match = _root
                    .DescendantsAndSelf()
                    .FirstOrDefault(node =>
                        string.Equals(
                            Path.GetFullPath(node.FullPath),
                            localPath,
                            StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    match.IsSelected = true;
                    return;
                }
            }
        }

        try
        {
            Clipboard.SetText(target);
            StatusText.Text =
                "Lien copié dans le presse-papiers (aucun appel réseau effectué).";
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
