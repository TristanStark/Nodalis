using Nodalis.Core.Links;

namespace Nodalis.Core.Impact;

/// <summary>One labeled node in a bounded, read-only impact diagram.</summary>
public sealed record ImpactDiagramNode(Guid Id, string Title, string Group, int Depth,
    LinkTargetKind Kind, ImpactEvidence Evidence);

/// <summary>An explanatory arrow from the element changed to an affected element.</summary>
public sealed record ImpactDiagramEdge(Guid FromId, Guid ToId, string RelationType,
    ImpactEvidence Evidence);

/// <summary>A bounded drawing projection; the full accessible list remains authoritative.</summary>
public sealed record ImpactDiagram(
    IReadOnlyList<ImpactDiagramNode> Nodes,
    IReadOnlyList<ImpactDiagramEdge> Edges,
    bool IsTruncated);

/// <summary>
/// Projects shortest evidence paths into a bounded presentation graph.
/// The projection cannot create business dependencies or alter the source graph.
/// </summary>
public static class ImpactDiagramProjection
{
    /// <summary>
    /// Produces nodes and arrows for the selected findings while bounding visual density.
    /// </summary>
    /// <param name="origin">Changed document.</param>
    /// <param name="findings">Already filtered list of impact findings.</param>
    /// <param name="graph">Stable identity registry for intermediate nodes.</param>
    /// <param name="maxNodes">Maximum nodes drawn including the origin.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Compact graph plus explicit truncation indicator.</returns>
    public static ImpactDiagram Create(
        LinkTargetEntry origin,
        IEnumerable<ImpactFinding> findings,
        ImpactGraphIndex graph,
        int maxNodes = 90,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(graph);
        if (maxNodes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNodes));
        }

        Dictionary<Guid, ImpactDiagramNode> nodes = [];
        Dictionary<string, ImpactDiagramEdge> arrows = new(StringComparer.Ordinal);
        nodes.Add(origin.Id, new ImpactDiagramNode(
            origin.Id, origin.DisplayName, origin.ScopeIdentity ?? "Global", 0,
            origin.Kind, ImpactEvidence.Explicit));
        bool truncated = false;
        foreach (ImpactFinding finding in findings
                     .OrderBy(item => item.Depth)
                     .ThenBy(item => item.Target.QualifiedName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid cursor = origin.Id;
            int depth = 0;
            bool accepted = true;
            foreach (ImpactGraphEdge edge in finding.Path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid next = finding.Evidence == ImpactEvidence.Explicit ||
                            edge.TargetId == origin.Id
                    ? edge.SourceId : edge.TargetId;
                depth++;
                if (!graph.Targets.TryGetValue(next, out LinkTargetEntry? target))
                {
                    accepted = false;
                    break;
                }

                if (!nodes.ContainsKey(next))
                {
                    if (nodes.Count >= maxNodes)
                    {
                        truncated = true;
                        accepted = false;
                        break;
                    }

                    nodes.Add(next, new ImpactDiagramNode(
                        next, target.DisplayName, target.ScopeIdentity ?? "Global",
                        depth, target.Kind, finding.Evidence));
                }
                string key = cursor.ToString("D") + ":" + next.ToString("D") + ":" +
                    edge.RelationType + ":" + finding.Evidence;
                arrows.TryAdd(key, new ImpactDiagramEdge(
                    cursor, next, edge.RelationType, finding.Evidence));
                cursor = next;
            }

            if (!accepted)
            {
                continue;
            }
        }

        return new ImpactDiagram(
            nodes.Values.OrderBy(node => node.Depth).ThenBy(node => node.Group,
                StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Title,
                StringComparer.OrdinalIgnoreCase).ToArray(),
            arrows.Values.ToArray(),
            truncated);
    }
}
