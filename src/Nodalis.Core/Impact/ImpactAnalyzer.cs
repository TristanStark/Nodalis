using Nodalis.Core.Links;

namespace Nodalis.Core.Impact;

/// <summary>
/// Bounds the impact query and controls whether it can cross workspace scopes.
/// </summary>
public sealed record ImpactAnalysisOptions
{
    /// <summary>Gets the maximum number of typed relations traversed along one path.</summary>
    public int MaxDepth { get; init; } = 6;

    /// <summary>Gets the maximum number of unique explicit results returned.</summary>
    public int MaxResults { get; init; } = 200;

    /// <summary>Gets the maximum number of distinct nodes expanded, including the origin.</summary>
    public int MaxVisitedNodes { get; init; } = 1000;

    /// <summary>Gets whether to also display one-hop contextual suggestions.</summary>
    public bool IncludeSuggested { get; init; } = true;

    /// <summary>Gets whether paths may leave the source document's stable scope.</summary>
    public bool IncludeCrossScope { get; init; }
}

/// <summary>
/// One potentially affected item with a readable and inspectable path of evidence.
/// </summary>
public sealed record ImpactFinding(
    LinkTargetEntry Target,
    ImpactEvidence Evidence,
    int Depth,
    IReadOnlyList<ImpactGraphEdge> Path);

/// <summary>
/// Read-only analysis result. Truncated results must never be mistaken for a complete answer.
/// </summary>
public sealed record ImpactAnalysisResult(
    Guid OriginId,
    IReadOnlyList<ImpactFinding> Findings,
    IReadOnlyList<ImpactGraphDiagnostic> Diagnostics,
    int ExpandedNodes,
    bool IsTruncated);

/// <summary>
/// Deterministic bounded graph traversal. Only semantically directional typed relations
/// propagate; plain backlinks and noncausal relations are direct suggestions at most.
/// </summary>
public static class ImpactAnalyzer
{
    private sealed record PendingPath(Guid NodeId, ImpactGraphEdge[] Path);

    /// <summary>
    /// Finds objects that could be affected by a change to an indexed element.
    /// This operation does not persist anything or modify the graph.
    /// </summary>
    /// <param name="graph">The reconstructed graph snapshot.</param>
    /// <param name="originId">Stable identifier of the element that could change.</param>
    /// <param name="options">Query bounds and scope options.</param>
    /// <param name="cancellationToken">Cancellation request checked throughout traversal.</param>
    /// <returns>Shortest explanatory paths and optional, explicitly unconfirmed context.</returns>
    public static ImpactAnalysisResult Analyze(
        ImpactGraphIndex graph,
        Guid originId,
        ImpactAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ImpactAnalysisOptions settings = options ?? new ImpactAnalysisOptions();
        if (settings.MaxDepth < 1 || settings.MaxResults < 1 || settings.MaxVisitedNodes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                "Les limites de profondeur, résultats et nœuds doivent être positives.");
        }

        if (!graph.Targets.TryGetValue(originId, out LinkTargetEntry? origin))
        {
            return new ImpactAnalysisResult(
                originId, [], graph.Diagnostics, 0, false);
        }

        Dictionary<Guid, List<ImpactGraphEdge>> incoming = [];
        foreach (ImpactGraphEdge edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (edge.Propagation != ImpactPropagation.TargetToSource)
            {
                continue;
            }

            if (!incoming.TryGetValue(edge.TargetId, out List<ImpactGraphEdge>? list))
            {
                list = [];
                incoming.Add(edge.TargetId, list);
            }

            list.Add(edge);
        }

        foreach (List<ImpactGraphEdge> list in incoming.Values)
        {
            list.Sort((left, right) =>
            {
                int result = string.Compare(
                    graph.Targets[left.SourceId].QualifiedName,
                    graph.Targets[right.SourceId].QualifiedName,
                    StringComparison.OrdinalIgnoreCase);
                return result != 0 ? result : string.Compare(
                    left.Id, right.Id, StringComparison.Ordinal);
            });
        }

