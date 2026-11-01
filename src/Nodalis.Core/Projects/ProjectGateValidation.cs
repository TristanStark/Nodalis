namespace Nodalis.Core.Projects;

/// <summary>
/// Strict validation of portable gate registries and frozen human verdicts.
/// Public for deterministic smoke tests; never infers a decision from sources.
/// </summary>
public static class ProjectGateValidation
{
    /// <summary>Validates a complete registry and all frozen decisions.</summary>
    /// <param name="registry">Deserialized or edited gate registry.</param>
    public static void Validate(ProjectGateRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.SchemaVersion != ProjectGateRegistry.CurrentSchemaVersion ||
            registry.ProjectId == Guid.Empty || registry.Gates is null ||
            registry.Decisions is null)
        {
            throw new InvalidDataException("Registre des gates incompatible.");
        }

        HashSet<Guid> gateIds = [];
        HashSet<Guid> criterionIds = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectGateDefinition gate in registry.Gates)
        {
            if (gate is null || gate.Id == Guid.Empty ||
                !gateIds.Add(gate.Id) || !IsText(gate.Name, 200) ||
                !names.Add(gate.Name) || gate.Criteria is null ||
                gate.MilestoneIds is null ||
                gate.MilestoneIds.Any(id => id == Guid.Empty) ||
                gate.MilestoneIds.Distinct().Count() != gate.MilestoneIds.Count)
            {
                throw new InvalidDataException("Définition de gate invalide ou dupliquée.");
            }

            foreach (ProjectGateCriterion criterion in gate.Criteria)
            {
                if (criterion is null || criterion.Id == Guid.Empty ||
                    !criterionIds.Add(criterion.Id) ||
                    !IsText(criterion.Name, 200))
                {
                    throw new InvalidDataException(
                        "Critère de gate invalide ou dupliqué.");
                }
            }

            if (gate.Criteria.Select(item => item.Name).Distinct(
                    StringComparer.OrdinalIgnoreCase).Count() != gate.Criteria.Count)
            {
                throw new InvalidDataException("Critères de gate dupliqués.");
            }
        }

        HashSet<Guid> historyIds = [];
        HashSet<Guid> reservationIds = [];
        foreach (ProjectGateDecision decision in registry.Decisions)
        {
            if (decision is null || decision.Id == Guid.Empty ||
                !historyIds.Add(decision.Id) || !gateIds.Contains(decision.GateId) ||
                !Enum.IsDefined(decision.Verdict) ||
                !IsText(decision.GateName, 200) ||
                !IsText(decision.Actor, 120) ||
                !IsText(decision.Scope, 500) ||
                !IsText(decision.Rationale, 8000) ||
                decision.Rationale.Length < 10 ||
                decision.RecordedUtc == default ||
                !IsOptionalText(decision.Version, 100) ||
                !IsOptionalText(decision.Environment, 100) ||
                decision.Assessments is null ||
                decision.Evidence is null ||
                decision.Reservations is null)
            {
                throw new InvalidDataException("Décision de gate invalide.");
            }

            HashSet<Guid> assessed = [];
            foreach (ProjectGateCriterionAssessment item in decision.Assessments)
            {
                if (item is null || item.CriterionId == Guid.Empty ||
                    !assessed.Add(item.CriterionId) ||
                    !IsText(item.Name, 200) ||
                    !Enum.IsDefined(item.Status) ||
                    item.Comment is null || item.Comment.Length > 4000)
                {
                    throw new InvalidDataException(
                        "Instantané de critère invalide.");
                }
            }

            HashSet<string> proofPaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectGateEvidenceSnapshot proof in decision.Evidence)
            {
                if (proof is null ||
                    !ProjectJourneySourcePath.IsSafe(proof.RelativePath) ||
                    !proofPaths.Add(proof.RelativePath) ||
                    !IsSha256(proof.Sha256))
                {
                    throw new InvalidDataException(
                        "Instantané de preuve invalide.");
                }
            }

            foreach (ProjectGateReservation reservation in decision.Reservations)
            {
                if (reservation is null || reservation.Id == Guid.Empty ||
                    !reservationIds.Add(reservation.Id) ||
                    !IsText(reservation.Description, 1000) ||
                    !IsOptionalText(reservation.Owner, 120))
                {
                    throw new InvalidDataException("Réserve de décision invalide.");
                }
            }

            bool approval = decision.Verdict is ProjectGateVerdict.Go or
                ProjectGateVerdict.GoWithReservations;
            if (approval && (decision.Evidence.Count == 0 ||
                decision.Assessments.Count == 0))
            {
                throw new InvalidDataException(
                    "Décision GO sans critères évalués ni preuve figée interdite.");
            }

            if (decision.Verdict == ProjectGateVerdict.Go &&
                decision.Assessments.Any(item =>
                    item.Status is ProjectGateCriterionStatus.Deviation or
                        ProjectGateCriterionStatus.Unknown ||
                    !item.Optional && item.Status !=
                        ProjectGateCriterionStatus.Conform))
            {
                throw new InvalidDataException(
                    "Un GO historique ne peut contenir d'écart, d'inconnu " +
                    "ni de critère obligatoire non conforme.");
            }

            if (decision.Verdict == ProjectGateVerdict.GoWithReservations &&
                decision.Reservations.Count == 0 ||
                decision.Verdict != ProjectGateVerdict.GoWithReservations &&
                decision.Reservations.Count > 0)
            {
                throw new InvalidDataException(
                    "Réserves incompatibles avec le verdict.");
            }
        }

        if (registry.ReservationEvents is null)
        {
            throw new InvalidDataException("Historique des réserves invalide.");
        }

        ValidateReservationEvents(registry);
    }

    /// <summary>
    /// Replays append-only reservation transitions without altering an
    /// original decision or deriving any Go/No-Go from their current states.
    /// </summary>
    /// <param name="registry">Registry including original reservations.</param>
    private static void ValidateReservationEvents(ProjectGateRegistry registry)
    {
        Dictionary<(Guid Decision, Guid Reservation), bool> state = [];
        Dictionary<Guid, DateTimeOffset> lastTime = [];
        foreach (ProjectGateDecision decision in registry.Decisions)
        {
            foreach (ProjectGateReservation reservation in decision.Reservations)
            {
                state.Add((decision.Id, reservation.Id), true);
                lastTime.Add(reservation.Id, decision.RecordedUtc);
            }
        }

        HashSet<Guid> eventIds = [];
        foreach (ProjectGateReservationEvent item in registry.ReservationEvents)
        {
            if (item is null || item.Id == Guid.Empty ||
                !eventIds.Add(item.Id) || item.DecisionId == Guid.Empty ||
                item.ReservationId == Guid.Empty ||
                !Enum.IsDefined(item.Kind) ||
                !IsText(item.Actor, 120) ||
                !IsText(item.Rationale, 4000) ||
                item.Rationale.Trim().Length < 10 ||
                item.RecordedUtc == default || item.Evidence is null ||
                !state.TryGetValue((item.DecisionId, item.ReservationId),
                    out bool open) ||
                item.RecordedUtc < lastTime[item.ReservationId])
            {
                throw new InvalidDataException(
                    "Événement de réserve absent, falsifié ou orphelin.");
            }

            if (item.Kind == ProjectGateReservationEventKind.Lifted && !open ||
                item.Kind == ProjectGateReservationEventKind.Reopened && open)
            {
                throw new InvalidDataException(
                    "Transitions de réserves incohérentes ou dupliquées.");
            }

            if (item.Kind == ProjectGateReservationEventKind.Lifted &&
                item.Evidence.Count == 0)
            {
                throw new InvalidDataException(
                    "Levée de réserve sans preuve historisée.");
            }

            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectGateEvidenceSnapshot proof in item.Evidence)
            {
                if (proof is null ||
                    !ProjectJourneySourcePath.IsSafe(proof.RelativePath) ||
                    !IsSha256(proof.Sha256) ||
                    !paths.Add(proof.RelativePath))
                {
                    throw new InvalidDataException(
                        "Preuve de réserve non sûre ou dupliquée.");
                }
            }

            state[(item.DecisionId, item.ReservationId)] =
                item.Kind == ProjectGateReservationEventKind.Reopened;
            lastTime[item.ReservationId] = item.RecordedUtc;
        }
    }

    /// <summary>Requires a bounded non-control-text label.</summary>
    /// <param name="value">User-supplied value.</param>
    /// <param name="maxLength">Allowed character count.</param>
    /// <param name="paramName">Argument name.</param>
    /// <returns>Trimmed validated value.</returns>
    public static string RequireText(
        string value, int maxLength, string paramName)
    {
        if (!IsText(value, maxLength))
        {
            throw new ArgumentException("Texte requis ou invalide.", paramName);
        }

        return value.Trim();
    }

    /// <summary>Checks a bounded user-entered string.</summary>
    /// <param name="value">Candidate.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <returns>Whether the value is valid.</returns>
    private static bool IsText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength &&
        !value.Any(character => character is '\0' or '\u0001');

    /// <summary>Checks optional version, environment and owner fields.</summary>
    /// <param name="value">Optional value.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <returns>Whether the optional field is valid.</returns>
    private static bool IsOptionalText(string? value, int maxLength) =>
        value is null || value.Length <= maxLength &&
        !value.Any(character => character is '\0' or '\u0001');

    /// <summary>Checks a 64-digit hexadecimal fingerprint.</summary>
    /// <param name="value">Hash value.</param>
    /// <returns>Whether the hash is valid.</returns>
    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
