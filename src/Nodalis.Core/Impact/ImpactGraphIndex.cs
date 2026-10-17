using Nodalis.Core.Links;
using Nodalis.Core.Relations;

namespace Nodalis.Core.Impact;

/// <summary>
/// Represents a derived edge. Source and target always retain the direction written in Markdown.
/// </summary>
public sealed record ImpactGraphEdge
{
    /// <summary>Gets the stable, deterministic identity of this indexed occurrence.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the document that declares the relation or link.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the resolved destination identifier.</summary>
    public required Guid TargetId { get; init; }

    /// <summary>Gets the original typed relation or contextual link label.</summary>
    public required string RelationType { get; init; }

    /// <summary>Gets the conservative direction of possible impact propagation.</summary>
    public required ImpactPropagation Propagation { get; init; }

    /// <summary>Gets whether this edge represents an explicit dependency or context only.</summary>
    public required ImpactEvidence Evidence { get; init; }

    /// <summary>Gets the one-based Markdown line where this edge occurs.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Gets the human-readable source excerpt, used as an audit trail.</summary>
    public required string Excerpt { get; init; }

    /// <summary>Gets whether this edge originates from an untyped internal link.</summary>
    public required bool IsBacklink { get; init; }
}

/// <summary>
/// Describes an unresolved or inconsistent element without synthesizing a graph target.
/// </summary>
public sealed record ImpactGraphDiagnostic(
    Guid SourceId,
    int LineNumber,
    string RawTarget,
    string Message);

/// <summary>
/// Reconstructible, read-only projection of the stable identity registry and Markdown references.
/// No separate persistent graph or business-file migration is required.
/// </summary>
public sealed class ImpactGraphIndex
{
    /// <summary>Gets all identifiable active workspace targets.</summary>
    public IReadOnlyDictionary<Guid, LinkTargetEntry> Targets { get; }

    /// <summary>Gets all resolved, directionally annotated edges.</summary>
    public IReadOnlyList<ImpactGraphEdge> Edges { get; }

