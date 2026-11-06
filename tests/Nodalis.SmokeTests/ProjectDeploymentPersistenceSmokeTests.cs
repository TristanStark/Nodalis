using Nodalis.Core.Domain;
using Nodalis.Core.Markdown;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Tests;
using Nodalis.Core.Tasks;
using Nodalis.Core.Milestones;
using Nodalis.Infrastructure.Tasks;
using Nodalis.Infrastructure.Milestones;
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
            Reservations = "Transférer la surveillance renforcée aux équipes.",
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

        // Canonical maintenance is the only incident source: no parallel
        // incident registry is created by the MEP feature.
        string maintenancePath = Path.Combine(project.ProjectDirectory,
            "Fiche de Maintenance.md");
        string maintenance = File.Exists(maintenancePath)
            ? await File.ReadAllTextAsync(maintenancePath)
            : "# Fiche de maintenance fictive\n";
        string incidentId = MaintenanceMarkdownCodec.NewId(
            MaintenanceRecordKind.Incident);
        maintenance = MaintenanceMarkdownCodec.Append(maintenance,
            new MaintenanceRecord
            {
                Kind = MaintenanceRecordKind.Incident,
                Id = incidentId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                Summary = "Incident synthétique relevé après MEP."
            });
        await File.WriteAllTextAsync(maintenancePath, maintenance);
        Check((await new ProjectGateIncidentCatalogService(workspace)
            .ListAsync(project.ProjectDirectory)).Candidates.Any(item =>
                item.Id == incidentId),
            "The maintenance incident must exist with a canonical stable ID.");

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
            IncidentIds = [incidentId],
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
        // A changed campaign revision cannot be silently reused.
        string recChanged = ProjectTestCampaignMarkdownCodec.Format(
            canonicalRec with { Title = "Contrôles après MEP modifiés" });
        await File.WriteAllTextAsync(recPath, recChanged);
        ProjectTestCampaignEntry updatedRec = (await recService.ListAsync(
            project.ProjectDirectory)).Campaigns.Single();
        await RejectAsync(() => service.SaveAsync(
            project.ProjectDirectory, postSaved,
            ProjectDeploymentEditor.RecordPostCheck(
                postSaved.Operation, post with
                {
                    Id = Guid.NewGuid(),
                    CampaignSha256 = updatedRec.SourceSha256,
                    IncidentIds = ["INC-" + Guid.NewGuid().ToString("N")]
                })),
            "A real but unrelated maintenance ID must be refused, even " +
            "when the REC revision is current.");

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

        // The MEP itself stays frozen, yet an explicitly transferred
        // obligation must remain operable after its business closure.
        ProjectDeploymentReservationService followup = new(workspace);
        string followupFolder = Path.Combine(project.ProjectDirectory,
            "Exploitation", "Déploiement", "Suivi");
        ProjectDeploymentReservationSnapshot emptyFollowup =
            await followup.ReadAsync(project.ProjectDirectory);
        Check(emptyFollowup.SourceSha256 is null &&
            !Directory.Exists(followupFolder) &&
            emptyFollowup.Ledger.Items.Count == 0,
            "Reading absent follow-up must not create folders or migrate MEP.");

        ProjectDeploymentReservationSource origin =
            ProjectDeploymentReservationOrigins.List(savedClosure.Operation)
                .Single(item => item.Id == refusal.Id);
        ProjectDeploymentReservation item = new()
        {
            Id = Guid.NewGuid(),
            Reference = "RES-0001",
            OperationId = savedClosure.Operation.Id,
            OriginKind = origin.Kind,
            OriginId = origin.Id,
            OriginSha256 = savedClosure.SourceSha256,
            OriginSummary = origin.Description,
            Title = "Surveillance exploitation",
            Description = "Contrôler les alertes et conserver la décision.",
            CreatedBy = "Responsable MEP",
            InitialOwner = "Équipe exploitation",
            CreatedUtc = closure.RecordedUtc.AddSeconds(1),
            DueDate = new DateOnly(2027, 1, 15)
        };
        ProjectDeploymentReservationSnapshot createdFollowup =
            await followup.AddAsync(project.ProjectDirectory,
                emptyFollowup, savedClosure, item);
        Check(createdFollowup.Ledger.Items.Single().Id == item.Id &&
            ProjectDeploymentReservationService.NextReference(
                createdFollowup.Ledger) == "RES-0002" &&
            (await service.ListAsync(project.ProjectDirectory)).Operations
                .Single().SourceSha256 == savedClosure.SourceSha256,
            "Reservation is tracked separately; sealed MEP bytes stay intact.");

        await RejectAsync(() => followup.AddAsync(
            project.ProjectDirectory, emptyFollowup, savedClosure,
            item with { Id = Guid.NewGuid() }),
            "A stale reservation registry revision must fail closed.");
        await RejectAsync(() => followup.AddAsync(
            project.ProjectDirectory, createdFollowup,
            savedClosure with
            {
                Operation = savedClosure.Operation with { Id = Guid.NewGuid() }
            },
            item with
            {
                Id = Guid.NewGuid(),
                Reference = "RES-0002",
                OperationId = Guid.NewGuid()
            }), "Foreign MEP owner must never create a reservation.");

        ProjectDeploymentReservationEvent taken = new()
        {
            Id = Guid.NewGuid(),
            ReservationId = item.Id,
            Action = ProjectDeploymentReservationAction.TakeInCharge,
            Actor = "Exploitant",
            Rationale = "Prise en charge explicite et vérifiée.",
            RecordedUtc = item.CreatedUtc.AddSeconds(1)
        };
        ProjectDeploymentReservationSnapshot inProgress =
            await followup.RecordAsync(project.ProjectDirectory,
                createdFollowup, taken);
        Check(ProjectDeploymentReservationCodec.GetState(
            inProgress.Ledger, item.Id).Status ==
                ProjectDeploymentReservationStatus.InProgress,
            "Taking charge is an explicit action, not inferred from MEP closure.");
        ProjectDeploymentReservationEvent assignment = taken with
        {
            Id = Guid.NewGuid(),
            Action = ProjectDeploymentReservationAction.Reassign,
            NewOwner = "Équipe de supervision",
            Rationale = "Reprise par l'équipe de supervision.",
            RecordedUtc = taken.RecordedUtc.AddSeconds(1)
        };
        ProjectDeploymentReservationSnapshot reassigned =
            await followup.RecordAsync(project.ProjectDirectory,
                inProgress, assignment);
        Check(ProjectDeploymentReservationCodec.GetState(
            reassigned.Ledger, item.Id).Owner == "Équipe de supervision",
            "Reassignment must be retained without overwriting original owner.");

        ProjectDeploymentReservationEvent resolved = taken with
        {
            Id = Guid.NewGuid(),
            Action = ProjectDeploymentReservationAction.Resolve,
            Actor = "Responsable exploitation",
            Rationale = "Contrôles stabilisés et acceptation humaine.",
            RecordedUtc = assignment.RecordedUtc.AddSeconds(1)
        };
        ProjectDeploymentReservationSnapshot completedFollowup =
            await followup.RecordAsync(project.ProjectDirectory,
                reassigned, resolved);
        Check(ProjectDeploymentReservationCodec.GetState(
            completedFollowup.Ledger, item.Id).Status ==
                ProjectDeploymentReservationStatus.Resolved &&
            completedFollowup.Ledger.Events.Count == 3,
            "Explicit human resolution is append-only.");
        await RejectAsync(() => followup.RecordAsync(
            project.ProjectDirectory, completedFollowup,
            resolved with
            {
                Id = Guid.NewGuid(),
                Action = ProjectDeploymentReservationAction.ProgressNote,
                RecordedUtc = resolved.RecordedUtc.AddSeconds(1)
            }), "Resolved reservation cannot silently receive progress events.");
        await RejectAsync(() => followup.RecordAsync(
            project.ProjectDirectory, inProgress,
            resolved with { Id = Guid.NewGuid() }),
            "Old source SHA cannot overwrite newer operational decisions.");
        await RejectAsync(() => followup.RecordAsync(
            project.ProjectDirectory, completedFollowup with
            {
                Ledger = completedFollowup.Ledger with
                {
                    Items = [item with { InitialOwner = "Falsifié" }]
                }
            }, resolved with
            {
                Id = Guid.NewGuid(),
                Action = ProjectDeploymentReservationAction.Reopen,
                RecordedUtc = resolved.RecordedUtc.AddSeconds(1)
            }), "Forged initial owner must be rejected even with current SHA.");

        ProjectDeploymentReservationEvent reopened = resolved with
        {
            Id = Guid.NewGuid(),
            Action = ProjectDeploymentReservationAction.Reopen,
            Rationale = "Incident de surveillance réapparu, réouvrir.",
            RecordedUtc = resolved.RecordedUtc.AddSeconds(1)
        };
        ProjectDeploymentReservationSnapshot reopenedFollowup =
            await followup.RecordAsync(project.ProjectDirectory,
                completedFollowup, reopened);
        ProjectDeploymentReservationSnapshot rereadFollowup =
            await followup.ReadAsync(project.ProjectDirectory);
        Check(rereadFollowup.SourceSha256 == reopenedFollowup.SourceSha256 &&
            ProjectDeploymentReservationCodec.GetState(
                rereadFollowup.Ledger, item.Id).Status ==
                ProjectDeploymentReservationStatus.Open &&
            rereadFollowup.Ledger.Events.Count == 4 &&
            (await service.ListAsync(project.ProjectDirectory)).Operations
                .Single().Operation.Closure?.Id == closure.Id,
            "Reopening preserves every prior decision and the closed MEP.");

        ProjectDeploymentReservationSource postOrigin =
            ProjectDeploymentReservationOrigins.List(savedClosure.Operation)
                .Single(source => source.Id == post.Id);
        ProjectDeploymentReservation secondItem = item with
        {
            Id = Guid.NewGuid(),
            Reference = "RES-0002",
            OriginKind = postOrigin.Kind,
            OriginId = postOrigin.Id,
            OriginSummary = postOrigin.Description,
            Title = "Requalifier l'anomalie de recette",
            Description = "Contrôle de la correction et du nouveau passage.",
            CreatedUtc = closure.RecordedUtc.AddSeconds(2)
        };
        ProjectDeploymentReservationSnapshot twoItems =
            await followup.AddAsync(project.ProjectDirectory,
                rereadFollowup, savedClosure, secondItem);
        IReadOnlyDictionary<Guid, ProjectDeploymentReservationState> states =
            ProjectDeploymentReservationCodec.GetStates(twoItems.Ledger);
        Check(states.Count == 2 &&
            states[item.Id].Status == ProjectDeploymentReservationStatus.Open &&
            states[item.Id].Owner == "Équipe de supervision" &&
            states[secondItem.Id].Status == ProjectDeploymentReservationStatus.Open &&
            twoItems.Ledger.Events.Count == 4 &&
            (await service.ListAsync(project.ProjectDirectory)).Operations
                .Single().SourceSha256 == savedClosure.SourceSha256,
            "Multiple human obligations from different source decisions " +
            "must keep independent statuses and an immutable closed MEP.");

        // Stable task/milestone identities come from their actual project
        // sources; linking never creates or changes their business state.
        WorkspaceTaskService taskService = new(workspace);
        TaskItem task = await taskService.CreateTaskAsync(
            project.ProjectDirectory, "Suivi de réserve exploitation",
            new TaskMetadataUpdate());
        WorkspaceMilestoneService milestoneService = new(workspace);
        MilestoneItem milestone = await milestoneService.AddAsync(
            project.ProjectDirectory,
            new MilestoneDraft { Name = "Stabilisation après production" });
        Check(task.PersistentId is Guid && milestone.Id != Guid.Empty,
            "Human links require an existing stable task and milestone.");
        ProjectDeploymentReservationTargetService targets = new(workspace);
        ProjectDeploymentReservationTarget taskTarget =
            (await targets.ListAsync(project.ProjectDirectory,
                ProjectDeploymentReservationRelationKind.Task))
            .Single(entry => entry.Id == task.PersistentId);
        ProjectDeploymentReservationTarget milestoneTarget =
            (await targets.ListAsync(project.ProjectDirectory,
                ProjectDeploymentReservationRelationKind.Milestone))
            .Single(entry => entry.Id == milestone.Id);
        ProjectDeploymentReservationRelationEvent relation = new()
        {
            Id = Guid.NewGuid(),
            ReservationId = item.Id,
            Kind = ProjectDeploymentReservationRelationKind.Task,
            TargetId = taskTarget.Id,
            SourceRelativePath = taskTarget.SourceRelativePath,
            Action = ProjectDeploymentReservationRelationAction.Link,
            Actor = "Responsable exploitation",
            Rationale = "Tâche de suivi déjà existante dans le projet.",
            RecordedUtc = secondItem.CreatedUtc.AddMinutes(1)
        };
        ProjectDeploymentReservationSnapshot taskLinked =
            await followup.RecordRelationAsync(project.ProjectDirectory,
                twoItems, relation);
        Check(ProjectDeploymentReservationRelations.Active(
            taskLinked.Ledger, item.Id).Single().TargetId == task.PersistentId &&
            taskLinked.Ledger.Relations?.Count == 1 &&
            (await taskService.GetTasksAsync(project.ProjectDirectory,
                includeCompleted: true)).Any(entry =>
                    entry.PersistentId == task.PersistentId && !entry.IsCompleted),
            "Linking a stable existing task may not complete or rewrite it.");
        await RejectAsync(() => followup.RecordRelationAsync(
            project.ProjectDirectory, taskLinked, relation with
            {
                Id = Guid.NewGuid(),
                RecordedUtc = relation.RecordedUtc.AddSeconds(1)
            }), "Duplicate active task links must not be created.");
        await RejectAsync(() => followup.RecordRelationAsync(
            project.ProjectDirectory, twoItems, relation with
            {
                Id = Guid.NewGuid(),
                RecordedUtc = relation.RecordedUtc.AddSeconds(1)
            }), "Stale reservation source SHA must reject relation writes.");
        await RejectAsync(() => followup.RecordRelationAsync(
            project.ProjectDirectory, taskLinked, relation with
            {
                Id = Guid.NewGuid(),
                TargetId = Guid.NewGuid(),
                RecordedUtc = relation.RecordedUtc.AddSeconds(1)
            }), "Missing or foreign task GUID must not be linked.");

        ProjectDeploymentReservationRelationEvent milestoneLink = relation with
        {
            Id = Guid.NewGuid(),
            Kind = ProjectDeploymentReservationRelationKind.Milestone,
            TargetId = milestoneTarget.Id,
            SourceRelativePath = milestoneTarget.SourceRelativePath,
            RecordedUtc = relation.RecordedUtc.AddSeconds(1)
        };
        ProjectDeploymentReservationSnapshot bothLinked =
            await followup.RecordRelationAsync(project.ProjectDirectory,
                taskLinked, milestoneLink);
        Check(ProjectDeploymentReservationRelations.Active(
                bothLinked.Ledger, item.Id).Count == 2 &&
            ProjectDeploymentReservationRelations.Active(
                bothLinked.Ledger, secondItem.Id).Count == 0,
            "Milestone and task relationships have independent stable keys.");
        ProjectDeploymentReservationRelationEvent unlink = relation with
        {
            Id = Guid.NewGuid(),
            Action = ProjectDeploymentReservationRelationAction.Unlink,
            Rationale = "Suivi repris directement par l'exploitation.",
            RecordedUtc = milestoneLink.RecordedUtc.AddSeconds(1)
        };
        ProjectDeploymentReservationSnapshot taskUnlinked =
            await followup.RecordRelationAsync(project.ProjectDirectory,
                bothLinked, unlink);
        Check(ProjectDeploymentReservationRelations.Active(
                taskUnlinked.Ledger, item.Id).Single().Kind ==
                ProjectDeploymentReservationRelationKind.Milestone &&
            taskUnlinked.Ledger.Relations?.Count == 3,
            "Dissociation must append history, without deleting milestone links.");
        await RejectAsync(() => followup.RecordRelationAsync(
            project.ProjectDirectory, taskUnlinked, unlink with
            {
                Id = Guid.NewGuid(),
                RecordedUtc = unlink.RecordedUtc.AddSeconds(1)
            }), "A target cannot be unlinked twice.");
        await RejectAsync(() => followup.RecordRelationAsync(
            project.ProjectDirectory, taskUnlinked, relation with
            {
                Id = Guid.NewGuid(),
                Action = ProjectDeploymentReservationRelationAction.Unlink,
                TargetId = milestoneTarget.Id,
                Kind = ProjectDeploymentReservationRelationKind.Milestone,
                SourceRelativePath = taskTarget.SourceRelativePath,
                RecordedUtc = unlink.RecordedUtc.AddSeconds(1)
            }), "Dissociation must preserve the last linked source path.");
        ProjectDeploymentReservationSnapshot rereadLinks =
            await followup.ReadAsync(project.ProjectDirectory);
        Check(rereadLinks.Ledger.Relations?.Count == 3 &&
            (await service.ListAsync(project.ProjectDirectory)).Operations
                .Single().SourceSha256 == savedClosure.SourceSha256,
            "Reopening and linking tasks/jalons never rewrites a closed MEP.");





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
