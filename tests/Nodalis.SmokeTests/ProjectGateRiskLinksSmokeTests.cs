using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Gate reservation/risk relationship regression: durable GUID ownership,
/// independent append-only audit, strict source verification and orphan unlink.
/// </summary>
internal static class ProjectGateRiskLinksSmokeTests
{
    /// <summary>Runs one complete verified project scenario.</summary>
    /// <param name="root">Disposable smoke parent.</param>
    /// <returns>Completed regression checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Gate-risk-links-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Gate risk associations");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "GateRiskQA");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                Project("Current", appId, app));
            ProjectCreationResult foreign = await creator.CreateAsync(
                Project("Foreign", appId, app));
            ProjectRiskRegistryService risks = new(workspace);
            ProjectRiskEntry localRisk = await risks.CreateAsync(
                project.ProjectDirectory, "Erreur lors du déploiement");
            ProjectRiskEntry secondRisk = await risks.CreateAsync(
                project.ProjectDirectory, "Rupture de disponibilité");
            ProjectRiskEntry externalRisk = await risks.CreateAsync(
                foreign.ProjectDirectory, "Incident externe");
            string riskPath = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", localRisk.Card.Reference + ".md");
            string originalRiskMarkdown = await File.ReadAllTextAsync(riskPath);
            ProjectRiskCatalog candidates =
                await risks.ListAsync(project.ProjectDirectory);
            Assert(candidates.Risks.Count == 2 &&
                candidates.Risks.Any(item =>
                    item.Card.Id == localRisk.Card.Id) &&
                !candidates.Risks.Any(item =>
                    item.Card.Id == externalRisk.Card.Id),
                "Gate candidates must derive from same-project canonical RIS.");

            ProjectGateRegistryService service = new(workspace);
            ProjectRiskGateBacklinkService backlinks = new(workspace);
            ProjectGateRegistrySnapshot original = await service.ReadAsync(
                project.ProjectDirectory);
            ProjectGateRegistry gates = ProjectGateEditor.CreateGate(
                original.Registry, "Contrôle avant mise en production");
            Guid gateId = gates.Gates.Single().Id;
            gates = ProjectGateEditor.AddCriterion(
                gates, gateId, "Tests de régression qualifiés");
            ProjectGateCriterion criterion = gates.Gates.Single()
                .Criteria.Single();
            string evidencePath = Path.Combine(project.ProjectDirectory,
                "Preuve.md");
            await File.WriteAllTextAsync(evidencePath,
                "# PV de recette\nTest indépendant du risque.");
            ProjectGateEvidenceSnapshot proof = await service.CaptureEvidenceAsync(
                new ProjectJourneySourceService(workspace)
                    .GetReferenceForExistingFile(evidencePath));
            ProjectGateRegistry reviewed = ProjectGateEditor.AppendDecision(
                gates, new ProjectGateReview
                {
                    GateId = gateId,
                    Verdict = ProjectGateVerdict.GoWithReservations,
                    Actor = "Responsable qualité",
                    Scope = "Livraison projet version de recette",
                    Rationale = "GO sous réserve explicite de la preuve du correctif.",
                    Evidence = [proof],
                    Assessments =
                    [
                        new ProjectGateCriterionAssessment
                        {
                            CriterionId = criterion.Id,
                            Name = criterion.Name,
                            Status = ProjectGateCriterionStatus.Deviation
                        }
                    ],
                    Reservations =
                    [
                        new ProjectGateReservation
                        {
                            Id = Guid.NewGuid(),
                            Description = "Qualifier les risques avant livraison"
                        }
                    ]
                });
            ProjectGateRegistrySnapshot saved = await service.SaveAsync(
                project.ProjectDirectory, reviewed, original.Sha256);
            Guid decisionId = saved.Registry.Decisions.Single().Id;
            Guid reservationId = saved.Registry.Decisions.Single()
                .Reservations.Single().Id;
            Assert(saved.Registry.RiskLinkEvents.Count == 0 &&
                ProjectGateEditor.GetLinkedRiskIds(
                    saved.Registry, decisionId, reservationId).Count == 0,
                "GO with reservations never auto-links a risk.");

