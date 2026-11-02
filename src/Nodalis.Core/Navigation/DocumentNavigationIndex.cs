namespace Nodalis.Core.Navigation;

/// <summary>
/// Read-only first-match document lookup. Preserves the original traversal
/// semantics when a document ID and a relative/absolute path disagree:
/// whichever matching document was encountered first in source order wins.
/// Does not load files or manage the lifetime of its source tree.
/// </summary>
/// <typeparam name="TNode">Type of an already materialized navigation node.</typeparam>
public sealed class DocumentNavigationIndex<TNode> where TNode : class
{
    /// <summary>One indexed document and its original traversal position.</summary>
    private readonly record struct Match(TNode Node, int Position);

    private readonly Dictionary<Guid, Match> _byId = [];
    private readonly Dictionary<string, Match> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Indexes documents once in their existing traversal order.</summary>
    /// <param name="documents">Already loaded navigation documents only.</param>
    /// <param name="getId">Stable node identity selector.</param>
    /// <param name="getPath">Absolute or workspace-relative node path selector.</param>
    public DocumentNavigationIndex(
        IEnumerable<TNode> documents,
        Func<TNode, Guid> getId,
        Func<TNode, string> getPath)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(getId);
        ArgumentNullException.ThrowIfNull(getPath);

        int position = 0;
        foreach (TNode node in documents)
        {
            Guid id = getId(node);
            string path = getPath(node);
            Match entry = new(node, position++);
            _byId.TryAdd(id, entry);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _byPath.TryAdd(path, entry);
            }
        }
    }

    /// <summary>
    /// Looks up a tab using its identity or path, retaining first-match
    /// precedence from the original linear navigation tree traversal.
    /// </summary>
    /// <param name="id">Document ID saved in the tab.</param>
    /// <param name="path">Document source path saved in the tab.</param>
    /// <returns>First matching document node, or null when absent.</returns>
    public TNode? Resolve(Guid id, string path)
    {
        bool hasId = _byId.TryGetValue(id, out Match byId);
        Match byPath = default;
        bool hasPath = !string.IsNullOrWhiteSpace(path) &&
            _byPath.TryGetValue(path, out byPath);
        if (hasId && hasPath)
        {
            return byId.Position <= byPath.Position ? byId.Node : byPath.Node;
        }

        if (hasId)
        {
            return byId.Node;
        }

        return hasPath ? byPath.Node : null;
    }

    /// <summary>Finds the first exact path match in traversal order.</summary>
    /// <param name="path">Canonical document path.</param>
    /// <returns>The first matching document node, or null.</returns>
    public TNode? ResolveByPath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : _byPath.TryGetValue(path, out Match match) ? match.Node : null;

    /// <summary>
    /// Finds the first exact ID match, without performing a path fallback.
    /// Useful for verifying that an open tab's original session still points
    /// to the expected source after a complete navigation rebuild.
    /// </summary>
    /// <param name="id">Stable document identity.</param>
    /// <returns>The original first matching document, or null.</returns>
    public TNode? ResolveById(Guid id) =>
        _byId.TryGetValue(id, out Match match) ? match.Node : null;
}
