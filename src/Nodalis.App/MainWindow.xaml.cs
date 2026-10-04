using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Nodalis.App.Dialogs;
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

    private NavigationNodeViewModel _root;
    private NavigationNodeViewModel? _selectedNode;
    private UserPreferences _preferences;
    private bool _allowClose;
    private bool _contextPanelOpen;
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
        _lastContextWidth = preferences.ContextPanelWidth;

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
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

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
                        : _lastContextWidth)
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

        _allowClose = true;
        Close();
    }

    private async void NavigationTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not NavigationNodeViewModel node)
        {
            return;
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
            try
            {
                DocumentContentText.Text = await File.ReadAllTextAsync(
                    node.FullPath);

                StatusText.Text =
                    $"Document · {relativePath} · lecture locale";
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                DocumentContentText.Text =
                    $"Impossible de lire le document.\n\n{exception.Message}";

                StatusText.Text = "Erreur de lecture";
            }

            return;
        }

        DocumentContentText.Text =
            $"{GetKindLabel(node.Kind)}\n\n" +
            $"{node.Children.Count} élément(s) enfant(s)\n\n" +
            $"{node.FullPath}";

        StatusText.Text =
            $"{GetKindLabel(node.Kind)} · {node.Children.Count} élément(s)";
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
        if (e.Key == Key.N &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await CreateNoteAsync();
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
