namespace Nodalis.Core.Glossary;

public static class GlossaryTextMatcher
{
    /// <summary>
    /// Performs the <c>Match</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="entries">The <c>entries</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static IReadOnlyList<GlossaryTextMatch> Match(
            string text,
            IReadOnlyList<GlossaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(entries);

        if (text.Length == 0 ||
            entries.Count == 0)
        {
            return [];
        }

        global::System.Collections.Generic.Dictionary<string, global::Nodalis.Core.Glossary.GlossaryEntry> tokens = BuildEffectiveTokens(entries);
        bool[] occupied = new bool[text.Length];
        global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryTextMatch> matches = new List<GlossaryTextMatch>();

        foreach (global::System.Collections.Generic.KeyValuePair<string, global::Nodalis.Core.Glossary.GlossaryEntry> pair in tokens
                     .OrderByDescending(pair => pair.Key.Length)
                     .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            string token = pair.Key;
            int searchFrom = 0;

            while (searchFrom < text.Length)
            {
                int index = text.IndexOf(
                    token,
                    searchFrom,
                    StringComparison.CurrentCultureIgnoreCase);

                if (index < 0)
                {
                    break;
                }

                int end = index + token.Length;

                if (IsBoundaryMatch(
                        text,
                        index,
                        end) &&
                    IsRangeFree(
                        occupied,
                        index,
                        token.Length))
                {
                    for (int position = index;
                         position < end;
                         position++)
                    {
                        occupied[position] = true;
                    }

                    matches.Add(new GlossaryTextMatch
                    {
                        Start = index,
                        Length = token.Length,
                        MatchedText = text.Substring(
                            index,
                            token.Length),
                        Entry = pair.Value
                    });
                }

                searchFrom = index + Math.Max(1, token.Length);
            }
        }

        return matches
            .OrderBy(match => match.Start)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>BuildEffectiveTokens</c> operation.
    /// </summary>
    /// <param name="entries">The <c>entries</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Dictionary<string, GlossaryEntry> BuildEffectiveTokens(
            IReadOnlyList<GlossaryEntry> entries)
    {
        global::System.Collections.Generic.Dictionary<string, global::Nodalis.Core.Glossary.GlossaryEntry> result = new Dictionary<string, GlossaryEntry>(
            StringComparer.CurrentCultureIgnoreCase);

        foreach (global::Nodalis.Core.Glossary.GlossaryEntry entry in entries)
        {
            AddToken(
                result,
                entry.Term,
                entry);

            foreach (string acronym in entry.Acronyms)
            {
                AddToken(
                    result,
                    acronym,
                    entry);
            }

            foreach (string synonym in entry.Synonyms)
            {
                AddToken(
                    result,
                    synonym,
                    entry);
            }
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>AddToken</c> operation.
    /// </summary>
    /// <param name="tokens">The <c>tokens</c> value.</param>
    /// <param name="value">The <c>value</c> value.</param>
    /// <param name="entry">The <c>entry</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static void AddToken(
            IDictionary<string, GlossaryEntry> tokens,
            string value,
            GlossaryEntry entry)
    {
        string token = value.Trim();

        if (token.Length < 2 ||
            tokens.ContainsKey(token))
        {
            return;
        }

        tokens[token] = entry;
    }

    /// <summary>
    /// Performs the <c>IsBoundaryMatch</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="start">The <c>start</c> value.</param>
    /// <param name="end">The <c>end</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsBoundaryMatch(
            string text,
            int start,
            int end)
    {
        bool beforeIsWord =
            start > 0 &&
            IsWordCharacter(text[start - 1]);

        bool afterIsWord =
            end < text.Length &&
            IsWordCharacter(text[end]);

        return !beforeIsWord &&
               !afterIsWord;
    }

    /// <summary>
    /// Performs the <c>IsWordCharacter</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsWordCharacter(char value) =>
            char.IsLetterOrDigit(value) ||
            value == '_' ||
            value == '\'' ||
            value == '’';

    /// <summary>
    /// Performs the <c>IsRangeFree</c> operation.
    /// </summary>
    /// <param name="occupied">The <c>occupied</c> value.</param>
    /// <param name="start">The <c>start</c> value.</param>
    /// <param name="length">The <c>length</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsRangeFree(
            IReadOnlyList<bool> occupied,
            int start,
            int length)
    {
        for (int index = start;
             index < start + length;
             index++)
        {
            if (occupied[index])
            {
                return false;
            }
        }

        return true;
    }
}
