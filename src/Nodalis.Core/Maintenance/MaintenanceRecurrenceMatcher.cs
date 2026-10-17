using System.Globalization;
using System.Text;

namespace Nodalis.Core.Maintenance;

/// <summary>One explainable hypothesis; matching never establishes incident identity.</summary>
public sealed record MaintenanceRecurrenceSuggestion
{
    public required MaintenanceHistoryItem First { get; init; }
    public required MaintenanceHistoryItem Second { get; init; }
    public required int Score { get; init; }
    public required string Explanation { get; init; }
    /// <summary>Stable pair reference independent of history sorting or dates.</summary>
    public string PairKey => First.SourcePath + "|" + First.Id + "|" + Second.Id;
    /// <summary>Compact label for list views.</summary>
    public string ListLabel => $"{First.Summary} ↔ {Second.Summary}";
    /// <summary>Visible confidence with no assertion of identity.</summary>
    public string ScoreLabel => $"{Score}/100 · hypothèse à vérifier";
}

/// <summary>
/// Conservative offline matching based only on explicit reference, symptoms, tags
/// and meaningful title tokens. No LLM, network, mutable state or auto-merging.
/// </summary>
public static class MaintenanceRecurrenceMatcher
{
    /// <summary>Minimum score for presenting a suggestion, not a proof.</summary>
    public const int MinimumScore = 70;

    private static readonly HashSet<string> StopWords = new HashSet<string>(
        [
            "avec", "sans", "dans", "pour", "chez", "par", "sur", "sous", "aux",
            "des", "les", "une", "un", "est", "ete", "etre", "plus", "pas",
            "que", "qui", "quoi", "dont", "leur", "elle", "nous", "vous",
            "service", "probleme", "incident", "erreur", "echec", "panne",
            "test", "maintenance", "application", "projet", "systeme"
        ], StringComparer.Ordinal);

    private sealed record NormalizedIncident(
        MaintenanceHistoryItem Item,
        string Reference,
        HashSet<string> Symptoms,
        HashSet<string> Keywords,
        HashSet<string> Tags);

    /// <summary>
    /// Rebuilds suggestions from the current Markdown-derived projection.
    /// Ignored or explicitly linked pairs are not proposed again.
    /// </summary>
    /// <param name="items">Maintenance history in the chosen context.</param>
    /// <returns>Stable, score-ranked suggestions from the same maintenance sheet.</returns>
    public static IReadOnlyList<MaintenanceRecurrenceSuggestion> Find(
        IEnumerable<MaintenanceHistoryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        List<MaintenanceRecurrenceSuggestion> result = [];
        IEnumerable<IGrouping<string, MaintenanceHistoryItem>> groups = items
            .Where(item => item.Kind == MaintenanceRecordKind.Incident &&
                !item.IsIncomplete && ValidId(item.Id))
            .GroupBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, MaintenanceHistoryItem> group in groups)
        {
            NormalizedIncident[] incidents = group
                .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(item => new NormalizedIncident(
                    item,
                    Normalize(item.Reference),
                    Tokens(item.Symptoms),
                    Tokens(item.Summary),
                    Tags(item.Tags)))
                .ToArray();

            for (int first = 0; first < incidents.Length; first++)
            {
                for (int second = first + 1; second < incidents.Length; second++)
                {
                    NormalizedIncident a = incidents[first];
                    NormalizedIncident b = incidents[second];
                    if (Reviewed(a.Item, b.Item))
                    {
                        continue;
                    }

                    MaintenanceRecurrenceSuggestion? candidate = Compare(a, b);
                    if (candidate is not null)
                    {
                        result.Add(candidate);
                    }
                }
            }
        }

