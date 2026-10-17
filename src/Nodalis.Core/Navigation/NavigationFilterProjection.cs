namespace Nodalis.Core.Navigation;

/// <summary>
/// Computes a visible worktree subset without mutating canonical workspace
/// nodes. Transparent organizational containers stay invisible.
/// </summary>
public static class NavigationFilterProjection
{
    /// <summary>
    /// Returns IDs of matching nodes, their ancestors and descendants of a
    /// matching container. Only one application is traversed in scoped mode.
    /// </summary>
    /// <param name="root">Canonical workspace root.</param>
    /// <param name="searchText">Case-insensitive partial name.</param>
    /// <param name="applicationId">Optional selected application.</param>
    /// <returns>IDs of rows eligible for display.</returns>
    public static IReadOnlySet<Guid> GetVisibleIds(
        WorkspaceNavigationNode root,
        string searchText,
        Guid? applicationId)
    {
        ArgumentNullException.ThrowIfNull(root);
        string query = searchText.Trim();
        HashSet<Guid> ids = new HashSet<Guid>();
        Traverse(root, false, true, query, applicationId, ids);
        return ids;
    }

    /// <summary>Visits the flattened visible navigation hierarchy once.</summary>
    /// <param name="node">Current canonical node.</param>
    /// <param name="parentMatched">Parent already matched the query.</param>
    /// <param name="isRoot">Whether this is the workspace root.</param>
    /// <param name="query">Trimmed search.</param>
    /// <param name="applicationId">Optional application filter.</param>
    /// <param name="ids">Accumulated visible IDs.</param>
    /// <returns>Whether the node or a descendant matches.</returns>
    private static bool Traverse(
        WorkspaceNavigationNode node,
        bool parentMatched,
        bool isRoot,
        string query,
        Guid? applicationId,
        HashSet<Guid> ids)
    {
        bool matched = parentMatched || query.Length == 0 ||
            (!isRoot && node.DisplayName.Contains(
                query, StringComparison.CurrentCultureIgnoreCase));
        bool descendantMatched = false;
        foreach (WorkspaceNavigationNode child in
                 NavigationPresentationRules.GetVisibleChildren(node))
        {
            if (isRoot && applicationId.HasValue &&
                (child.Kind != WorkspaceNodeKind.Application ||
                 child.Id != applicationId.Value))
            {
                continue;
            }

            descendantMatched |= Traverse(
                child, matched, false, query, applicationId, ids);
        }

        if (matched || descendantMatched)
        {
            ids.Add(node.Id);
        }

        return matched || descendantMatched;
    }
}
