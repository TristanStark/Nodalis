using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Regression for explicit post-restart recovery audit. No model, user data,
/// raw prompt, screenshots or network resources are used.
/// </summary>
internal static class ProjectAiRecoveryHistorySmokeTests
{
    /// <summary>
    /// Simulates interrupted title and mixed batches, with durable attempts
    /// and independent outcomes never misrepresenting partial publication.
    /// </summary>
    /// <param name="root">Disposable smoke-test root.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-recovery-history-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                Request(app, appId, "Projet fictif alpha"));
            ProjectCreationResult foreign = await creator.CreateAsync(
                Request(app, appId, "Projet fictif beta"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry one = await registry.CreateAsync(
                project.ProjectDirectory, "Titre alpha");
            ProjectRequirementEntry two = await registry.CreateAsync(
                project.ProjectDirectory, "Titre beta");
            ProjectRequirementCatalog catalog = await registry.ListAsync(
                project.ProjectDirectory);

            ProjectAiResponsePreview titleResponse = Preview(catalog, false);
            IReadOnlyList<ProjectAiRequirementReview> titleReviews =
                ProjectAiRequirementReviewService.Compare(titleResponse, catalog);
            ProjectAiPreparedReviewPlan titlePlan =
                ProjectAiImportDecisionPlanner.Prepare(titleResponse, titleReviews,
                    titleResponse.Document.Proposals.Select(proposal =>
                        new ProjectAiFieldDecision(proposal.ProposalId,
                            "title", ProjectAiFieldDecisionKind.Accept)).ToArray());
            string titleKey = ProjectAiBatchTitleImportTransactionService
                .PlanFingerprint(titlePlan);
            ProjectAiBatchTitleImportTransactionService titleEngine = new(workspace);
            await RejectAsync(() => titleEngine.CommitWithCheckpointAsync(
                project.ProjectDirectory, titlePlan, CancellationToken.None, count =>
                {
                    if (count == 1)
                    {
                        throw new InvalidOperationException(
                            "Interruption simulée après le premier titre.");
                    }
                }), "Title journal must remain after an injected crash.");

            ProjectAiRecoveryHistoryService history = new(workspace);
            string rootAudit = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", "recoveries");
            Assert(!Directory.Exists(rootAudit),
                "Interrupted business batch does not implicitly record human consent.");

            await RejectAsync(() => history.StartAsync(
                foreign.ProjectDirectory, "title-batch", titleKey, 1, 1),
                "A foreign project's recovery journal may not be archived.");
            Assert(!Directory.Exists(Path.Combine(
                    foreign.ProjectDirectory, ".nodalis-ai-import")),
                "A foreign project must not gain a recovery audit folder.");

            await RejectAsync(() => history.StartAsync(
                project.ProjectDirectory, "title-batch", titleKey, 0, 2),
                "Progress changed since user confirmation must be refused.");
            await RejectAsync(() => history.StartAsync(
                project.ProjectDirectory, "title-batch", "../invalid", 1, 1),
                "A recovery key may not supply paths or an arbitrary filename.");

            ProjectAiRecoveryAttempt interrupted = await history.StartAsync(
                project.ProjectDirectory, "title-batch", titleKey, 1, 1);
            Assert(interrupted.ProjectId == project.Project.Id &&
                interrupted.Kind == "title-batch" &&
                interrupted.PublishedBefore == 1 &&
                interrupted.PendingBefore == 1 &&
                interrupted.TransactionKey == "batch-" + titleKey &&
                interrupted.Targets.All(item => item.Operation == "update"),
                "The immutable attempt must reflect the reviewed journal.");
            Assert(await history.ReadOutcomeAsync(
                project.ProjectDirectory, interrupted.AttemptId) is null,
                "A saved intent with no outcome means interrupted/unknown, NOT success.");

            string auditFile = Path.Combine(rootAudit,
                interrupted.AttemptId.ToString("N"), "attempt.json");
            Assert(File.Exists(auditFile) &&
                !(await File.ReadAllTextAsync(auditFile)).Contains(
                    "Titre alpha", StringComparison.Ordinal) &&
                !(await File.ReadAllTextAsync(auditFile)).Contains(
                    "title-batch response text", StringComparison.Ordinal),
                "Recovery audit records references, never raw AI text or titles.");

            ProjectAiRecoveryAttempt failed = await history.StartAsync(
                project.ProjectDirectory, "title-batch", titleKey, 1, 1);
            string secondPath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", two.Card.Reference + ".md");
            await File.AppendAllTextAsync(secondPath,
                "\nModification concurrente non approuvée.\n");
            await RejectAsync(() => titleEngine.ResumeAsync(
                project.ProjectDirectory, titleKey),
                "Divergent target must block the business recovery.");
            ProjectAiRecoveryOutcome failedResult = await history.FinishAsync(
                project.ProjectDirectory, failed.AttemptId, "Failed");
            Assert(failedResult.Result == "Failed" &&
                !failedResult.PostStateVerified &&
                failedResult.PendingAfter is null,
                "An errored replay must not claim zero pending or all-original files.");
            ProjectAiRecoveryHistoryService reopened = new(workspace);
            Assert((await reopened.ReadOutcomeAsync(
                    project.ProjectDirectory, failed.AttemptId))?.Result ==
                "Failed", "Failed attempts must survive a service restart.");
            Assert(await reopened.ReadOutcomeAsync(
                project.ProjectDirectory, interrupted.AttemptId) is null,
                "A separate interrupted attempt remains without outcome.");

            string originalSecond = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", "batch-" + titleKey,
                "original", "0001.md");
            await File.WriteAllBytesAsync(secondPath,
                await File.ReadAllBytesAsync(originalSecond));
            ProjectAiRecoveryAttempt resumed = await reopened.StartAsync(
                project.ProjectDirectory, "title-batch", titleKey, 1, 1);
            ProjectAiRecoveryAttempt tamperCheck = await reopened.StartAsync(
                project.ProjectDirectory, "title-batch", titleKey, 1, 1);
            ProjectAiBatchTitleCommitStatus applied =
                await titleEngine.ResumeAsync(project.ProjectDirectory, titleKey);
            Assert(applied == ProjectAiBatchTitleCommitStatus.Applied,
                "The remaining title must publish under the verified journal.");

            // A plausible, still parseable journal edit during recovery must
            // not be accepted as the same user-confirmed source even when
            // every business file reached its expected published SHA.
            string sourceJournal = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", "batch-" + titleKey, "intent.json");
            string originalJournal = await File.ReadAllTextAsync(sourceJournal);
            await File.AppendAllTextAsync(sourceJournal, "\n");
            ProjectAiRecoveryOutcome untrusted = await reopened.FinishAsync(
                project.ProjectDirectory, tamperCheck.AttemptId, "Applied");
            Assert(untrusted.Result == "NeedsInspection" &&
                !untrusted.PostStateVerified,
                "A changed business journal cannot generate a verified success.");
            await File.WriteAllTextAsync(sourceJournal, originalJournal);

            ProjectAiRecoveryOutcome success = await reopened.FinishAsync(
                project.ProjectDirectory, resumed.AttemptId, "Applied");
            Assert(success.Result == "Applied" &&
                success.PostStateVerified &&
                success.PublishedAfter == 2 && success.PendingAfter == 0,
                "A successful recovery is true only when ALL targets are published.");

            await RejectAsync(() => reopened.FinishAsync(
                project.ProjectDirectory, resumed.AttemptId, "Failed"),
                "Immutable completion cannot be replaced by a later claim.");

            IReadOnlyList<ProjectAiRecoveryAttempt> listed =
                await reopened.ListAsync(project.ProjectDirectory);
            Assert(listed.Count == 4 &&
                listed.Any(item => item.AttemptId == tamperCheck.AttemptId) &&
                listed.Any(item => item.AttemptId == resumed.AttemptId) &&
                listed.Any(item => item.AttemptId == interrupted.AttemptId),
                "All confirmed attempts must remain distinct in local history.");
            await RejectAsync(() => reopened.ReadAsync(
                foreign.ProjectDirectory, resumed.AttemptId),
                "History of project A must never be readable from project B.");

            // A mixed batch stores created GUIDs in its original journal.
            // Its recovery audit must reference them without creating new ones.
            ProjectRequirementCatalog afterTitles =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectAiResponsePreview mixedResponse = Preview(afterTitles, true);
            ProjectAiPreparedReviewPlan mixedPlan =
                ProjectAiImportDecisionPlanner.Prepare(mixedResponse,
                    ProjectAiRequirementReviewService.Compare(mixedResponse,
                        afterTitles),
                    mixedResponse.Document.Proposals.Select(proposal =>
                        new ProjectAiFieldDecision(proposal.ProposalId, "title",
                            ProjectAiFieldDecisionKind.Accept)).ToArray());
            ProjectAiMixedImportTransactionService mixedEngine = new(workspace);
            IReadOnlyList<string> references =
                await mixedEngine.ProposeReferencesAsync(
                    project.ProjectDirectory, mixedPlan);
            string mixedKey = ProjectAiMixedImportTransactionService
                .PlanFingerprint(mixedPlan);
            await RejectAsync(() => mixedEngine.CommitWithCheckpointAsync(
                project.ProjectDirectory, mixedPlan, references,
                CancellationToken.None, count =>
                {
                    if (count == 1)
                    {
                        throw new InvalidOperationException(
                            "Interruption simulée après la mise à jour.");
                    }
                }), "Mixed batch must remain replayable after one update.");
            ProjectAiMixedRecoveryPreview mixedPreview =
                await mixedEngine.ReviewRecoveryAsync(
                    project.ProjectDirectory, mixedKey);
            ProjectAiRecoveryAttempt mixedAttempt = await reopened.StartAsync(
                project.ProjectDirectory, "mixed", mixedKey,
                mixedPreview.Published, mixedPreview.Pending);
            Assert(mixedAttempt.Targets.Any(item => item.Operation == "create") &&
                mixedAttempt.Targets.Any(item => item.Operation == "update") &&
                mixedAttempt.Targets.All(item => item.RequirementId != Guid.Empty),
                "Mixed recovery audits refer to real project-owned EXI GUIDs.");
            Assert((await reopened.ReadOutcomeAsync(
                    project.ProjectDirectory, mixedAttempt.AttemptId)) is null,
                "An attempt does not imply that the mixed batch is published.");
            ProjectAiMixedCommitStatus mixedResult = await mixedEngine.ResumeAsync(
                project.ProjectDirectory, mixedKey);
            Assert(mixedResult == ProjectAiMixedCommitStatus.Applied,
                "The mixed replay must publish the remaining new EXI.");
            ProjectAiRecoveryOutcome mixedOutcome = await reopened.FinishAsync(
                project.ProjectDirectory, mixedAttempt.AttemptId, "Applied");
            Assert(mixedOutcome.PostStateVerified &&
                mixedOutcome.Result == "Applied" &&
                mixedOutcome.PendingAfter == 0,
                "Mixed outcomes must revalidate every new EXI and title.");

            string resultFile = Path.Combine(rootAudit,
                resumed.AttemptId.ToString("N"), "outcome.json");
            string goodOutcome = await File.ReadAllTextAsync(resultFile);
            await File.WriteAllTextAsync(resultFile, "{corrompu");
            await RejectAsync(() => reopened.ReadOutcomeAsync(
                project.ProjectDirectory, resumed.AttemptId),
                "Malformed outcomes must fail closed.");
            await File.WriteAllTextAsync(resultFile, goodOutcome);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Builds purely synthetic external requirement proposals.</summary>
    private static ProjectAiResponsePreview Preview(
        ProjectRequirementCatalog catalog, bool includeCreation)
    {
        List<ProjectAiImportCandidate> proposals = [];
        foreach (ProjectRequirementEntry entry in catalog.Requirements
            .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
            .Take(includeCreation ? 1 : 2))
        {
            proposals.Add(new ProjectAiImportCandidate
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "update",
                Certainty = "hypothesis",
                TargetId = entry.Card.Id,
                CandidateReference = entry.Card.Reference,
                BaseFields = new Dictionary<string, string>
                {
                    ["title"] = entry.Card.Title
                },
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = entry.Card.Title + " approuvé"
                }
            });
        }

        if (includeCreation)
        {
            proposals.Add(new ProjectAiImportCandidate
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "create",
                Certainty = "hypothesis",
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Nouvelle exigence fictive"
                }
            });
        }

        return new ProjectAiResponsePreview(new ProjectAiResponseEnvelope
        {
            Schema = "nodalis.import.v1",
            ResponseId = Guid.NewGuid(),
            Proposals = proposals
        }, new string('D', 64));
    }

    /// <summary>Creates one project under a fake application path.</summary>
    private static ProjectCreationRequest Request(
        string application, Guid applicationId, string name) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Application fictive",
            ParentDirectory = application,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Expects a controlled safety refusal, not arbitrary failures.</summary>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Asserts synthetic test invariants.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
