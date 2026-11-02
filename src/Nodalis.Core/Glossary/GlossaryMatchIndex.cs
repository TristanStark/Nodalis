namespace Nodalis.Core.Glossary;

/// <summary>
/// Binary lookup over the sorted, non-overlapping results emitted by
/// <see cref="GlossaryTextMatcher"/>. Does not allocate or scan all matches.
/// </summary>
public static class GlossaryMatchIndex
{
    /// <summary>
    /// Finds the half-open range of matches intersecting the visible character interval.
    /// </summary>
    /// <param name="matches">Sorted, non-overlapping matches.</param>
    /// <param name="firstVisible">First visible character offset, inclusive.</param>
    /// <param name="endVisibleExclusive">First character after the visible range.</param>
    /// <returns>Indices of the first and one-past-last visible match.</returns>
    public static (int Start, int EndExclusive) GetVisibleRange(
            IReadOnlyList<GlossaryTextMatch> matches,
            int firstVisible,
            int endVisibleExclusive)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count == 0 || endVisibleExclusive <= firstVisible)
        {
            return (0, 0);
        }

        // First match whose end is strictly after the visible start.
        int low = 0;
        int high = matches.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            GlossaryTextMatch match = matches[middle];
            if ((long)match.Start + match.Length <= firstVisible)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        int first = low;
        high = matches.Count;

        // First match starting on or after the viewport end.
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (matches[middle].Start < endVisibleExclusive)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return (first, low);
    }

    /// <summary>Finds a term under the mouse or caret without a linear scan.</summary>
    /// <param name="matches">Sorted, non-overlapping matches.</param>
    /// <param name="characterOffset">Zero-based character offset.</param>
    /// <returns>The match containing the character, or null.</returns>
    public static GlossaryTextMatch? FindAt(
            IReadOnlyList<GlossaryTextMatch> matches,
            int characterOffset)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (characterOffset < 0 || matches.Count == 0)
        {
            return null;
        }

        int low = 0;
        int high = matches.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (matches[middle].Start <= characterOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low == 0)
        {
            return null;
        }

        GlossaryTextMatch match = matches[low - 1];
        return (long)match.Start + match.Length > characterOffset
            ? match
            : null;
    }
}
