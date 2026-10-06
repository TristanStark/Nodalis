using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Core.Quality;

namespace Nodalis.App;

/// <summary>
/// Provides project coverage and coherence analysis commands.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens the deterministic coverage analysis for the selected project context.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OpenProjectCoverage_Click(
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
                "Couverture projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ProjectCoverageDialog dialog =
            new ProjectCoverageDialog(
                _root.FullPath,
                project.FullPath)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() !=
                true ||
            dialog.SelectedSource is not ProjectCoverageSource source)
        {
            return;
        }

        await OpenProjectCoverageSourceAsync(
            source);
    }

    /// <summary>
    /// Opens one Markdown source referenced by a coverage finding.
    /// </summary>
    /// <param name="source">The selected source reference.</param>
    /// <returns>A task representing navigation.</returns>
    private async Task OpenProjectCoverageSourceAsync(
            ProjectCoverageSource source)
    {
        if (!source.RelativePath.EndsWith(
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _root.FullPath,
                    source.RelativePath.Replace(
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
                "Source de couverture introuvable : " +
                source.RelativePath;
            return;
        }

        if (_selectedNode is not null &&
            _selectedNode.Kind == WorkspaceNodeKind.Document &&
            !ReferenceEquals(
                _selectedNode,
                node) &&
            !await TryCloseCurrentDocumentAsync(
                "ouvrir la source de couverture"))
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

        if (source.LineNumber is int lineNumber &&
            lineNumber >
                0)
        {
            MoveCaretToLine(
                lineNumber);
        }

        StatusText.Text =
            "Couverture projet · " +
            source.LocationLabel;
    }
}
