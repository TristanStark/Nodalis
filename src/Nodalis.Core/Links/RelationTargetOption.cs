namespace Nodalis.Core.Links;

/// <summary>
/// One safe, searchable target presented to a relation editor. A block option
/// retains the owning document identity for navigation and human context.
/// </summary>
public sealed record RelationTargetOption
{
    /// <summary>Unique persistent GUID of the target entity or block.</summary>
    public required Guid Id { get; init; }

    /// <summary>Human-visible target name used in canonical Markdown relations.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Readable document/project path plus block heading, when applicable.</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Whether this option represents an identified Markdown heading.</summary>
    public required bool IsBlock { get; init; }

    /// <summary>Owning document identity for a block, or null for non-block targets.</summary>
    public Guid? DocumentId { get; init; }
}

/// <summary>
/// Builds safe relation-picker options solely from a shared index snapshot.
/// Missing, duplicate and cross-kind conflicting GUIDs cannot be selected.
/// </summary>
public static class RelationTargetIndexQuery
{
    /// <summary>
    /// Returns unique workspace entities and blocks in readable order, retaining
    /// blocks belonging to the source document for same-file relationships.
    /// </summary>
    /// <param name="catalog">Shared incremental index snapshot.</param>
    /// <param name="excludeDocumentId">Optional source document excluded as an entity target.</param>
    /// <returns>Unique, searchable target options without filesystem operations.</returns>
    public static IReadOnlyList<RelationTargetOption> GetSelectable(
        LinkIndexCatalog catalog, Guid? excludeDocumentId = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        Dictionary<Guid, int> targetCounts = catalog.Targets
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.Count());
        Dictionary<Guid, int> blockCounts = catalog.Blocks
            .GroupBy(block => block.Id)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<Guid, LinkTargetEntry> uniqueDocuments = catalog.Targets
            .Where(target => target.Kind == LinkTargetKind.Document &&
                targetCounts[target.Id] == 1)
            .ToDictionary(target => target.Id);

        List<RelationTargetOption> choices = [];

        foreach (LinkTargetEntry target in catalog.Targets)
        {
            if (target.Id == excludeDocumentId ||
                targetCounts[target.Id] != 1 ||
                blockCounts.ContainsKey(target.Id))
            {
                continue;
            }

            choices.Add(new RelationTargetOption
            {
                Id = target.Id,
                DisplayName = target.DisplayName,
                QualifiedName = target.QualifiedName,
                IsBlock = false
            });
        }

        foreach (IndexedMarkdownBlock block in catalog.Blocks)
        {
            if (blockCounts[block.Id] != 1 ||
                targetCounts.ContainsKey(block.Id) ||
                !uniqueDocuments.TryGetValue(block.DocumentId,
                    out LinkTargetEntry? owner))
            {
                continue;
            }

            choices.Add(new RelationTargetOption
            {
                Id = block.Id,
                DisplayName = block.HeadingTitle,
                QualifiedName = $"{owner.QualifiedName} › {block.HeadingTitle} · bloc · ligne {block.HeadingLine}",
                IsBlock = true,
                DocumentId = owner.Id
            });
        }

        return choices.OrderBy(choice => choice.QualifiedName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(choice => choice.Id)
            .ToArray();
    }
}
