using Nodalis.Core.Markdown;

namespace Nodalis.Core.Relations;

/// <summary>One Markdown relation with its conservatively attributed block source.</summary>
/// <param name="Relation">Original parsed relation.</param>
/// <param name="SourceBlockId">Directly enclosing unique marked heading, or null.</param>
public sealed record MarkdownRelationSourceBinding(
    MarkdownRelationEntry Relation,
    Guid? SourceBlockId);

/// <summary>
/// Associates relation lines with exactly the nearest preceding ATX heading.
/// Unmarked nested headings prevent attribution to a marked parent section.
/// </summary>
public static class MarkdownRelationSourceResolver
{
    /// <summary>
    /// Resolves all source blocks in a single chronological pass over headings
    /// and relations, without modifying the Markdown.
    /// </summary>
    /// <param name="relations">Canonical relation lines in the source document.</param>
    /// <param name="scan">Headings and identity issues from the same document.</param>
    /// <returns>Deterministic source assignments; null is deliberately safe.</returns>
    public static IReadOnlyList<MarkdownRelationSourceBinding> Bind(
        IReadOnlyList<MarkdownRelationEntry> relations,
        MarkdownBlockIdentityScan scan)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(scan);

        HashSet<Guid> duplicateIds = scan.Issues
            .Where(issue => issue.Kind == MarkdownBlockIdentityIssueKind.DuplicateId &&
                issue.Id.HasValue)
            .Select(issue => issue.Id!.Value)
            .ToHashSet();

        List<MarkdownRelationSourceBinding> result = [];
        int nextHeadingIndex = 0;
        MarkdownBlockIdentityEntry? heading = null;

        foreach (MarkdownRelationEntry relation in relations.OrderBy(entry => entry.LineNumber))
        {
            while (nextHeadingIndex < scan.Blocks.Count &&
                scan.Blocks[nextHeadingIndex].HeadingLine < relation.LineNumber)
            {
                heading = scan.Blocks[nextHeadingIndex];
                nextHeadingIndex++;
            }

            Guid? id = heading?.Id is Guid parsedId &&
                !duplicateIds.Contains(parsedId) ? parsedId : null;
            result.Add(new MarkdownRelationSourceBinding(relation, id));
        }

        return result;
    }
}
