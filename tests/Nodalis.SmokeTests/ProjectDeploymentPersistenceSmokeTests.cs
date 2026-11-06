using Nodalis.Core.Domain;
using Nodalis.Core.Markdown;
using Nodalis.Core.Tests;
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

        // Read-only preflight includes project risks even when the gate matches.
        ProjectRiskRegistryService risks = new(workspace);
        await risks.CreateAsync(project.ProjectDirectory,
            "Risque synthétique de préparation");
        ProjectDeploymentPreflightService preflight = new(workspace);
        ProjectDeploymentPreflightReport beforeReview =
            await preflight.ReviewAsync(
                project.ProjectDirectory, firstSaved, first.Id);
        Check(beforeReview.HistoricalGateConditionsMatch &&
            beforeReview.ExplicitHumanReviewRequired &&
            beforeReview.RisksFound >= 1 &&
            beforeReview.Findings.Any(item => item.Area ==
                ProjectDeploymentPreflightArea.Risks) &&
            beforeReview.Findings.Any(item => item.Area ==
                ProjectDeploymentPreflightArea.OperationalChecks),
            "An unchanged GO still needs human risk and operational review.");

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

        ProjectDeploymentPreflightReport changedSources =
            await preflight.ReviewAsync(
                project.ProjectDirectory, closed, first.Id);
        Check(!changedSources.HistoricalGateConditionsMatch &&
            changedSources.Findings.Any(item =>
                item.Area == ProjectDeploymentPreflightArea.Procedure ||
                item.Area == ProjectDeploymentPreflightArea.Gate),
            "Changed proof or procedure must invalidate the historical match.");
        await RejectAsync(() => preflight.ReviewAsync(
            project.ProjectDirectory, firstSaved, first.Id),
            "Preflight must reject MEP source revisions that became stale.");

        // Refusal of the transfer after an interrupted MEP is recorded
        // explicitly; no transfer success is inferred from a failed attempt.
        ProjectDeploymentHandoverReview refusal = new()
        {
            Id = Guid.NewGuid(),
            AttemptId = first.Id,
            RecordedUtc = first.StartedUtc.AddMinutes(3),
            Actor = "Responsable MEP",
            Recipient = "Exploitation",
            Outcome = ProjectDeploymentHandoverOutcome.Refused,
            Rationale = "Stabilisation non concluante, transfert refusé.",
            Checks = ProjectDeploymentHandoverValidation.RequiredAreas.Select(
                area => new ProjectDeploymentHandoverCheck
                {
                    Area = area,
                    Status = ProjectDeploymentHandoverStatus.NotVerified,
                    Note = "Qualification restant à réaliser."
                }).ToList()
        };
        ProjectDeploymentOperation handoverDraft =
            ProjectDeploymentEditor.ReviewHandover(closed.Operation, refusal);
        ProjectDeploymentEntry savedTransfer = await service.SaveAsync(
            project.ProjectDirectory, closed, handoverDraft);
        Check(savedTransfer.Operation.HandoverReviews?.Single().Id == refusal.Id,
            "Refused transfer must survive project-local Markdown persistence.");

        // An apparently valid source path and forged SHA cannot authenticate
        // a transfer's evidence, even when the decision itself is well-formed.
        ProjectDeploymentHandoverReview forged = refusal with
        {
            Id = Guid.NewGuid(),
            Checks = refusal.Checks.Select(check => check with
            {
                Evidence = check.Area == ProjectDeploymentHandoverArea.Monitoring
                    ? back with { Sha256 = new string('C', 64) }
                    : null
            }).ToList()
        };
        ProjectDeploymentOperation forgedDraft =
            ProjectDeploymentEditor.ReviewHandover(
                savedTransfer.Operation, forged);
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, savedTransfer, forgedDraft),
            "Forged or stale handover evidence must be refused at commit time.");

        await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
            savedTransfer, savedTransfer.Operation with
            {
                HandoverReviews = []
            }), "Historic handover cannot be deleted.");
        await RejectAsync(() => service.SaveAsync(project.ProjectDirectory,
            savedTransfer, savedTransfer.Operation with
            {
                HandoverReviews = [refusal with
                    { Outcome = ProjectDeploymentHandoverOutcome.Deferred }]
            }), "Historic handover verdict cannot be rewritten.");

        // A REC passage is a real project-owned canonical source. No
        // independent MEP test register is invented for post-deployment checks.
        string testsFolder = Path.Combine(project.ProjectDirectory,
            "Tests", "Recette");
        Directory.CreateDirectory(testsFolder);
        Guid testId = Guid.NewGuid();
        string testMarkdown = "## TEST-0001 — Vérification après MEP\n" +
            MarkdownBlockIdentityCodec.FormatMarker(testId) +
            "\n### Attendu\n- Comportement vérifié par un humain\n";
        string testPath = Path.Combine(testsFolder, "Cahier.md");
        await File.WriteAllTextAsync(testPath, testMarkdown);
        ProjectTestDefinition definition = ProjectTestDefinitionCodec
            .Inspect(testMarkdown).Tests.Single();
        Check(definition.CanFreeze && definition.Id == testId,
            "Synthetic test definition must be canonical.");
        ProjectTestCampaignService recService = new(workspace);
        ProjectTestCampaignEntry recDraft = await recService.CreateAsync(
            project.ProjectDirectory, "Contrôles après MEP", "REC-0001");
        ProjectTestPassage passage = new()
        {
            Id = Guid.NewGuid(),
            Number = 1,
            Environment = "PROD",
            ApplicationVersion = "2.0.0",
            StartedBy = "Testeur",
            StartedUtc = first.StartedUtc.AddMinutes(2),
            Tests = [new ProjectTestFrozenDefinition
            {
                TestId = testId,
                Reference = definition.Reference,
                SourceRelativePath =
                    sources.GetReferenceForExistingFile(testPath),
                Markdown = definition.Markdown,
                Sha256 = definition.Sha256,
                HeadingLine = 1
            }],
            Results = [new ProjectTestResultEvent
            {
                Id = Guid.NewGuid(),
                TestId = testId,
                Outcome = ProjectTestOutcome.Failed,
                Actor = "Testeur",
                Rationale = "Contrôle réel non conforme.",
                RecordedUtc = first.StartedUtc.AddMinutes(3)
            }]
        };
        ProjectTestCampaign canonicalRec = recDraft.Campaign with
        {
            State = ProjectTestCampaignState.InProgress,
            Passages = [passage]
        };
        string recPath = Path.Combine(project.ProjectDirectory,
            "Tests", "Campagnes", "REC-0001.md");
        await File.WriteAllTextAsync(recPath,
            ProjectTestCampaignMarkdownCodec.Format(canonicalRec));
        ProjectTestCampaignEntry rec = (await recService.ListAsync(
            project.ProjectDirectory)).Campaigns.Single();

        ProjectDeploymentPostCheck post = new()
        {
            Id = Guid.NewGuid(),
            AttemptId = first.Id,
            CampaignId = rec.Campaign.Id,
            PassageId = passage.Id,
            CampaignSha256 = rec.SourceSha256,
            FrozenTests = 1,
            Passed = 0,
            Failed = 1,
            Blocked = 0,
            NotApplicable = 0,
            RecordedUtc = first.StartedUtc.AddMinutes(3),
            Actor = "Responsable REC",
            Rationale = "Contrôle post-déploiement et non-conformité à suivre.",
            Verdict = ProjectDeploymentPostCheckVerdict.ToReview
        };
        ProjectDeploymentOperation withCheck =
            ProjectDeploymentEditor.RecordPostCheck(
                savedTransfer.Operation, post);
        ProjectDeploymentEntry postSaved = await service.SaveAsync(
            project.ProjectDirectory, savedTransfer, withCheck);
        Check((await service.ListAsync(project.ProjectDirectory))
            .Operations.Single().Operation.PostChecks?.Single().Id == post.Id &&
            postSaved.Operation.HandoverReviews?.Single().Id == refusal.Id,
            "Post-deployment link persists while preserving the transfer.");
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, postSaved,
            postSaved.Operation with { PostChecks = [] }),
            "Historical post-deployment links cannot be removed.");
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, postSaved,
            postSaved.Operation with { PostChecks =
                [post with { Verdict = ProjectDeploymentPostCheckVerdict.Conform }] }),
            "Historical human assessments cannot be rewritten.");
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, postSaved,
            ProjectDeploymentEditor.RecordPostCheck(
                postSaved.Operation, post with
                {
                    Id = Guid.NewGuid(),
                    CampaignSha256 = new string('A', 64)
                })),
            "Forged historical REC SHA must be rejected.");
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, postSaved,
            ProjectDeploymentEditor.RecordPostCheck(
                postSaved.Operation, post with
                {
                    Id = Guid.NewGuid(),
                    CampaignSha256 = new string('B', 64),
                    IncidentIds = ["INC-" + Guid.NewGuid().ToString("D")]
                })),
            "Unknown incidents cannot be inferred as valid for a project.");
        // A changed campaign revision cannot be silently reused.
        string recChanged = ProjectTestCampaignMarkdownCodec.Format(
            canonicalRec with { Title = "Contrôles après MEP modifiés" });
        await File.WriteAllTextAsync(recPath, recChanged);
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, postSaved,
            ProjectDeploymentEditor.RecordPostCheck(
                postSaved.Operation, post with
                {
                    Id = Guid.NewGuid(),
                    CampaignSha256 = rec.SourceSha256,
                    PassageId = Guid.NewGuid()
                })),
            "Missing passage or outdated REC source must not be accepted.");

        // Close one failed MEP explicitly, preserving all earlier events.
        ProjectDeploymentClosure closure = new()
        {
            Id = Guid.NewGuid(),
            AttemptId = first.Id,
            Outcome = ProjectDeploymentOutcome.Failed,
            Actor = "Responsable",
            Rationale = "Bilan de clôture après interruption et qualification.",
            RecordedUtc = first.StartedUtc.AddMinutes(4)
        };
        ProjectDeploymentOperation closedDraft =
            ProjectDeploymentEditor.Close(postSaved.Operation, closure);
        ProjectDeploymentEntry savedClosure = await service.SaveAsync(
            project.ProjectDirectory, postSaved, closedDraft);
        Check(savedClosure.Operation.Closure?.Outcome ==
            ProjectDeploymentOutcome.Failed &&
            (await service.ListAsync(project.ProjectDirectory))
                .Operations.Single().Operation.Closure?.Id == closure.Id &&
            savedClosure.Operation.Events.Count == closed.Operation.Events.Count &&
            savedClosure.Operation.HandoverReviews?.Single().Id == refusal.Id &&
            savedClosure.Operation.PostChecks?.Single().Id == post.Id,
            "Closure must preserve attempts, transfer and REC post-check.");
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, savedClosure, savedClosure.Operation),
            "A frozen closure can never be written over.");
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, closed, closedDraft),
            "Old snapshot must not overwrite the persisted closure.");


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
