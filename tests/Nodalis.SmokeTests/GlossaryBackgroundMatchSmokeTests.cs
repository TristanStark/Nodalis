using Nodalis.Core.Glossary;

/// <summary>
/// Exercises large, fully synthetic glossary matching on a worker. The WPF
/// activation guard is checked separately by the XAML/accessibility CI job.
/// </summary>
internal static class GlossaryBackgroundMatchSmokeTests
{
    /// <summary>Checks equivalence of direct and worker matching and cancellation.</summary>
    /// <returns>Background equivalence/cancellation assertions.</returns>
    public static async Task RunAsync()
    {
        GlossaryScope scope = new()
        {
            Kind = GlossaryScopeKind.Project,
            DisplayName = "Projet fictif",
            DirectoryPath = "synthetic/project",
            FilePath = "synthetic/project/Glossaire.md"
        };
        GlossaryEntry[] terms =
        [
            new()
            {
                Term = "Architecture technique",
                Definition = "Expression longue",
                Scope = scope
            },
            new()
            {
                Term = "Architecture",
                Definition = "Expression courte",
                Scope = scope
            },
            new()
            {
                Term = "Recette",
                Definition = "Vérification",
                Synonyms = ["Validation"],
                Scope = scope
            }
        ];

        string markdown = string.Concat(Enumerable.Repeat(
            "# Architecture technique\nLa validation de la recette.\n" +
            "Architectures et recetter ne sont pas des mots complets.\n",
            420));
        IReadOnlyList<GlossaryTextMatch> direct =
            GlossaryTextMatcher.Match(markdown, terms);
        IReadOnlyList<GlossaryTextMatch> worker =
            await Task.Run(() => GlossaryTextMatcher.Match(markdown, terms));
        Assert(direct.Count > 0 &&
            direct.Count == worker.Count &&
            direct.Zip(worker).All(pair =>
                pair.First.Start == pair.Second.Start &&
                pair.First.Length == pair.Second.Length &&
                pair.First.MatchedText == pair.Second.MatchedText &&
                pair.First.Entry.Term == pair.Second.Entry.Term),
            "Moving glossary matching to a worker must preserve all offsets, " +
            "longest-overlap precedence and term identity.");
        Assert(worker.All(match =>
                match.Start >= 0 &&
                match.Start + match.Length <= markdown.Length &&
                markdown.Substring(match.Start, match.Length) ==
                    match.MatchedText),
            "Worker matching cannot alter editor text or annotated offsets.");

        string overlaps = "Architecture technique. Architecture validation. " +
            "Architectures et validations ne sont pas des mots complets.";
        IReadOnlyList<GlossaryTextMatch> overlapMatches =
            GlossaryTextMatcher.Match(overlaps, terms);
        Assert(overlapMatches.Count == 3 &&
               overlapMatches[0].MatchedText == "Architecture technique" &&
               overlapMatches[1].MatchedText == "Architecture" &&
               overlapMatches[2].MatchedText == "validation" &&
               overlapMatches.Select(match => match.Start).Distinct().Count() == 3,
            "Bit-packed occupancy must retain longest-token precedence, " +
            "word boundaries and nonoverlapping offsets.");

        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        bool canceledCorrectly = false;
        try
        {
            await Task.Run(() => GlossaryTextMatcher.Match(
                markdown, terms, canceled.Token));
        }
        catch (OperationCanceledException)
        {
            canceledCorrectly = true;
        }

        Assert(canceledCorrectly,
            "Canceled glossary matching must not publish stale annotations.");
    }

    /// <summary>Fails a regression with a fixed, privacy-safe diagnostic.</summary>
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
