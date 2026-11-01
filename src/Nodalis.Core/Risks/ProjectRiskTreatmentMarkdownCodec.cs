using System.Text.Json;

namespace Nodalis.Core.Risks;

/// <summary>
/// Stores human-approved treatment plans as an append-only JSON-comment
/// history in the canonical risk Markdown. This block always precedes the
/// qualification and rating journals, whose existing bytes stay intact.
/// </summary>
public static class ProjectRiskTreatmentMarkdownCodec
{
    private const string StartMarker = "<!-- nodalis:risk-treatment:start -->";
    private const string EndMarker = "<!-- nodalis:risk-treatment:end -->";
    private const string EventPrefix = "<!-- nodalis:risk-treatment-event:";
    private const string EventSuffix = " -->";
    private const string QualificationStart =
        "<!-- nodalis:risk-qualification:start -->";
    private const string RatingStart = "<!-- nodalis:risk-history:start -->";
    private const int MaxEvents = 256;

    /// <summary>
    /// Reads a plan without inventing strategy or measures from free text.
    /// Checks event identity, chronological order, revision chain and bounds.
    /// </summary>
    /// <param name="markdown">Exact Markdown document.</param>
    /// <returns>Validated treatment timeline, possibly empty.</returns>
    public static ProjectRiskTreatmentHistory Inspect(string markdown)
    {
        ProjectRiskCard card = ProjectRiskMarkdownCodec.Parse(markdown);
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string startLine = "\n" + StartMarker + "\n";
        int start = normalized.IndexOf(startLine, StringComparison.Ordinal);
        if (start < 0)
        {
            if (card.TreatmentHeadId is not null ||
                normalized.Contains("nodalis:risk-treatment:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-treatment-event:", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Journal de traitement incomplet ou détaché.");
            }

            return new ProjectRiskTreatmentHistory(card, []);
        }

        string endLine = "\n" + EndMarker;
        int afterStart = start + startLine.Length;
        int end = normalized.IndexOf(endLine, afterStart, StringComparison.Ordinal);
        if (end < 0 ||
            normalized.IndexOf(startLine, afterStart, StringComparison.Ordinal) >= 0 ||
            normalized.IndexOf(endLine, end + endLine.Length,
                StringComparison.Ordinal) >= 0 ||
            normalized[..start].Contains("nodalis:risk-treatment-event:",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Journal de traitement dupliqué, incomplet ou déplacé.");
        }

        string trailing = normalized[(end + endLine.Length)..].Trim();
        if (trailing.Length > 0 &&
            !trailing.StartsWith(QualificationStart + "\n",
                StringComparison.Ordinal) &&
            !trailing.StartsWith(RatingStart + "\n",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Le traitement doit précéder les journaux métier et de criticité.");
        }

        string[] lines = normalized[afterStart..end].Split('\n');
        if (lines.Length is < 1 or > MaxEvents ||
            lines.Any(line => !line.StartsWith(EventPrefix,
                StringComparison.Ordinal) ||
                !line.EndsWith(EventSuffix, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Événements de traitement absents ou malformés.");
        }

        List<ProjectRiskTreatmentEvent> events = [];
        HashSet<Guid> ids = [];
        DateTimeOffset previousTime = card.CreatedUtc;
        Guid? previousId = null;
        ProjectRiskTreatmentPlan? previousPlan = null;
        foreach (string line in lines)
        {
            if (line.Length > 15000)
            {
                throw new InvalidDataException(
                    "Événement de traitement trop volumineux.");
            }

            ProjectRiskTreatmentEvent item;
            try
            {
                item = JsonSerializer.Deserialize<ProjectRiskTreatmentEvent>(
                    line.Substring(EventPrefix.Length,
                        line.Length - EventPrefix.Length - EventSuffix.Length)) ??
                    throw new InvalidDataException("Événement de traitement vide.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Événement de traitement invalide.", exception);
            }

            if (item.Id == Guid.Empty || !ids.Add(item.Id) ||
                item.RiskId != card.Id || item.PreviousId != previousId ||
                item.Plan is null ||
                !IsValidPlan(item.Plan) ||
                item.Plan == previousPlan ||
                !ValidText(item.Actor, 120, 1, false) ||
                !ValidText(item.Rationale, 4000, 10, true) ||
                item.RecordedUtc == default || item.RecordedUtc < previousTime)
            {
                throw new InvalidDataException(
                    "Plan de traitement incomplet, répété ou hors chronologie.");
            }

            events.Add(item);
            previousId = item.Id;
            previousPlan = item.Plan;
            previousTime = item.RecordedUtc;
        }

        if (events.Count == 0 || card.TreatmentHeadId != events[^1].Id)
        {
            throw new InvalidDataException(
                "Tête de traitement et dernière révision incohérentes.");
        }

        return new ProjectRiskTreatmentHistory(card, events);
    }

    /// <summary>
    /// Creates or revises a plan in memory only after its human owner and
    /// measures have been explicitly supplied; preserves all older events.
    /// </summary>
    /// <param name="markdown">Existing exact canonical source.</param>
    /// <param name="plan">Proposed complete treatment plan.</param>
    /// <param name="actor">Human reviewer.</param>
    /// <param name="rationale">Motivation for the initial plan or revision.</param>
    /// <returns>Fully validated updated Markdown source.</returns>
    public static string Append(
        string markdown, ProjectRiskTreatmentPlan plan,
        string actor, string rationale)
    {
        ProjectRiskTreatmentHistory old = Inspect(markdown);
        ProjectRiskQualificationMarkdownCodec.Inspect(markdown);
        ProjectRiskReviewMarkdownCodec.Inspect(markdown);
        if (plan is null || !IsValidPlan(plan) ||
            plan == old.Current ||
            !ValidText(actor, 120, 1, false) ||
            !ValidText(rationale, 4000, 10, true))
        {
            throw new InvalidDataException(
                "Stratégie, mesures, responsable, auteur et motif requis.");
        }

        if (old.Events.Count >= MaxEvents)
        {
            throw new InvalidDataException(
                "Historique de traitement saturé.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (old.Events.Count > 0 && now < old.Events[^1].RecordedUtc)
        {
            throw new InvalidDataException(
                "Horloge système antérieure au dernier plan.");
        }

        ProjectRiskTreatmentEvent item = new()
        {
            Id = Guid.NewGuid(),
            RiskId = old.Card.Id,
            PreviousId = old.Events.Count == 0 ? null : old.Events[^1].Id,
            Plan = plan with
            {
                Owner = plan.Owner.Trim(),
                Prevention = plan.Prevention.Trim(),
                Mitigation = plan.Mitigation.Trim(),
                Contingency = plan.Contingency.Trim()
            },
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            RecordedUtc = now
        };

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string eventLine = EventPrefix + JsonSerializer.Serialize(item) +
            EventSuffix;
        string withHead = UpdateHead(
            markdown, old.Card.TreatmentHeadId, item.Id);
        string updated;
        if (old.Events.Count == 0)
        {
            string block = newline + StartMarker + newline + eventLine +
                newline + EndMarker + newline;
            string qualificationAnchor = newline + QualificationStart + newline;
            string ratingAnchor = newline + RatingStart + newline;
            int qualificationPosition = withHead.IndexOf(
                qualificationAnchor, StringComparison.Ordinal);
            int ratingPosition = withHead.IndexOf(
                ratingAnchor, StringComparison.Ordinal);
            int position = qualificationPosition >= 0
                ? qualificationPosition : ratingPosition;
            updated = position >= 0
                ? withHead[..position] + block + withHead[position..]
                : withHead + block;
        }
        else
        {
            string close = newline + EndMarker;
            int position = withHead.IndexOf(close, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidDataException(
                    "Fin du journal de traitement introuvable.");
            }

            updated = withHead[..position] + newline + eventLine +
                withHead[position..];
        }

        ProjectRiskTreatmentHistory verified = Inspect(updated);
        ProjectRiskQualificationMarkdownCodec.Inspect(updated);
        ProjectRiskReviewMarkdownCodec.Inspect(updated);
        if (verified.Events.Count != old.Events.Count + 1 ||
            verified.Events[^1].Id != item.Id)
        {
            throw new InvalidDataException(
                "Le nouveau plan ne figure pas dans le journal vérifié.");
        }

        return updated;
    }

    /// <summary>
    /// Updates the protected last-event head in the canonical frontmatter.
    /// Legacy 12-field files gain the optional field only on explicit action;
    /// new files already include its initial null marker.
    /// </summary>
    /// <param name="markdown">Existing unchanged source.</param>
    /// <param name="previous">Verified old treatment head.</param>
    /// <param name="next">New human-reviewed plan event GUID.</param>
    /// <returns>Markdown with one updated or added head scalar.</returns>
    private static string UpdateHead(
        string markdown, Guid? previous, Guid next)
    {
        const string anchor = "\n" + "treatment-head-id: ";
        int headerEnd = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            throw new InvalidDataException(
                "En-tête de risque introuvable pour la révision.");
        }

        int start = markdown.IndexOf(anchor, StringComparison.Ordinal);
        if (start >= 0 && start < headerEnd)
        {
            int again = markdown.IndexOf(
                anchor, start + anchor.Length, StringComparison.Ordinal);
            if (again >= 0 && again < headerEnd)
            {
                throw new InvalidDataException(
                    "Plusieurs identifiants de tête dans le risque.");
            }

            int valueStart = start + anchor.Length;
            int valueEnd = markdown.IndexOf('\n', valueStart);
            if (valueEnd < 0 || valueEnd > headerEnd)
            {
                throw new InvalidDataException(
                    "Tête de traitement tronquée.");
            }

            int contentEnd = valueEnd > valueStart &&
                markdown[valueEnd - 1] == '\r' ? valueEnd - 1 : valueEnd;
            string oldValue = previous?.ToString("D") ?? "null";
            if (markdown[valueStart..contentEnd] != oldValue)
            {
                throw new InvalidDataException(
                    "Tête de traitement modifiée depuis la lecture.");
            }

            return markdown[..valueStart] + next.ToString("D") +
                markdown[contentEnd..];
        }

        if (previous is not null)
        {
            throw new InvalidDataException(
                "Tête de traitement manquante malgré un historique.");
        }

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        int insertAt = headerEnd > 0 && markdown[headerEnd - 1] == '\r'
            ? headerEnd - 1 : headerEnd;
        return markdown[..insertAt] + newline +
            "treatment-head-id: " + next.ToString("D") +
            markdown[insertAt..];
    }

    /// <summary>Checks a complete plan without inferring any missing measure.</summary>
    /// <param name="plan">Human plan.</param>
    /// <returns>Whether all fields are bounded and present.</returns>
    public static bool IsValidPlan(ProjectRiskTreatmentPlan? plan) =>
        plan is not null && Enum.IsDefined(plan.Strategy) &&
        ValidText(plan.Owner, 120, 1, false) &&
        ValidText(plan.Prevention, 2000, 8, true) &&
        ValidText(plan.Mitigation, 2000, 8, true) &&
        ValidText(plan.Contingency, 2000, 8, true);

    /// <summary>Validates a human text without control-character injection.</summary>
    /// <param name="value">Text to validate.</param>
    /// <param name="max">Maximum length.</param>
    /// <param name="min">Minimum content length after trimming.</param>
    /// <param name="multiline">Whether newlines are permitted.</param>
    /// <returns>True when valid.</returns>
    private static bool ValidText(
        string? value, int max, int min, bool multiline) =>
        value is not null && value.Length <= max &&
        value.Trim().Length >= min &&
        !value.Any(character => char.IsControl(character) &&
            (!multiline || character is not ('\r' or '\n')));
}
