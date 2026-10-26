using System.IO;
using System.Text;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Diagnostics;
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
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App;

/// <summary>Hosts the evolving v1.5 dynamic views without creating a second workspace.</summary>
public partial class MainWindow
{
    private WorkspaceFileChangeMonitor? _viewsMonitor;
    private readonly SemaphoreSlim _viewsLoadGate = new(1, 1);
    private WorkspaceViewProjection? _viewsCache;
    private long _viewsChangeVersion;
    private long _viewsCachedVersion = -1;

    /// <summary>Watches canonical sources; saved definitions do not invalidate read projections.</summary>
    private void EnsureViewsMonitor()
    {
        if (_viewsMonitor is not null)
        {
            return;
        }

        _viewsMonitor = new WorkspaceFileChangeMonitor(_root.FullPath);
        _viewsMonitor.FileChanged += (_, change) =>
        {
            string file = change.FullPath;
            string oldFile = change.OldFullPath ?? "";
            if (IsViewsCanonicalChange(file) || IsViewsCanonicalChange(oldFile))
            {
                Interlocked.Increment(ref _viewsChangeVersion);
            }
        };
        Closed += (_, _) =>
        {
            _viewsMonitor?.Dispose();
            _viewsMonitor = null;
        };
    }

    /// <summary>Recognizes changes capable of affecting canonical projections.</summary>
    /// <param name="path">Changed or previous source path.</param>
    /// <returns>True for Markdown, structural metadata, and folder moves.</returns>
    private static bool IsViewsCanonicalChange(string path)
    {
        string file = Path.GetFileName(path);
        return file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            file is ".project.json" or ".application.json" or ".workspace.json" ||
            !Path.HasExtension(path);
    }
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
        EnsureViewsMonitor();
        WorkspaceViewProjection projection =
            await LoadWorkspaceViewsProjectionAsync(CancellationToken.None);

        FileSystemWorkspaceViewStore store =
            new(_root.FullPath, _root.Id);
        WorkspaceViewsDialog dialog = new(projection, store, _workspaceReadOnly, id, visibility,
            LoadWorkspaceViewsProjectionAsync, _viewsMonitor)
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


