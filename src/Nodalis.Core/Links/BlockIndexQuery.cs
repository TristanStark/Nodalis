namespace Nodalis.Core.Links;

/// <summary>
/// Pure in-memory resolution for indexed Markdown block identities, reusing the
/// canonical document targets and never touching the filesystem or a UI thread.
/// </summary>
public static class BlockIndexQuery
{
    /// <summary>
    /// Resolves a block to exactly one heading and exactly one owning document.
    /// Colliding blocks or document identities are explicit ambiguities.
    /// </summary>
    /// <param name="catalog">One complete derived workspace index snapshot.</param>
    /// <param name="id">Stable block ID, not a title or source line.</param>
    /// <returns>Resolution with diagnostic candidates.</returns>
    public static MarkdownBlockIndexResolution Resolve(LinkIndexCatalog catalog, Guid id)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (id == Guid.Empty)
        {
            return new MarkdownBlockIndexResolution
            {
                Status = LinkResolutionStatus.Missing
            };
        }

        List<IndexedMarkdownBlock> candidates = catalog.Blocks
            .Where(block => block.Id == id).ToList();

        if (candidates.Count == 0)
        {
            return new MarkdownBlockIndexResolution
            {
                Status = LinkResolutionStatus.Missing
            };
        }

        if (candidates.Count > 1)
        {
            return new MarkdownBlockIndexResolution
            {
                Status = LinkResolutionStatus.Ambiguous,
                Candidates = candidates
            };
        }

        IndexedMarkdownBlock candidate = candidates[0];
        List<LinkTargetEntry> owners = catalog.Targets
            .Where(target => target.Kind == LinkTargetKind.Document &&
                target.Id == candidate.DocumentId).ToList();

        if (owners.Count != 1)
        {
            return new MarkdownBlockIndexResolution
            {
                Status = owners.Count == 0
                    ? LinkResolutionStatus.Missing
                    : LinkResolutionStatus.Ambiguous,
                Candidates = candidates
            };
        }

        return new MarkdownBlockIndexResolution
        {
            Status = LinkResolutionStatus.Resolved,
            Block = candidate,
            Document = owners[0],
            Candidates = candidates
        };
    }

    /// <summary>
    /// Returns block IDs that are repeated anywhere in the indexed workspace,
    /// without rereading Markdown files or guessing which block is authoritative.
    /// </summary>
    /// <param name="catalog">Complete derived workspace index.</param>
    /// <returns>Ambiguous IDs in deterministic GUID order.</returns>
    public static IReadOnlyList<Guid> FindAmbiguousIds(LinkIndexCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.Blocks
            .GroupBy(block => block.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToList();
    }
}
