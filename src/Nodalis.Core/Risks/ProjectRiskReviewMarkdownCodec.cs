using System.Globalization;
using System.Text.Json;

namespace Nodalis.Core.Risks;

/// <summary>
/// Append-only human risk-rating history within the canonical Markdown file.
/// Keeps the user's free-form text intact, verifies the frozen initial score
/// and refuses header/history discrepancies or unsupported legacy histories.
/// </summary>
public static class ProjectRiskReviewMarkdownCodec
{
    private const string StartMarker = "<!-- nodalis:risk-history:start -->";
    private const string EndMarker = "<!-- nodalis:risk-history:end -->";
    private const string EventPrefix = "<!-- nodalis:risk-review:";
    private const string EventSuffix = " -->";
    private const int MaxEvents = 256;

    /// <summary>
    /// Reads a v2 risk document, verifying that the frontmatter scores agree
    /// with exactly the chronological signed-off review events. Does not write.
    /// </summary>
    /// <param name="markdown">Complete canonical Markdown source.</param>
    /// <returns>Verified card and append-only assessment timeline.</returns>
    public static ProjectRiskReviewHistory Inspect(string markdown)
    {
        ProjectRiskCard card = ProjectRiskMarkdownCodec.Parse(markdown);
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string startLine = "\n" + StartMarker + "\n";
        int start = normalized.IndexOf(startLine, StringComparison.Ordinal);
        if (start < 0)
        {
            if (normalized.Contains("nodalis:risk-history:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-review:", StringComparison.Ordinal) ||
                ProjectRiskMatrix.Evaluate(card.Initial) !=
                    ProjectRiskSeverity.Unassessed ||
                ProjectRiskMatrix.Evaluate(card.Residual) !=
                    ProjectRiskSeverity.Unassessed)
            {
                throw new InvalidDataException(
                    "Historique de risque absent, fragmenté ou score non historisé.");
            }

            return new ProjectRiskReviewHistory(card, []);
        }

        string endLine = "\n" + EndMarker;
        int afterStart = start + startLine.Length;
        int end = normalized.IndexOf(endLine, afterStart, StringComparison.Ordinal);
        if (end < 0 ||
            normalized.IndexOf(startLine, afterStart, StringComparison.Ordinal) >= 0 ||
            normalized.IndexOf(endLine, end + endLine.Length,
                StringComparison.Ordinal) >= 0 ||
            !string.IsNullOrWhiteSpace(normalized[(end + endLine.Length)..]))
        {
            throw new InvalidDataException(
                "Bloc d'historique du risque incomplet ou répété.");
        }

        string segment = normalized[afterStart..end];
        string[] lines = segment.Split('\n');
        if (lines.Length == 0 || lines.Length > MaxEvents ||
            lines.Any(line => !line.StartsWith(EventPrefix,
                StringComparison.Ordinal) ||
                !line.EndsWith(EventSuffix, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "L'historique contient des éléments inattendus.");
        }

        List<ProjectRiskReviewEvent> events = [];
        HashSet<Guid> ids = [];
        ProjectRiskRating initial = new(null, null);
        ProjectRiskRating residual = new(null, null);
        DateTimeOffset lastTime = card.CreatedUtc;
        foreach (string line in lines)
        {
            if (line.Length > 12000)
            {
                throw new InvalidDataException("Événement de risque trop volumineux.");
            }

            ProjectRiskReviewEvent item;
            try
            {
                item = JsonSerializer.Deserialize<ProjectRiskReviewEvent>(
                    line.Substring(EventPrefix.Length,
                        line.Length - EventPrefix.Length - EventSuffix.Length)) ??
                    throw new InvalidDataException("Événement de risque vide.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Événement de risque malformé.", exception);
            }

            if (item.Id == Guid.Empty || !ids.Add(item.Id) ||
                item.RiskId != card.Id ||
                !Enum.IsDefined(item.Kind) ||
                item.Rating is null ||
                ProjectRiskMatrix.Evaluate(item.Rating) ==
                    ProjectRiskSeverity.Unassessed ||
                !IsHumanText(item.Actor, 120, 1) ||
                !IsHumanText(item.Rationale, 4000, 10, allowNewlines: true) ||
                item.RecordedUtc == default || item.RecordedUtc < lastTime)
            {
                throw new InvalidDataException(
                    "Avis de risque invalide, dupliqué ou hors chronologie.");
            }

            if (item.Kind == ProjectRiskReviewKind.Initial)
            {
                if (events.Count != 0)
                {
                    throw new InvalidDataException(
                        "L'évaluation initiale ne peut être remplacée.");
                }

                initial = item.Rating;
            }
            else if (events.Count == 0)
            {
                throw new InvalidDataException(
                    "Réévaluation résiduelle sans avis initial.");
            }
            else
            {
                residual = item.Rating;
            }

            lastTime = item.RecordedUtc;
            events.Add(item);
        }

        if (events.Count == 0 || card.Initial != initial ||
            card.Residual != residual)
        {
            throw new InvalidDataException(
                "Criticité et historique incohérents : qualification humaine requise.");
        }

        return new ProjectRiskReviewHistory(card, events);
    }

    /// <summary>
    /// Applies a single explicitly human-approved review in memory. Updates
    /// only relevant frontmatter numbers and appends a historical event.
    /// Unrelated Markdown bytes and previously recorded events are preserved.
    /// </summary>
    /// <param name="markdown">Exact current source snapshot.</param>
    /// <param name="kind">One-time initial or later residual rating.</param>
    /// <param name="rating">Full probability and impact from human review.</param>
    /// <param name="actor">Person approving the evaluation.</param>
    /// <param name="rationale">Justification of the qualified scores.</param>
    /// <returns>New canonical source with a complete, validated timeline.</returns>
    public static string Append(
        string markdown, ProjectRiskReviewKind kind, ProjectRiskRating rating,
        string actor, string rationale)
    {
        ProjectRiskReviewHistory old = Inspect(markdown);
        if (!Enum.IsDefined(kind) || rating is null ||
            ProjectRiskMatrix.Evaluate(rating) == ProjectRiskSeverity.Unassessed ||
            !IsHumanText(actor, 120, 1) ||
            !IsHumanText(rationale, 4000, 10, allowNewlines: true))
        {
            throw new InvalidDataException(
                "Probabilité, impact, auteur et justification humaine requis.");
        }

        if (kind == ProjectRiskReviewKind.Initial && old.Events.Count != 0 ||
            kind == ProjectRiskReviewKind.Residual && old.Events.Count == 0)
        {
            throw new InvalidOperationException(
                "Évaluation initiale déjà figée ou réévaluation prématurée.");
        }

        if (old.Events.Count >= MaxEvents)
        {
            throw new InvalidDataException(
                "Historique de risque saturé : révision du contrat requise.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (old.Events.Count > 0 &&
            now < old.Events[^1].RecordedUtc)
        {
            throw new InvalidDataException(
                "Horloge système antérieure à l'historique : correction requise.");
        }

        ProjectRiskReviewEvent item = new()
        {
            Id = Guid.NewGuid(),
            RiskId = old.Card.Id,
            Kind = kind,
            Rating = rating,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            RecordedUtc = now
        };

        string prefix = kind == ProjectRiskReviewKind.Initial
            ? "initial" : "residual";
        ProjectRiskRating previous = kind == ProjectRiskReviewKind.Initial
            ? old.Card.Initial : old.Card.Residual;
        string updated = ReplaceFrontmatterField(markdown,
            prefix + "-probability",
            RatingPart(previous.Probability),
            RatingPart(rating.Probability));
        updated = ReplaceFrontmatterField(updated, prefix + "-impact",
            RatingPart(previous.Impact),
            RatingPart(rating.Impact));

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string eventLine = EventPrefix + JsonSerializer.Serialize(item) +
            EventSuffix;
        if (old.Events.Count == 0)
        {
            // The first review claims the end of the file. Existing free-form
            // contents, including exact trailing whitespace, are untouched.
            updated += newline + StartMarker + newline + eventLine +
                newline + EndMarker + newline;
        }
        else
        {
            string close = newline + EndMarker;
            int end = updated.LastIndexOf(close, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidDataException(
                    "La fin de l'historique ne peut être localisée.");
            }

            updated = updated[..end] + newline + eventLine + updated[end..];
        }

        ProjectRiskReviewHistory verified = Inspect(updated);
        if (verified.Events.Count != old.Events.Count + 1 ||
            verified.Events[^1].Id != item.Id)
        {
            throw new InvalidDataException(
                "Relecture de l'historique de risque non conforme.");
        }

        return updated;
    }

    /// <summary>Checks bounded human explanations without line controls.</summary>
    /// <param name="value">Candidate human text.</param>
    /// <param name="max">Maximum length.</param>
    /// <param name="min">Minimum nonspace length.</param>
    /// <param name="allowNewlines">Whether the reason can include paragraphs.</param>
    /// <returns>Whether the human field is acceptable.</returns>
    private static bool IsHumanText(
        string? value, int max, int min, bool allowNewlines = false) =>
        value is not null &&
        value.Length <= max && value.Trim().Length >= min &&
        !value.Any(character => char.IsControl(character) &&
            (!allowNewlines || character is not ('\r' or '\n')));

    /// <summary>Formats unknown and numeric paired risk rating values.</summary>
    /// <param name="value">Null or scale entry.</param>
    /// <returns>Canonical YAML scalar.</returns>
    private static string RatingPart(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    /// <summary>Edits one validated frontmatter scalar, not the Markdown body.</summary>
    /// <param name="markdown">Original document.</param>
    /// <param name="key">Known rating property.</param>
    /// <param name="expected">Previously read scalar.</param>
    /// <param name="replacement">Human-reviewed new scalar.</param>
    /// <returns>Lossless source update outside the targeted scalar.</returns>
    private static string ReplaceFrontmatterField(
        string markdown, string key, string expected, string replacement)
    {
        int headerEnd = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        string anchor = "\n" + key + ": ";
        int start = markdown.IndexOf(anchor, StringComparison.Ordinal);
        if (headerEnd < 0 || start < 0 || start >= headerEnd ||
            markdown.IndexOf(anchor, start + 1, StringComparison.Ordinal) is
                int duplicate && duplicate >= 0 && duplicate < headerEnd)
        {
            throw new InvalidDataException(
                "Champ d'évaluation absent ou dupliqué.");
        }

        int valueStart = start + anchor.Length;
        int valueEnd = markdown.IndexOf('\n', valueStart);
        if (valueEnd < 0)
        {
            throw new InvalidDataException(
                "En-tête de risque tronqué.");
        }

        int contentEnd = valueEnd > 0 && markdown[valueEnd - 1] == '\r'
            ? valueEnd - 1 : valueEnd;
        if (!string.Equals(markdown[valueStart..contentEnd], expected,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Valeur évaluée modifiée depuis la relecture.");
        }

        return markdown[..valueStart] + replacement + markdown[contentEnd..];
    }
}