        Queue<PendingPath> pending = new();
        HashSet<Guid> visited = [originId];
        Dictionary<Guid, ImpactFinding> found = [];
        pending.Enqueue(new PendingPath(originId, []));
        int expanded = 0;
        bool truncated = false;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expanded >= settings.MaxVisitedNodes)
            {
                truncated = true;
                break;
            }

            PendingPath current = pending.Dequeue();
            expanded++;
            if (!incoming.TryGetValue(current.NodeId, out List<ImpactGraphEdge>? edges))
            {
                continue;
            }

            foreach (ImpactGraphEdge edge in edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!graph.Targets.TryGetValue(edge.SourceId, out LinkTargetEntry? affected) ||
                    (!settings.IncludeCrossScope && !SameScope(origin, affected)) ||
                    visited.Contains(affected.Id))
                {
                    continue;
                }

                if (current.Path.Length >= settings.MaxDepth)
                {
                    truncated = true;
                    continue;
                }

                if (found.Count >= settings.MaxResults)
                {
                    truncated = true;
                    break;
                }

                ImpactGraphEdge[] path = [.. current.Path, edge];
                visited.Add(affected.Id);
                found.Add(affected.Id, new ImpactFinding(
                    affected, ImpactEvidence.Explicit, path.Length, path));
                pending.Enqueue(new PendingPath(affected.Id, path));
            }

            if (found.Count >= settings.MaxResults && pending.Count > 0)
            {
                truncated = true;
                break;
            }
        }

        List<ImpactFinding> results = found.Values.ToList();
        if (settings.IncludeSuggested)
        {
            foreach (ImpactGraphEdge edge in graph.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (edge.Propagation != ImpactPropagation.None)
                {
                    continue;
                }

                // A plain link only suggests its author when its destination changes.
                // A typed contextual relation can be considered in either direction,
                // but it must NEVER be traversed transitively.
                Guid candidateId;
                if (edge.TargetId == originId)
                {
                    candidateId = edge.SourceId;
                }
                else if (!edge.IsBacklink && edge.SourceId == originId)
                {
                    candidateId = edge.TargetId;
                }
                else
                {
                    continue;
                }

                if (candidateId == originId ||
                    found.ContainsKey(candidateId) ||
                    !graph.Targets.TryGetValue(candidateId, out LinkTargetEntry? candidate) ||
                    (!settings.IncludeCrossScope && !SameScope(origin, candidate)))
                {
                    continue;
                }

                if (results.Count >= settings.MaxResults)
                {
                    truncated = true;
                    break;
                }

                if (results.Any(result => result.Target.Id == candidateId))
                {
                    continue;
                }

                results.Add(new ImpactFinding(
                    candidate, ImpactEvidence.Suggested, 1, [edge]));
            }
        }

        return new ImpactAnalysisResult(
            originId,
            results.OrderBy(item => item.Evidence)
                .ThenBy(item => item.Depth)
                .ThenBy(item => item.Target.QualifiedName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Target.Id)
                .ToArray(),
            graph.Diagnostics,
            expanded,
            truncated);
    }

    /// <summary>
    /// Restricts default traversal to the same stable project, module, application or global scope.
    /// </summary>
    /// <param name="origin">Starting element.</param>
    /// <param name="candidate">Candidate affected element.</param>
    /// <returns>Whether the two elements share the same scope identity.</returns>
    private static bool SameScope(LinkTargetEntry origin, LinkTargetEntry candidate)
    {
        return !string.IsNullOrWhiteSpace(origin.ScopeIdentity) &&
            !string.IsNullOrWhiteSpace(candidate.ScopeIdentity) &&
            string.Equals(origin.ScopeIdentity, candidate.ScopeIdentity,
                StringComparison.OrdinalIgnoreCase);
    }
}
