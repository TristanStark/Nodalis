using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>
/// Human closure review commands stay separate from the existing reversible
/// archive operation and from the project journey's own step status.
/// </summary>
public partial class MainWindow
{
    /// <summary>Opens the explicit, revision-checked closure review command.</summary>
    /// <param name="sender">Projet menu action.</param>
    /// <param name="e">WPF click event.</param>
    private async void PilotageClosureReview_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Bilan de clôture du projet",
            ReviewProjectClosureAsync);

    /// <summary>
    /// Inspects canonical sources off the UI thread, lets humans sign a new
    /// review, and refreshes the Worktree only after the explicit write.
    /// </summary>
    /// <returns>Interactive review or read-only closure history.</returns>
    private async Task ReviewProjectClosureAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Bilan de clôture");
            return;
        }

        Guid projectId = project.Id;
        string directory = project.FullPath;
        bool archived = IsUnderArchives(directory);
        bool readOnly = PilotageReadOnly(project);
        ProjectClosureReportService reports =
            new ProjectClosureReportService(_root.FullPath);
        ProjectClosureAuditService audits =
            new ProjectClosureAuditService(_root.FullPath);

        (ProjectClosureAuditSnapshot? Audit, ProjectClosureReportSnapshot History)
            loaded = await Task.Run(async () =>
            {
                ProjectClosureReportSnapshot history =
                    await reports.ReadAsync(directory);
                ProjectClosureAuditSnapshot? audit = archived
                    ? null : await audits.ReadAsync(directory);
                return (audit, history);
            });
        NavigationNodeViewModel? current = PilotageProject();
        if (current is null || current.Id != projectId ||
            !string.Equals(current.FullPath, directory,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text =
                "La sélection a changé pendant la préparation du bilan.";
            return;
        }

        ProjectClosureReviewDialog dialog = new(
            loaded.Audit, loaded.History, readOnly)
        { Owner = this };
        if (dialog.ShowDialog() != true ||
            dialog.ApprovedInput is not ProjectClosureReviewInput input)
        {
            return;
        }

        current = PilotageProject();
        if (current is null || current.Id != projectId ||
            !string.Equals(current.FullPath, directory,
                StringComparison.OrdinalIgnoreCase) ||
            PilotageReadOnly(current) || loaded.Audit is null)
        {
            StatusText.Text =
                "Projet modifié, archivé ou non modifiable : bilan non enregistré.";
            return;
        }

        ProjectClosureReportSnapshot recorded = await Task.Run(() =>
            reports.RecordAsync(directory, loaded.History, loaded.Audit,
                input.Outcome, input.Actor, input.Rationale));
        await RefreshNavigationAsync(directory);
        StatusText.Text = "Bilan de clôture · revue n°" +
            recorded.Report.Reviews.Count + " consignée sans archivage.";
    }
}
