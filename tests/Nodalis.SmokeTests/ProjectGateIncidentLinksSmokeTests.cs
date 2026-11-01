using Nodalis.Core.Domain;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Tests audited reservation-to-canonical-incident relations and safe scope.
/// </summary>
internal static class ProjectGateIncidentLinksSmokeTests
{
    /// <summary>Runs the canonical incident linking and history regressions.</summary>
    /// <param name="root">Disposable root.</param>
    /// <returns>Completed smoke scenario.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Gate-incident-links");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("Gate incident QA");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "QA");
            Directory.CreateDirectory(app);
            FileSystemProjectCreator creator = new(workspace);
            Guid appId = Guid.NewGuid();
            ProjectCreationResult project = await creator.CreateAsync(
                Project("QA", appId, app));
            ProjectCreationResult foreign = await creator.CreateAsync(
                Project("Foreign", appId, app));
            string sheet = Path.Combine(project.ProjectDirectory,
                "Fiche de Maintenance.md");
            string otherSheet = Path.Combine(foreign.ProjectDirectory,
                "Fiche de Maintenance.md");
            string validId = MaintenanceMarkdownCodec.NewId(
                MaintenanceRecordKind.Incident);
            string otherId = MaintenanceMarkdownCodec.NewId(
                MaintenanceRecordKind.Incident);
            string markdown = MaintenanceMarkdownCodec.Append(
                "# Fiche de Maintenance\n",
                new MaintenanceRecord
                {
                    Kind = MaintenanceRecordKind.Incident,
                    Id = validId,
                    Date = new DateOnly(2026, 11, 1),
                    Summary = "Erreur de chargement qualifiée"
                });
            await File.WriteAllTextAsync(sheet, markdown);
            await File.WriteAllTextAsync(otherSheet, MaintenanceMarkdownCodec.Append(
                "# Fiche de Maintenance\n",
                new MaintenanceRecord
                {
                    Kind = MaintenanceRecordKind.Incident,
                    Id = otherId,
                    Date = new DateOnly(2026, 11, 1),
                    Summary = "Incident d'un autre projet"
                }));
            ProjectGateIncidentCatalogService incidents = new(workspace);
            ProjectGateIncidentCatalog catalog =
                await incidents.ListAsync(project.ProjectDirectory);
            Assert(catalog.Candidates.Count == 1 &&
                catalog.Candidates[0].Id == validId &&
                catalog.Candidates[0].RelativePath.Contains(
                    "Fiche de Maintenance.md", StringComparison.Ordinal) &&
                !catalog.Candidates.Any(item => item.Id == otherId),
                "Only a valid incident in the current project is proposed.");
            Assert(await File.ReadAllTextAsync(sheet) == markdown,
                "Incident selection cannot rewrite canonical Markdown.");

