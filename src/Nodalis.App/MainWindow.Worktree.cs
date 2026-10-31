using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

/// <summary>Presentation-only worktree navigation actions; no workspace mutation.</summary>
public partial class MainWindow
{
    private bool _allowExplicitWorktreeBringIntoView;

    /// <summary>
    /// Keeps the explorer's manually chosen scroll position when another
    /// document tab updates the selected TreeViewItem programmatically.
    /// Keyboard navigation inside the tree and explicit reveal still scroll.
    /// </summary>
    /// <param name="sender">The worktree requesting the scroll.</param>
    /// <param name="e">WPF's request to bring a selected row into view.</param>
    private void NavigationTree_RequestBringIntoView(
        object sender,
        RequestBringIntoViewEventArgs e)
    {
        if (_allowExplicitWorktreeBringIntoView ||
            NavigationTree.IsKeyboardFocusWithin)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject origin &&
            FindVisualParent<TreeViewItem>(origin) is not null)
        {
            e.Handled = true;
        }
    }

    /// <summary>Creates an application now that its grouping row is not displayed.</summary>
    /// <param name="sender">New application button.</param>
    /// <param name="e">Click event.</param>
    private async void CreateApplication_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Nouvelle application", CreateApplicationAsync);
    }

    /// <summary>Finds the active tab in the tree with no workspace switching.</summary>
    /// <param name="sender">Toolbar button.</param>
    /// <param name="e">Click event.</param>
    private void RevealActiveDocument_Click(object sender, RoutedEventArgs e)
    {
        RunUiAction("Révéler dans l'explorateur", RevealActiveDocumentInTree);
    }

    /// <summary>Restores the active tab's path and selection in the navigation tree.</summary>
    private void RevealActiveDocumentInTree()
    {
        string? path = _activeDocumentTab?.FullPath ?? _selectedNode?.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "Aucun document ou projet à révéler.";
            return;
        }

        NavigationNodeViewModel? node = _activeDocumentTab is null
            ? _selectedNode
            : FindAndExpand(_root, Path.GetFullPath(path));
        if (node is null ||
            node.Kind == WorkspaceNodeKind.Workspace ||
            NavigationPresentationRules.IsTransparentContainer(node.Kind))
        {
            StatusText.Text = "L'élément n'est plus présent dans l'explorateur.";
            return;
        }

        RevealNavigationNode(node);
    }

    /// <summary>
    /// Selects an existing visual node, expands its actual hierarchy and scrolls
    /// the current TreeView; never reloads the underlying workspace catalog.
    /// </summary>
    /// <param name="node">Node to make visible.</param>
    private void RevealNavigationNode(NavigationNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        NavigationNodeViewModel? target = node.Kind == WorkspaceNodeKind.Global
            ? node
            : FindAndExpand(_root, Path.GetFullPath(node.FullPath));
        if (target is null)
        {
            return;
        }

        _selectedNode = target;
        EnsureWorktreeNodeIsVisible(target);
        target.IsSelected = true;
        ScheduleRevealTreeItem(target);
        ScheduleWorktreeP1Refresh();
        StatusText.Text = "Explorateur · " + target.DisplayName;
    }

    /// <summary>
    /// Restores the selected project or document from local preferences,
    /// without modifying the workspace or reloading the entire TreeView.
    /// </summary>
    private void RestoreSavedWorktreeSelection()
    {
        if (_preferences.ActiveNavigationNodeId is not Guid selectedId)
        {
            return;
        }

        NavigationNodeViewModel? node = _root.DescendantsAndSelf()
            .FirstOrDefault(candidate =>
                candidate.Id == selectedId &&
                candidate.Kind != WorkspaceNodeKind.Workspace &&
                !NavigationPresentationRules.IsTransparentContainer(candidate.Kind));
        if (node is null)
        {
            return;
        }

        if (node.Kind != WorkspaceNodeKind.Global)
        {
            FindAndExpand(_root, Path.GetFullPath(node.FullPath));
        }

        _selectedNode = node;
        node.IsSelected = true;
        ScheduleRevealTreeItem(node);
    }

    /// <summary>Defers container work until WPF has realized the selected branch.</summary>
    /// <param name="node">Target item.</param>
    private void ScheduleRevealTreeItem(NavigationNodeViewModel node)
    {
        EnsureWorktreeNodeIsVisible(node);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => RunUiAction(
                "Explorateur · révéler la sélection",
                () =>
                {
                    _allowExplicitWorktreeBringIntoView = true;
                    try
                    {
                        RevealTreeItemContainer(node);
                    }
                    finally
                    {
                        _allowExplicitWorktreeBringIntoView = false;
                    }
                },
                showDialog: false)));
    }

    /// <summary>
    /// Traverses visible ancestors (never hidden grouping nodes) to reveal the
    /// selected row. Virtualized containers are allowed to materialize lazily.
    /// </summary>
    /// <param name="node">Visual node to reveal.</param>
    private void RevealTreeItemContainer(NavigationNodeViewModel node)
    {
        // Check ancestor identity in O(depth), avoiding a whole-tree scan per click.
        NavigationNodeViewModel? ancestor = node;
        while (ancestor?.DisplayParent is not null)
        {
            ancestor = ancestor.DisplayParent;
        }

        if (!ReferenceEquals(ancestor, _root))
        {
            return;
        }

        Stack<NavigationNodeViewModel> chain =
            new Stack<NavigationNodeViewModel>();
        for (NavigationNodeViewModel? current = node;
             current is not null && current.Kind != WorkspaceNodeKind.Workspace;
             current = current.DisplayParent)
        {
            chain.Push(current);
        }

        ItemsControl parent = NavigationTree;
        TreeViewItem? last = null;
        while (chain.Count > 0)
        {
            NavigationNodeViewModel current = chain.Pop();
            parent.UpdateLayout();
            TreeViewItem? item =
                parent.ItemContainerGenerator.ContainerFromItem(current)
                    as TreeViewItem;
            if (item is null)
            {
                // WPF recycling can leave an offscreen selection unmaterialized.
                // Realize only its visual index rather than rebuilding the full tree.
                VirtualizingPanel? panel = FindOwnedVirtualizingPanel(parent, parent);
                int index = parent.Items.IndexOf(current);
                if (index >= 0 && panel is not null)
                {
                    panel.BringIndexIntoViewPublic(index);
                    parent.UpdateLayout();
                    item = parent.ItemContainerGenerator.ContainerFromItem(current)
                        as TreeViewItem;
                }

                if (item is null)
                {
                    last?.BringIntoView();
                    return;
                }
            }

            item.BringIntoView();
            if (chain.Count > 0)
            {
                item.IsExpanded = true;
            }

            last = item;
            parent = item;
        }

        last?.BringIntoView();
    }

    /// <summary>
    /// Finds the virtualizing items panel owned by the selected parent only;
    /// panels belonging to a nested branch must never receive its item index.
    /// </summary>
    /// <param name="element">Visual subtree to inspect.</param>
    /// <param name="owner">The ItemsControl owning the desired item.</param>
    /// <returns>The owning panel or null if it has not been created.</returns>
    private static VirtualizingPanel? FindOwnedVirtualizingPanel(
        DependencyObject element,
        ItemsControl owner)
    {
        if (element is VirtualizingPanel panel &&
            ReferenceEquals(ItemsControl.GetItemsOwner(panel), owner))
        {
            return panel;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, index);
            VirtualizingPanel? found = FindOwnedVirtualizingPanel(child, owner);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Collapses sibling projects while leaving the current project, its
    /// ancestors and all canonical file nodes intact.
    /// </summary>
    /// <param name="selected">Project in whose context menu the action was requested.</param>
    private void CollapseOtherProjects(NavigationNodeViewModel? selected)
    {
        if (selected is null || selected.Kind != WorkspaceNodeKind.Project)
        {
            return;
        }

        string chosenPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(selected.FullPath));
        foreach (NavigationNodeViewModel candidate in
                 _root.DescendantsAndSelf().Where(item =>
                     item.Kind == WorkspaceNodeKind.Project))
        {
            string candidatePath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(candidate.FullPath));
            bool keep = string.Equals(
                    candidatePath, chosenPath,
                    StringComparison.OrdinalIgnoreCase) ||
                chosenPath.StartsWith(
                    candidatePath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            if (!keep)
            {
                candidate.IsExpanded = false;
            }
        }

        RevealNavigationNode(selected);
    }
}
