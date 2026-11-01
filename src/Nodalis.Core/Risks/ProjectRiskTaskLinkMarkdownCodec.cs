using System.Text.Json;

namespace Nodalis.Core.Risks;

/// <summary>
/// Preserves explicit, reversible task relations in the canonical risk file.
/// The link journal must precede measure, treatment, qualification and rating
/// histories; all other audit block bytes remain untouched.
/// </summary>
public static class ProjectRiskTaskLinkMarkdownCodec
{
    private const string Start = "<!-- nodalis:risk-task-links:start -->";
    private const string End = "<!-- nodalis:risk-task-links:end -->";
    private const string HeadPrefix = "<!-- nodalis:risk-task-links-head:";
    private const string EventPrefix = "<!-- nodalis:risk-task-link-event:";
    private const string Suffix = " -->";
    private const string MeasureStart = "<!-- nodalis:risk-measures:start -->";
    private const string TreatmentStart = "<!-- nodalis:risk-treatment:start -->";
    private const int MaxEvents = 512;

    /// <summary>
    /// Validates the entire local event chain, last-event head, each stable
    /// plan version, reversible state transitions and human provenance.
    /// Does NOT assert that linked tasks still exist in the workspace.
    /// </summary>
    /// <param name="markdown">Complete canonical risk source.</param>
    /// <returns>Verified risk-side relation history.</returns>
    public static ProjectRiskTaskLinkHistory Inspect(string markdown)
    {
        ProjectRiskTreatmentHistory plans =
            ProjectRiskTreatmentMarkdownCodec.Inspect(markdown);
        ProjectRiskCard card = plans.Card;
        string normalized = markdown.Replace("\r\n", "\n",
            StringComparison.Ordinal);
        string open = "\n" + Start + "\n";
        int start = normalized.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
        {
            if (normalized.Contains("nodalis:risk-task-links:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-task-links-head:", StringComparison.Ordinal) ||
                normalized.Contains("nodalis:risk-task-link-event:", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Journal de liens tâche/mesure incomplet.");
            }

            return new ProjectRiskTaskLinkHistory(card, []);
        }

        string close = "\n" + End;
        int contentStart = start + open.Length;
        int end = normalized.IndexOf(close, contentStart, StringComparison.Ordinal);
        if (end < 0 ||
            normalized.IndexOf(open, contentStart, StringComparison.Ordinal) >= 0 ||
            normalized.IndexOf(close, end + close.Length,
                StringComparison.Ordinal) >= 0 ||
            normalized[..start].Contains("nodalis:risk-task-link-event:",
                StringComparison.Ordinal) ||
            normalized[..start].Contains("nodalis:risk-task-links-head:",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Journal tâche/mesure dupliqué ou déplacé.");
        }

        string after = normalized[(end + close.Length)..].Trim();
        if (!after.StartsWith(MeasureStart + "\n", StringComparison.Ordinal) &&
            !after.StartsWith(TreatmentStart + "\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Les liens doivent précéder les journaux de mesures et de plan.");
        }

        string[] lines = normalized[contentStart..end].Split('\n');
        if (lines.Length is < 2 or > MaxEvents + 1 ||
            !lines[0].StartsWith(HeadPrefix, StringComparison.Ordinal) ||
            !lines[0].EndsWith(Suffix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(lines[0].Substring(HeadPrefix.Length,
                lines[0].Length - HeadPrefix.Length - Suffix.Length),
                "D", out Guid head) || head == Guid.Empty)
        {
            throw new InvalidDataException(
                "Tête du journal tâche/mesure manquante ou invalide.");
        }

        HashSet<Guid> ids = [];
        HashSet<(Guid Plan, ProjectRiskMeasureKind Kind, Guid Task)> active = [];
        List<ProjectRiskTaskLinkEvent> events = [];
        Guid? previousId = null;
        DateTimeOffset previousTime = card.CreatedUtc;
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length > 13000 ||
                !line.StartsWith(EventPrefix, StringComparison.Ordinal) ||
                !line.EndsWith(Suffix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Événement de liaison malformé.");
            }

            ProjectRiskTaskLinkEvent item;
            try
            {
                item = JsonSerializer.Deserialize<ProjectRiskTaskLinkEvent>(
                    line.Substring(EventPrefix.Length,
                        line.Length - EventPrefix.Length - Suffix.Length)) ??
                    throw new InvalidDataException("Événement de liaison vide.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Événement tâche/mesure JSON invalide.", exception);
            }

            ProjectRiskTreatmentEvent? version =
                plans.Events.LastOrDefault(plan => plan.Id == item.TreatmentId);
            ProjectRiskTreatmentEvent? activeAtEvent =
                plans.Events.LastOrDefault(plan =>
                    plan.RecordedUtc <= item.RecordedUtc);
            (Guid, ProjectRiskMeasureKind, Guid) key =
                (item.TreatmentId, item.Kind, item.TaskId);
            if (item.Id == Guid.Empty || !ids.Add(item.Id) ||
                item.RiskId != card.Id || item.TaskId == Guid.Empty ||
                item.PreviousId != previousId ||
                !Enum.IsDefined(item.Kind) ||
                !Enum.IsDefined(item.Action) ||
                version is null || activeAtEvent?.Id != version.Id ||
                !ValidHumanText(item.Actor, 120, 1, false) ||
                !ValidHumanText(item.Rationale, 4000, 10, true) ||
                item.RecordedUtc == default || item.RecordedUtc < previousTime)
            {
                throw new InvalidDataException(
                    "Lien tâche/mesure incohérent, hors plan ou sans auteur.");
            }

            bool stateChanged = item.Action == ProjectRiskTaskLinkAction.Link
                ? active.Add(key) : active.Remove(key);
            if (!stateChanged)
            {
                throw new InvalidDataException(
                    "Liaison/déliaison répétée sans changement d'état.");
            }

            events.Add(item);
            previousId = item.Id;
            previousTime = item.RecordedUtc;
        }

        if (events.Count == 0 || head != events[^1].Id)
        {
            throw new InvalidDataException(
                "Tête du journal et dernière liaison incohérentes.");
        }

        return new ProjectRiskTaskLinkHistory(card, events);
    }

    /// <summary>
    /// Appends one confirmed link or unlink in memory. The caller must first
    /// verify a target PersistentId in the current project when linking.
    /// Unlink is permitted even when a referenced task disappeared.
    /// </summary>
    /// <param name="markdown">Exact risk source snapshot.</param>
    /// <param name="kind">Named treatment measure.</param>
    /// <param name="taskId">Permanent Markdown checkbox GUID.</param>
    /// <param name="action">Link or unlink action chosen by a human.</param>
    /// <param name="actor">Person responsible for this relationship.</param>
    /// <param name="rationale">Explicit reason for the change.</param>
    /// <returns>Revalidated updated Markdown without other mutations.</returns>
    public static string Append(
        string markdown, ProjectRiskMeasureKind kind, Guid taskId,
        ProjectRiskTaskLinkAction action, string actor, string rationale)
    {
        ProjectRiskTaskLinkHistory old = Inspect(markdown);
        ProjectRiskTreatmentHistory plans =
            ProjectRiskTreatmentMarkdownCodec.Inspect(markdown);
        ProjectRiskMeasureMarkdownCodec.Inspect(markdown);
        ProjectRiskQualificationMarkdownCodec.Inspect(markdown);
        ProjectRiskReviewMarkdownCodec.Inspect(markdown);
        if (plans.Events.Count == 0 || !Enum.IsDefined(kind) ||
            !Enum.IsDefined(action) || taskId == Guid.Empty ||
            !ValidHumanText(actor, 120, 1, false) ||
            !ValidHumanText(rationale, 4000, 10, true) ||
            old.Events.Count >= MaxEvents)
        {
            throw new InvalidDataException(
                "Plan actuel, GUID de tâche, auteur et motif requis.");
        }

        Guid planId = plans.Events[^1].Id;
        bool linked = old.ActiveTaskIds(planId, kind).Contains(taskId);
        if (linked != (action == ProjectRiskTaskLinkAction.Unlink))
        {
            throw new InvalidOperationException(
                "Liaison déjà active ou absente : aucune opération implicite.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < plans.Events[^1].RecordedUtc ||
            old.Events.Count > 0 && now < old.Events[^1].RecordedUtc)
        {
            throw new InvalidDataException(
                "Horloge antérieure au plan ou à la dernière liaison.");
        }

        ProjectRiskTaskLinkEvent item = new()
        {
            Id = Guid.NewGuid(),
            RiskId = old.Card.Id,
            TreatmentId = planId,
            Kind = kind,
            TaskId = taskId,
            PreviousId = old.Events.Count == 0 ? null : old.Events[^1].Id,
            Action = action,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            RecordedUtc = now
        };

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string line = EventPrefix + JsonSerializer.Serialize(item) + Suffix;
        string updated;
        if (old.Events.Count == 0)
        {
            string block = newline + Start + newline +
                HeadPrefix + item.Id.ToString("D") + Suffix + newline +
                line + newline + End + newline;
            string beforeMeasure = newline + MeasureStart + newline;
            string beforePlan = newline + TreatmentStart + newline;
            int position = markdown.IndexOf(beforeMeasure, StringComparison.Ordinal);
            if (position < 0)
            {
                position = markdown.IndexOf(beforePlan, StringComparison.Ordinal);
            }

            if (position < 0)
            {
                throw new InvalidDataException(
                    "Journal de plan de traitement absent.");
            }

            updated = markdown[..position] + block + markdown[position..];
        }
        else
        {
            string oldHead = HeadPrefix +
                old.Events[^1].Id.ToString("D") + Suffix;
            string nextHead = HeadPrefix + item.Id.ToString("D") + Suffix;
            int start = markdown.IndexOf(oldHead, StringComparison.Ordinal);
            if (start < 0 || markdown.IndexOf(oldHead,
                    start + oldHead.Length, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException(
                    "Tête des liens modifiée entre la lecture et l'écriture.");
            }

            updated = markdown[..start] + nextHead +
                markdown[(start + oldHead.Length)..];
            string close = newline + End;
            int position = updated.IndexOf(close, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidDataException(
                    "Fin du journal de liens absente.");
            }

            updated = updated[..position] + newline + line +
                updated[position..];
        }

        ProjectRiskTaskLinkHistory verified = Inspect(updated);
        ProjectRiskMeasureMarkdownCodec.Inspect(updated);
        ProjectRiskQualificationMarkdownCodec.Inspect(updated);
        ProjectRiskReviewMarkdownCodec.Inspect(updated);
        if (verified.Events.Count != old.Events.Count + 1 ||
            verified.Events[^1].Id != item.Id)
        {
            throw new InvalidDataException(
                "La liaison n'a pas été conservée après validation.");
        }

        return updated;
    }

    /// <summary>Validates bounded human reviewer text.</summary>
    /// <param name="value">Untrusted human text.</param>
    /// <param name="max">Maximum characters.</param>
    /// <param name="min">Minimum trimmed characters.</param>
    /// <param name="allowNewline">Whether paragraphs are allowed.</param>
    /// <returns>Whether the human field meets the contract.</returns>
    private static bool ValidHumanText(
        string? value, int max, int min, bool allowNewline) =>
        value is not null && value.Length <= max &&
        value.Trim().Length >= min &&
        !value.Any(character => char.IsControl(character) &&
            (!allowNewline || character is not ('\r' or '\n')));
}
