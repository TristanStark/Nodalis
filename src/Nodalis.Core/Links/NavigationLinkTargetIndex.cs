namespace Nodalis.Core.Links;

/// <summary>
/// Immutable lookup built from one link-index snapshot. In case of duplicate
/// IDs or paths, the first entry retains historical FirstOrDefault semantics.
/// Lookup contains no document text and performs no filesystem I/O.
/// </summary>
public sealed class NavigationLinkTargetIndex
{
    private readonly Dictionary<Guid, LinkTargetEntry> _byId = [];
    private readonly Dictionary<string, LinkTargetEntry> _documentsByPath =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkTargetEntry> _projectsByPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds three first-match indexes in one source traversal.</summary>
    /// <param name="targets">Targets in their original catalog order.</param>
    public NavigationLinkTargetIndex(IEnumerable<LinkTargetEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        foreach (LinkTargetEntry target in targets)
        {
            _byId.TryAdd(target.Id, target);
            string path = target.RelativePath.Replace('\\', '/');
            if (target.Kind == LinkTargetKind.Document)
            {
                _documentsByPath.TryAdd(path, target);
            }
            else if (target.Kind == LinkTargetKind.Project)
            {
                _projectsByPath.TryAdd(path.TrimEnd('/'), target);
            }
        }
    }

    /// <summary>Resolves the earliest target bearing the supplied identity.</summary>
    /// <param name="id">Stable identity from the navigation model.</param>
    /// <returns>Target or null.</returns>
    public LinkTargetEntry? FindById(Guid id) =>
        _byId.GetValueOrDefault(id);

    /// <summary>Resolves a Markdown document by its workspace-relative path.</summary>
    /// <param name="relativePath">Canonical workspace-relative path.</param>
    /// <returns>The first matching document target, or null.</returns>
    public LinkTargetEntry? FindDocumentByRelativePath(string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath)
            ? null
            : _documentsByPath.GetValueOrDefault(
                relativePath.Replace('\\', '/'));

    /// <summary>
    /// Finds the nearest project scope enclosing a document. The longest
    /// parent path wins, and duplicated paths retain their first target.
    /// This is equivalent to ordering all candidate project ancestors by
    /// descending path length, without scanning every project.
    /// </summary>
    /// <param name="documentRelativePath">Workspace-relative document path.</param>
    /// <returns>Nearest project, or null when the document is global.</returns>
    public LinkTargetEntry? FindNearestProject(string documentRelativePath)
    {
        if (string.IsNullOrWhiteSpace(documentRelativePath))
        {
            return null;
        }

        string path = documentRelativePath.Replace('\\', '/');
        int slash = path.LastIndexOf('/');
        while (slash > 0)
        {
            if (_projectsByPath.TryGetValue(path[..slash],
                out LinkTargetEntry? project))
            {
                return project;
            }

            slash = path.LastIndexOf('/', slash - 1);
        }

        return null;
    }
}
