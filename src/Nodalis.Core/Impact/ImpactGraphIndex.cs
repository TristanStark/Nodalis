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

        int occurrence = 0;
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
            edges.Add(new ImpactGraphEdge
            {
                Id = $"relation:{relation.SourceId:D}:{relation.LineNumber}:{occurrence++}",
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

            edges.Add(new ImpactGraphEdge
            {
                Id = $"backlink:{reference.SourceId:D}:{reference.LineNumber}:{occurrence++}",
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
            edges.AsReadOnly(),
            diagnostics.AsReadOnly());
    }
}
