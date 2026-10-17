using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Calendar;
using Nodalis.Core.Quality;

namespace Nodalis.App;

/// <summary>
/// Provides workspace calendar commands and source navigation.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens the derived workspace calendar.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void OpenWorkspaceCalendar_Click(
            object sender,
            RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Calendrier workspace",
            OpenWorkspaceCalendarAsync);
    }

    /// <summary>
    /// Executes the calendar workflow behind the common recoverable UI boundary.
    /// </summary>
    /// <returns>A task representing the workflow.</returns>
    private async Task OpenWorkspaceCalendarAsync()
    {
        WorkspaceCalendarDialog dialog =
            new WorkspaceCalendarDialog(
                _root.FullPath)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() !=
                true ||
            dialog.SelectedEvent is not CalendarEventItem item)
        {
            return;
        }

        await OpenProjectCoverageSourceAsync(
            new ProjectCoverageSource
            {
                RelativePath =
                    item.SourceRelativePath,
                LineNumber =
                    item.LineNumber
            });
    }
}
