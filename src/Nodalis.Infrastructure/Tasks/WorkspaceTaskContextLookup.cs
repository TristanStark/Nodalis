using Nodalis.Core.Links;

namespace Nodalis.Infrastructure.Tasks;

/// <summary>
/// Indexes filesystem scope paths once per refresh so documents can resolve
/// their nearest project and application without scanning every scope.
/// </summary>
internal sealed class WorkspaceTaskContextLookup
{
    private readonly Dictionary<string, LinkTargetEntry> _projects =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkTargetEntry> _applications =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the two scope maps, preserving first-match semantics.</summary>
    internal WorkspaceTaskContextLookup(IReadOnlyList<LinkTargetEntry> targets)
    {
        foreach (LinkTargetEntry target in targets)
        {
            if (target.Kind == LinkTargetKind.Project)
            {
                _projects.TryAdd(target.RelativePath, target);
            }
            else if (target.Kind == LinkTargetKind.Application)
            {
                _applications.TryAdd(target.RelativePath, target);
            }
        }
    }

    /// <summary>Returns the longest ancestor project path, if any.</summary>
    internal LinkTargetEntry? FindProject(string relativePath) =>
        FindNearestAncestor(_projects, relativePath);

    /// <summary>Returns the longest ancestor application path, if any.</summary>
    internal LinkTargetEntry? FindApplication(string relativePath) =>
        FindNearestAncestor(_applications, relativePath);

    /// <summary>Walks whole path segments and avoids prefix-name collisions.</summary>
    private static LinkTargetEntry? FindNearestAncestor(
        IReadOnlyDictionary<string, LinkTargetEntry> scopes,
        string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        int separator = normalized.LastIndexOf('/');
        while (separator > 0)
        {
            string ancestor = normalized[..separator];
            if (scopes.TryGetValue(ancestor, out LinkTargetEntry? match))
            {
                return match;
            }

            separator = ancestor.LastIndexOf('/');
        }

        return null;
    }
}
