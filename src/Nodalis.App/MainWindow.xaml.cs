using System.ComponentModel;
using System.Windows;
using Nodalis.App.Navigation;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Navigation;
using Nodalis.Core.Settings;

namespace Nodalis.App;

public partial class MainWindow : Window
{
    private readonly NavigationNodeViewModel _root;
    private readonly IUserPreferencesStore _preferencesStore;
    private UserPreferences _preferences;
    private bool _allowClose;
    private bool _contextPanelOpen;
    private double _lastContextWidth;

    public MainWindow(
        WorkspaceNavigationNode root,
        UserPreferences preferences,
        IUserPreferencesStore preferencesStore)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(preferencesStore);

        InitializeComponent();

        _preferences = preferences;
        _preferencesStore = preferencesStore;
        _contextPanelOpen = preferences.IsContextPanelOpen;
        _lastContextWidth = preferences.ContextPanelWidth;

        _root = new NavigationNodeViewModel(
            root,
            preferences.ExpandedNodeIds);

        _root.IsExpanded = true;

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
            var expandedIds = _root
                .DescendantsAndSelf()
                .Where(node => node.IsExpanded)
                .Select(node => node.Id)
                .ToHashSet();

            _preferences = _preferences with
            {
                ExpandedNodeIds = expandedIds,
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
