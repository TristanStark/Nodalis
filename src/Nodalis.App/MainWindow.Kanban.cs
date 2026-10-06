using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Quality;
using Nodalis.Core.Tasks;

namespace Nodalis.App;

/// <summary>
/// Provides workspace Kanban commands and task source navigation.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens the lightweight Markdown-backed Kanban board.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OpenWorkspaceKanban_Click(
            object sender,
            RoutedEventArgs e)
    {
        WorkspaceKanbanDialog dialog =
            new WorkspaceKanbanDialog(
                _root.FullPath)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() !=
                true ||
            dialog.SelectedTask is not TaskItem task)
        {
            return;
        }

        await OpenProjectCoverageSourceAsync(
            new ProjectCoverageSource
            {
                RelativePath =
                    task.SourceRelativePath,
                LineNumber =
                    task.LineNumber
            });
    }
}