    /// <summary>Reuses the in-memory read index until a canonical filesystem change invalidates it.</summary>
    /// <param name="cancellationToken">Cancels ongoing source collection.</param>
    /// <returns>Current immutable view projection; no cached results are persisted.</returns>
    private async Task<WorkspaceViewProjection> LoadWorkspaceViewsProjectionAsync(
        CancellationToken cancellationToken)
    {
        using DispatcherHeartbeatMonitor.OperationScope measurement =
            _heartbeatMonitor.TrackOperation("Vues dynamiques");
        await _viewsLoadGate.WaitAsync(cancellationToken);
        try
        {
            long version = Interlocked.Read(ref _viewsChangeVersion);
            if (_viewsCache is not null && _viewsCachedVersion == version)
            {
                return _viewsCache;
            }

            LinkIndexCatalog catalog = _workspaceReadOnly
                ? await _linkIndexService.LoadAsync(cancellationToken)
                : await _linkIndexService.RefreshAsync(cancellationToken);
            IReadOnlyList<TaskItem> tasks = [];
            IReadOnlyList<MilestoneItem> milestones = [];
            IReadOnlyList<DecisionRecord> decisions = [];
            IReadOnlyList<MaintenanceHistoryItem> maintenance = [];
            List<WorkspaceViewProjectionDiagnostic> diagnostics = [];

            if (!_workspaceReadOnly)
            {
                // One versioned link snapshot is shared by every derived view projection.
                tasks = await _taskService.GetTasksAsync(null, includeCompleted: true,
                    cancellationToken: cancellationToken, indexSnapshot: catalog);
                milestones = await _milestoneService.GetMilestonesAsync(null,
                    cancellationToken, indexSnapshot: catalog);

                // Resolve each canonical decision folder once, across applications/projects.
                List<DecisionRecord> allDecisions = [];
                foreach (NavigationNodeViewModel scope in _root.DescendantsAndSelf()
                    .Where(node => node.Kind is WorkspaceNodeKind.Project or WorkspaceNodeKind.Application))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    allDecisions.AddRange(await _decisionService.GetDecisionsAsync(
                        scope.FullPath, cancellationToken));
                }

                decisions = allDecisions;
            }
            else
            {
                diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                    "workspace", "Mode lecture seule : projections métier non rafraîchies."));
            }

            // The navigation tree can lag behind external moves/renames. Discover
            // maintenance sheets from the refreshed canonical index instead.
            string workspace = Path.GetFullPath(_root.FullPath);
            string prefix = Path.TrimEndingDirectorySeparator(workspace) +
                Path.DirectorySeparatorChar;
            string[] maintenanceFiles = catalog.Targets
                .Where(target => target.Kind == LinkTargetKind.Document &&
                    IsMaintenanceDocument(target.RelativePath))
                .Select(target => Path.GetFullPath(Path.Combine(workspace,
                    target.RelativePath.Replace('/', Path.DirectorySeparatorChar))))
                .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            using (SemaphoreSlim throttle = new(4))
            {
                Task<(string Path, string Project, IReadOnlyList<MaintenanceHistoryItem> Rows,
                    string? Error)>[] loads = maintenanceFiles.Select(path =>
                        LoadViewMaintenanceHistoryAsync(path, throttle, cancellationToken)).ToArray();
                (string Path, string Project, IReadOnlyList<MaintenanceHistoryItem> Rows,
                    string? Error)[] results = await Task.WhenAll(loads);
                cancellationToken.ThrowIfCancellationRequested();
                maintenance = results.SelectMany(item => item.Rows).ToArray();
                diagnostics.AddRange(results.Where(item => item.Error is not null)
                    .Select(item => new WorkspaceViewProjectionDiagnostic(item.Path,
                        item.Error ?? "Fiche de maintenance inaccessible.")));
            }

            // The already refreshed immutable index is shared by tasks, jalons,
            // and the final view. A second JSON load provides no newer canonical
            // data and can race another refresh. Build the CPU-heavy projection on
            // a worker; only present the finished snapshot on the Dispatcher.
            string projectionRoot = _root.FullPath;
            WorkspaceViewProjection projection = await Task.Run(
                () => WorkspaceViewProjector.Build(
                    catalog, tasks, milestones, decisions, maintenance,
                    workspaceRoot: projectionRoot, cancellationToken: cancellationToken),
                cancellationToken);
            if (diagnostics.Count > 0)
            {
                projection = projection with
                {
                    Diagnostics = [.. projection.Diagnostics, .. diagnostics]
                };
            }


            cancellationToken.ThrowIfCancellationRequested();
            _viewsCache = projection;
            _viewsCachedVersion = version;
            return projection;
        }
        finally
        {
            _viewsLoadGate.Release();
        }
    }

    /// <summary>Reads canonical maintenance files, including those moved externally.</summary>
    /// <param name="path">Absolute path resolved from the refreshed link index.</param>
    /// <param name="throttle">Bounded concurrent disk access.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Parsed maintenance history or source diagnostic.</returns>
    private static async Task<(string Path, string Project,
        IReadOnlyList<MaintenanceHistoryItem> Rows, string? Error)>
        LoadViewMaintenanceHistoryAsync(string path, SemaphoreSlim throttle,
            CancellationToken cancellationToken)
    {
        string project = Path.GetFileName(Path.GetDirectoryName(path)) ?? "(Projet)";
        await throttle.WaitAsync(cancellationToken);
        try
        {
            string text = await File.ReadAllTextAsync(path, cancellationToken);
            IReadOnlyList<MaintenanceHistoryItem> items =
                MaintenanceHistoryIndex.Parse(path, project, text);
            return (path, project, items, null);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or DecoderFallbackException)
        {
            return (path, project, [], exception.Message);
        }
        finally
        {
            throttle.Release();
        }
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


    /// <summary>Duplicates one definition using the same optimistic revision as the normal editor.</summary>
    /// <param name="id">Stable original view identity.</param>
    /// <param name="visibility">Storage location of the selected view.</param>
    /// <returns>Opens the copied view if saving succeeds.</returns>
    private async Task DuplicateSavedViewAsync(Guid id, WorkspaceViewVisibility visibility)
    {
        if (_workspaceReadOnly)
        {
            MessageBox.Show(this, "Les modifications sont désactivées en lecture seule.",
                "Vues dynamiques", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FileSystemWorkspaceViewStore store = new(_root.FullPath, _root.Id);
        WorkspaceViewDefinitionSnapshot saved = await store.LoadAsync(visibility);
        if (saved.IsReadOnly)
        {
            throw new InvalidDataException("Version future de définitions : duplication interdite.");
        }

        WorkspaceViewDefinition original = saved.Views.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidDataException("La vue source a disparu : rechargez les vues.");
        WorkspaceViewDefinition duplicate = original with
        {
            Id = Guid.NewGuid(),
            Name = original.Name + " (copie)",
            Order = saved.Views.Count,
            Pinned = false
        };
        await store.SaveAsync(visibility, [.. saved.Views, duplicate], saved.Revision);
        await ShowWorkspaceViewsAsync(duplicate.Id, visibility);
        await RefreshSavedViewsDashboardAsync();
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
        commands.Add(new PaletteCommand
        {
            Id = "views.create",
            Title = "Créer une vue dynamique",
            Subtitle = "Ouvrir une nouvelle définition de vue",
            Keywords = ["vue", "nouvelle", "créer", "filtre"],
            ExecuteAsync = ShowWorkspaceViewsAsync
        });
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
                commands.Add(new PaletteCommand
                {
                    Id = "views.duplicate:" + target.Visibility + ":" + target.Id.ToString("N"),
                    Title = "Dupliquer la vue · " + target.Name,
                    Subtitle = target.Context + " · nouvelle identité GUID",
                    Keywords = ["vue", "dupliquer", target.Name],
                    ExecuteAsync = () => DuplicateSavedViewAsync(target.Id, target.Visibility)
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
