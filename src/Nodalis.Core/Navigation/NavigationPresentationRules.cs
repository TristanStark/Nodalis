using Nodalis.Core.Navigation;

namespace Nodalis.Core.Navigation;

/// <summary>
/// Defines the visual-only flattening of grouping nodes. The persisted
/// workspace hierarchy and node identities remain unchanged.
/// </summary>
public static class NavigationPresentationRules
{
    /// <summary>Identifies structural containers hidden in the visual tree.</summary>
    /// <param name="kind">Canonical node kind.</param>
    /// <returns>Whether the node should be visually bypassed.</returns>
    public static bool IsTransparentContainer(WorkspaceNodeKind kind) =>
        kind is WorkspaceNodeKind.ApplicationsRoot or WorkspaceNodeKind.ProjectsRoot;

    /// <summary>
    /// Projects immediate visible children without modifying the source.
    /// Also works for subprojects stored below a transparent grouping node.
    /// </summary>
    /// <param name="node">Canonical parent node.</param>
    /// <returns>Visible direct children, sharing the original node instances.</returns>
    public static IReadOnlyList<WorkspaceNavigationNode> GetVisibleChildren(
        WorkspaceNavigationNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        List<WorkspaceNavigationNode> children = new List<WorkspaceNavigationNode>();
        foreach (WorkspaceNavigationNode child in node.Children)
        {
            AppendVisible(child, children);
        }

        return children;
    }

    /// <summary>Appends flattened children without losing canonical nodes.</summary>
    /// <param name="node">Possible transparent container.</param>
    /// <param name="result">Visible nodes.</param>
    private static void AppendVisible(
        WorkspaceNavigationNode node,
        List<WorkspaceNavigationNode> result)
    {
        if (!IsTransparentContainer(node.Kind))
        {
            result.Add(node);
            return;
        }

        foreach (WorkspaceNavigationNode child in node.Children)
        {
            AppendVisible(child, result);
        }
    }
}
