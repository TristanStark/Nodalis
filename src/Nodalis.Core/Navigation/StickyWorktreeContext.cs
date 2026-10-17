namespace Nodalis.Core.Navigation;

/// <summary>
/// Describes one visible ancestor of the first worktree row inside the
/// scroll viewport, including whether its heading has scrolled away.
/// </summary>
/// <param name="Kind">Canonical node kind.</param>
/// <param name="HeaderHidden">Whether the entire heading is above the viewport.</param>
public readonly record struct StickyWorktreeHeading(
    WorkspaceNodeKind Kind,
    bool HeaderHidden);

/// <summary>
/// Computes sticky project and folder headings from the scrolled visual path.
/// This projection never changes the workspace data or node expansion state.
/// </summary>
public static class StickyWorktreeContext
{
    /// <summary>
    /// Retains the currently selected document's parent folder when the
    /// viewport has scrolled past that folder into direct project children.
    /// Never pins a folder from another project or duplicates a viewport folder.
    /// </summary>
    /// <param name="visibleScopeId">Project (or application) at the top of the scroll viewport.</param>
    /// <param name="selectedScopeId">Project (or application) owning the selection.</param>
    /// <param name="viewportAlreadyHasFolder">True when a scrolled folder ancestor is already pinned.</param>
    /// <param name="folderPosition">Visible-tree sibling indices from the root to the selected folder.</param>
    /// <param name="firstVisiblePosition">Visible-tree sibling indices from the root to the first visible row.</param>
    /// <returns>True when the active folder is earlier than the viewport and belongs to its project.</returns>
    public static bool ShouldPinSelectedFolder(
        Guid? visibleScopeId,
        Guid? selectedScopeId,
        bool viewportAlreadyHasFolder,
        IReadOnlyList<int> folderPosition,
        IReadOnlyList<int> firstVisiblePosition)
    {
        ArgumentNullException.ThrowIfNull(folderPosition);
        ArgumentNullException.ThrowIfNull(firstVisiblePosition);

        if (visibleScopeId is null || selectedScopeId is null ||
            visibleScopeId != selectedScopeId || viewportAlreadyHasFolder)
        {
            return false;
        }

        int commonLength = Math.Min(
            folderPosition.Count, firstVisiblePosition.Count);
        for (int index = 0; index < commonLength; index++)
        {
            if (folderPosition[index] != firstVisiblePosition[index])
            {
                return folderPosition[index] < firstVisiblePosition[index];
            }
        }

        // The selected folder is an ancestor of the visible document:
        // its title may have scrolled out while descendants stay visible.
        return folderPosition.Count < firstVisiblePosition.Count;
    }

    /// <summary>
    /// Chooses the deepest project heading and deepest folder heading above
    /// the viewport. Without a project, an application or Global can anchor
    /// the current context. Indices refer to the input root-to-leaf lineage.
    /// </summary>
    /// <param name="lineage">The first visible row and its displayed ancestors.</param>
    /// <returns>Zero, one or two indices in display order.</returns>
    public static IReadOnlyList<int> SelectPinnedIndices(
        IReadOnlyList<StickyWorktreeHeading> lineage)
    {
        ArgumentNullException.ThrowIfNull(lineage);

        int projectIndex = -1;
        int scopeIndex = -1;
        int folderIndex = -1;
        bool hasProject = false;

        for (int index = 0; index < lineage.Count; index++)
        {
            StickyWorktreeHeading heading = lineage[index];
            hasProject |= heading.Kind == WorkspaceNodeKind.Project;
            if (!heading.HeaderHidden)
            {
                continue;
            }

            switch (heading.Kind)
            {
                case WorkspaceNodeKind.Project:
                    projectIndex = index;
                    break;
                case WorkspaceNodeKind.Application:
                case WorkspaceNodeKind.Global:
                    scopeIndex = index;
                    break;
                case WorkspaceNodeKind.Module:
                case WorkspaceNodeKind.Folder:
                case WorkspaceNodeKind.Section:
                    folderIndex = index;
                    break;
            }
        }

        List<int> pinned = new List<int>(2);
        int rootIndex = projectIndex >= 0
            ? projectIndex
            : hasProject ? -1 : scopeIndex;
        if (rootIndex >= 0)
        {
            pinned.Add(rootIndex);
        }

        if (folderIndex > rootIndex)
        {
            pinned.Add(folderIndex);
        }

        return pinned;
    }
}