        return result
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.First.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.First.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Second.Id, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToArray();
    }

    /// <summary>Scores only independent concrete signals, disallowing text-only matches.</summary>
    /// <param name="a">First normalized incident.</param>
    /// <param name="b">Second normalized incident.</param>
    /// <returns>Proposed pair or null when evidence is insufficient.</returns>
    private static MaintenanceRecurrenceSuggestion? Compare(
        NormalizedIncident a, NormalizedIncident b)
    {
        List<string> reasons = [];
        int score = 0;
        int signals = 0;
        bool reference = a.Reference.Length >= 3 &&
            string.Equals(a.Reference, b.Reference, StringComparison.Ordinal);
        if (reference)
        {
            score += 50;
            signals++;
            reasons.Add("même référence explicite");
        }

        int symptomOverlap = a.Symptoms.Intersect(b.Symptoms).Count();
        double symptoms = Similarity(a.Symptoms, b.Symptoms);
        bool strongSymptoms = symptoms >= 0.82 && symptomOverlap >= 3;
        if (strongSymptoms)
        {
            score += 75;
            signals++;
            reasons.Add($"symptômes très proches ({symptoms:P0})");
        }
        else if (symptoms >= 0.65 && symptomOverlap >= 2)
        {
            score += 55;
            signals++;
            reasons.Add($"symptômes similaires ({symptoms:P0})");
        }

        int commonTags = a.Tags.Intersect(b.Tags).Count();
        if (commonTags >= 2)
        {
            score += 25;
            signals++;
            reasons.Add($"{commonTags} tags communs");
        }
        else if (commonTags == 1)
        {
            score += 10;
            signals++;
            reasons.Add("1 tag commun");
        }

        int keywordsOverlap = a.Keywords.Intersect(b.Keywords).Count();
        double keywords = Similarity(a.Keywords, b.Keywords);
        if (keywords >= 0.6 && keywordsOverlap >= 3 && commonTags >= 2)
        {
            score += 45;
            signals++;
            reasons.Add($"mots clés concordants ({keywords:P0})");
        }
        else if (keywords >= 0.5 && keywordsOverlap >= 2)
        {
            score += 30;
            signals++;
            reasons.Add($"mots clés proches ({keywords:P0})");
        }
        else if (keywords >= 0.35 && keywordsOverlap >= 2)
        {
            score += 15;
            signals++;
            reasons.Add($"quelques mots clés communs ({keywords:P0})");
        }

        // A shared generic phrase cannot trigger a match by itself.
        if (score < MinimumScore || (!strongSymptoms && signals < 2))
        {
            return null;
        }

        return new MaintenanceRecurrenceSuggestion
        {
            First = a.Item,
            Second = b.Item,
            Score = Math.Min(100, score),
            Explanation = string.Join(" · ", reasons)
        };
    }

    /// <summary>Checks whether a human already linked or dismissed this exact pair.</summary>
    /// <param name="a">First incident.</param>
    /// <param name="b">Second incident.</param>
    /// <returns>True for a prior explicit human decision.</returns>
    private static bool Reviewed(MaintenanceHistoryItem a, MaintenanceHistoryItem b)
    {
        return ContainsId(a.LinkedRecurrences, b.Id) ||
            ContainsId(a.IgnoredRecurrences, b.Id) ||
            ContainsId(b.LinkedRecurrences, a.Id) ||
            ContainsId(b.IgnoredRecurrences, a.Id);
    }

    /// <summary>Safely compares full token values in a comma-separated reviewed list.</summary>
    /// <param name="list">Saved incident IDs.</param>
    /// <param name="id">Requested ID.</param>
    /// <returns>Whether the exact ID was saved.</returns>
    private static bool ContainsId(string list, string? id)
    {
        return id is not null && list.Split(
                ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Checks for contract-format incident identifiers.</summary>
    /// <param name="value">Potential incident ID.</param>
    /// <returns>Whether the identifier can be linked safely.</returns>
    private static bool ValidId(string? value)
    {
        return value is not null &&
            value.StartsWith("INC-", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParseExact(value[4..], "N", out Guid _);
    }

    /// <summary>Normalizes case and diacritics deterministically using invariant culture.</summary>
    /// <param name="value">Raw text.</param>
    /// <returns>Lowercase alphanumeric and single space separators.</returns>
    private static string Normalize(string value)
    {
        string decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder output = new StringBuilder(decomposed.Length);
        bool space = false;
        foreach (char symbol in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(symbol) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(symbol))
            {
                output.Append(symbol);
                space = false;
            }
            else if (!space && output.Length > 0)
            {
                output.Append(' ');
                space = true;
            }
        }

        return output.ToString().Trim();
    }

    /// <summary>Removes short and generic words to avoid spurious title matches.</summary>
    /// <param name="value">Raw symptoms or summary.</param>
    /// <returns>Distinct significant tokens.</returns>
    private static HashSet<string> Tokens(string value)
    {
        return Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Parses tags as full values rather than matching random substrings.</summary>
    /// <param name="value">Comma-separated tags.</param>
    /// <returns>Normalized nonempty tags.</returns>
    private static HashSet<string> Tags(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(tag => Normalize(tag.TrimStart('#')))
            .Where(tag => tag.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Jaccard similarity excluding empty sets (empty never means identical).</summary>
    /// <param name="a">First token set.</param>
    /// <param name="b">Second token set.</param>
    /// <returns>A number from zero to one.</returns>
    private static double Similarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        int intersection = a.Intersect(b).Count();
        return intersection == 0 ? 0 :
            (double)intersection / (a.Count + b.Count - intersection);
    }
}
