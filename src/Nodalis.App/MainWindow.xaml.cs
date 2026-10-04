using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Nodalis.App.Dialogs;
using Nodalis.App.Markdown;
using Nodalis.App.Navigation;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Navigation;
using Nodalis.Core.Settings;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
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

    private async void MainWindow_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
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