            const string actor = "Comité de validation";
            const string reason = "Le risque explique la réserve sur la recette.";
            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.AppendRiskLinkEvent(
                    saved.Registry, decisionId, reservationId,
                    externalRisk.Card.Id,
                    ProjectGateReservationRiskLinkKind.Linked, actor, reason),
                saved.Sha256),
                "Foreign RIS GUID must be rejected by source verification.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendRiskLinkEvent(
                    saved.Registry, decisionId, Guid.NewGuid(),
                    localRisk.Card.Id,
                    ProjectGateReservationRiskLinkKind.Linked, actor, reason)),
                "A link cannot invent a historical reservation.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendRiskLinkEvent(
                    saved.Registry, decisionId, reservationId,
                    Guid.Empty, ProjectGateReservationRiskLinkKind.Linked,
                    actor, reason)),
                "Risk GUID must be nonempty.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendRiskLinkEvent(
                    saved.Registry, decisionId, reservationId,
                    localRisk.Card.Id, ProjectGateReservationRiskLinkKind.Unlinked,
                    actor, reason)),
                "Unlink without existing link must fail.");

            ProjectGateRegistry linked = ProjectGateEditor.AppendRiskLinkEvent(
                saved.Registry, decisionId, reservationId,
                localRisk.Card.Id, ProjectGateReservationRiskLinkKind.Linked,
                actor, reason);
            Assert(linked.RiskLinkEvents.Count == 1 &&
                linked.RiskLinkEvents[0].RiskId == localRisk.Card.Id &&
                linked.RiskLinkEvents[0].DecisionId == decisionId &&
                linked.RiskLinkEvents[0].ReservationId == reservationId &&
                ProjectGateEditor.GetLinkedRiskIds(
                    linked, decisionId, reservationId)
                    .SequenceEqual([localRisk.Card.Id]) &&
                saved.Registry.RiskLinkEvents.Count == 0,
                "One reviewed association must create exactly one event.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendRiskLinkEvent(
                    linked, decisionId, reservationId, localRisk.Card.Id,
                    ProjectGateReservationRiskLinkKind.Linked, actor, reason)),
                "Duplicate risk association is not a review event.");

            ProjectGateRegistrySnapshot first = await service.SaveAsync(
                project.ProjectDirectory, linked, saved.Sha256);
            ProjectRiskGateBacklinkCatalog reverse =
                await backlinks.ListAsync(project.ProjectDirectory, localRisk);
            Assert(reverse.RiskId == localRisk.Card.Id &&
                reverse.Links.Count == 1 &&
                reverse.Links[0].GateId == gateId &&
                reverse.Links[0].DecisionId == decisionId &&
                reverse.Links[0].ReservationId == reservationId &&
                reverse.Links[0].IsLinked &&
                reverse.Links[0].IsOpen &&
                reverse.Links[0].Verdict ==
                    ProjectGateVerdict.GoWithReservations,
                "Reverse RIS/Gate view must preserve original human verdict.");
            Assert(first.Registry.Decisions.Single().Verdict ==
                    ProjectGateVerdict.GoWithReservations &&
                first.Registry.ReservationEvents.Count == 0 &&
                first.Registry.IncidentLinkEvents.Count == 0 &&
                await File.ReadAllTextAsync(riskPath) == originalRiskMarkdown,
                "Linking does not edit original decision, reservation or RIS.");
            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                linked, saved.Sha256),
                "Gate source SHA must reject concurrent stale saves.");
            ProjectGateRegistry editedHistory = first.Registry with
            {
                RiskLinkEvents = first.Registry.RiskLinkEvents.Select(item =>
                    item with { Rationale = "Historique modifié illégalement." })
                    .ToList()
            };
            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                editedHistory, first.Sha256),
                "Existing association history must be append-only.");

            ProjectGateRegistry two = ProjectGateEditor.AppendRiskLinkEvent(
                first.Registry, decisionId, reservationId,
                secondRisk.Card.Id, ProjectGateReservationRiskLinkKind.Linked,
                actor, "La disponibilité nécessite une qualification séparée.");
            ProjectGateRegistrySnapshot second = await service.SaveAsync(
                project.ProjectDirectory, two, first.Sha256);
            Assert(ProjectGateEditor.GetLinkedRiskIds(
                    second.Registry, decisionId, reservationId).Count == 2,
                "Two independent project risks can be linked to one reservation.");
            ProjectGateRegistry withLift = ProjectGateEditor.AppendReservationEvent(
                second.Registry, decisionId, reservationId,
                ProjectGateReservationEventKind.Lifted, actor,
                "La réserve est levée après vérification humaine indépendante.",
                [proof]);
            ProjectGateRegistrySnapshot lifted = await service.SaveAsync(
                project.ProjectDirectory, withLift, second.Sha256);
            ProjectRiskGateBacklinkCatalog afterLift =
                await backlinks.ListAsync(project.ProjectDirectory, secondRisk);
            Assert(afterLift.Links.Count == 1 &&
                afterLift.Links[0].IsLinked &&
                !afterLift.Links[0].IsOpen &&
                afterLift.Links[0].Verdict ==
                    ProjectGateVerdict.GoWithReservations &&
                lifted.Registry.ReservationEvents.Count == 1,
                "Lifting a reservation must not unlink RIS or change verdict.");

            // Source removed AFTER approval must not block explicit unlink.
            File.Delete(riskPath);
            ProjectGateRegistry removed = ProjectGateEditor.AppendRiskLinkEvent(
                lifted.Registry, decisionId, reservationId,
                localRisk.Card.Id, ProjectGateReservationRiskLinkKind.Unlinked,
                actor, "Le document RIS a disparu, dissociation manuelle requise.");
            ProjectGateRegistrySnapshot third = await service.SaveAsync(
                project.ProjectDirectory, removed, lifted.Sha256);
            Assert(third.Registry.RiskLinkEvents.Count == 3 &&
                third.Registry.ReservationEvents.Count == 1 &&
                ProjectGateEditor.GetLinkedRiskIds(
                    third.Registry, decisionId, reservationId)
                    .SequenceEqual([secondRisk.Card.Id]),
                "Orphan unlink preserves earlier GUID and original human verdict.");
            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.AppendRiskLinkEvent(
                    third.Registry, decisionId, reservationId,
                    localRisk.Card.Id, ProjectGateReservationRiskLinkKind.Linked,
                    actor, "Relier une source supprimée doit rester interdit."),
                third.Sha256),
                "Removed risk source cannot be relinked.");

            ProjectRiskGateBacklinkCatalog afterUnlink =
                await backlinks.ListAsync(project.ProjectDirectory, secondRisk);
            Assert(afterUnlink.Links.Count == 1 &&
                afterUnlink.Links[0].IsLinked &&
                !afterUnlink.Links[0].IsOpen,
                "Orphan unlink never affects another risk's backlink.");

            // Explicitly copied RIS with duplicate GUID cannot be linked.
            string secondRiskFile = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", secondRisk.Card.Reference + ".md");
            string duplicatedRiskFile = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", "copie-risque.md");
            await File.WriteAllTextAsync(duplicatedRiskFile,
                await File.ReadAllTextAsync(secondRiskFile));
            ProjectRiskCatalog duplicates = await risks.ListAsync(
                project.ProjectDirectory);
            Assert(duplicates.Diagnostics.Count > 0 &&
                !duplicates.Risks.Any(item =>
                    item.Card.Id == secondRisk.Card.Id),
                "Duplicated canonical RIS GUID must be disqualified as target.");
            // Reopen a previous event, then attempt a NEW link to the now
            // duplicated GUID: save-time source check must reject it.
            ProjectGateRegistry unlinkedSecond =
                ProjectGateEditor.AppendRiskLinkEvent(third.Registry,
                    decisionId, reservationId, secondRisk.Card.Id,
                    ProjectGateReservationRiskLinkKind.Unlinked, actor,
                    "Deux sources RIS ont désormais un GUID identique.");
            ProjectGateRegistrySnapshot fourth = await service.SaveAsync(
                project.ProjectDirectory, unlinkedSecond, third.Sha256);
            await RejectAsync(() => service.SaveAsync(
                project.ProjectDirectory,
                ProjectGateEditor.AppendRiskLinkEvent(
                    fourth.Registry, decisionId, reservationId,
                    secondRisk.Card.Id, ProjectGateReservationRiskLinkKind.Linked,
                    actor, "Une cible RIS dupliquée n'est pas réutilisable."),
                fourth.Sha256),
                "Duplicated RIS source must be rejected at save-time.");

            Assert((await service.ReadAsync(project.ProjectDirectory))
                .Registry.Decisions.Single().Verdict ==
                ProjectGateVerdict.GoWithReservations,
                "No risk association ever changes the human Go/No-Go verdict.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates a complex project with preserved legacy risks table.</summary>
    /// <param name="name">Project display name.</param>
    /// <param name="appId">Application owner GUID.</param>
    /// <param name="application">Application directory.</param>
    /// <returns>Project creation request.</returns>
    private static ProjectCreationRequest Project(
        string name, Guid appId, string application) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "GateRiskQA",
            ParentDirectory = application,
            DisplayName = "GateRiskQA"
        }
    };

    /// <summary>Requires any unsafe operation to be rejected without writes.</summary>
    /// <param name="action">Command under test.</param>
    /// <param name="message">Expected invariant.</param>
    /// <returns>Completed negative assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or UnauthorizedAccessException or
            KeyNotFoundException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Fails on any broken Gate risk-link invariant.</summary>
    /// <param name="condition">Required state.</param>
    /// <param name="message">Diagnostic message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
