using System.Text.Json;

namespace Nodalis.Core.Risks;

/// <summary>
/// Audited execution progress of treatment measures, without task-ID guesses.
/// The progress block precedes treatment/qualification/score blocks; its
/// protected head identifies the last event so even terminal deletion fails.
/// </summary>
public static class ProjectRiskMeasureMarkdownCodec
{
    private const string StartMarker = "<!-- nodalis:risk-measures:start -->";
    private const string EndMarker = "<!-- nodalis:risk-measures:end -->";
    private const string HeadPrefix = "<!-- nodalis:risk-measures-head:";
    private const string EventPrefix = "<!-- nodalis:risk-measure-event:";
    private const string Suffix = " -->";
    private const string TreatmentStart = "<!-- nodalis:risk-treatment:start -->";
    private const int MaxEvents = 256;

    /// <summary>
    /// Replays a strict measure history, verifying plan identity, previous
    /// states, event chain and proof for every human-completed slot.
    /// </summary>
    /// <param name="markdown">Existing source file.</param>
    /// <returns>Verified independent progress history.</returns>
    public static ProjectRiskMeasureHistory Inspect(string markdown)
    {
        ProjectRiskTreatmentHistory plans =
            ProjectRiskTreatmentMarkdownCodec.Inspect(markdown);
        ProjectRiskCard card = plans.Card;
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string startLine = "\n" + StartMarker + "\n";
        int start = normalized.IndexOf(startLine, StringComparison.Ordinal);
        if (start < 0)
        {
            if (normalized.Contains("nodalis:risk-measures:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-measures-head:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-measure-event:", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Journal des mesures incomplet ou fragmenté.");
            }

            return new ProjectRiskMeasureHistory(card, []);
        }

        string endLine = "\n" + EndMarker;
        int afterStart = start + startLine.Length;
        int end = normalized.IndexOf(endLine, afterStart, StringComparison.Ordinal);
        if (end < 0 ||
            normalized.IndexOf(startLine, afterStart, StringComparison.Ordinal) >= 0 ||
            normalized.IndexOf(endLine, end + endLine.Length,
                StringComparison.Ordinal) >= 0 ||
            normalized[..start].Contains("nodalis:risk-measure-event:",
                StringComparison.Ordinal) ||
            normalized[..start].Contains("nodalis:risk-measures-head:",
                StringComparison.Ordinal) ||
            !normalized[(end + endLine.Length)..].TrimStart().StartsWith(
                TreatmentStart + "\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Journal des mesures déplacé, dupliqué ou tronqué.");
        }

        string[] lines = normalized[afterStart..end].Split('\n');
        if (lines.Length < 2 || lines.Length > MaxEvents + 1 ||
            !lines[0].StartsWith(HeadPrefix, StringComparison.Ordinal) ||
            !lines[0].EndsWith(Suffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Tête de l'historique des mesures absente.");
        }

        Guid head;
        if (!Guid.TryParseExact(lines[0].Substring(HeadPrefix.Length,
            lines[0].Length - HeadPrefix.Length - Suffix.Length), "D",
            out head) || head == Guid.Empty)
        {
            throw new InvalidDataException(
                "Identifiant de dernière mesure invalide.");
        }

        List<ProjectRiskMeasureEvent> events = [];
        HashSet<Guid> ids = [];
        Dictionary<(Guid, ProjectRiskMeasureKind), ProjectRiskMeasureStatus>
            previousStates = [];
        Guid? previousId = null;
        DateTimeOffset previousTime = card.CreatedUtc;
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length > 14000 ||
                !line.StartsWith(EventPrefix, StringComparison.Ordinal) ||
                !line.EndsWith(Suffix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Événement d'exécution malformé.");
            }

            ProjectRiskMeasureEvent item;
            try
            {
                item = JsonSerializer.Deserialize<ProjectRiskMeasureEvent>(
                    line.Substring(EventPrefix.Length,
                        line.Length - EventPrefix.Length - Suffix.Length)) ??
                    throw new InvalidDataException(
                        "Événement d'exécution vide.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Événement d'exécution JSON invalide.", exception);
            }

            ProjectRiskTreatmentEvent? treatment =
                plans.Events.LastOrDefault(plan =>
                    plan.Id == item.TreatmentId);
            ProjectRiskTreatmentEvent? activeAtEvent =
                plans.Events.LastOrDefault(plan =>
                    plan.RecordedUtc <= item.RecordedUtc);
            (Guid, ProjectRiskMeasureKind) key = (item.TreatmentId, item.Kind);
            ProjectRiskMeasureStatus oldStatus =
                previousStates.TryGetValue(key, out ProjectRiskMeasureStatus state)
                    ? state : ProjectRiskMeasureStatus.Pending;
            if (item.Id == Guid.Empty || !ids.Add(item.Id) ||
                item.RiskId != card.Id ||
                item.PreviousId != previousId ||
                !Enum.IsDefined(item.Kind) ||
                !Enum.IsDefined(item.Status) ||
                !Enum.IsDefined(item.PreviousStatus) ||
                item.PreviousStatus != oldStatus ||
                item.Status == oldStatus ||
                treatment is null || activeAtEvent?.Id != item.TreatmentId ||
                !ValidText(item.Actor, 120, 1, false) ||
                !ValidText(item.Rationale, 4000, 10, true) ||
                item.Evidence is not null &&
                    !ValidText(item.Evidence, 2000, 1, true) ||
                item.Status == ProjectRiskMeasureStatus.Completed &&
                    !ValidText(item.Evidence, 2000, 10, true) ||
                item.RecordedUtc == default ||
                item.RecordedUtc < previousTime)
            {
                throw new InvalidDataException(
                    "Exécution incohérente, non attribuée ou sans preuve de réalisation.");
            }

            previousStates[key] = item.Status;
            previousId = item.Id;
            previousTime = item.RecordedUtc;
            events.Add(item);
        }

        if (events.Count == 0 || head != events[^1].Id)
        {
            throw new InvalidDataException(
                "Dernière mesure et tête du journal incohérentes.");
        }

        return new ProjectRiskMeasureHistory(card, events);
    }

    /// <summary>
    /// Records exactly one manually confirmed progress change on the current
    /// treatment version; a later treatment revision starts fresh Pending slots.
    /// </summary>
    /// <param name="markdown">Original verified risk source.</param>
    /// <param name="kind">Prevention, mitigation or contingency measure.</param>
    /// <param name="status">Explicit new progress status.</param>
    /// <param name="actor">Human responsible for this declaration.</param>
    /// <param name="rationale">Mandatory motive for the transition.</param>
    /// <param name="evidence">Human-entered evidence description, required when done.</param>
    /// <returns>Updated, independently verifiable Markdown.</returns>
    public static string Append(
        string markdown, ProjectRiskMeasureKind kind,
        ProjectRiskMeasureStatus status, string actor,
        string rationale, string? evidence)
    {
        ProjectRiskMeasureHistory old = Inspect(markdown);
        ProjectRiskQualificationMarkdownCodec.Inspect(markdown);
        ProjectRiskReviewMarkdownCodec.Inspect(markdown);
        ProjectRiskTreatmentHistory treatment =
            ProjectRiskTreatmentMarkdownCodec.Inspect(markdown);
        if (treatment.Events.Count == 0 ||
            !Enum.IsDefined(kind) || !Enum.IsDefined(status) ||
            !ValidText(actor, 120, 1, false) ||
            !ValidText(rationale, 4000, 10, true) ||
            evidence is not null && !ValidText(evidence, 2000, 1, true) ||
            status == ProjectRiskMeasureStatus.Completed &&
                !ValidText(evidence, 2000, 10, true))
        {
            throw new InvalidDataException(
                "Plan actuel, mesure, état, auteur, motif et éventuelle preuve requis.");
        }

        Guid treatmentId = treatment.Events[^1].Id;
        ProjectRiskMeasureStatus previousStatus = old.StatusFor(
            treatmentId, kind);
        if (status == previousStatus)
        {
            throw new InvalidOperationException(
                "La mesure est déjà dans cet état : aucune transition implicite.");
        }

        if (old.Events.Count >= MaxEvents)
        {
            throw new InvalidDataException(
                "Journal d'exécution saturé.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < treatment.Events[^1].RecordedUtc ||
            old.Events.Count > 0 && now < old.Events[^1].RecordedUtc)
        {
            throw new InvalidDataException(
                "Horloge antérieure au dernier avis de plan ou de mesure.");
        }

        ProjectRiskMeasureEvent item = new()
        {
            Id = Guid.NewGuid(),
            RiskId = old.Card.Id,
            TreatmentId = treatmentId,
            PreviousId = old.Events.Count == 0 ? null : old.Events[^1].Id,
            Kind = kind,
            PreviousStatus = previousStatus,
            Status = status,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            Evidence = string.IsNullOrWhiteSpace(evidence)
                ? null : evidence.Trim(),
            RecordedUtc = now
        };

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string eventLine = EventPrefix + JsonSerializer.Serialize(item) + Suffix;
        string updated;
        if (old.Events.Count == 0)
        {
            string block = newline + StartMarker + newline +
                HeadPrefix + item.Id.ToString("D") + Suffix + newline +
                eventLine + newline + EndMarker + newline;
            string anchor = newline + TreatmentStart + newline;
            int position = markdown.IndexOf(anchor, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidDataException(
                    "Plan de traitement requis pour le suivi d'exécution.");
            }

            updated = markdown[..position] + block + markdown[position..];
        }
        else
        {
            string head = HeadPrefix + old.Events[^1].Id.ToString("D") + Suffix;
            string replacement = HeadPrefix + item.Id.ToString("D") + Suffix;
            int pos = markdown.IndexOf(head, StringComparison.Ordinal);
            if (pos < 0 || markdown.IndexOf(head, pos + head.Length,
                StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException(
                    "La tête du journal d'exécution a été modifiée.");
            }

            updated = markdown[..pos] + replacement +
                markdown[(pos + head.Length)..];
            string close = newline + EndMarker;
            int end = updated.IndexOf(close, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidDataException(
                    "Fin du journal d'exécution absente.");
            }

            updated = updated[..end] + newline + eventLine + updated[end..];
        }

        ProjectRiskMeasureHistory verified = Inspect(updated);
        if (verified.Events.Count != old.Events.Count + 1 ||
            verified.Events[^1].Id != item.Id)
        {
            throw new InvalidDataException(
                "Nouvelle transition de mesure absente du journal vérifié.");
        }

        return updated;
    }

    /// <summary>Ensures untrusted human fields are bounded and non-control.</summary>
    /// <param name="value">Candidate human text.</param>
    /// <param name="max">Maximum number of characters.</param>
    /// <param name="min">Minimum nonwhitespace content.</param>
    /// <param name="multiline">Whether paragraphs are permitted.</param>
    /// <returns>Whether the field is acceptable.</returns>
    private static bool ValidText(
        string? value, int max, int min, bool multiline) =>
        value is not null && value.Length <= max &&
        value.Trim().Length >= min &&
        !value.Any(c => char.IsControl(c) &&
            (!multiline || c is not ('\r' or '\n')));
}
