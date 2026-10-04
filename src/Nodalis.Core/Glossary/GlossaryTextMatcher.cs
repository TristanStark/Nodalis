namespace Nodalis.Core.Glossary;

public static class GlossaryTextMatcher
{
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

        var tokens = BuildEffectiveTokens(entries);
        var occupied = new bool[text.Length];
        var matches = new List<GlossaryTextMatch>();

        foreach (var pair in tokens
                     .OrderByDescending(pair => pair.Key.Length)
                     .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var token = pair.Key;
            var searchFrom = 0;

            while (searchFrom < text.Length)
            {
                var index = text.IndexOf(
                    token,
                    searchFrom,
                    StringComparison.CurrentCultureIgnoreCase);

                if (index < 0)
                {
                    break;
                }

                var end = index + token.Length;

                if (IsBoundaryMatch(
                        text,
                        index,
                        end) &&
                    IsRangeFree(
                        occupied,
                        index,
                        token.Length))
                {
                    for (var position = index;
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

    private static Dictionary<string, GlossaryEntry> BuildEffectiveTokens(
        IReadOnlyList<GlossaryEntry> entries)
    {
        var result = new Dictionary<string, GlossaryEntry>(
            StringComparer.CurrentCultureIgnoreCase);

        foreach (var entry in entries)
        {
            AddToken(
                result,
                entry.Term,
                entry);

            foreach (var acronym in entry.Acronyms)
            {
                AddToken(
                    result,
                    acronym,
                    entry);
            }

            foreach (var synonym in entry.Synonyms)
            {
                AddToken(
                    result,
                    synonym,
                    entry);
            }
        }

        return result;
    }

    private static void AddToken(
        IDictionary<string, GlossaryEntry> tokens,
        string value,
        GlossaryEntry entry)
    {
        var token = value.Trim();

        if (token.Length < 2 ||
            tokens.ContainsKey(token))
        {
            return;
        }

        tokens[token] = entry;
    }

    private static bool IsBoundaryMatch(
        string text,
        int start,
        int end)
    {
        var beforeIsWord =
            start > 0 &&
            IsWordCharacter(text[start - 1]);

        var afterIsWord =
            end < text.Length &&
            IsWordCharacter(text[end]);

        return !beforeIsWord &&
               !afterIsWord;
    }

    private static bool IsWordCharacter(char value) =>
        char.IsLetterOrDigit(value) ||
        value == '_' ||
        value == '\'' ||
        value == '’';

    private static bool IsRangeFree(
        IReadOnlyList<bool> occupied,
        int start,
        int length)
    {
        for (var index = start;
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
