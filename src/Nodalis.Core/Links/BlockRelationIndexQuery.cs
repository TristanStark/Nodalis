namespace Nodalis.Core.Links;

/// <summary>
/// Typed relations incoming to one block with an explicit target resolution.
/// </summary>
/// <param name="Resolution">Current target status, including ambiguity.</param>
/// <param name="Relations">Readable incoming relation occurrences from unique source documents.</param>
public sealed record BlockIncomingRelationResult(
    MarkdownBlockIndexResolution Resolution,
    IReadOnlyList<ReferenceSearchEntry> Relations);

/// <summary>
/// Projects typed Markdown relations towards stable blocks without persisting
/// a duplicate business registry or guessing an ambiguous destination.
/// </summary>
public static class BlockRelationIndexQuery
{
    /// <summary>
    /// Finds incoming relations targeting a unique block ID. Relations with
    /// missing or multiply registered source documents are not resolved.
    /// </summary>
    /// <param name="catalog">Shared workspace link index snapshot.</param>
    /// <param name="blockId">Target block GUID.</param>
    /// <returns>Target resolution and stable incoming relation occurrences.</returns>
    public static BlockIncomingRelationResult GetIncoming(
        LinkIndexCatalog catalog,
        Guid blockId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        MarkdownBlockIndexResolution target = BlockIndexQuery.Resolve(catalog, blockId);

        if (target.Status != LinkResolutionStatus.Resolved ||
            target.Document is null)
        {
            return new BlockIncomingRelationResult(target, []);
        }

        List<ReferenceSearchEntry> incoming = [];
        foreach (Nodalis.Core.Relations.TypedRelationEntry relation in catalog.Relations)
        {
            if (relation.TargetId != blockId)
            {
                continue;
            }

            LinkTargetEntry[] sources = catalog.Targets.Where(candidate =>
                candidate.Kind == LinkTargetKind.Document &&
                candidate.Id == relation.SourceId).ToArray();

            if (sources.Length != 1)
            {
                continue;
            }

            LinkTargetEntry source = sources[0];
            incoming.Add(new ReferenceSearchEntry
            {
                Source = source,
                LineNumber = relation.LineNumber,
                Excerpt = relation.Excerpt,
                TypeLabel = $"Relation · {relation.RelationType}",
                RelationType = relation.RelationType,
                IsSameScope = string.Equals(
                    source.ScopeIdentity,
                    target.Document.ScopeIdentity,
                    StringComparison.OrdinalIgnoreCase)
            });
        }

        return new BlockIncomingRelationResult(target, incoming
            .OrderBy(reference => reference.Source.QualifiedName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(reference => reference.LineNumber)
            .ThenBy(reference => reference.RelationType,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }
}
