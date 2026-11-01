using Nodalis.Core.Projects;

/// <summary>
/// Checks reservation overview totals, ordering and non-owning projection
/// across several gates and historical human decisions.
/// </summary>
internal static class ProjectGateReservationOverviewSmokeTests
{
    /// <summary>Exercises pure project-level summary and current-state projection.</summary>
    public static void Run()
    {
        ProjectGateRegistry empty = new()
        {
            ProjectId = Guid.NewGuid()
        };
        DateOnly today = new(2026, 11, 10);
        ProjectGateReservationOverview none =
            ProjectGateReservationOverviewBuilder.Build(empty, today);
        Assert(none.Rows.Count == 0 && none.OpenCount == 0 &&
            none.LiftedCount == 0 && none.OverdueCount == 0 &&
            none.MissingOwnerCount == 0 && none.ProjectId == empty.ProjectId,
            "An absent gate file cannot imply a missing or approved reservation.");

        ProjectGateRegistry registry = ProjectGateEditor.CreateGate(
            empty, "Gate recette");
        registry = ProjectGateEditor.CreateGate(registry, "Gate production");
        Guid recetteId = registry.Gates.Single(item => item.Name == "Gate recette").Id;
        Guid productionId = registry.Gates.Single(item =>
            item.Name == "Gate production").Id;
        registry = ProjectGateEditor.AddCriterion(
            registry, recetteId, "Preuves de recette");
        registry = ProjectGateEditor.AddCriterion(
            registry, productionId, "Procédure de retour arrière");
        Guid firstReservation = Guid.NewGuid();
        Guid secondReservation = Guid.NewGuid();
        Guid thirdReservation = Guid.NewGuid();
        ProjectGateEvidenceSnapshot evidence = new()
        {
            RelativePath = "Applications/Tests/Recette.md",
            Sha256 = new string('A', 64)
        };

        registry = ProjectGateEditor.AppendDecision(registry,
            BuildReview(registry.Gates.Single(item => item.Id == recetteId),
                evidence, [
                    new ProjectGateReservation
                    {
                        Id = firstReservation,
                        Description = "Finaliser le rapport de tests",
                        Owner = "Équipe qualité",
                        DueDate = new DateOnly(2026, 11, 7)
                    },
                    new ProjectGateReservation
                    {
                        Id = secondReservation,
                        Description = "Vérifier les anomalies bloquantes",
                        DueDate = new DateOnly(2026, 11, 5)
                    }
                ]));
        registry = ProjectGateEditor.AppendDecision(registry,
            BuildReview(registry.Gates.Single(item => item.Id == productionId),
                evidence, [
                    new ProjectGateReservation
                    {
                        Id = thirdReservation,
                        Description = "Confirmer le plan de retour arrière",
                        Owner = "Exploitation",
                        DueDate = new DateOnly(2026, 11, 20)
                    }
                ]));
        ProjectGateDecision firstDecision = registry.Decisions[0];
        ProjectGateDecision secondDecision = registry.Decisions[1];
        string originalFirst = System.Text.Json.JsonSerializer.Serialize(firstDecision);
        string originalSecond = System.Text.Json.JsonSerializer.Serialize(secondDecision);

        ProjectGateReservationOverview initial =
            ProjectGateReservationOverviewBuilder.Build(registry, today);
        Assert(initial.Rows.Count == 3 &&
            initial.OpenCount == 3 && initial.LiftedCount == 0 &&
            initial.OverdueCount == 2 &&
            initial.MissingOwnerCount == 1 &&
            initial.Rows[0].ReservationId == secondReservation &&
            initial.Rows[1].ReservationId == firstReservation &&
            initial.Rows[2].ReservationId == thirdReservation,
            "Open overdue reservations must be sorted before future deadlines.");

        registry = ProjectGateEditor.AppendReservationEvent(
            registry, firstDecision.Id, secondReservation,
            ProjectGateReservationEventKind.Lifted, "QA",
            "L'ensemble des anomalies a été revu manuellement.",
            [evidence]);
        ProjectGateReservationOverview lifted =
            ProjectGateReservationOverviewBuilder.Build(registry, today);
        Assert(lifted.OpenCount == 2 && lifted.LiftedCount == 1 &&
            lifted.OverdueCount == 1 && lifted.MissingOwnerCount == 0 &&
            lifted.Rows[^1].ReservationId == secondReservation &&
            lifted.Rows[^1].EventCount == 1 &&
            !lifted.Rows[^1].IsOpen,
            "Lifting must update summary counts without deleting the reservation.");

        registry = ProjectGateEditor.AppendReservationEvent(
            registry, firstDecision.Id, secondReservation,
            ProjectGateReservationEventKind.Reopened, "QA",
            "Nouvelle anomalie détectée lors de la qualification.", []);
        ProjectGateReservationOverview reopened =
            ProjectGateReservationOverviewBuilder.Build(registry, today);
        ProjectGateReservationOverviewRow reopenedRow = reopened.Rows.Single(item =>
            item.ReservationId == secondReservation);
        Assert(reopened.OpenCount == 3 && reopened.LiftedCount == 0 &&
            reopened.OverdueCount == 2 && reopened.MissingOwnerCount == 1 &&
            reopenedRow.EventCount == 2 &&
            reopenedRow.LatestEvent?.Kind ==
                ProjectGateReservationEventKind.Reopened &&
            reopenedRow.DecisionId == firstDecision.Id &&
            reopenedRow.GateId == recetteId &&
            reopenedRow.HistoricalVerdict == ProjectGateVerdict.GoWithReservations,
            "Reopened reservations must retain their canonical origin and verdict.");

        registry = ProjectGateEditor.AppendReservationEvent(
            registry, firstDecision.Id, firstReservation,
            ProjectGateReservationEventKind.Lifted, "QA",
            "Rapport de tests entièrement relu et signé.",
            [evidence]);
        ProjectGateReservationOverview final =
            ProjectGateReservationOverviewBuilder.Build(registry, today);
        Assert(final.Rows.Count == 3 && final.OpenCount == 2 &&
            final.LiftedCount == 1 && final.OverdueCount == 1 &&
            final.MissingOwnerCount == 1 &&
            final.Rows.Count(item => item.ReservationId == firstReservation) == 1 &&
            final.Rows.Count(item => item.ReservationId == secondReservation) == 1 &&
            final.Rows.Count(item => item.ReservationId == thirdReservation) == 1 &&
            final.Rows.First().ReservationId == secondReservation &&
            final.Rows[^1].ReservationId == firstReservation,
            "Overviews must show one row per reservation, with open ones first.");
        ProjectGateReservationOverview later =
            ProjectGateReservationOverviewBuilder.Build(registry,
                new DateOnly(2026, 11, 21));
        Assert(later.OverdueCount == 2 &&
            final.OverdueCount == 1 &&
            registry.Decisions.Count == 2 &&
            System.Text.Json.JsonSerializer.Serialize(registry.Decisions[0]) ==
                originalFirst &&
            System.Text.Json.JsonSerializer.Serialize(registry.Decisions[1]) ==
                originalSecond,
            "Overdue counts depend on the explicit day, never mutate frozen approvals.");
    }

    /// <summary>Builds a human-approved conditional review for one gate.</summary>
    /// <param name="gate">Existing gate with criterion.</param>
    /// <param name="evidence">Selected and fingerprinted source.</param>
    /// <param name="reservations">Original reservations to freeze.</param>
    /// <returns>Human review fixture.</returns>
    private static ProjectGateReview BuildReview(ProjectGateDefinition gate,
        ProjectGateEvidenceSnapshot evidence,
        List<ProjectGateReservation> reservations) => new()
    {
        GateId = gate.Id,
        Verdict = ProjectGateVerdict.GoWithReservations,
        Actor = "Responsable projet",
        Scope = "Version 2.0 / Recette",
        Rationale = "Une revue humaine a confirmé les réserves restantes.",
        Evidence = [evidence],
        Reservations = reservations,
        Assessments = gate.Criteria.Select(item =>
            new ProjectGateCriterionAssessment
            {
                CriterionId = item.Id,
                Name = item.Name,
                Optional = item.Optional,
                Status = ProjectGateCriterionStatus.Unknown
            }).ToList()
    };

    /// <summary>Throws on a failed pure projection contract.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure context.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
