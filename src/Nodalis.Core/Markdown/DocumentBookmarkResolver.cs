using Nodalis.Core.Settings;

namespace Nodalis.Core.Markdown;

/// <summary>
/// Resolves persisted document bookmarks against the current Markdown source without rewriting it.
/// </summary>
public static class DocumentBookmarkResolver
{
    /// <summary>
    /// Resolves a bookmark to the best matching current heading.
    /// Exact heading title and level are required; source offset and saved occurrence only disambiguate duplicates.
    /// </summary>
    /// <param name="bookmark">The persisted local bookmark.</param>
    /// <param name="markdown">The current Markdown source.</param>
    /// <returns>The resolved heading, or <see langword="null"/> when the saved heading no longer exists.</returns>
    public static MarkdownOutlineEntry? Resolve(
            DocumentBookmarkReference bookmark,
            string markdown)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        ArgumentNullException.ThrowIfNull(markdown);

        global::System.Collections.Generic.List<global::Nodalis.Core.Markdown.MarkdownOutlineEntry> candidates =
            MarkdownOutlineParser.Parse(markdown)
                .Where(entry =>
                    entry.Level == bookmark.HeadingLevel &&
                    string.Equals(
                        entry.Title,
                        bookmark.HeadingTitle,
                        StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        MarkdownOutlineEntry best =
            candidates[0];

        long bestScore =
            (Math.Abs(
                (long)best.Offset -
                bookmark.OriginalOffset) * 10L) +
            Math.Abs(
                bookmark.HeadingOccurrence);

        for (int index = 1;
             index < candidates.Count;
             index++)
        {
            MarkdownOutlineEntry candidate =
                candidates[index];

            long score =
                (Math.Abs(
                    (long)candidate.Offset -
                    bookmark.OriginalOffset) * 10L) +
                Math.Abs(
                    (long)index -
                    bookmark.HeadingOccurrence);

            if (score < bestScore)
            {
                best =
                    candidate;
                bestScore =
                    score;
            }
        }

        return best;
    }
}
