using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

/// <summary>
/// Optional explorer P1 tools. All filtering is a projection over existing
/// navigation VMs; no workspace, project or source document is modified.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _worktreeFilterTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(170)
    };
    private bool _worktreeP1Initialized;
    private bool _worktreeDisplayUpdating;
    private HashSet<Guid>? _expansionBeforeWorktreeFilter;

    /// <summary>
    /// Loads local display preferences after the initial canonical tree exists.
    /// </summary>
    private void InitializeWorktreeP1()
    {
        _worktreeFilterTimer.Tick += WorktreeFilterTimer_Tick;
        _worktreeP1Initialized = true;
        NavigationTree.Tag = _preferences.NavigationDensity == "Compact"
            ? "Compact"
            : "Comfortable";
        WorktreeDensityComboBox.SelectedIndex =
            _preferences.NavigationDensity == "Compact" ? 1 : 0;
        WorktreeApplicationScopeCheck.IsChecked =
            _preferences.NavigationCurrentApplicationOnly;
        WorktreeFavoritesExpander.IsExpanded =
            _preferences.NavigationFavoritesExpanded;
        WorktreeRecentsExpander.IsExpanded =
            _preferences.NavigationRecentsExpanded;
        ApplyWorktreeDisplayFilter();
    }

    /// <summary>Debounces typing so large worktrees are not reprojected per keystroke.</summary>
    /// <param name="sender">Filter textbox.</param>
    /// <param name="e">Changed text.</param>
    private void WorktreeFilterBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!_worktreeP1Initialized)
        {
            return;
        }

        _worktreeFilterTimer.Stop();
        _worktreeFilterTimer.Start();
    }

    /// <summary>Applies the already debounced text query.</summary>
    /// <param name="sender">Timer.</param>
    /// <param name="e">Elapsed event.</param>
    private void WorktreeFilterTimer_Tick(object? sender, EventArgs e)
    {
        _worktreeFilterTimer.Stop();
        RunUiAction(
            "Explorateur · filtre instantané",
            ApplyWorktreeDisplayFilter,
            showDialog: false);
    }

    /// <summary>Restores the unfiltered tree via the clear button.</summary>
    /// <param name="sender">Clear button.</param>
    /// <param name="e">Click event.</param>
    private void ClearWorktreeFilter_Click(object sender, RoutedEventArgs e)
    {
        WorktreeFilterBox.Clear();
        ApplyWorktreeDisplayFilter();
        WorktreeFilterBox.Focus();
    }

    /// <summary>Changes the worktree scope without changing workspace.</summary>
    /// <param name="sender">Scope check box.</param>
    /// <param name="e">Toggle event.</param>
    private void WorktreeApplicationScope_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (!_worktreeP1Initialized)
        {
            return;
        }

        ApplyWorktreeDisplayFilter();
    }

    /// <summary>Applies the selected line-density presentation style.</summary>
    /// <param name="sender">Density combo.</param>
    /// <param name="e">Selection event.</param>
    private void WorktreeDensityComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_worktreeP1Initialized)
        {
            return;
        }

        NavigationTree.Tag = WorktreeDensityComboBox.SelectedIndex == 1
            ? "Compact"
            : "Comfortable";
        ScheduleStickyWorktreeRefresh();
    }

    /// <summary>
    /// Refreshes only the visible child projections using the canonical VMs.
    /// Keeps user expansion preferences when a search temporarily expands
    /// the ancestors of matching documents.
    /// </summary>
    private void ApplyWorktreeDisplayFilter()
    {
        if (!_worktreeP1Initialized || _worktreeDisplayUpdating)
        {
            return;
        }

        _worktreeDisplayUpdating = true;
        try
        {
            string query = WorktreeFilterBox.Text.Trim();
            Guid? applicationId =
                WorktreeApplicationScopeCheck.IsChecked == true
                    ? FindSelectedWorktreeApplication()?.Id
                    : null;

            if (query.Length > 0 && _expansionBeforeWorktreeFilter is null)
            {
                _expansionBeforeWorktreeFilter = GetExpandedNodeIds();
            }

            // This only mutates the observable VisibleChildren collections, not
            // the full Children tree or the original workspace navigation model.
            _root.ApplyDisplayFilter(query, applicationId);

            if (query.Length == 0 && _expansionBeforeWorktreeFilter is not null)
            {
                HashSet<Guid> expanded = _expansionBeforeWorktreeFilter;
                _expansionBeforeWorktreeFilter = null;
                foreach (NavigationNodeViewModel node in _root.DescendantsAndSelf())
                {
                    node.IsExpanded = expanded.Contains(node.Id);
                }
            }

            ScheduleStickyWorktreeRefresh();
        }
        finally
        {
            _worktreeDisplayUpdating = false;
        }
    }

    /// <summary>Returns the current application's VM, or null for Global.</summary>
    /// <returns>Canonical application node.</returns>
    private NavigationNodeViewModel? FindSelectedWorktreeApplication()
    {
        NavigationNodeViewModel? selected = _selectedNode;
        for (NavigationNodeViewModel? node = selected;
             node is not null;
             node = node.DisplayParent)
        {
            if (node.Kind == WorkspaceNodeKind.Application)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Schedules a projection refresh after tab switching / Quick Open. A
    /// project change switches the optional scoped view to the new app.
    /// </summary>
    private void ScheduleWorktreeP1Refresh()
    {
        if (!_worktreeP1Initialized)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (!_worktreeP1Initialized)
                {
                    return;
                }

                RunUiAction(
                    "Explorateur · contexte d'application",
                    () =>
                    {
                        ApplyWorktreeDisplayFilter();
                        RefreshWorktreeShortcutLists();
                    },
                    showDialog: false);
            }));
    }

    /// <summary>
    /// Ensures palette/shortcut navigation can reach a node hidden by a
    /// previous search or a different application-scoped projection.
    /// </summary>
    /// <param name="node">Navigation destination.</param>
    private void EnsureWorktreeNodeIsVisible(NavigationNodeViewModel node)
    {
        if (!_worktreeP1Initialized)
        {
            return;
        }

        bool inDisplay = true;
        for (NavigationNodeViewModel? candidate = node;
             candidate?.DisplayParent is NavigationNodeViewModel parent;
             candidate = parent)
        {
            if (!parent.VisibleChildren.Contains(candidate))
            {
                inDisplay = false;
                break;
            }
        }

        if (inDisplay)
        {
            return;
        }

        WorktreeFilterBox.Clear();
        if (WorktreeApplicationScopeCheck.IsChecked == true &&
            FindSelectedWorktreeApplication()?.Id !=
            FindNodeApplicationId(node))
        {
            WorktreeApplicationScopeCheck.IsChecked = false;
        }

        ApplyWorktreeDisplayFilter();
    }

    /// <summary>Finds an ancestor application's identifier without traversal through disk.</summary>
    /// <param name="node">Target navigation VM.</param>
    /// <returns>Application ID, if any.</returns>
    private static Guid? FindNodeApplicationId(NavigationNodeViewModel node)
    {
        for (NavigationNodeViewModel? current = node;
             current is not null;
             current = current.DisplayParent)
        {
            if (current.Kind == WorkspaceNodeKind.Application)
            {
                return current.Id;
            }
        }

        return null;
    }

    /// <summary>Represents collapsible shortcuts using the existing Quick Open index.</summary>
    /// <param name="sender">Expander.</param>
    /// <param name="e">State event.</param>
    private void WorktreeShortcuts_ExpandedChanged(object sender, RoutedEventArgs e)
    {
        if (_worktreeP1Initialized)
        {
            RefreshWorktreeShortcutLists();
        }
    }

    /// <summary>
    /// Reuses favorites, recent history and bookmark resolution from Quick Open;
    /// does not persist a duplicate shortcuts collection.
    /// </summary>
    private void RefreshWorktreeShortcutLists()
    {
        if (!_worktreeP1Initialized ||
            (!WorktreeFavoritesExpander.IsExpanded &&
             !WorktreeRecentsExpander.IsExpanded))
        {
            return;
        }

        IReadOnlyList<QuickOpenEntry> entries = BuildQuickOpenEntries();
        if (WorktreeFavoritesExpander.IsExpanded)
        {
            WorktreeFavoritesList.ItemsSource = entries
                .Where(entry => entry.IsFavorite && !entry.IsBroken)
                .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(20)
                .ToArray();
        }

        if (WorktreeRecentsExpander.IsExpanded)
        {
            WorktreeRecentsList.ItemsSource = entries
                .Where(entry =>
                    entry.RecentRank >= 0 &&
                    entry.KindLabel.Contains(
                        "document", StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(entry => entry.RecentRank)
                .Take(20)
                .ToArray();
        }
    }

    /// <summary>Opens a shortcut on double-click, not on a mere focus change.</summary>
    /// <param name="sender">Shortcut list.</param>
    /// <param name="e">Mouse event.</param>
    private async void WorktreeShortcutList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is ListBox list &&
            list.SelectedItem is QuickOpenEntry entry)
        {
            await RunUiActionAsync(
                "Explorateur · raccourci",
                () => OpenWorktreeShortcutAsync(entry));
        }
    }

    /// <summary>Opens a shortcut using Enter; keep native arrow navigation.</summary>
    /// <param name="sender">Shortcut list.</param>
    /// <param name="e">Key event.</param>
    private async void WorktreeShortcutList_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox list ||
            list.SelectedItem is not QuickOpenEntry entry)
        {
            return;
        }

        e.Handled = true;
        await RunUiActionAsync(
            "Explorateur · raccourci clavier",
            () => OpenWorktreeShortcutAsync(entry));
    }

    /// <summary>
    /// Navigates with the existing document/bookmark machinery; no duplicate
    /// editor implementation or separate favorite state.
    /// </summary>
    /// <param name="entry">Resolved Quick Open entry.</param>
    /// <returns>Navigation task.</returns>
    private async Task OpenWorktreeShortcutAsync(QuickOpenEntry entry)
    {
        if (entry.BookmarkId is Guid bookmarkId)
        {
            global::Nodalis.Core.Settings.DocumentBookmarkReference? bookmark =
                _preferences.Bookmarks.FirstOrDefault(item => item.Id == bookmarkId);
            if (bookmark is not null)
            {
                await NavigateToBookmarkAsync(bookmark);
            }

            return;
        }

        NavigationNodeViewModel? node = _root.DescendantsAndSelf()
            .FirstOrDefault(candidate => candidate.Id == entry.NodeId);
        if (node is null)
        {
            StatusText.Text = "Raccourci introuvable ou déplacé.";
            return;
        }

        RevealNavigationNode(node);
        await DisplayNodeAsync(node);
    }
}
