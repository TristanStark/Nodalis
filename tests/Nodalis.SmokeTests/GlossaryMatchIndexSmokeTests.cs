using Nodalis.Core.Glossary;

/// <summary>
/// Exercises large synthetic sorted glossary match sets for viewport and hover
/// lookup, including both ends of intervals and offscreen matches.
/// </summary>
internal static class GlossaryMatchIndexSmokeTests
{
    /// <summary>Checks binary lookup agrees with direct interval scanning.</summary>
    public static void Run()
    {
        GlossaryScope scope = new()
        {
            Kind = GlossaryScopeKind.Project,
            DisplayName = "Projet fictif",
            DirectoryPath = "synthetic/project",
            FilePath = "synthetic/project/Glossaire.md"
        };
        GlossaryEntry entry = new()
        {
            Term = "Mot fictif",
            Definition = "Définition fictive",
            Scope = scope
        };
        GlossaryTextMatch[] matches = Enumerable.Range(0, 12000)
            .Select(index => new GlossaryTextMatch
            {
                Start = index * 16,
                Length = 5,
                MatchedText = "terme",
                Entry = entry
            }).ToArray();

        (int Start, int EndExclusive) first =
            GlossaryMatchIndex.GetVisibleRange(matches, 32, 65);
        Assert(first.Start == 2 && first.EndExclusive == 5,
            "The viewport must include matches crossing either boundary.");

        (int Start, int EndExclusive) gap =
            GlossaryMatchIndex.GetVisibleRange(matches, 37, 48);
        Assert(gap.Start == 3 && gap.EndExclusive == 3,
            "The gap between terms must not request drawing.");

        (int Start, int EndExclusive) spanning =
            GlossaryMatchIndex.GetVisibleRange(matches, 34, 35);
        Assert(spanning.Start == 2 && spanning.EndExclusive == 3,
            "A match spanning the complete viewport must remain visible.");

        (int Start, int EndExclusive) beyond =
            GlossaryMatchIndex.GetVisibleRange(matches, 192000, 193000);
        Assert(beyond.Start == matches.Length &&
               beyond.EndExclusive == matches.Length,
            "Scrolling after the final match must draw nothing.");

        Assert(GlossaryMatchIndex.GetVisibleRange(matches, 50, 50) == (0, 0) &&
               GlossaryMatchIndex.GetVisibleRange([], 0, 10) == (0, 0),
            "Empty intervals must not read text layout.");

        for (int offset = -1; offset <= 192000; offset += 31)
        {
            GlossaryTextMatch? expected = matches.FirstOrDefault(match =>
                match.Start <= offset && (long)match.Start + match.Length > offset);
            GlossaryTextMatch? actual = GlossaryMatchIndex.FindAt(matches, offset);
            Assert(ReferenceEquals(expected, actual),
                "Binary hover lookup must agree with a direct match scan.");
        }

        Assert(GlossaryMatchIndex.FindAt(matches, 32) == matches[2] &&
               GlossaryMatchIndex.FindAt(matches, 36) == matches[2] &&
               GlossaryMatchIndex.FindAt(matches, 37) is null &&
               GlossaryMatchIndex.FindAt(matches, 47) is null &&
               GlossaryMatchIndex.FindAt(matches, 48) == matches[3],
            "The first and last character boundaries are inclusive/exclusive.");

        for (int offset = 0; offset < 192000; offset += 181)
        {
            int end = offset + 240;
            (int Start, int EndExclusive) range =
                GlossaryMatchIndex.GetVisibleRange(matches, offset, end);
            int[] expected = Enumerable.Range(0, matches.Length)
                .Where(index => matches[index].Start < end &&
                    (long)matches[index].Start + matches[index].Length > offset)
                .ToArray();
            Assert(range.EndExclusive - range.Start == expected.Length &&
                   (expected.Length == 0 || expected[0] == range.Start),
                "A clipped viewport must agree with direct intersection checks.");
        }
    }

    /// <summary>Reports a fixed, synthetic failure without workspace data.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Synthetic failure description.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
