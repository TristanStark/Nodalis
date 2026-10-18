namespace Nodalis.Core.Composites;

/// <summary>
/// Reconstructible in-memory inclusion graph; these edges are not causal
/// business dependencies and never enter the impact-analysis index.
/// </summary>
public sealed class CompositeInclusionGraph
{
    private readonly Dictionary<Guid, HashSet<Guid>> _parentsBySource = [];

    /// <summary>Rebuilds the derived graph atomically from currently resolved links.</summary>
    /// <param name="edges">Canonical source-to-target inclusion references.</param>
    public void Replace(IReadOnlyList<CompositeEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        Dictionary<Guid, HashSet<Guid>> next = [];
        foreach (CompositeEdge edge in edges)
        {
            if (edge.SourceId == Guid.Empty || edge.TargetId == Guid.Empty)
            {
                continue;
            }

            if (!next.TryGetValue(edge.TargetId, out HashSet<Guid>? parents))
            {
                parents = [];
                next.Add(edge.TargetId, parents);
            }

            parents.Add(edge.SourceId);
        }

        _parentsBySource.Clear();
        foreach (KeyValuePair<Guid, HashSet<Guid>> entry in next)
        {
            _parentsBySource.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>Finds the transitive set of parent documents to refresh after a source edit.</summary>
    /// <param name="changedSourceId">GUID of an edited, moved or removed source.</param>
    /// <returns>Unique impacted parents, ordered by stable GUID.</returns>
    public IReadOnlyList<Guid> AffectedParents(Guid changedSourceId)
    {
        HashSet<Guid> found = [];
        Queue<Guid> queue = new();
        queue.Enqueue(changedSourceId);
        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();
            if (!_parentsBySource.TryGetValue(current, out HashSet<Guid>? parents))
            {
                continue;
            }

            foreach (Guid parent in parents)
            {
                if (parent != changedSourceId && found.Add(parent))
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return found.OrderBy(id => id).ToArray();
    }

    /// <summary>Clears a disposable cache; source Markdown remains untouched.</summary>
    public void Clear() => _parentsBySource.Clear();
}
