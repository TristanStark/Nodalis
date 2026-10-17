using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

/// <summary>
/// Keeps the worktree project and deepest active folder visible while the
/// hierarchy scrolls. Headings are a read-only view of existing nodes.
/// </summary>
public partial class MainWindow
{
    private readonly List<NavigationNodeViewModel> _pinnedWorktreeNodes =
        new List<NavigationNodeViewModel>();
    private ScrollContentPresenter? _worktreeScrollViewport;
    private bool _stickyWorktreeUpdatePending;
    private bool _stickyWorktreeHandlersAttached;

    /// <summary>
    /// Installs the expansion handlers once the TreeView visual subtree exists.
    /// </summary>
    /// <param name="sender">Worktree TreeView.</param>
    /// <param name="e">Loaded event.</param>
    private void NavigationTree_StickyLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_stickyWorktreeHandlersAttached)
        {
            NavigationTree.AddHandler(
                TreeViewItem.ExpandedEvent,
                new RoutedEventHandler(NavigationTree_StickyExpansionChanged));
            NavigationTree.AddHandler(
                TreeViewItem.CollapsedEvent,
                new RoutedEventHandler(NavigationTree_StickyExpansionChanged));
            _stickyWorktreeHandlersAttached = true;
        }

        ScheduleStickyWorktreeRefresh();
    }

    /// <summary>Updates the pinned headings after the visual scroll position changes.</summary>
    /// <param name="sender">The worktree.</param>
    /// <param name="e">Scroll position change.</param>
    private void NavigationTree_StickyScrollChanged(
        object sender,
        ScrollChangedEventArgs e) =>
        ScheduleStickyWorktreeRefresh();

    /// <summary>Recomputes the top context after the explorer width or height changes.</summary>
    /// <param name="sender">The worktree.</param>
    /// <param name="e">Size change.</param>
    private void NavigationTree_StickySizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        ScheduleStickyWorktreeRefresh();

    /// <summary>Recomputes the heading path after a branch expands or collapses.</summary>
    /// <param name="sender">The worktree.</param>
    /// <param name="e">Branch state event.</param>
    private void NavigationTree_StickyExpansionChanged(
        object sender,
        RoutedEventArgs e) =>
        ScheduleStickyWorktreeRefresh();

    /// <summary>
    /// Coalesces fast mouse wheel / touchpad / keyboard scroll events into one
    /// read of the realized WPF viewport; never rebuilds the navigation tree.
    /// </summary>
    private void ScheduleStickyWorktreeRefresh()
    {
        if (_stickyWorktreeUpdatePending)
        {
            return;
        }

        _stickyWorktreeUpdatePending = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _stickyWorktreeUpdatePending = false;
                RunUiAction(
                    "Explorateur · en-têtes persistants",
                    RefreshStickyWorktree,
                    showDialog: false);
            }));
    }

    /// <summary>
    /// Reads the first realized visible row and its WPF container ancestry,
    /// then pins only the relevant scrolled-out project/folder headings.
    /// </summary>
    private void RefreshStickyWorktree()
    {
        if (!NavigationTree.IsLoaded)
        {
            return;
        }

        _worktreeScrollViewport ??=
            FindVisualDescendant<ScrollContentPresenter>(NavigationTree);
        if (_worktreeScrollViewport is null ||
            _worktreeScrollViewport.ActualWidth <= 0 ||
            _worktreeScrollViewport.ActualHeight <= 0)
        {
            return;
        }

        Point origin = _worktreeScrollViewport.TranslatePoint(
            new Point(0, 0), NavigationTree);
        double viewportTop = origin.Y;

        TreeViewItem? first = FindFirstWorktreeItem(
            origin,
            _worktreeScrollViewport.ActualWidth,
            _worktreeScrollViewport.ActualHeight);
        if (first is null)
        {
            // Virtualization can leave a transient gap while recycling.
            // Do not flicker the pinned context in that frame.
            return;
        }

        List<(NavigationNodeViewModel Node, StickyWorktreeHeading State)> lineage =
            new List<(NavigationNodeViewModel Node, StickyWorktreeHeading State)>();

        TreeViewItem? current = first;
        while (current is not null)
        {
            if (current.DataContext is NavigationNodeViewModel node)
            {
                FrameworkElement header =
                    current.Template.FindName("RowSurface", current)
                        as FrameworkElement ?? current;
                double headerBottom = header.TranslatePoint(
                    new Point(0, header.ActualHeight), NavigationTree).Y;
                lineage.Add((
                    node,
                    new StickyWorktreeHeading(
                        node.Kind, headerBottom <= viewportTop + 1)));
            }

            current = ItemsControl.ItemsControlFromItemContainer(current)
                as TreeViewItem;
        }

        lineage.Reverse();
        StickyWorktreeHeading[] headingStates = lineage
            .Select(item => item.State)
            .ToArray();
        IReadOnlyList<int> indices =
            StickyWorktreeContext.SelectPinnedIndices(headingStates);
        List<NavigationNodeViewModel> pinned =
            new List<NavigationNodeViewModel>(indices.Count);
        foreach (int index in indices)
        {
            pinned.Add(lineage[index].Node);
        }

        ApplyPinnedWorktreeHeadings(pinned);
    }

    /// <summary>
    /// Finds the topmost visible item's container without enumerating all
    /// project nodes. Several narrow hit points tolerate border and guide gaps.
    /// </summary>
    /// <param name="origin">The viewport's top-left within the TreeView.</param>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    /// <returns>The first realized row near the viewport top, or null.</returns>
    private TreeViewItem? FindFirstWorktreeItem(
        Point origin,
        double width,
        double height)
    {
        double[] verticalSamples = { 3, 10, 19, 29, 40 };
        double[] horizontalSamples = { 0.75, 0.55, 0.35 };
        foreach (double deltaY in verticalSamples)
        {
            if (deltaY >= height)
            {
                break;
            }

            foreach (double position in horizontalSamples)
            {
                double x = origin.X + Math.Max(
                    5, Math.Min(width - 6, width * position));
                HitTestResult? hit = VisualTreeHelper.HitTest(
                    NavigationTree, new Point(x, origin.Y + deltaY));
                TreeViewItem? item =
                    FindVisualParent<TreeViewItem>(hit?.VisualHit);
                if (item is not null)
                {
                    return item;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Updates the two small fixed headers only when the actual node identities
    /// change; prevents relayout/rebinding during incremental wheel scrolling.
    /// </summary>
    /// <param name="pinned">The visible persistent project/folder path.</param>
    private void ApplyPinnedWorktreeHeadings(
        IReadOnlyList<NavigationNodeViewModel> pinned)
    {
        if (_pinnedWorktreeNodes.Count == pinned.Count &&
            _pinnedWorktreeNodes.Select((node, index) =>
                ReferenceEquals(node, pinned[index])).All(equal => equal))
        {
            return;
        }

        _pinnedWorktreeNodes.Clear();
        _pinnedWorktreeNodes.AddRange(pinned);
        WorktreeStickyItems.ItemsSource = pinned.ToArray();
        WorktreeStickyHost.Visibility = pinned.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>Reveals a pinned header's real project/folder row when clicked.</summary>
    /// <param name="sender">Sticky header button.</param>
    /// <param name="e">Button click.</param>
    private void WorktreeStickyEntry_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is NavigationNodeViewModel node)
        {
            RunUiAction(
                "Explorateur · revenir au dossier",
                () => RevealNavigationNode(node));
        }
    }

    /// <summary>Finds one realized visual descendant of a requested type.</summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="root">Root of the WPF visual subtree.</param>
    /// <returns>The descendant or null if the template is not ready.</returns>
    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            T? result = FindVisualDescendant<T>(
                VisualTreeHelper.GetChild(root, index));
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
