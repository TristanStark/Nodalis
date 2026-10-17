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
    /// Determines whether the selected context heading has already scrolled above
    /// the worktree viewport. Selection remains authoritative even when the
    /// viewport reaches an unrelated application or another project.
    /// </summary>
    /// <param name="headingPosition">Visible-tree sibling indices of the selected project or folder.</param>
    /// <param name="firstVisiblePosition">Visible-tree sibling indices of the first visible item.</param>
    /// <param name="headingVisible">Whether the real heading is currently on screen.</param>
    /// <returns>Whether to keep the selected heading in the sticky context.</returns>
    public static bool ShouldPinCurrentHeading(
        IReadOnlyList<int> headingPosition,
        IReadOnlyList<int> firstVisiblePosition,
        bool headingVisible)
    {
        ArgumentNullException.ThrowIfNull(headingPosition);
        ArgumentNullException.ThrowIfNull(firstVisiblePosition);

        if (headingVisible || headingPosition.Count == 0 ||
            firstVisiblePosition.Count == 0)
        {
            return false;
        }

        int length = Math.Min(headingPosition.Count, firstVisiblePosition.Count);
        for (int index = 0; index < length; index++)
        {
            if (headingPosition[index] != firstVisiblePosition[index])
            {
                return headingPosition[index] < firstVisiblePosition[index];
            }
        }

        // The selected heading is a scrolled ancestor of the visible row.
        return headingPosition.Count < firstVisiblePosition.Count;
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
