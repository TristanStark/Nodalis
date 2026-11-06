using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Projects;

/// <summary>
/// Strict canonical Markdown + JSON deployment ledger. Only explicit human
/// events are persisted; no computed status can become a second source.
/// </summary>
public static class ProjectDeploymentMarkdownCodec
{
    private const string Opening =
        "\n\n<!-- nodalis:deployment-operation:v1 -->\n\n~~~json\n";
    private const string Closing = "\n~~~\n";
    private const int MaxCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    /// <summary>Checks readable MEP-0001..MEP-9999 identifiers.</summary>
    /// <param name="reference">Human MEP reference.</param>
    /// <returns>Whether the reference is canonical.</returns>
    public static bool IsReference(string? reference) =>
        reference is { Length: 8 } &&
        reference.StartsWith("MEP-", StringComparison.Ordinal) &&
        int.TryParse(reference[4..], out int number) &&
        number is >= 1 and <= 9999 &&
        string.Equals(reference, "MEP-" +
            number.ToString("D4",
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>Checks the complete operation and event ordering.</summary>
    /// <param name="operation">Untrusted source to verify.</param>
    public static void Validate(ProjectDeploymentOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.SchemaVersion != 1 || operation.Id == Guid.Empty ||
            operation.ProjectId == Guid.Empty ||
            !IsReference(operation.Reference) ||
            !SingleLine(operation.Title, 240) ||
            operation.CreatedUtc == default ||
            operation.Attempts is null || operation.Attempts.Count > 50 ||
            operation.Events is null || operation.Events.Count > 5000)
        {
            throw new InvalidDataException("Fiche de MEP invalide.");
        }

        HashSet<Guid> attempts = [];
        HashSet<Guid> events = [];
        int nextEvent = 0;
        DateTimeOffset last = operation.CreatedUtc;
        bool previousFinished = true;
        for (int index = 0; index < operation.Attempts.Count; index++)
        {
            ProjectDeploymentAttempt attempt = operation.Attempts[index];
            if (attempt is null || attempt.Id == Guid.Empty ||
                !attempts.Add(attempt.Id) || attempt.Number != index + 1 ||
                !previousFinished || attempt.StartedUtc < last ||
                !SingleLine(attempt.StartedBy, 160) ||
                !Text(attempt.Rationale, 4000) ||
                !SingleLine(attempt.ObservedVersionBefore, 160) ||
                !SingleLine(attempt.TargetVersion, 160) ||
                !SingleLine(attempt.Environment, 160) ||
                !SingleLine(attempt.Scope, 500) ||
                !ValidProof(attempt.Procedure) ||
                !ValidProof(attempt.RollbackProcedure) ||
                attempt.GateDecisionId == Guid.Empty ||
                !SingleLine(attempt.AuthorizationReviewedBy, 160) ||
                !Text(attempt.AuthorizationRationale, 4000))
            {
                throw new InvalidDataException(
                    "Tentative sans contexte figé ou revue humaine.");
            }

            bool requested = false;
            bool started = false;
            bool observed = false;
            bool finished = false;
            last = attempt.StartedUtc;
            while (nextEvent < operation.Events.Count &&
                operation.Events[nextEvent] is ProjectDeploymentAttemptEvent next &&
                next.AttemptId == attempt.Id)
            {
                ProjectDeploymentAttemptEvent action = operation.Events[nextEvent++];
                if (action.Id == Guid.Empty || !events.Add(action.Id) ||
                    action.RecordedUtc < last || finished ||
                    !SingleLine(action.Actor, 160) ||
                    !Text(action.Rationale, 4000) ||
                    !Enum.IsDefined(action.Kind) ||
                    action.Evidence is not null && !ValidProof(action.Evidence) ||
                    action.ObservedVersionAfter is not null &&
                        !SingleLine(action.ObservedVersionAfter, 160))
                {
                    throw new InvalidDataException(
                        "Événement MEP falsifié, mal ordonné ou dupliqué.");
                }

                switch (action.Kind)
                {
                    case ProjectDeploymentEventKind.ActionLogged:
                        RequireNoRollbackFields(action);
                        break;
                    case ProjectDeploymentEventKind.RollbackRequested:
                        RequireNoRollbackFields(action);
                        if (requested)
                        {
                            throw new InvalidDataException(
                                "Rollback déjà demandé.");
                        }

                        requested = true;
                        break;
                    case ProjectDeploymentEventKind.RollbackStarted:
                        RequireNoRollbackFields(action);
                        if (!requested || started)
                        {
                            throw new InvalidDataException(
                                "Exécution rollback sans demande unique.");
                        }

                        started = true;
                        break;
                    case ProjectDeploymentEventKind.RollbackObserved:
                        if (!requested || !started || observed ||
                            action.RollbackOutcome is null ||
                            !Enum.IsDefined(action.RollbackOutcome.Value) ||
                            !SingleLine(action.ObservedVersionAfter, 160))
                        {
                            throw new InvalidDataException(
                                "Constat rollback sans exécution/état observé.");
                        }

                        observed = true;
                        break;
                    case ProjectDeploymentEventKind.FinishedSucceeded:
                        RequireNoRollbackFields(action);
                        if (requested || !string.Equals(
                            action.ObservedVersionAfter, attempt.TargetVersion,
                            StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "Une réussite exige la version cible observée " +
                                "et aucun rollback demandé.");
                        }

                        finished = true;
                        break;
                    case ProjectDeploymentEventKind.FinishedFailed:
                    case ProjectDeploymentEventKind.FinishedInterrupted:
                        if (action.RollbackOutcome is not null)
                        {
                            throw new InvalidDataException(
                                "Le verdict final ne remplace pas le constat rollback.");
                        }

                        finished = true;
                        break;
                    default:
                        throw new InvalidDataException("Type d'événement MEP inconnu.");
                }

                last = action.RecordedUtc;
            }

            // A new attempt after a partial or failed rollback must freeze a
            // NEW observed source version, never replay old expectations.
            previousFinished = finished;
        }

        if (nextEvent != operation.Events.Count)
        {
            throw new InvalidDataException(
                "Événement hors tentative ou historique réordonné.");
        }
    }

    /// <summary>Serializes a fully validated, canonical MEP Markdown file.</summary>
    /// <param name="operation">Document to serialize.</param>
    /// <returns>Canonical source, without inferred verdicts.</returns>
    public static string Format(ProjectDeploymentOperation operation)
    {
        Validate(operation);
        return FormatValidated(operation);
    }

    /// <summary>Loads only the single canonical marker and exact JSON payload.</summary>
    /// <param name="markdown">Exact UTF-8 Markdown document.</param>
    /// <returns>Validated MEP, without any write or migration.</returns>
    public static ProjectDeploymentOperation Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxCharacters ||
            !markdown.StartsWith("# MEP-", StringComparison.Ordinal) ||
            !markdown.EndsWith(Closing, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fiche MEP absente ou incompatible.");
        }

        int marker = markdown.IndexOf(Opening, StringComparison.Ordinal);
        if (marker < 1 || markdown.IndexOf(
            Opening, marker + Opening.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidDataException("Bloc MEP absent ou répété.");
        }

        try
        {
            ProjectDeploymentOperation operation =
                JsonSerializer.Deserialize<ProjectDeploymentOperation>(
                    markdown[(marker + Opening.Length)..^Closing.Length],
                    Options) ??
                throw new InvalidDataException("Données MEP manquantes.");
            Validate(operation);
            if (!string.Equals(markdown, FormatValidated(operation),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Fiche MEP non canonique ou altérée.");
            }

            return operation;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "JSON du journal MEP illisible.", exception);
        }
    }

    /// <summary>Projects one attempt without altering recorded history.</summary>
    /// <param name="operation">Validated MEP source.</param>
    /// <param name="attemptId">Existing historical attempt.</param>
    /// <returns>Event-derived state.</returns>
    public static ProjectDeploymentAttemptState GetAttemptState(
        ProjectDeploymentOperation operation, Guid attemptId)
    {
        Validate(operation);
        if (!operation.Attempts.Any(item => item.Id == attemptId))
        {
            throw new KeyNotFoundException("Tentative MEP introuvable.");
        }

        ProjectDeploymentAttemptEvent[] history = operation.Events
            .Where(item => item.AttemptId == attemptId).ToArray();
        ProjectDeploymentAttemptEvent? last = history.LastOrDefault();
        return new ProjectDeploymentAttemptState
        {
            AttemptId = attemptId,
            IsFinished = last is not null && IsFinish(last.Kind),
            RollbackRequested = history.Any(item =>
                item.Kind == ProjectDeploymentEventKind.RollbackRequested),
            RollbackStarted = history.Any(item =>
                item.Kind == ProjectDeploymentEventKind.RollbackStarted),
            RollbackObserved = history.LastOrDefault(item =>
                item.Kind == ProjectDeploymentEventKind.RollbackObserved)?
                    .RollbackOutcome,
            LastEventKind = last?.Kind
        };
    }

    /// <summary>Identifies a terminal human-declared attempt event.</summary>
    /// <param name="kind">Event category.</param>
    /// <returns>Whether the attempt is finished.</returns>
    public static bool IsFinish(ProjectDeploymentEventKind kind) =>
        kind is ProjectDeploymentEventKind.FinishedSucceeded or
            ProjectDeploymentEventKind.FinishedFailed or
            ProjectDeploymentEventKind.FinishedInterrupted;

    /// <summary>Formats already checked data with no second full replay.</summary>
    /// <param name="operation">Validated source.</param>
    /// <returns>Exact canonical source.</returns>
    private static string FormatValidated(ProjectDeploymentOperation operation)
    {
        string markdown = "# " + operation.Reference + " — " +
            operation.Title + Opening +
            JsonSerializer.Serialize(operation, Options) + Closing;
        if (markdown.Length > MaxCharacters)
        {
            throw new InvalidDataException("Historique de MEP trop volumineux.");
        }

        return markdown;
    }

    /// <summary>Disallows rollback-only metadata on other event types.</summary>
    /// <param name="action">Human event to check.</param>
    private static void RequireNoRollbackFields(ProjectDeploymentAttemptEvent action)
    {
        if (action.RollbackOutcome is not null ||
            action.ObservedVersionAfter is not null &&
            action.Kind is not ProjectDeploymentEventKind.FinishedSucceeded)
        {
            throw new InvalidDataException(
                "Métadonnées rollback sur un événement non adapté.");
        }
    }

    /// <summary>Checks a portable SHA fingerprint and workspace-relative path.</summary>
    /// <param name="proof">Snapshot supplied by the human.</param>
    /// <returns>Whether the evidence is syntactically valid.</returns>
    private static bool ValidProof(ProjectGateEvidenceSnapshot? proof) =>
        proof is not null &&
        ProjectJourneySourcePath.IsSafe(proof.RelativePath) &&
        proof.Sha256 is { Length: 64 } &&
        proof.Sha256.All(Uri.IsHexDigit);

    /// <summary>Checks a human label without control characters.</summary>
    /// <param name="value">Untrusted text.</param>
    /// <param name="max">Limit.</param>
    /// <returns>Whether valid.</returns>
    private static bool SingleLine(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);

    /// <summary>Checks bounded, non-empty multiline human notes.</summary>
    /// <param name="value">Untrusted note.</param>
    /// <param name="max">Maximum characters.</param>
    /// <returns>Whether valid.</returns>
    private static bool Text(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(character => character == '\0');
}
