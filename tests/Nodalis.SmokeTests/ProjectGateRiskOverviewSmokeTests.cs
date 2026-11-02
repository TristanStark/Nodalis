using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Tasks;

/// <summary>
/// End-to-end read-only Gate/reservation/RIS dashboard regression with
/// missing sources, human measure status and historic linked task references.
/// </summary>
internal static class ProjectGateRiskOverviewSmokeTests
{
    /// <summary>Exercises project-scoped snapshots without mutating documents.</summary>
    /// <param name="root">Smoke workspace parent folder.</param>
    /// <returns>Completed cross-register assertions.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Gate-risk-overview-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Gate risk summary QA");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Overview");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                MakeProject("Current", appId, app));
            ProjectCreationResult foreign = await creator.CreateAsync(
                MakeProject("Foreign", appId, app));
            ProjectRiskRegistryService risks = new(workspace);
            WorkspaceTaskService tasks = new(workspace);
            ProjectGateRegistryService gates = new(workspace);

            ProjectRiskEntry first = await risks.CreateAsync(
                project.ProjectDirectory, "Contrôle de recette incomplet");
            ProjectRiskEntry orphan = await risks.CreateAsync(
                project.ProjectDirectory, "Instabilité du service");
            ProjectRiskEntry foreignRisk = await risks.CreateAsync(
                foreign.ProjectDirectory, "Source externe");

