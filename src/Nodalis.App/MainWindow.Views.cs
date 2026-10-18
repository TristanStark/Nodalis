using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Commands;
using System.Windows.Controls;
using System.Windows.Input;
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
    private Task ShowWorkspaceViewsAsync() =>
        ShowWorkspaceViewsAsync(null, null);

    /// <summary>Opens a specific saved view without altering the current navigation scope.</summary>
    /// <param name="id">Saved definition GUID, if supplied.</param>
    /// <param name="visibility">Private or workspace definition collection.</param>
    /// <returns>Navigation completion if a source is opened.</returns>
    private async Task ShowWorkspaceViewsAsync(Guid? id,
        WorkspaceViewVisibility? visibility)
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
        WorkspaceViewsDialog dialog = new(projection, store, _workspaceReadOnly, id, visibility)
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

    private sealed record SavedViewShortcut(
        Guid Id, string Name, string Context, WorkspaceViewVisibility Visibility,
        bool Pinned);

    /// <summary>Loads portable and local shortcuts without ever persisting view results.</summary>
    /// <returns>Saved views ordered with pinned entries first.</returns>
    private async Task<IReadOnlyList<SavedViewShortcut>> ReadSavedViewShortcutsAsync()
    {
        FileSystemWorkspaceViewStore store = new(_root.FullPath, _root.Id);
        WorkspaceViewDefinitionSnapshot workspace =
            await store.LoadAsync(WorkspaceViewVisibility.Workspace);
        WorkspaceViewDefinitionSnapshot local =
            await store.LoadAsync(WorkspaceViewVisibility.Private);
        return workspace.Views.Select(item => new SavedViewShortcut(item.Id, item.Name,
                "Workspace", WorkspaceViewVisibility.Workspace, item.Pinned))
            .Concat(local.Views.Select(item => new SavedViewShortcut(item.Id, item.Name,
                "Privée", WorkspaceViewVisibility.Private, item.Pinned)))
            .OrderByDescending(item => item.Pinned)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Populates the existing dashboard and compact worktree shortcuts.</summary>
    /// <returns>Completion of local definition reads.</returns>
    private async Task RefreshSavedViewsDashboardAsync()
    {
        try
        {
            IReadOnlyList<SavedViewShortcut> shortcuts =
                await ReadSavedViewShortcutsAsync();
            SavedViewsDashboardList.ItemsSource = shortcuts;
            WorktreeViewsList.ItemsSource = shortcuts;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            SavedViewsDashboardList.ItemsSource = Array.Empty<SavedViewShortcut>();
            WorktreeViewsList.ItemsSource = Array.Empty<SavedViewShortcut>();
            StatusText.Text = "Vues sauvegardées indisponibles : " + exception.Message;
        }
    }

    /// <summary>Extends Command Palette with direct, identity-safe saved-view actions.</summary>
    /// <param name="existing">All established palette commands.</param>
    /// <returns>The existing commands plus one entry per saved view.</returns>
    private async Task<IReadOnlyList<PaletteCommand>> BuildViewPaletteCommandsAsync(
        IReadOnlyList<PaletteCommand> existing)
    {
        List<PaletteCommand> commands = [.. existing];
        try
        {
            foreach (SavedViewShortcut shortcut in await ReadSavedViewShortcutsAsync())
            {
                SavedViewShortcut target = shortcut;
                commands.Add(new PaletteCommand
                {
                    Id = "views.open:" + target.Visibility + ":" + target.Id.ToString("N"),
                    Title = "Vue · " + target.Name,
                    Subtitle = target.Context + (target.Pinned ? " · Épinglée" : ""),
                    Keywords = ["vue", "ouvrir", "favoris", target.Name],
                    ExecuteAsync = () => ShowWorkspaceViewsAsync(target.Id, target.Visibility)
                });
            }
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Impossible de charger les vues : " + exception.Message;
        }

        return commands;
    }

    /// <summary>Opens a saved view from either dashboard or worktree in one gesture.</summary>
    /// <param name="sender">Saved view list.</param>
    /// <param name="e">Mouse gesture.</param>
    private async void SavedViewsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await OpenSavedViewSelectionAsync(sender);
    }

    /// <summary>Supports keyboard Enter without requiring the pointer.</summary>
    /// <param name="sender">Saved view list.</param>
    /// <param name="e">Keyboard event.</param>
    private async void SavedViewsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await OpenSavedViewSelectionAsync(sender);
        }
    }

    /// <summary>Preserves source-tree selection and opens only the saved definition.</summary>
    /// <param name="sender">A shortcut list.</param>
    /// <returns>Completion of the requested navigation.</returns>
    private async Task OpenSavedViewSelectionAsync(object sender)
    {
        if (sender is ListBox list && list.SelectedItem is SavedViewShortcut shortcut)
        {
            await RunUiActionAsync("Vue · " + shortcut.Name,
                () => ShowWorkspaceViewsAsync(shortcut.Id, shortcut.Visibility));
            await RefreshSavedViewsDashboardAsync();
        }
    }

    /// <summary>Opens the views browser with a new temporary query.</summary>
    /// <param name="sender">Dashboard button.</param>
    /// <param name="e">Click event.</param>
    private async void OpenAllViews_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Vues dynamiques", ShowWorkspaceViewsAsync);
        await RefreshSavedViewsDashboardAsync();
    }

}
