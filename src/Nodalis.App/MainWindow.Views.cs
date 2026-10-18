using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Decisions;
using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Milestones;
using Nodalis.Core.Navigation;
using Nodalis.Core.Tasks;
using Nodalis.Core.Views;
using Nodalis.Infrastructure.Views;

namespace Nodalis.App;

/// <summary>Hosts the evolving v1.5 dynamic views without creating a second workspace.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Reads existing canonical projections and displays the local views browser.
    /// Existing task/milestone services are reused; no view query changes a note.
    /// </summary>
    /// <returns>Browser and optional navigation to the selected Markdown source.</returns>
    private async Task ShowWorkspaceViewsAsync()
    {
        LinkIndexCatalog catalog = await _linkIndexService.LoadAsync();
        IReadOnlyList<TaskItem> tasks = [];
        IReadOnlyList<MilestoneItem> milestones = [];
        IReadOnlyList<DecisionRecord> decisions = [];
        IReadOnlyList<MaintenanceHistoryItem> maintenance = [];
        List<WorkspaceViewProjectionDiagnostic> diagnostics = [];

        if (!_workspaceReadOnly)
        {
            // Services are sequential because some rebuild the same derived link index.
            tasks = await _taskService.GetTasksAsync(null, includeCompleted: true);
            milestones = await _milestoneService.GetMilestonesAsync(null);

            // The first 1.5 UI reads decisions of the current project/application.
            // The full cross-workspace decision collector is scheduled for #133.
            NavigationNodeViewModel? scope = _selectedNode is null ? null :
                _root.DescendantsAndSelf()
                    .Where(node => node.Kind is WorkspaceNodeKind.Project or
                        WorkspaceNodeKind.Application &&
                        IsProjectPathAncestorOrEqual(node.FullPath, _selectedNode.FullPath))
                    .OrderByDescending(node => node.FullPath.Length)
                    .FirstOrDefault();

            if (scope is not null)
            {
                decisions = await _decisionService.GetDecisionsAsync(scope.FullPath);
            }
        }
        else
        {
            diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                "workspace", "Mode lecture seule : projections métier non rafraîchies."));
        }

        NavigationNodeViewModel[] maintenanceFiles = _root.DescendantsAndSelf()
            .Where(node => node.Kind == WorkspaceNodeKind.Document &&
                IsMaintenanceDocument(node.FullPath)).ToArray();
        using (SemaphoreSlim throttle = new(4))
        {
            Task<(string Path, string Project, IReadOnlyList<MaintenanceHistoryItem> Rows,
                string? Error)>[] loads = maintenanceFiles.Select(node =>
                    LoadMaintenanceHistoryFileAsync(node, throttle)).ToArray();
            (string Path, string Project, IReadOnlyList<MaintenanceHistoryItem> Rows,
                string? Error)[] results = await Task.WhenAll(loads);
            maintenance = results.SelectMany(item => item.Rows).ToArray();
            diagnostics.AddRange(results.Where(item => item.Error is not null)
                .Select(item => new WorkspaceViewProjectionDiagnostic(item.Path,
                    item.Error ?? "Fiche de maintenance inaccessible.")));
        }

        // A service refresh may have updated only the *derived* link index.
        // Use the latest load, not a manual guess or a newly minted identity.
        catalog = await _linkIndexService.LoadAsync();
        WorkspaceViewProjection projection = WorkspaceViewProjector.Build(
            catalog, tasks, milestones, decisions, maintenance,
            workspaceRoot: _root.FullPath);
        if (diagnostics.Count > 0)
        {
            projection = projection with
            {
                Diagnostics = [.. projection.Diagnostics, .. diagnostics]
            };
        }

        FileSystemWorkspaceViewStore store =
            new(_root.FullPath, _root.Id);
        WorkspaceViewsDialog dialog = new(projection, store, _workspaceReadOnly)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true ||
            dialog.SelectedSource is not WorkspaceViewItem item)
        {
            return;
        }

        // Resolve the original GUID again rather than opening a stale homonym.
        LinkIndexCatalog latest = await _linkIndexService.LoadAsync();
        LinkTargetEntry? document = latest.Targets.FirstOrDefault(target =>
            target.Id == item.Source.DocumentId &&
            target.Kind == LinkTargetKind.Document);
        if (document is null)
        {
            MessageBox.Show(this,
                "Le document source a été déplacé hors du registre ou supprimé. " +
                "Actualisez les liens avant de réessayer.",
                "Vues dynamiques", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await NavigateToLinkTargetAsync(document, item.Source.LineNumber);
        StatusText.Text = "Vues dynamiques · source : " + document.QualifiedName;
    }
}
