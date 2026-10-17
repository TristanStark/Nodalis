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
