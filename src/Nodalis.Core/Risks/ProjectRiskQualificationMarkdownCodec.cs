using System.Text.Json;

namespace Nodalis.Core.Risks;

/// <summary>
/// Validates and appends human business-qualification decisions inside the
/// canonical Markdown, without changing scores, lifecycle, or free-form notes.
/// A separate block precedes the optional risk-score history, which stays last.
/// </summary>
public static class ProjectRiskQualificationMarkdownCodec
{
    private const string StartMarker = "<!-- nodalis:risk-qualification:start -->";
    private const string EndMarker = "<!-- nodalis:risk-qualification:end -->";
    private const string EventPrefix = "<!-- nodalis:risk-qualification-event:";
    private const string EventSuffix = " -->";
    private const string RatingStartMarker = "<!-- nodalis:risk-history:start -->";
    private const int MaxEvents = 256;

    /// <summary>
    /// Verifies the complete chronological human decision history and the
    /// current frontmatter state. No source changes or inferred validations.
    /// </summary>
    /// <param name="markdown">Canonical Markdown source.</param>
    /// <returns>Qualification state and verified history.</returns>
    public static ProjectRiskQualificationHistory Inspect(string markdown)
    {
        ProjectRiskCard card = ProjectRiskMarkdownCodec.Parse(markdown);
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string startLine = "\n" + StartMarker + "\n";
        int start = normalized.IndexOf(startLine, StringComparison.Ordinal);
        if (start < 0)
        {
            if (card.Qualification != ProjectRiskQualification.ToReview ||
                normalized.Contains("nodalis:risk-qualification:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-qualification-event:", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Qualification métier présente sans décision humaine valide.");
            }

            return new ProjectRiskQualificationHistory(card, []);
        }

        string endLine = "\n" + EndMarker;
        int afterStart = start + startLine.Length;
        int end = normalized.IndexOf(endLine, afterStart, StringComparison.Ordinal);
        if (end < 0 ||
            normalized.IndexOf(startLine, afterStart, StringComparison.Ordinal) >= 0 ||
            normalized.IndexOf(endLine, end + endLine.Length,
                StringComparison.Ordinal) >= 0)
        {
            throw new InvalidDataException(
                "Historique de qualification incomplet ou dupliqué.");
        }

        // The qualification block is append-only and always precedes the
        // rating block. No user text can be silently put after an audit block.
        string remaining = normalized[(end + endLine.Length)..].Trim();
        if (remaining.Length != 0 &&
            !remaining.StartsWith(RatingStartMarker + "\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Texte inattendu après le journal de qualification.");
        }

        string[] lines = normalized[afterStart..end].Split('\n');
        if (lines.Length is < 1 or > MaxEvents ||
            lines.Any(line => !line.StartsWith(EventPrefix,
                StringComparison.Ordinal) ||
                !line.EndsWith(EventSuffix, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Décisions de qualification manquantes ou malformées.");
        }

        List<ProjectRiskQualificationEvent> events = [];
        HashSet<Guid> ids = [];
        ProjectRiskQualification previous = ProjectRiskQualification.ToReview;
        DateTimeOffset lastTime = card.CreatedUtc;
        foreach (string line in lines)
        {
            if (line.Length > 12000)
            {
                throw new InvalidDataException(
                    "Événement de qualification trop volumineux.");
            }

            ProjectRiskQualificationEvent item;
            try
            {
                item = JsonSerializer.Deserialize<ProjectRiskQualificationEvent>(
                    line.Substring(EventPrefix.Length,
                        line.Length - EventPrefix.Length - EventSuffix.Length)) ??
                    throw new InvalidDataException("Avis de qualification vide.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Avis de qualification non déchiffrable.", exception);
            }

            if (item.Id == Guid.Empty || !ids.Add(item.Id) ||
                item.RiskId != card.Id ||
                !Enum.IsDefined(item.Previous) ||
                !Enum.IsDefined(item.Status) ||
                item.Previous != previous || item.Status == previous ||
                !IsHumanText(item.Actor, 120, 1, false) ||
                !IsHumanText(item.Rationale, 4000, 10, true) ||
                item.RecordedUtc == default || item.RecordedUtc < lastTime)
            {
                throw new InvalidDataException(
                    "Transition métier invalide, sans auteur, ou hors chronologie.");
            }

            events.Add(item);
            previous = item.Status;
            lastTime = item.RecordedUtc;
        }

        if (card.Qualification != previous)
        {
            throw new InvalidDataException(
                "État métier et décisions historisées incohérents.");
        }

        return new ProjectRiskQualificationHistory(card, events);
    }

    /// <summary>
    /// Adds one explicitly human-confirmed status transition in memory.
    /// Initial/residual ratings, their history, and free Markdown stay intact.
    /// Re-qualification back to ToReview is allowed only as a new human event.
    /// </summary>
    /// <param name="markdown">Exact canonical source snapshot.</param>
    /// <param name="status">New business state chosen by the reviewer.</param>
    /// <param name="actor">Human decision maker.</param>
    /// <param name="rationale">Recorded decision justification.</param>
    /// <returns>Updated, fully revalidated Markdown.</returns>
    public static string Append(
        string markdown, ProjectRiskQualification status,
        string actor, string rationale)
    {
        ProjectRiskQualificationHistory old = Inspect(markdown);
        // A business decision never ignores an invalid rating history.
        ProjectRiskReviewMarkdownCodec.Inspect(markdown);
        if (!Enum.IsDefined(status) || status == old.Card.Qualification ||
            !IsHumanText(actor, 120, 1, false) ||
            !IsHumanText(rationale, 4000, 10, true))
        {
            throw new InvalidDataException(
                "Nouvel état, décideur et justification motivée requis.");
        }

        if (old.Events.Count >= MaxEvents)
        {
            throw new InvalidDataException(
                "Journal de qualification saturé.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (old.Events.Count > 0 && now < old.Events[^1].RecordedUtc)
        {
            throw new InvalidDataException(
                "Horloge antérieure à la dernière décision métier.");
        }

        ProjectRiskQualificationEvent decision = new()
        {
            Id = Guid.NewGuid(),
            RiskId = old.Card.Id,
            Previous = old.Card.Qualification,
            Status = status,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            RecordedUtc = now
        };

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string updated = ReplaceQualification(
            markdown, old.Card.Qualification, status);
        string eventLine = EventPrefix + JsonSerializer.Serialize(decision) +
            EventSuffix;
        if (old.Events.Count == 0)
        {
            string block = newline + StartMarker + newline + eventLine +
                newline + EndMarker + newline;
            string ratingStart = newline + RatingStartMarker + newline;
            int ratingPosition = updated.IndexOf(
                ratingStart, StringComparison.Ordinal);
            updated = ratingPosition >= 0
                ? updated[..ratingPosition] + block + updated[ratingPosition..]
                : updated + block;
        }
        else
        {
            string close = newline + EndMarker;
            int position = updated.IndexOf(close, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidDataException(
                    "Journal de qualification absent au moment de l'écriture.");
            }

            updated = updated[..position] + newline + eventLine +
                updated[position..];
        }

        ProjectRiskQualificationHistory verified = Inspect(updated);
        ProjectRiskReviewMarkdownCodec.Inspect(updated);
        if (verified.Events.Count != old.Events.Count + 1 ||
            verified.Events[^1].Id != decision.Id)
        {
            throw new InvalidDataException(
                "Transition de qualification non retrouvée après écriture.");
        }

        return updated;
    }

    /// <summary>Only one canonical YAML scalar can be changed by a decision.</summary>
    /// <param name="markdown">Original source.</param>
    /// <param name="previous">State currently validated by event replay.</param>
    /// <param name="next">Proposed new state.</param>
    /// <returns>Markdown with only the qualification scalar replaced.</returns>
    private static string ReplaceQualification(
        string markdown, ProjectRiskQualification previous,
        ProjectRiskQualification next)
    {
        const string anchor = "\nqualification: ";
        int start = markdown.IndexOf(anchor, StringComparison.Ordinal);
        int headerEnd = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (start < 0 || headerEnd < 0 || start >= headerEnd)
        {
            throw new InvalidDataException("Champ qualification absent.");
        }

        int other = markdown.IndexOf(anchor, start + anchor.Length,
            StringComparison.Ordinal);
        if (other >= 0 && other < headerEnd)
        {
            throw new InvalidDataException(
                "Qualification dupliquée dans l'en-tête.");
        }

        int valueStart = start + anchor.Length;
        int valueEnd = markdown.IndexOf('\n', valueStart);
        if (valueEnd < 0 || valueEnd > headerEnd)
        {
            throw new InvalidDataException("En-tête de qualification tronqué.");
        }

        int scalarEnd = valueEnd > valueStart &&
            markdown[valueEnd - 1] == '\r' ? valueEnd - 1 : valueEnd;
        if (!string.Equals(markdown[valueStart..scalarEnd],
            previous.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "La qualification a changé depuis la dernière lecture.");
        }

        return markdown[..valueStart] + next + markdown[scalarEnd..];
    }

    /// <summary>Validates the human decision context and reason.</summary>
    /// <param name="value">Text entered by the user.</param>
    /// <param name="max">Maximum length.</param>
    /// <param name="min">Minimum trimmed length.</param>
    /// <param name="allowNewlines">Whether paragraphs are permitted.</param>
    /// <returns>Whether the text can be persisted.</returns>
    private static bool IsHumanText(
        string? value, int max, int min, bool allowNewlines) =>
        value is not null && value.Length <= max &&
        value.Trim().Length >= min &&
        !value.Any(character => char.IsControl(character) &&
            (!allowNewlines || character is not ('\r' or '\n')));
}
