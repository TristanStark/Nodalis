using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>
/// One reusable journey tab per project, isolated from Markdown autosave tabs.
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<ProjectJourneyTabEntry> _projectJourneyTabs = [];
    private ProjectJourneyTabEntry? _activeProjectJourneyTab;
    private bool _suppressProjectJourneyTabSelection;

    /// <summary>Connects the lightweight journey tab strip to its collection.</summary>
    private void InitializeProjectJourneyTabs()
    {
        ProjectJourneyTabsList.ItemsSource = _projectJourneyTabs;
    }

    /// <summary>
    /// Opens the workspace-wide catalog editor; saved changes apply only to
    /// future journey initializations, not to existing project documents.
    /// </summary>
    /// <returns>Completed user-reviewed template action.</returns>
    private async Task ManageJourneyTemplatesAsync()
    {
        ProjectJourneyTemplatesDialog dialog = new(
            _root.FullPath, _workspaceReadOnly)
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            if (_activeProjectJourneyTab is ProjectJourneyTabEntry tab)
            {
                await tab.View.RefreshTemplatesAsync();
            }

            StatusText.Text =
                "Modèles de parcours enregistrés · projets existants inchangés.";
        }
    }

    /// <summary>Opens the selected project's journey from the Worktree header.</summary>
    /// <param name="sender">Worktree command.</param>
    /// <param name="e">Click event.</param>
    private async void OpenProjectJourney_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Parcours projet", OpenProjectJourneyAsync);
    }

    /// <summary>
    /// Opens or activates the project journey, without creating JSON while
    /// navigating and without attaching a Markdown session or autosave.
    /// </summary>
    /// <returns>Completed UI action.</returns>
    private Task OpenProjectJourneyAsync()
    {
        NavigationNodeViewModel? project = GetArchiveContextProject();
        if (project is null)
        {
            MessageBox.Show(this,
                "Sélectionnez un projet ou un document appartenant à un projet.",
                "Parcours projet · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        // A duplicated project GUID would make the tab identity ambiguous.
        // Refuse to reuse a view for the wrong source folder.
        int sameIdProjects = _root.DescendantsAndSelf().Count(node =>
            node.Kind == WorkspaceNodeKind.Project && node.Id == project.Id);
        if (sameIdProjects != 1)
        {
            MessageBox.Show(this,
                "Cet identifiant de projet est absent ou partagé par plusieurs dossiers. " +
                "Résolvez la collision avant d'ouvrir le parcours.",
                "Parcours projet · identifiant ambigu",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        ProjectJourneyTabEntry? tab = _projectJourneyTabs.FirstOrDefault(item =>
            item.ProjectId == project.Id);
        if (tab is not null && !string.Equals(
                tab.ProjectDirectory, project.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            // Never retain an editor pointing to a stale or relocated path.
            CloseProjectJourneyTab(tab);
            tab = null;
        }

        if (tab is null)
        {
            ProjectJourneyView view = new(_root.FullPath, project.FullPath,
                _workspaceReadOnly);
            tab = new ProjectJourneyTabEntry(
                project.Id, project.DisplayName, project.FullPath, view);
            view.CloseRequested += ProjectJourneyView_CloseRequested;
            view.SourceOpenRequested += ProjectJourneyView_SourceOpenRequested;
            _projectJourneyTabs.Add(tab);
            UpdateDocumentTabsVisibility();
        }

        ActivateProjectJourneyTab(tab);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Makes a journey view the active editor surface without changing any
    /// document tab or its autosave controller.
    /// </summary>
    /// <param name="tab">The unique project journey tab.</param>
    private void ActivateProjectJourneyTab(ProjectJourneyTabEntry tab)
    {
        if (!_projectJourneyTabs.Contains(tab))
        {
            return;
        }

        // Save the in-memory state of the previous Markdown editor before
        // switching surfaces. The document tabs remain open for later reuse.
        DeactivateDocumentTabView();
        _activeProjectJourneyTab = tab;
        _suppressProjectJourneyTabSelection = true;
        ProjectJourneyTabsList.SelectedItem = tab;
        _suppressProjectJourneyTabSelection = false;

        DashboardHost.Visibility = Visibility.Collapsed;
        NodeSummaryHost.Visibility = Visibility.Collapsed;
        EditorToolbar.Visibility = Visibility.Collapsed;
        DocumentEditorHost.Visibility = Visibility.Collapsed;
        ProjectJourneyHost.Content = tab.View;
        ProjectJourneyHost.Visibility = Visibility.Visible;
        DocumentTitleText.Text = "Parcours · " + tab.ProjectName;
        DocumentPathText.Text = Path.GetRelativePath(
            _root.FullPath, tab.ProjectDirectory);
        StatusText.Text = "Parcours projet · " + tab.ProjectName;
    }

    /// <summary>
    /// Leaves a journey surface when a Markdown tab, dashboard or project
    /// summary is activated. The journey tab itself remains available.
    /// </summary>
    private void DeactivateProjectJourneyTabView()
    {
        if (_activeProjectJourneyTab is null)
        {
            return;
        }

        _activeProjectJourneyTab = null;
        _suppressProjectJourneyTabSelection = true;
        ProjectJourneyTabsList.SelectedItem = null;
        _suppressProjectJourneyTabSelection = false;
        ProjectJourneyHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>Activates the journey selected in the dedicated tab strip.</summary>
    /// <param name="sender">Tab strip.</param>
    /// <param name="e">Selection change.</param>
    private void ProjectJourneyTabsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressProjectJourneyTabSelection &&
            ProjectJourneyTabsList.SelectedItem is ProjectJourneyTabEntry tab)
        {
            ActivateProjectJourneyTab(tab);
        }
    }

    /// <summary>Routes an explicit evidence open request through the UI guard.</summary>
    /// <param name="sender">Journey tab view.</param>
    /// <param name="e">Stored workspace-relative source reference.</param>
    private async void ProjectJourneyView_SourceOpenRequested(
        object? sender, ProjectJourneySourceOpenRequestedEventArgs e)
    {
        await RunUiActionAsync("Parcours projet · ouvrir une preuve",
            () => OpenProjectJourneySourceAsync(e.RelativePath, e.LineNumber));
    }

    /// <summary>
    /// Verifies workspace containment and junctions again at open time,
    /// then uses the canonical Markdown tabs or an explicit OS open for
    /// known document/image file types. Executables are never launched.
    /// </summary>
    /// <param name="reference">Portable relative reference.</param>
    /// <param name="lineNumber">Optional canonical Markdown source line.</param>
    /// <returns>Completed user-initiated navigation.</returns>
    private async Task OpenProjectJourneySourceAsync(
        string reference, int? lineNumber = null)
    {
        ProjectJourneySourceService sources = new(_root.FullPath);
        string path = sources.ResolveExistingFile(reference);
        string extension = Path.GetExtension(path);

        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            NavigationNodeViewModel? node = FindAndExpand(_root, path);
            if (node is null)
            {
                await RefreshNavigationAsync(path);
                node = FindAndExpand(_root, path);
            }

            if (node?.Kind == WorkspaceNodeKind.Document)
            {
                _selectedNode = node;
                _restoringSelection = true;
                node.IsSelected = true;
                _restoringSelection = false;
                await OpenDocumentAsync(node);
                if (lineNumber is int line && line > 0)
                {
                    MoveCaretToLine(line);
                }

                StatusText.Text = "Preuve ouverte dans Nodalis · " + reference;
                return;
            }
        }

        HashSet<string> supportedExtensions = new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".txt", ".csv", ".json", ".pdf", ".docx", ".xlsx",
            ".vsdx", ".png", ".jpg", ".jpeg", ".webp"
        };
        if (!supportedExtensions.Contains(extension))
        {
            MessageBox.Show(this,
                "Cette référence pointe vers un format que Nodalis n'ouvre pas " +
                "pour des raisons de sécurité.\n\n" + reference +
                "\n\nLe lien reste présent, sans exécution du fichier.",
                "Format de preuve non ouvrable", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
            "Ouvrir le fichier suivant avec l'application associée de Windows ?\n\n" +
            reference + "\n\nNodalis ne modifie pas son contenu.",
            "Ouvrir une preuve", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
        StatusText.Text = "Fichier lié ouvert · " + reference;
    }

    /// <summary>Closes the journey tab via its tab-strip button.</summary>
    /// <param name="sender">Tab close button.</param>
    /// <param name="e">Click event.</param>
    private void CloseProjectJourneyTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button button &&
            button.Tag is ProjectJourneyTabEntry tab)
        {
            CloseProjectJourneyTab(tab);
        }
    }

    /// <summary>Closes the journey tab via its embedded Close button.</summary>
    /// <param name="sender">Journey view.</param>
    /// <param name="e">Close request.</param>
    private void ProjectJourneyView_CloseRequested(object? sender, EventArgs e)
    {
        ProjectJourneyTabEntry? tab = _projectJourneyTabs.FirstOrDefault(
            item => ReferenceEquals(item.View, sender));
        if (tab is not null)
        {
            CloseProjectJourneyTab(tab);
        }
    }

    /// <summary>Releases a journey view without affecting document editors.</summary>
    /// <param name="tab">Tab to close.</param>
    private void CloseProjectJourneyTab(ProjectJourneyTabEntry tab)
    {
        bool active = ReferenceEquals(tab, _activeProjectJourneyTab);
        tab.View.CloseRequested -= ProjectJourneyView_CloseRequested;
        tab.View.SourceOpenRequested -= ProjectJourneyView_SourceOpenRequested;
        if (active)
        {
            DeactivateProjectJourneyTabView();
            ProjectJourneyHost.Content = null;
        }
        else if (ReferenceEquals(ProjectJourneyHost.Content, tab.View))
        {
            ProjectJourneyHost.Content = null;
        }

        _projectJourneyTabs.Remove(tab);
        UpdateDocumentTabsVisibility();
        if (active)
        {
            ShowDashboard();
        }
    }

    /// <summary>
    /// Drops all non-document views on workspace switch and application exit.
    /// No project file is modified.
    /// </summary>
    private void DisposeAllProjectJourneyTabs()
    {
        DeactivateProjectJourneyTabView();
        ProjectJourneyHost.Content = null;
        foreach (ProjectJourneyTabEntry tab in _projectJourneyTabs)
        {
            tab.View.CloseRequested -= ProjectJourneyView_CloseRequested;
            tab.View.SourceOpenRequested -= ProjectJourneyView_SourceOpenRequested;
        }

        _projectJourneyTabs.Clear();
        UpdateDocumentTabsVisibility();
    }

    /// <summary>
    /// Rejects tab sessions whose project was renamed, moved or removed by
    /// a navigation refresh. Never reuse a stale path with a stable GUID.
    /// </summary>
    private void ReconcileProjectJourneyTabsFromNavigation()
    {
        if (_projectJourneyTabs.Count == 0)
        {
            return;
        }

        Dictionary<Guid, string> projectPaths = _root.DescendantsAndSelf()
            .Where(node => node.Kind == WorkspaceNodeKind.Project)
            .GroupBy(node => node.Id)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().FullPath);
        foreach (ProjectJourneyTabEntry tab in _projectJourneyTabs.ToArray())
        {
            if (!projectPaths.TryGetValue(tab.ProjectId, out string? path) ||
                !string.Equals(path, tab.ProjectDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                CloseProjectJourneyTab(tab);
            }
        }
    }

    /// <summary>Stable project tab identity and attached view instance.</summary>
    private sealed record ProjectJourneyTabEntry(
        Guid ProjectId,
        string ProjectName,
        string ProjectDirectory,
        ProjectJourneyView View)
    {
        /// <summary>Visual label, independent of document tab identities.</summary>
        public string TabTitle => "▦ " + ProjectName;

        /// <summary>Visual tooltip with the project's current path.</summary>
        public string FullPath => ProjectDirectory;
    }
}