    /// <summary>Gets malformed, missing and inconsistent references that were not traversed.</summary>
    public IReadOnlyList<ImpactGraphDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Initializes an immutable graph snapshot.
    /// </summary>
    /// <param name="targets">Active objects keyed by stable identifier.</param>
    /// <param name="edges">Resolved edges.</param>
    /// <param name="diagnostics">Unresolved source references.</param>
    private ImpactGraphIndex(
        IReadOnlyDictionary<Guid, LinkTargetEntry> targets,
        IReadOnlyList<ImpactGraphEdge> edges,
        IReadOnlyList<ImpactGraphDiagnostic> diagnostics)
    {
        Targets = targets;
        Edges = edges;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Builds a deterministic graph from the canonical identity registry and its derived references.
    /// The input is never written to, and a missing target never creates a fictional node.
    /// </summary>
    /// <param name="catalog">Workspace link index with stable targets and parsed Markdown relations.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>An independently reconstructible graph snapshot.</returns>
    public static ImpactGraphIndex Build(
        LinkIndexCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        Dictionary<Guid, LinkTargetEntry> targets = [];
        List<ImpactGraphEdge> edges = [];
        List<ImpactGraphDiagnostic> diagnostics = [];

        foreach (LinkTargetEntry target in catalog.Targets.OrderBy(
                     target => target.QualifiedName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Id == Guid.Empty || !targets.TryAdd(target.Id, target))
            {
                diagnostics.Add(new ImpactGraphDiagnostic(
                    target.Id, 0, target.RelativePath,
                    "Identité de cible vide ou dupliquée dans le registre."));
            }
        }

        Dictionary<Guid, int> relationOrdinals = [];
        foreach (TypedRelationEntry relation in catalog.Relations
                     .OrderBy(relation => relation.SourceId)
                     .ThenBy(relation => relation.LineNumber)
                     .ThenBy(relation => relation.RelationType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(relation => relation.RawTargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!targets.ContainsKey(relation.SourceId))
            {
                diagnostics.Add(new ImpactGraphDiagnostic(
                    relation.SourceId, relation.LineNumber, relation.RawTargetId,
                    "Document source de relation absent du registre actif."));
                continue;
            }

            if (!relation.TargetId.HasValue ||
                !targets.ContainsKey(relation.TargetId.Value))
            {
                diagnostics.Add(new ImpactGraphDiagnostic(
                    relation.SourceId, relation.LineNumber, relation.RawTargetId,
                    "ID cible invalide ou cible supprimée/introuvable."));
                continue;
            }

            ImpactRelationRule rule = ImpactRelationPolicy.GetRule(relation.RelationType);
            int ordinal = relationOrdinals.GetValueOrDefault(relation.SourceId);
            relationOrdinals[relation.SourceId] = ordinal + 1;
            edges.Add(new ImpactGraphEdge
            {
                Id = $"relation:{relation.SourceId:D}:{relation.LineNumber}:{ordinal}",
                SourceId = relation.SourceId,
                TargetId = relation.TargetId.Value,
                RelationType = relation.RelationType,
                Propagation = rule.Propagation,
                Evidence = rule.Evidence,
                LineNumber = relation.LineNumber,
                Excerpt = relation.Excerpt,
                IsBacklink = false
            });
        }

        Dictionary<Guid, int> backlinkOrdinals = [];
        foreach (LinkReferenceEntry reference in catalog.References
                     .OrderBy(reference => reference.SourceId)
                     .ThenBy(reference => reference.LineNumber)
                     .ThenBy(reference => reference.RawTarget, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!targets.ContainsKey(reference.SourceId))
            {
                diagnostics.Add(new ImpactGraphDiagnostic(
                    reference.SourceId, reference.LineNumber, reference.RawTarget,
                    "Document source du lien absent du registre actif."));
                continue;
            }

            if (!reference.TargetId.HasValue ||
                !targets.ContainsKey(reference.TargetId.Value))
            {
                diagnostics.Add(new ImpactGraphDiagnostic(
                    reference.SourceId, reference.LineNumber, reference.RawTarget,
                    "Lien interne non résolu ou ambigu."));
                continue;
            }

            int ordinal = backlinkOrdinals.GetValueOrDefault(reference.SourceId);
            backlinkOrdinals[reference.SourceId] = ordinal + 1;
            edges.Add(new ImpactGraphEdge
            {
                Id = $"backlink:{reference.SourceId:D}:{reference.LineNumber}:{ordinal}",
                SourceId = reference.SourceId,
                TargetId = reference.TargetId.Value,
                RelationType = "lien interne",
                Propagation = ImpactPropagation.None,
                Evidence = ImpactEvidence.Suggested,
                LineNumber = reference.LineNumber,
                Excerpt = reference.Excerpt,
                IsBacklink = true
            });
        }

        return new ImpactGraphIndex(
            targets,
            OrderEdges(edges),
            OrderDiagnostics(diagnostics));
    }

    /// <summary>
    /// Rebuilds only the Markdown source portions whose references changed while
    /// retaining unaffected source edges from an earlier immutable graph.
    /// Membership changes (target deletion/restoration) must also be represented
    /// in changedSourceIds by the caller.
    /// </summary>
    /// <param name="catalog">Refreshed canonical identity and derived link index.</param>
    /// <param name="previous">Previously fully validated graph.</param>
    /// <param name="changedSourceIds">Sources requiring a new relation projection.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Graph equivalent to a complete rebuild of the input index.</returns>
    public static ImpactGraphIndex RebuildSources(
        LinkIndexCatalog catalog,
        ImpactGraphIndex previous,
        IReadOnlySet<Guid> changedSourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(changedSourceIds);
        cancellationToken.ThrowIfCancellationRequested();
        LinkIndexCatalog fragment = new()
        {
            Targets = catalog.Targets,
            Relations = catalog.Relations
                .Where(item => changedSourceIds.Contains(item.SourceId)).ToList(),
            References = catalog.References
                .Where(item => changedSourceIds.Contains(item.SourceId)).ToList()
        };
        ImpactGraphIndex changed = Build(fragment, cancellationToken);
        List<ImpactGraphEdge> edges = previous.Edges
            .Where(item => !changedSourceIds.Contains(item.SourceId) &&
                changed.Targets.ContainsKey(item.SourceId) &&
                changed.Targets.ContainsKey(item.TargetId))
            .Concat(changed.Edges)
            .ToList();
        List<ImpactGraphDiagnostic> diagnostics = previous.Diagnostics
            .Where(item => item.LineNumber > 0 &&
                !changedSourceIds.Contains(item.SourceId) &&
                changed.Targets.ContainsKey(item.SourceId))
            .Concat(changed.Diagnostics)
            .ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return new ImpactGraphIndex(changed.Targets, OrderEdges(edges),
            OrderDiagnostics(diagnostics));
    }

    /// <summary>Returns deterministic edge order for full and incremental builds.</summary>
    /// <param name="edges">Edges to order.</param>
    /// <returns>Sorted immutable snapshot.</returns>
    private static IReadOnlyList<ImpactGraphEdge> OrderEdges(
        IEnumerable<ImpactGraphEdge> edges)
    {
        return edges.OrderBy(item => item.IsBacklink)
            .ThenBy(item => item.SourceId)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.RelationType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Orders broken reference and identity diagnostics reproducibly.</summary>
    /// <param name="diagnostics">Read-only diagnostics.</param>
    /// <returns>Sorted snapshot.</returns>
    private static IReadOnlyList<ImpactGraphDiagnostic> OrderDiagnostics(
        IEnumerable<ImpactGraphDiagnostic> diagnostics)
    {
        return diagnostics.OrderBy(item => item.SourceId)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.RawTarget, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToArray();
    }
}