            ProjectRiskEntry planned = await risks.PlanTreatmentAsync(
                project.ProjectDirectory, first,
                new ProjectRiskTreatmentPlan(
                    ProjectRiskTreatmentStrategy.Reduce,
                    "Responsable de la qualification",
                    "Contrôler les tests avant la bascule de production.",
                    "Réduire les impacts d'une panne détectée.",
                    "Réactiver la précédente version en cas d'incident."),
                "Comité recette",
                "Stratégie de traitement retenue lors de la revue métier.");
            ProjectRiskEntry blocked = await risks.UpdateMeasureAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.Blocked,
                "Responsable recette",
                "La procédure de qualification reste indisponible.",
                null);
            TaskItem task = await tasks.CreateTaskAsync(
                project.ProjectDirectory, "Préparer les jeux de tests",
                new TaskMetadataUpdate());
            ProjectRiskEntry withTask = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, blocked,
                ProjectRiskMeasureKind.Prevention,
                task.PersistentId!.Value, ProjectRiskTaskLinkAction.Link,
                "Responsable recette",
                "Cette tâche contribue à la prévention du risque identifié.");

            ProjectGateRegistrySnapshot original =
                await gates.ReadAsync(project.ProjectDirectory);
            ProjectGateRegistry created = ProjectGateEditor.CreateGate(
                original.Registry, "Gate de recette");
            Guid gateId = created.Gates.Single().Id;
            created = ProjectGateEditor.AddCriterion(
                created, gateId, "Tests et preuves revus");
            ProjectGateCriterion criterion = created.Gates.Single()
                .Criteria.Single();
            string evidenceFile = Path.Combine(project.ProjectDirectory,
                "Preuve.md");
            await File.WriteAllTextAsync(evidenceFile,
                "# Validation\nDécision humaine conservée séparément.");
            ProjectGateEvidenceSnapshot proof = await gates.CaptureEvidenceAsync(
                new ProjectJourneySourceService(workspace)
                    .GetReferenceForExistingFile(evidenceFile));
            Guid r1 = Guid.NewGuid();
            Guid r2 = Guid.NewGuid();
            Guid r3 = Guid.NewGuid();
            ProjectGateRegistry reviewed = ProjectGateEditor.AppendDecision(
                created, new ProjectGateReview
                {
                    GateId = gateId,
                    Verdict = ProjectGateVerdict.GoWithReservations,
                    Actor = "Comité de recette",
                    Scope = "Livraison de la version candidate",
                    Rationale = "Les réserves demeurent sous contrôle humain.",
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
                            Id = r1,
                            Description = "Vérifier les mesures correctives",
                            Owner = "Équipe recette",
                            DueDate = new DateOnly(2026, 11, 1)
                        },
                        new ProjectGateReservation
                        {
                            Id = r2,
                            Description = "Revoir les justificatifs restants"
                        },
                        new ProjectGateReservation
                        {
                            Id = r3,
                            Description = "Confirmer la couverture du support",
                            Owner = "Exploitation"
                        }
                    ]
                });
            ProjectGateRegistrySnapshot saved = await gates.SaveAsync(
                project.ProjectDirectory, reviewed, original.Sha256);
            Guid decisionId = saved.Registry.Decisions.Single().Id;
            const string actor = "Comité de recette";
            const string reason = "Risque identifié comme cause de la réserve.";
            ProjectGateRegistry link = ProjectGateEditor.AppendRiskLinkEvent(
                saved.Registry, decisionId, r1, withTask.Card.Id,
                ProjectGateReservationRiskLinkKind.Linked, actor, reason);
            link = ProjectGateEditor.AppendRiskLinkEvent(
                link, decisionId, r1, orphan.Card.Id,
                ProjectGateReservationRiskLinkKind.Linked, actor, reason);
            link = ProjectGateEditor.AppendRiskLinkEvent(
                link, decisionId, r3, orphan.Card.Id,
                ProjectGateReservationRiskLinkKind.Linked, actor, reason);
            ProjectGateRegistrySnapshot linked = await gates.SaveAsync(
                project.ProjectDirectory, link, saved.Sha256);
            ProjectGateRegistry lifted = ProjectGateEditor.AppendReservationEvent(
                linked.Registry, decisionId, r3,
                ProjectGateReservationEventKind.Lifted, actor,
                "La réserve est levée après revue indépendante des preuves.",
                [proof]);
            ProjectGateRegistrySnapshot final = await gates.SaveAsync(
                project.ProjectDirectory, lifted, linked.Sha256);

            ProjectRiskCatalog beforeDelete = await risks.ListAsync(
                project.ProjectDirectory);
            TaskCollection taskCatalog = await tasks.RefreshAsync();
            ProjectGateRiskOverview prior =
                ProjectGateRiskOverviewBuilder.Build(final.Registry,
                    beforeDelete, new DateOnly(2026, 11, 2), taskCatalog);
            Assert(prior.Rows.Count == 3 &&
                prior.OpenCount == 2 &&
                prior.LiftedCount == 1 &&
                prior.OpenWithoutRiskCount == 1 &&
                prior.OpenOrphanCount == 0 &&
                prior.OpenBlockedCount == 1 &&
                prior.OpenUnassessedCount == 1,
                "Summary must compute distinct human states from one snapshot.");
            ProjectGateRiskOverviewRow current = prior.Rows.Single(row =>
                row.Reservation.ReservationId == r1);
            ProjectGateRiskOverviewRiskRow firstRisk = current.Risks.Single(row =>
                row.RiskId == first.Card.Id);
            Assert(current.Reservation.IsOpen && current.Reservation.IsOverdue &&
                current.Risks.Count == 2 &&
                firstRisk.BlockedMeasures == 1 &&
                firstRisk.UnfinishedMeasures == 3 &&
                firstRisk.LinkedTaskReferences == 1 &&
                firstRisk.TaskProvenance.VerifiedSourceCount == 1 &&
                firstRisk.TaskProvenance.ProblemSourceCount == 0 &&
                firstRisk.TaskProvenance.CheckedCheckboxCount == 0 &&
                firstRisk.TaskProvenance.HumanCompletedMeasures == 0 &&
                firstRisk.TaskProvenance.DeclaredCompletionEvidence == 0 &&
                prior.OpenTaskSourceIssueCount == 0 &&
                firstRisk.HasTreatmentPlan &&
                firstRisk.InitialSeverity == ProjectRiskSeverity.Unassessed &&
                firstRisk.ResidualSeverity == ProjectRiskSeverity.Unassessed &&
                firstRisk.RelativePath == withTask.RelativePath &&
                current.Reservation.HistoricalVerdict ==
                    ProjectGateVerdict.GoWithReservations,
                "Blocked progress, task refs and unassessed severity remain independent.");

            Assert(!prior.Rows.Single(row =>
                    row.Reservation.ReservationId == r3).Reservation.IsOpen &&
                prior.Rows.Single(row =>
                    row.Reservation.ReservationId == r3).Risks.Count == 1 &&
                !prior.Rows.Single(row =>
                    row.Reservation.ReservationId == r3).HasBlockedMeasures,
                "Lifted reservations retain their active risk association.");

            string sourceRisk = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", withTask.Card.Reference + ".md");
            string sourceTask = Path.Combine(project.ProjectDirectory, "Tâches.md");
            byte[] riskBytes = await File.ReadAllBytesAsync(sourceRisk);
            byte[] taskBytes = await File.ReadAllBytesAsync(sourceTask);
            string gateFile = Path.Combine(project.ProjectDirectory,
                ProjectGateRegistryService.RegistryFileName);
            byte[] gateBytes = await File.ReadAllBytesAsync(gateFile);
            ProjectGateRiskOverview again =
                ProjectGateRiskOverviewBuilder.Build(final.Registry,
                    beforeDelete, new DateOnly(2026, 11, 2));
            Assert(again.Rows.Count == prior.Rows.Count &&
                (await File.ReadAllBytesAsync(sourceRisk))
                    .SequenceEqual(riskBytes) &&
                (await File.ReadAllBytesAsync(sourceTask))
                    .SequenceEqual(taskBytes) &&
                (await File.ReadAllBytesAsync(gateFile))
                    .SequenceEqual(gateBytes),
                "Repeated overview projections must not change any source.");

            // A checked Markdown checkbox cannot complete a blocked human
            // measure even though its source remains uniquely resolved.
            await tasks.SetCompletedAsync(task, true);
            TaskCollection checkedTasks = await tasks.RefreshAsync();
            ProjectGateRiskOverview checkedOverview =
                ProjectGateRiskOverviewBuilder.Build(final.Registry,
                    beforeDelete, new DateOnly(2026, 11, 2), checkedTasks);
            ProjectGateRiskOverviewRiskRow checkedRisk =
                checkedOverview.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks.Single(row =>
                    row.RiskId == first.Card.Id);
            Assert(checkedRisk.TaskProvenance.CheckedCheckboxCount == 1 &&
                checkedRisk.TaskProvenance.HumanCompletedMeasures == 0 &&
                checkedRisk.BlockedMeasures == 1,
                "A checked source cannot qualify a RIS treatment measure.");

            // A duplicate marker on another project is deliberately removed
            // from linkable TaskItems. Typed diagnostics preserve ambiguity.
            TaskItem foreignTask = await tasks.CreateTaskAsync(
                foreign.ProjectDirectory, "Tâche en conflit de GUID",
                new TaskMetadataUpdate());
            string foreignTasksFile = Path.Combine(
                foreign.ProjectDirectory, "Tâches.md");
            string originalForeignText =
                await File.ReadAllTextAsync(foreignTasksFile);
            string replacedForeignText = originalForeignText.Replace(
                foreignTask.PersistentId!.Value.ToString("D"),
                task.PersistentId!.Value.ToString("D"),
                StringComparison.OrdinalIgnoreCase);
            Assert(replacedForeignText != originalForeignText,
                "Fixture must inject a duplicate persistent marker.");
            await File.WriteAllTextAsync(foreignTasksFile, replacedForeignText);
            TaskCollection duplicateTasks = await tasks.RefreshAsync();
            ProjectGateRiskOverview duplicateTaskOverview =
                ProjectGateRiskOverviewBuilder.Build(final.Registry,
                    beforeDelete, new DateOnly(2026, 11, 2), duplicateTasks);
            Assert(duplicateTasks.ConflictingPersistentIds.Contains(
                    task.PersistentId!.Value) &&
                duplicateTaskOverview.OpenTaskSourceIssueCount == 1 &&
                duplicateTaskOverview.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks.Single(row =>
                    row.RiskId == first.Card.Id).TaskProvenance.TaskSources
                    .Single().State == ProjectGateTaskSourceState.Ambiguous,
                "Duplicate global task IDs must not look like missing sources.");

            File.Delete(foreignTasksFile);
            TaskCollection restoredTasks = await tasks.RefreshAsync();
            ProjectGateRiskOverview restored =
                ProjectGateRiskOverviewBuilder.Build(final.Registry,
                    beforeDelete, new DateOnly(2026, 11, 2), restoredTasks);
            Assert(restored.OpenTaskSourceIssueCount == 0 &&
                restored.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks.Single(row =>
                    row.RiskId == first.Card.Id).TaskProvenance
                    .VerifiedSourceCount == 1,
                "Removing the duplicate restores unique source provenance.");

            string orphanSource = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", orphan.Card.Reference + ".md");
            File.Delete(orphanSource);
            ProjectRiskCatalog afterDelete = await risks.ListAsync(
                project.ProjectDirectory);
            ProjectGateRiskOverview missing =
                ProjectGateRiskOverviewBuilder.Build(final.Registry,
                    afterDelete, new DateOnly(2026, 11, 2));
            ProjectGateRiskOverviewRow orphanRow = missing.Rows.Single(row =>
                row.Reservation.ReservationId == r1);
            ProjectGateRiskOverviewRow liftedRow = missing.Rows.Single(row =>
                row.Reservation.ReservationId == r3);
            Assert(missing.OpenOrphanCount == 1 &&
                orphanRow.OrphanCount == 1 &&
                orphanRow.Risks.Single(row =>
                    row.RiskId == orphan.Card.Id).RelativePath is null &&
                liftedRow.OrphanCount == 1 && !liftedRow.Reservation.IsOpen &&
                missing.OpenWithoutRiskCount == 1 &&
                !missing.Rows.Any(row =>
                    row.Risks.Any(risk => risk.RiskId == foreignRisk.Card.Id)),
                "Missing and foreign RIS cannot be turned into invented sources.");

            ProjectGateRegistry unlink =
                ProjectGateEditor.AppendRiskLinkEvent(final.Registry,
                    decisionId, r1, orphan.Card.Id,
                    ProjectGateReservationRiskLinkKind.Unlinked, actor,
                    "La source a disparu, le lien est dissocié explicitement.");
            ProjectGateRegistrySnapshot afterUnlink = await gates.SaveAsync(
                project.ProjectDirectory, unlink, final.Sha256);
            ProjectGateRiskOverview unlinked =
                ProjectGateRiskOverviewBuilder.Build(afterUnlink.Registry,
                    await risks.ListAsync(project.ProjectDirectory),
                    new DateOnly(2026, 11, 2));
            Assert(unlinked.OpenOrphanCount == 0 &&
                unlinked.Rows.Single(row =>
                    row.Reservation.ReservationId == r1)
                    .HistoricalUnlinks == 1 &&
                unlinked.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks.Count == 1 &&
                unlinked.Rows.Single(row =>
                    row.Reservation.ReservationId == r3).OrphanCount == 1,
                "Explicit unlink must clear only its own active relation.");

            // Duplicating a canonical source invalidates its target without
            // creating a new link or interfering with frozen Gate history.
            // Completion is an explicit reviewed event with declarative
            // evidence, regardless of the corresponding checkbox state.
            ProjectRiskEntry qualified = await risks.UpdateMeasureAsync(
                project.ProjectDirectory, withTask,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.Completed,
                "Responsable recette",
                "La qualification de la mesure a été explicitement revue.",
                "Compte rendu de revue signé par le responsable de recette.");
            ProjectGateRiskOverview qualifiedOverview =
                ProjectGateRiskOverviewBuilder.Build(afterUnlink.Registry,
                    await risks.ListAsync(project.ProjectDirectory),
                    new DateOnly(2026, 11, 2), restoredTasks);
            ProjectGateRiskTaskProvenance evidence =
                qualifiedOverview.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks.Single(row =>
                    row.RiskId == qualified.Card.Id).TaskProvenance;
            Assert(evidence.HumanCompletedMeasures == 1 &&
                evidence.DeclaredCompletionEvidence == 1,
                "Only a reviewed completed-measure event counts as qualified.");

            File.Delete(sourceTask);
            TaskCollection missingTasks = await tasks.RefreshAsync();
            ProjectGateRiskOverview missingTaskOverview =
                ProjectGateRiskOverviewBuilder.Build(afterUnlink.Registry,
                    await risks.ListAsync(project.ProjectDirectory),
                    new DateOnly(2026, 11, 2), missingTasks);
            ProjectGateRiskTaskProvenance missingTask =
                missingTaskOverview.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks.Single(row =>
                    row.RiskId == first.Card.Id).TaskProvenance;
            Assert(missingTask.TaskSources.Single().State ==
                    ProjectGateTaskSourceState.Missing &&
                missingTask.HumanCompletedMeasures == 1 &&
                missingTaskOverview.OpenTaskSourceIssueCount == 1,
                "A missing source cannot erase human qualification history.");

            string copy = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", "copy-conflicting-risk.md");
            await File.WriteAllTextAsync(copy,
                await File.ReadAllTextAsync(sourceRisk));
            ProjectGateRiskOverview duplicates =
                ProjectGateRiskOverviewBuilder.Build(afterUnlink.Registry,
                    await risks.ListAsync(project.ProjectDirectory),
                    new DateOnly(2026, 11, 2));
            Assert(duplicates.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).OrphanCount == 1 &&
                duplicates.OpenOrphanCount == 1 &&
                duplicates.Rows.Single(row =>
                    row.Reservation.ReservationId == r1).Risks
                    .Single().RelativePath is null,
                "Duplicate RIS IDs must become nonnavigable orphan links.");

            Assert((await gates.ReadAsync(project.ProjectDirectory))
                .Registry.Decisions.Single().Verdict ==
                    ProjectGateVerdict.GoWithReservations,
                "No dashboard computation ever changes a human verdict.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Builds an isolated complex project under one application.</summary>
    /// <param name="name">Project label.</param>
    /// <param name="appId">Application identity.</param>
    /// <param name="app">Application folder.</param>
    /// <returns>Fixture request.</returns>
    private static ProjectCreationRequest MakeProject(
        string name, Guid appId, string app) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Overview",
            ParentDirectory = app,
            DisplayName = "Overview"
        }
    };

    /// <summary>Throws for broken dashboard invariant.</summary>
    /// <param name="valid">Verified requirement.</param>
    /// <param name="reason">Test failure diagnostic.</param>
    private static void Assert(bool valid, string reason)
    {
        if (!valid)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
