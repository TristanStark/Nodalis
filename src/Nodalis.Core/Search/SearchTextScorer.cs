using System.Globalization;
using System.Text;

namespace Nodalis.Core.Search;

/// <summary>
/// Provides deterministic dependency-free text similarity scoring for local search.
/// </summary>
public static class SearchTextScorer
{
    /// <summary>
    /// Scores a candidate against all query terms.
    /// </summary>
    /// <param name="query">The user query text.</param>
    /// <param name="candidate">The candidate title, heading or body line.</param>
    /// <param name="mode">The requested match mode.</param>
    /// <param name="quality">The normalized quality from zero to one.</param>
    /// <returns><see langword="true"/> when the candidate satisfies the query.</returns>
    public static bool TryScore(
            string query,
            string candidate,
            SearchMatchMode mode,
            out double quality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            query);
        ArgumentNullException.ThrowIfNull(
            candidate);

        string normalizedQuery =
            Normalize(
                query);
        string normalizedCandidate =
            Normalize(
                candidate);

        if (normalizedQuery.Length == 0 ||
            normalizedCandidate.Length == 0)
        {
            quality =
                0;
            return false;
        }

        if (mode == SearchMatchMode.Exact)
        {
            bool exact =
                normalizedCandidate.Contains(
                    normalizedQuery,
                    StringComparison.Ordinal);

            quality =
                exact
                    ? 1
                    : 0;
            return exact;
        }

        if (normalizedCandidate.Contains(
                normalizedQuery,
                StringComparison.Ordinal))
        {
            quality =
                1;
            return true;
        }

        string[] queryTerms =
            Tokenize(
                normalizedQuery);

        string[] candidateTerms =
            Tokenize(
                normalizedCandidate);

        if (queryTerms.Length == 0 ||
            candidateTerms.Length == 0)
        {
            quality =
                0;
            return false;
        }

        double total =
            0;

        foreach (string queryTerm in queryTerms)
        {
            double best =
                0;

            foreach (string candidateTerm in candidateTerms)
            {
                double termScore =
                    ScoreTerm(
                        queryTerm,
                        candidateTerm);

                if (termScore >
                    best)
                {
                    best =
                        termScore;
                }
            }

            double threshold =
                queryTerm.Length switch
                {
                    <= 2 => 0.99,
                    <= 4 => 0.72,
                    _ => 0.62
                };

            if (best <
                threshold)
            {
                quality =
                    0;
                return false;
            }

            total +=
                best;
        }

        quality =
            total /
            queryTerms.Length;
        return true;
    }

    /// <summary>
    /// Removes case and diacritic differences for search comparison.
    /// </summary>
    /// <param name="value">The text to normalize.</param>
    /// <returns>A lowercase diacritic-free representation.</returns>
    public static string Normalize(
            string value)
    {
        ArgumentNullException.ThrowIfNull(
            value);

        string decomposed =
            value.Normalize(
                NormalizationForm.FormD);

        StringBuilder builder =
            new StringBuilder(
                decomposed.Length);

        foreach (char character in decomposed)
        {
            UnicodeCategory category =
                CharUnicodeInfo.GetUnicodeCategory(
                    character);

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(
                char.ToLowerInvariant(
                    character));
        }

        return builder
            .ToString()
            .Normalize(
                NormalizationForm.FormC);
    }

    /// <summary>
    /// Computes a similarity score for one normalized query token and candidate token.
    /// </summary>
    /// <param name="queryTerm">The normalized query token.</param>
    /// <param name="candidateTerm">The normalized candidate token.</param>
    /// <returns>A quality value from zero to one.</returns>
    private static double ScoreTerm(
            string queryTerm,
            string candidateTerm)
    {
        if (string.Equals(
                queryTerm,
                candidateTerm,
                StringComparison.Ordinal))
        {
            return 1;
        }

        int shorterLength =
            Math.Min(
                queryTerm.Length,
                candidateTerm.Length);

        if (shorterLength >= 3 &&
            (candidateTerm.StartsWith(
                 queryTerm,
                 StringComparison.Ordinal) ||
             queryTerm.StartsWith(
                 candidateTerm,
                 StringComparison.Ordinal)))
        {
            double lengthRatio =
                (double)shorterLength /
                Math.Max(
                    queryTerm.Length,
                    candidateTerm.Length);

            return 0.88 +
                (0.1 * lengthRatio);
        }

        if (queryTerm.Length >= 3 &&
            candidateTerm.Contains(
                queryTerm,
                StringComparison.Ordinal))
        {
            return 0.86;
        }

        if (candidateTerm.Length >= 3 &&
            queryTerm.Contains(
                candidateTerm,
                StringComparison.Ordinal))
        {
            return 0.82;
        }

        int distance =
            LevenshteinDistance(
                queryTerm,
                candidateTerm);

        int maximumLength =
            Math.Max(
                queryTerm.Length,
                candidateTerm.Length);

        return maximumLength == 0
            ? 1
            : 1 -
              ((double)distance /
               maximumLength);
    }

    /// <summary>
    /// Splits normalized text into alphanumeric search tokens.
    /// </summary>
    /// <param name="value">The normalized value.</param>
    /// <returns>The non-empty tokens.</returns>
    private static string[] Tokenize(
            string value)
    {
        global::System.Collections.Generic.List<string> tokens =
            new List<string>();
        StringBuilder current =
            new StringBuilder();

        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                current.Append(
                    character);
                continue;
            }

            if (current.Length >
                0)
            {
                tokens.Add(
                    current.ToString());
                current.Clear();
            }
        }

        if (current.Length >
            0)
        {
            tokens.Add(
                current.ToString());
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Computes Levenshtein edit distance with two rolling rows.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>The edit distance.</returns>
    private static int LevenshteinDistance(
            string left,
            string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        int[] previous =
            new int[right.Length + 1];
        int[] current =
            new int[right.Length + 1];

        for (int column = 0;
             column <= right.Length;
             column++)
        {
            previous[column] =
                column;
        }

        for (int row = 1;
             row <= left.Length;
             row++)
        {
            current[0] =
                row;

            for (int column = 1;
                 column <= right.Length;
                 column++)
            {
                int substitutionCost =
                    left[row - 1] ==
                    right[column - 1]
                        ? 0
                        : 1;

                current[column] =
                    Math.Min(
                        Math.Min(
                            current[column - 1] +
                            1,
                            previous[column] +
                            1),
                        previous[column - 1] +
                        substitutionCost);
            }

            int[] swap =
                previous;
            previous =
                current;
            current =
                swap;
        }

        return previous[right.Length];
    }
}
