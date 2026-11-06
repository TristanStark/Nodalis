using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic persisted MEP regression: no project names, secrets or screenshots.
/// </summary>
internal static class ProjectDeploymentPersistenceSmokeTests
{
    /// <summary>Checks no-write reads, SHA-CAS and strict gate requalification.</summary>
    /// <param name="root">Disposable smoke root.</param>
    /// <returns>Completed regression.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "MEP-synthetic-v2");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync(
            "MEP synthetic");
        string app = Path.Combine(workspace,
            WorkspaceLayout.ApplicationsDirectoryName, "Sample");
        Directory.CreateDirectory(app);
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "MEP sample",
                Complexity = ProjectComplexity.Complex,
                Target = new ProjectCreationTarget
                {
                    ApplicationId = Guid.NewGuid(),
                    ApplicationName = "Sample",
                    ParentDirectory = app,
                    DisplayName = "Sample"
                }
            });
        ProjectDeploymentService service = new(workspace);
        ProjectDeploymentCatalog initial = await service.ListAsync(
            project.ProjectDirectory);
        Check(initial.Operations.Count == 0 &&
            initial.NextReference == "MEP-0001" &&
            !Directory.Exists(Path.Combine(project.ProjectDirectory,
                "Exploitation", "Déploiement", "Opérations")),
            "Reading a project must never create MEP folders.");

        ProjectDeploymentEntry created = await service.CreateAsync(
            project.ProjectDirectory, "Livraison exemple", "MEP-0001");
        Check(created.Operation.Attempts.Count == 0 &&
            created.SourceSha256.Length == 64 &&
            (await service.ListAsync(project.ProjectDirectory))
                .Operations.Count == 1,
            "A user-approved creation persists exactly one MEP.");
        await RejectAsync(() => service.CreateAsync(project.ProjectDirectory,
            "Autre", "MEP-0001"), "Stale reference must refuse creation.");

        string folder = Path.Combine(project.ProjectDirectory, "Exploitation");
        string procedure = Path.Combine(folder, "Plan.md");
        string rollback = Path.Combine(folder, "Retour.md");
        await File.WriteAllTextAsync(procedure, "# Plan de déploiement");
        await File.WriteAllTextAsync(rollback, "# Plan de retour");
        ProjectJourneySourceService sources = new(workspace);
        ProjectGateRegistryService gates = new(workspace);
        ProjectGateEvidenceSnapshot plan = await gates.CaptureEvidenceAsync(
            sources.GetReferenceForExistingFile(procedure));
        ProjectGateEvidenceSnapshot back = await gates.CaptureEvidenceAsync(
            sources.GetReferenceForExistingFile(rollback));

        ProjectGateRegistrySnapshot initialGates = await gates.ReadAsync(
            project.ProjectDirectory);
        ProjectGateRegistry registry = ProjectGateEditor.CreateGate(
            initialGates.Registry, "GO déploiement");
        Guid gateId = registry.Gates.Single().Id;
        registry = ProjectGateEditor.AddCriterion(registry, gateId,
            "Validation opérationnelle");
        ProjectGateCriterion criterion = registry.Gates.Single().Criteria.Single();
        registry = ProjectGateEditor.AppendDecision(registry,
            new ProjectGateReview
            {
                GateId = gateId,
                Verdict = ProjectGateVerdict.Go,
                Actor = "Relecteur",
                Scope = "Application A",
                Rationale = "Conditions validées humainement par le responsable.",
                Version = "2.0.0",
                Environment = "PROD",
                Assessments = [new ProjectGateCriterionAssessment
                {
                    CriterionId = criterion.Id,
                    Name = criterion.Name,
                    Optional = criterion.Optional,
                    Status = ProjectGateCriterionStatus.Conform
                }],
                Evidence = [plan, back]
            });
        await gates.SaveAsync(project.ProjectDirectory,
            registry, initialGates.Sha256);
        Guid decisionId = registry.Decisions.Single().Id;
        ProjectDeploymentAttempt first = new()
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartedUtc = created.Operation.CreatedUtc.AddMinutes(1),
            StartedBy = "Opérateur",
            Rationale = "Première tentative déclarée et qualifiée.",
            ObservedVersionBefore = "1.9.0",
            TargetVersion = "2.0.0",
            Environment = "PROD",
            Scope = "Application A",
            Procedure = plan,
            RollbackProcedure = back,
            GateDecisionId = decisionId,
            AuthorizationReviewedBy = "Relecteur",
            AuthorizationRationale = "Révision explicite du gate avant MEP."
        };
        ProjectDeploymentOperation starting =
            ProjectDeploymentEditor.StartAttempt(created.Operation, first);
        ProjectDeploymentEntry firstSaved = await service.SaveAsync(
            project.ProjectDirectory, created, starting);
        Check((await service.ListAsync(project.ProjectDirectory))
            .Operations.Single().Operation.Attempts.Count == 1,
            "Explicit attempt must persist under the original operation.");
        await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
            created, starting), "Stale SHA must reject overwrite.");
        await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
            firstSaved, firstSaved.Operation with { Attempts = [] }),
            "Historic attempt cannot be removed.");

        ProjectDeploymentOperation finished = ProjectDeploymentEditor.AppendEvent(
            firstSaved.Operation, new ProjectDeploymentAttemptEvent
            {
                Id = Guid.NewGuid(),
                AttemptId = first.Id,
                Kind = ProjectDeploymentEventKind.FinishedInterrupted,
                Actor = "Opérateur",
                Rationale = "Tentative interrompue et état à vérifier.",
                RecordedUtc = first.StartedUtc.AddMinutes(2)
            });
        ProjectDeploymentEntry closed = await service.SaveAsync(
            project.ProjectDirectory, firstSaved, finished);
        await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
            closed, closed.Operation with { Events = [] }),
            "Historic event cannot be removed.");

        ProjectDeploymentAttempt mismatch = first with
        {
            Id = Guid.NewGuid(),
            Number = 2,
            StartedUtc = first.StartedUtc.AddMinutes(3),
            ObservedVersionBefore = "1.9.0",
            TargetVersion = "2.0.1"
        };
        ProjectDeploymentOperation bad = ProjectDeploymentEditor.StartAttempt(
            closed.Operation, mismatch);
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, closed, bad),
            "Approval for 2.0.0 must never silently approve 2.0.1.");
        await File.AppendAllTextAsync(procedure, "\n# Différence");
        ProjectDeploymentOperation stale = ProjectDeploymentEditor.StartAttempt(
            closed.Operation, mismatch with { TargetVersion = "2.0.0" });
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, closed, stale),
            "Procedure changed after GO invalidates source proof.");
        Check((await service.ListAsync(project.ProjectDirectory))
            .Operations.Single().Operation.Attempts.Count == 1,
            "Rejected attempts must not corrupt persisted ledger.");
    }

    /// <summary>Verifies a rejected persisted action.</summary>
    /// <param name="action">Action which must fail.</param>
    /// <param name="context">Test diagnostic.</param>
    /// <returns>Completed assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(context);
    }

    /// <summary>Asserts an expected business invariant.</summary>
    /// <param name="condition">Boolean predicate.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