            ProjectGateRegistryService service = new(workspace);
            ProjectGateRegistrySnapshot original =
                await service.ReadAsync(project.ProjectDirectory);
            ProjectGateRegistry draft = ProjectGateEditor.CreateGate(
                original.Registry, "Decision test");
            Guid gateId = draft.Gates.Single().Id;
            draft = ProjectGateEditor.AddCriterion(draft, gateId, "Preuve vérifiée");
            ProjectGateDefinition gate = draft.Gates.Single();
            string evidencePath = Path.Combine(project.ProjectDirectory,
                "Preuve.md");
            await File.WriteAllTextAsync(evidencePath, "# Preuve\nValidée");
            ProjectGateEvidenceSnapshot proof = await service.CaptureEvidenceAsync(
                new ProjectJourneySourceService(workspace)
                    .GetReferenceForExistingFile(evidencePath));
            ProjectGateReview review = new()
            {
                GateId = gate.Id,
                Verdict = ProjectGateVerdict.GoWithReservations,
                Actor = "Responsable recette",
                Scope = "Campagne de tests 2.0",
                Rationale = "Réserve justifiée par un incident de maintenance.",
                Evidence = [proof],
                Assessments =
                [
                    new ProjectGateCriterionAssessment
                    {
                        CriterionId = gate.Criteria.Single().Id,
                        Name = gate.Criteria.Single().Name,
                        Status = ProjectGateCriterionStatus.Deviation
                    }
                ],
                Reservations =
                [
                    new ProjectGateReservation
                    {
                        Id = Guid.NewGuid(),
                        Description = "Réparer et requalifier le chargement"
                    }
                ]
            };
            ProjectGateRegistry approved = ProjectGateEditor.AppendDecision(
                draft, review);
            ProjectGateRegistrySnapshot saved = await service.SaveAsync(
                project.ProjectDirectory, approved, original.Sha256);
            Guid decisionId = saved.Registry.Decisions.Single().Id;
            Guid reservationId = saved.Registry.Decisions.Single()
                .Reservations.Single().Id;
            const string actor = "Responsable exploitation";
            const string reason = "Association vérifiée manuellement avec l'incident.";
            ProjectGateRegistry linked = ProjectGateEditor.AppendIncidentLinkEvent(
                saved.Registry, decisionId, reservationId, validId,
                ProjectGateReservationIncidentLinkKind.Linked, actor, reason);
            Assert(ProjectGateEditor.GetLinkedIncidentIds(
                    linked, decisionId, reservationId).SequenceEqual([validId]) &&
                saved.Registry.IncidentLinkEvents.Count == 0,
                "Linking must not change prior register snapshots.");

            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.AppendIncidentLinkEvent(
                    saved.Registry, decisionId, reservationId, otherId,
                    ProjectGateReservationIncidentLinkKind.Linked, actor, reason),
                saved.Sha256), "Cross-project incidents must not be linked.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendIncidentLinkEvent(linked, decisionId,
                    reservationId, validId,
                    ProjectGateReservationIncidentLinkKind.Linked, actor, reason)),
                "Duplicate incident association must be refused.");
            ProjectGateRegistrySnapshot persisted = await service.SaveAsync(
                project.ProjectDirectory, linked, saved.Sha256);
            Assert(persisted.Registry.Decisions.Single().Verdict ==
                    ProjectGateVerdict.GoWithReservations &&
                (await service.ReadAsync(project.ProjectDirectory))
                    .Registry.IncidentLinkEvents.Count == 1,
                "Audit links must survive a read and leave the GO verdict untouched.");
            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                persisted.Registry with { IncidentLinkEvents = [] }, persisted.Sha256),
                "Historical link events cannot be erased.");

            // The canonical incident may disappear: preserve history and allow
            // unrelated edits and explicit human unlink, not silent relinking.
            await File.WriteAllTextAsync(sheet, "# Incident absent\n");
            ProjectGateRegistry extra = ProjectGateEditor.AddCriterion(
                persisted.Registry, gateId, "Autre contrôle manuel");
            ProjectGateRegistrySnapshot edited = await service.SaveAsync(
                project.ProjectDirectory, extra, persisted.Sha256);
            ProjectGateRegistry unlinked = ProjectGateEditor.AppendIncidentLinkEvent(
                edited.Registry, decisionId, reservationId, validId,
                ProjectGateReservationIncidentLinkKind.Unlinked, actor,
                "Retrait manuel après disparition de la source.");
            ProjectGateRegistrySnapshot removed = await service.SaveAsync(
                project.ProjectDirectory, unlinked, edited.Sha256);
            Assert(ProjectGateEditor.GetLinkedIncidentIds(removed.Registry,
                    decisionId, reservationId).Count == 0 &&
                removed.Registry.IncidentLinkEvents.Count == 2 &&
                removed.Registry.Decisions.Single().Verdict ==
                    ProjectGateVerdict.GoWithReservations,
                "Unlink must preserve audited events and initial human verdict.");
            await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.AppendIncidentLinkEvent(removed.Registry,
                    decisionId, reservationId, validId,
                    ProjectGateReservationIncidentLinkKind.Linked, actor, reason),
                removed.Sha256), "Stale incident references must not be relinked.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates a complex fixture project.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="appId">Application identity.</param>
    /// <param name="directory">Application path.</param>
    /// <returns>Project request.</returns>
    private static ProjectCreationRequest Project(
        string name, Guid appId, string directory) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "QA",
            ParentDirectory = directory,
            DisplayName = "QA"
        }
    };

    /// <summary>Asserts a guarded operation is rejected.</summary>
    /// <param name="operation">Expected failure.</param>
    /// <param name="message">Test diagnostic.</param>
    /// <returns>Completion.</returns>
    private static async Task RejectAsync(Func<Task> operation, string message)
    {
        bool rejected = false;
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or KeyNotFoundException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Throws on a broken invariant.</summary>
    /// <param name="condition">Condition.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
