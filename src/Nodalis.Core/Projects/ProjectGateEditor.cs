namespace Nodalis.Core.Projects;

/// <summary>
/// Pure human-driven gate operations. Decisions are append-only,
/// capture the criterion names in force at that instant and never
/// change milestone completion or project journey status.
/// </summary>
public static class ProjectGateEditor
{
    /// <summary>Adds an explicitly named gate without creating a verdict.</summary>
    /// <param name="registry">Current registry.</param>
    /// <param name="name">Gate name.</param>
    /// <returns>New registry with one stable gate.</returns>
    public static ProjectGateRegistry CreateGate(ProjectGateRegistry registry, string name)
    {
        ProjectGateValidation.Validate(registry);
        string label = ProjectGateValidation.RequireText(name, 200, nameof(name));
        if (registry.Gates.Any(item =>
            string.Equals(item.Name, label, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Un gate de ce nom existe déjà.");
        }

        return registry with
        {
            Gates = [.. registry.Gates, new ProjectGateDefinition
            {
                Id = Guid.NewGuid(),
                Name = label
            }]
        };
    }

    /// <summary>Adds a criterion used in future reviews, never previous reviews.</summary>
    /// <param name="registry">Current registry.</param>
    /// <param name="gateId">Existing gate.</param>
    /// <param name="name">Criterion name.</param>
    /// <param name="optional">Whether the criterion is facultative.</param>
    /// <returns>New registry.</returns>
    public static ProjectGateRegistry AddCriterion(
        ProjectGateRegistry registry, Guid gateId, string name, bool optional = false)
    {
        string label = ProjectGateValidation.RequireText(name, 200, nameof(name));
        return UpdateGate(registry, gateId, gate =>
        {
            if (gate.Criteria.Any(item =>
                string.Equals(item.Name, label, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Critère déjà présent.");
            }

            return gate with
            {
                Criteria = [.. gate.Criteria, new ProjectGateCriterion
                {
                    Id = Guid.NewGuid(),
                    Name = label,
                    Optional = optional
                }]
            };
        });
    }

    /// <summary>
    /// Appends a human-reviewed verdict and freezes its criterion assessments.
    /// A GO requires a positive assessment of every mandatory criterion and
    /// at least one evidence snapshot. Reservations are never auto-lifted.
    /// </summary>
    /// <param name="registry">Current registry.</param>
    /// <param name="review">Explicitly approved decision draft.</param>
    /// <returns>New registry containing the immutable decision.</returns>
    public static ProjectGateRegistry AppendDecision(
        ProjectGateRegistry registry, ProjectGateReview review)
    {
        ProjectGateValidation.Validate(registry);
        ArgumentNullException.ThrowIfNull(review);
        ProjectGateDefinition gate = registry.Gates.SingleOrDefault(item =>
            item.Id == review.GateId) ??
            throw new KeyNotFoundException("Gate introuvable.");
        if (!Enum.IsDefined(review.Verdict) ||
            review.Assessments is null || review.Evidence is null ||
            review.Reservations is null)
        {
            throw new InvalidDataException("Dossier de décision invalide.");
        }

        string actor = ProjectGateValidation.RequireText(review.Actor, 120,
            nameof(review.Actor));
        string scope = ProjectGateValidation.RequireText(review.Scope, 500,
            nameof(review.Scope));
        string reason = ProjectGateValidation.RequireText(review.Rationale, 8000,
            nameof(review.Rationale));
        if (reason.Length < 10)
        {
            throw new InvalidDataException(
                "Une justification explicite d'au moins 10 caractères est exigée.");
        }

        HashSet<Guid> assessedIds = [];
        foreach (ProjectGateCriterionAssessment assessment in review.Assessments)
        {
            ProjectGateCriterion? criterion = gate.Criteria.FirstOrDefault(item =>
                item.Id == assessment.CriterionId);
            if (criterion is null || !assessedIds.Add(assessment.CriterionId) ||
                !Enum.IsDefined(assessment.Status) ||
                !string.Equals(assessment.Name, criterion.Name,
                    StringComparison.Ordinal) ||
                assessment.Optional != criterion.Optional ||
                assessment.Comment is null || assessment.Comment.Length > 4000)
            {
                throw new InvalidDataException(
                    "Les évaluations ne correspondent pas aux critères du gate.");
            }
        }

        if (assessedIds.Count != gate.Criteria.Count)
        {
            throw new InvalidDataException(
                "Chaque critère doit être qualifié explicitement avant décision.");
        }

        bool approval = review.Verdict is ProjectGateVerdict.Go or
            ProjectGateVerdict.GoWithReservations;
        if (approval && (gate.Criteria.Count == 0 ||
            review.Evidence.Count == 0))
        {
            throw new InvalidDataException(
                "Un GO ou GO sous réserves exige des critères qualifiés " +
                "et au moins une preuve examinée.");
        }

        if (review.Verdict == ProjectGateVerdict.Go &&
            (review.Assessments.Any(item =>
                item.Status is ProjectGateCriterionStatus.Deviation or
                    ProjectGateCriterionStatus.Unknown) ||
             gate.Criteria.Any(criterion => !criterion.Optional &&
                review.Assessments.Single(item =>
                    item.CriterionId == criterion.Id).Status !=
                ProjectGateCriterionStatus.Conform)))
        {
            throw new InvalidDataException(
                "Un GO sans réserves exige la conformité explicite de " +
                "tous les critères obligatoires, sans écart ni inconnue.");
        }

        if (review.Verdict == ProjectGateVerdict.GoWithReservations &&
            review.Reservations.Count == 0)
        {
            throw new InvalidDataException(
                "GO sous réserves exige au moins une réserve identifiée.");
        }

        if (review.Verdict != ProjectGateVerdict.GoWithReservations &&
            review.Reservations.Count > 0)
        {
            throw new InvalidDataException(
                "Les réserves de cette version sont réservées au GO sous réserves.");
        }

        ProjectGateDecision decision = new()
        {
            Id = Guid.NewGuid(),
            GateId = gate.Id,
            GateName = gate.Name,
            RecordedUtc = DateTimeOffset.UtcNow,
            Verdict = review.Verdict,
            Actor = actor,
            Scope = scope,
            Rationale = reason,
            Version = review.Version,
            Environment = review.Environment,
            Assessments = review.Assessments.Select(item => item with { }).ToList(),
            Evidence = review.Evidence.Select(item => item with { }).ToList(),
            Reservations = review.Reservations.Select(item => item with { }).ToList()
        };
        ProjectGateRegistry result = registry with
        {
            Decisions = [.. registry.Decisions, decision]
        };
        ProjectGateValidation.Validate(result);
        return result;
    }

    /// <summary>
    /// Returns the current state of an original reservation from the separate
    /// event history. Merely reading it can never modify the original verdict.
    /// </summary>
    /// <param name="registry">Current validated register.</param>
    /// <param name="decisionId">Historical GO-with-reservations decision.</param>
    /// <param name="reservationId">Original reservation ID.</param>
    /// <returns>Calculated state and the latest explicit human event.</returns>
    public static ProjectGateReservationState GetReservationState(
        ProjectGateRegistry registry, Guid decisionId, Guid reservationId)
    {
        ProjectGateValidation.Validate(registry);
        ProjectGateDecision decision = registry.Decisions.SingleOrDefault(item =>
            item.Id == decisionId) ??
            throw new KeyNotFoundException("Décision d'origine introuvable.");
        if (decision.Reservations.All(item => item.Id != reservationId))
        {
            throw new KeyNotFoundException("Réserve d'origine introuvable.");
        }

        ProjectGateReservationEvent? last = registry.ReservationEvents.LastOrDefault(
            item => item.DecisionId == decisionId &&
                item.ReservationId == reservationId);
        return new ProjectGateReservationState(decisionId, reservationId,
            last is null || last.Kind == ProjectGateReservationEventKind.Reopened,
            last);
    }

    /// <summary>
    /// Appends an explicit lift or reopening. A lift requires reviewed proof;
    /// no event edits its decision, reservation text, verdict or milestone.
    /// </summary>
    /// <param name="registry">Current register.</param>
    /// <param name="decisionId">Owning historical decision.</param>
    /// <param name="reservationId">Original reservation ID.</param>
    /// <param name="kind">Human action.</param>
    /// <param name="actor">Human responsible for the action.</param>
    /// <param name="rationale">Explicit reason and qualification.</param>
    /// <param name="evidence">Fingerprinted local evidence; required for lift.</param>
    /// <returns>Fresh register with one additional immutable event.</returns>
    public static ProjectGateRegistry AppendReservationEvent(
        ProjectGateRegistry registry, Guid decisionId, Guid reservationId,
        ProjectGateReservationEventKind kind, string actor, string rationale,
        IReadOnlyList<ProjectGateEvidenceSnapshot> evidence)
    {
        ProjectGateReservationState current =
            GetReservationState(registry, decisionId, reservationId);
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("Type d'événement de réserve invalide.");
        }

        if (kind == ProjectGateReservationEventKind.Lifted && !current.IsOpen ||
            kind == ProjectGateReservationEventKind.Reopened && current.IsOpen)
        {
            throw new InvalidOperationException(
                "Cette transition de réserve a déjà été effectuée. " +
                "Actualisez le registre avant de poursuivre.");
        }

        ArgumentNullException.ThrowIfNull(evidence);
        if (kind == ProjectGateReservationEventKind.Lifted && evidence.Count == 0)
        {
            throw new InvalidDataException(
                "La levée d'une réserve exige une preuve revue par un humain.");
        }

        string reviewedActor = ProjectGateValidation.RequireText(
            actor, 120, nameof(actor));
        string reviewedReason = ProjectGateValidation.RequireText(
            rationale, 4000, nameof(rationale));
        if (reviewedReason.Length < 10)
        {
            throw new InvalidDataException(
                "Motif de qualification requis (10 caractères minimum).");
        }

        ProjectGateRegistry changed = registry with
        {
            ReservationEvents =
            [
                .. registry.ReservationEvents,
                new ProjectGateReservationEvent
                {
                    Id = Guid.NewGuid(),
                    DecisionId = decisionId,
                    ReservationId = reservationId,
                    Kind = kind,
                    Actor = reviewedActor,
                    Rationale = reviewedReason,
                    RecordedUtc = DateTimeOffset.UtcNow,
                    Evidence = evidence.Select(item => item with { }).ToList()
                }
            ]
        };
        ProjectGateValidation.Validate(changed);
        return changed;
    }

    /// <summary>
    /// Adds a non-owning milestone association to a gate. Callers must verify
    /// that the milestone has a real, unique persisted ID in the project.
    /// </summary>
    /// <param name="registry">Current registry.</param>
    /// <param name="gateId">Gate identity.</param>
    /// <param name="milestoneId">Verified milestone identity.</param>
    /// <returns>Updated gate definition without changing milestones.</returns>
    public static ProjectGateRegistry LinkMilestone(
        ProjectGateRegistry registry, Guid gateId, Guid milestoneId)
    {
        if (milestoneId == Guid.Empty)
        {
            throw new ArgumentException("Identifiant du jalon invalide.",
                nameof(milestoneId));
        }

        return UpdateGate(registry, gateId, gate =>
            gate.MilestoneIds.Contains(milestoneId)
                ? gate
                : gate with { MilestoneIds = [.. gate.MilestoneIds, milestoneId] });
    }

    /// <summary>Updates one gate definition, preserving all decision history.</summary>
    /// <param name="registry">Current registry.</param>
    /// <param name="gateId">Existing gate.</param>
    /// <param name="update">Pure change to the definition only.</param>
    /// <returns>New registry.</returns>
    private static ProjectGateRegistry UpdateGate(
        ProjectGateRegistry registry, Guid gateId,
        Func<ProjectGateDefinition, ProjectGateDefinition> update)
    {
        ProjectGateValidation.Validate(registry);
        if (gateId == Guid.Empty ||
            registry.Gates.All(item => item.Id != gateId))
        {
            throw new KeyNotFoundException("Gate introuvable.");
        }

        ProjectGateRegistry proposed = registry with
        {
            Gates = registry.Gates.Select(item =>
                item.Id == gateId ? update(item) : item).ToList()
        };
        ProjectGateValidation.Validate(proposed);
        return proposed;
    }
}
