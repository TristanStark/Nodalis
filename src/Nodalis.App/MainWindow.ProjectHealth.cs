using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Core.Quality;

namespace Nodalis.App;

/// <summary>
/// Provides project health-check commands and source navigation for the main window.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens the deterministic Health Check for the currently selected project context.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OpenProjectHealth_Click(
            object sender,
            RoutedEventArgs e)
    {
        NavigationNodeViewModel? project =
            ResolveSelectedProjectForHealthCheck();

        if (project is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez d'abord un projet ou un document appartenant à un projet.",
                "Project Health Check",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ProjectHealthDialog dialog =
            new ProjectHealthDialog(
                _root.FullPath,
                project.FullPath)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true ||
            dialog.SelectedIssue is not ProjectHealthIssue issue)
        {
            return;
        }

        await OpenProjectHealthSourceAsync(
            issue);
    }

    /// <summary>
    /// Resolves the project containing the current navigation selection.
    /// </summary>
    /// <returns>The nearest selected project context, or <see langword="null"/> when none exists.</returns>
    private NavigationNodeViewModel? ResolveSelectedProjectForHealthCheck()
    {
        if (_selectedNode is null)
        {
            return null;
        }

        if (_selectedNode.Kind ==
            WorkspaceNodeKind.Project)
        {
            return _selectedNode;
        }

        string selectedPath =
            Path.GetFullPath(
                _selectedNode.FullPath);

        return _root
            .DescendantsAndSelf()
            .Where(node =>
                node.Kind == WorkspaceNodeKind.Project &&
                IsProjectPathAncestorOrEqual(
                    node.FullPath,
                    selectedPath))
            .OrderByDescending(node =>
                Path.GetFullPath(
                        node.FullPath)
                    .Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Opens the Markdown source attached to a Health Check finding and positions the editor on its line when available.
    /// </summary>
    /// <param name="issue">The selected Health Check finding.</param>
    /// <returns>A task representing navigation.</returns>
    private async Task OpenProjectHealthSourceAsync(
            ProjectHealthIssue issue)
    {
        if (!issue.IsNavigable ||
            string.IsNullOrWhiteSpace(
                issue.RelativePath))
        {
            return;
        }

        string fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _root.FullPath,
                    issue.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        NavigationNodeViewModel? node =
            FindAndExpand(
                _root,
                fullPath);

        if (node is null)
        {
            await RefreshNavigationAsync();

            node =
                FindAndExpand(
                    _root,
                    fullPath);
        }

        if (node is null)
        {
            StatusText.Text =
                "Source du Health Check introuvable : " +
                issue.RelativePath;
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir la source du Health Check"))
        {
            return;
        }

        _restoringSelection =
            true;
        node.IsSelected =
            true;
        _restoringSelection =
            false;

        _selectedNode =
            node;

        await DisplayNodeAsync(
            node);

        if (issue.LineNumber is int lineNumber &&
            lineNumber > 0)
        {
            MoveCaretToLine(
                lineNumber);
        }

        StatusText.Text =
            "Health Check · " +
            issue.Code +
            " · " +
            issue.LocationLabel;
    }

    /// <summary>
    /// Determines whether a selected path is equal to or nested below a project directory.
    /// </summary>
    /// <param name="projectPath">The project directory.</param>
    /// <param name="candidatePath">The candidate file or directory.</param>
    /// <returns><see langword="true"/> when the candidate belongs to the project path.</returns>
    private static bool IsProjectPathAncestorOrEqual(
            string projectPath,
            string candidatePath)
    {
        string fullProjectPath =
            Path.GetFullPath(
                projectPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        string fullCandidatePath =
            Path.GetFullPath(
                candidatePath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullProjectPath,
                   fullCandidatePath,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidatePath.StartsWith(
                   fullProjectPath +
                   Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
