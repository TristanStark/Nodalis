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
                SourceBlockId = ResolveIncomingSourceBlock(catalog, relation, source),
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

    /// <summary>
    /// Validates that a relation's persisted source block belongs uniquely to
    /// the source document. Ambiguous or moved-outside references are omitted.
    /// </summary>
    /// <param name="catalog">Index snapshot.</param>
    /// <param name="relation">Relation carrying an optional source block ID.</param>
    /// <param name="source">Uniquely identified source document.</param>
    /// <returns>A verified block ID or null.</returns>
    private static Guid? ResolveIncomingSourceBlock(
        LinkIndexCatalog catalog,
        Nodalis.Core.Relations.TypedRelationEntry relation,
        LinkTargetEntry source)
    {
        if (relation.SourceBlockId is not Guid id)
        {
            return null;
        }

        MarkdownBlockIndexResolution resolution = BlockIndexQuery.Resolve(catalog, id);
        return resolution.Status == LinkResolutionStatus.Resolved &&
            resolution.Document?.Id == source.Id ? id : null;
    }

    /// <summary>
    /// Returns typed relations originating in one uniquely identified Markdown
    /// block. Targets are resolved as either blocks or workspace entities.
    /// Missing and ambiguous target IDs remain visible as diagnostics.
    /// </summary>
    /// <param name="catalog">Shared workspace index snapshot.</param>
    /// <param name="blockId">Stable source block GUID.</param>
    /// <returns>Block resolution and outgoing typed relations.</returns>
    public static BlockOutgoingRelationResult GetOutgoing(
        LinkIndexCatalog catalog, Guid blockId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        MarkdownBlockIndexResolution source = BlockIndexQuery.Resolve(catalog, blockId);
        if (source.Status != LinkResolutionStatus.Resolved ||
            source.Document is null)
        {
            return new BlockOutgoingRelationResult(source, []);
        }

        List<BlockOutgoingRelationEntry> entries = [];
        foreach (Nodalis.Core.Relations.TypedRelationEntry relation in catalog.Relations)
        {
            if (relation.SourceBlockId != blockId ||
                relation.SourceId != source.Document.Id)
            {
                continue;
            }

            LinkResolutionStatus status = LinkResolutionStatus.Missing;
            IndexedMarkdownBlock? targetBlock = null;
            LinkTargetEntry? targetEntity = null;

            if (relation.TargetId is Guid targetId)
            {
                MarkdownBlockIndexResolution blockResolution =
                    BlockIndexQuery.Resolve(catalog, targetId);

                if (blockResolution.Status == LinkResolutionStatus.Resolved)
                {
                    targetBlock = blockResolution.Block;
                    targetEntity = blockResolution.Document;
                    status = LinkResolutionStatus.Resolved;
                }
                else if (blockResolution.Status == LinkResolutionStatus.Ambiguous)
                {
                    status = LinkResolutionStatus.Ambiguous;
                }
                else
                {
                    LinkTargetEntry[] candidates = catalog.Targets
                        .Where(target => target.Id == targetId).ToArray();
                    if (candidates.Length == 1)
                    {
                        status = LinkResolutionStatus.Resolved;
                        targetEntity = candidates[0];
                    }
                    else if (candidates.Length > 1)
                    {
                        status = LinkResolutionStatus.Ambiguous;
                    }
                }
            }

            entries.Add(new BlockOutgoingRelationEntry
            {
                RelationType = relation.RelationType,
                LineNumber = relation.LineNumber,
                TargetId = relation.TargetId,
                TargetStatus = status,
                TargetBlock = targetBlock,
                TargetEntity = targetEntity,
                TargetLabel = targetBlock?.HeadingTitle ??
                    targetEntity?.DisplayName ??
                    relation.TargetLabel ??
                    relation.RawTargetId
            });
        }

        return new BlockOutgoingRelationResult(source, entries
            .OrderBy(entry => entry.LineNumber).ToArray());
    }

}
