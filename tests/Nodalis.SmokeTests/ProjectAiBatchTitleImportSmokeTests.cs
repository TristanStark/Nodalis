using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic multi-EXI AI batch tests: no external models or real user files.
/// </summary>
internal static class ProjectAiBatchTitleImportSmokeTests
{
    /// <summary>Checks preflight, partial interruption, replay and fail-closed recovery.</summary>
    /// <param name="root">Disposable suite root.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-EXI-batch-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(application);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject(application, appId, "Projet fictif A"));
            ProjectCreationResult other = await creator.CreateAsync(
                NewProject(application, appId, "Projet fictif B"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry one = await registry.CreateAsync(
                project.ProjectDirectory, "Exigence alpha");
            ProjectRequirementEntry two = await registry.CreateAsync(
                project.ProjectDirectory, "Exigence beta");

            string firstPath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", one.Card.Reference + ".md");
            string secondPath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", two.Card.Reference + ".md");
            await File.AppendAllTextAsync(firstPath,
                "\n## Note privée\nContenu conservé alpha.\n");
            await File.AppendAllTextAsync(secondPath,
                "\n## Note privée\nContenu conservé beta.\n");
            byte[] beforeOne = await File.ReadAllBytesAsync(firstPath);
            byte[] beforeTwo = await File.ReadAllBytesAsync(secondPath);
            ProjectRequirementCatalog catalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectAiResponsePreview preview = Preview(catalog);
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
                    preview.Document.Proposals.Select(candidate =>
                        new ProjectAiFieldDecision(candidate.ProposalId,
                            "title", ProjectAiFieldDecisionKind.Accept)).ToArray());
            Assert(plan.Changes.Count == 2 && plan.Creations.Count == 0,
                "Only both explicitly approved titles enter a batch.");
            ProjectAiBatchTitleImportTransactionService service = new(workspace);
            string key = ProjectAiBatchTitleImportTransactionService
                .PlanFingerprint(plan);
            Assert(key.Length == 64 && key.All(Uri.IsHexDigit),
                "A deterministic authorization key binds all approved fields.");
            ProjectAiPreparedReviewPlan reversed = plan with
            {
                Changes = plan.Changes.Reverse().ToArray()
            };
            Assert(key == ProjectAiBatchTitleImportTransactionService
                .PlanFingerprint(reversed),
                "Review order may not change batch identity or duplicate journals.");

            await RejectAsync(() => service.CommitAsync(
                other.ProjectDirectory, plan),
                "Another project must not accept the approved EXI identities.");
            Assert(!Directory.Exists(Path.Combine(other.ProjectDirectory,
                    ".nodalis-ai-import")),
                "Cross-project import must not stage any journal.");

            await File.AppendAllTextAsync(secondPath,
                "\nUne modification non approuvée.\n");
            byte[] concurrent = await File.ReadAllBytesAsync(secondPath);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan),
                "A stale last file must reject the whole preflight.");
            Assert((await File.ReadAllBytesAsync(firstPath)).SequenceEqual(beforeOne) &&
                (await File.ReadAllBytesAsync(secondPath)).SequenceEqual(concurrent),
                "No file may be published before every target passes preflight.");
            await File.WriteAllBytesAsync(secondPath, beforeTwo);

            int checkpoints = 0;
            await RejectAsync(() => service.CommitWithCheckpointAsync(
                project.ProjectDirectory, plan, CancellationToken.None,
                completed =>
                {
                    checkpoints = completed;
                    if (completed == 1)
                    {
                        throw new InvalidOperationException(
                            "Interruption simulée après une publication atomique.");
                    }
                }), "A simulated interruption must leave a replayable journal.");
            Assert(checkpoints == 1,
                "The first file must be published before injected interruption.");
            byte[] partiallyApplied = await File.ReadAllBytesAsync(firstPath);
            Assert(!partiallyApplied.SequenceEqual(beforeOne) &&
                (await File.ReadAllBytesAsync(secondPath)).SequenceEqual(beforeTwo),
                "An interrupted batch must never pretend to be all-or-nothing.");
            string directory = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", "batch-" + key);
            string manifest = Path.Combine(directory, "intent.json");
            Assert(File.Exists(manifest) &&
                File.Exists(Path.Combine(directory, "original", "0000.md")) &&
                File.Exists(Path.Combine(directory, "approved", "0001.md")),
                "All batch snapshots and the intent must predate publication.");

            ProjectAiBatchTitleImportTransactionService reopened = new(workspace);
            IReadOnlyList<ProjectAiBatchRecoveryPreview> discover =
                await reopened.ListPendingBatchesAsync(project.ProjectDirectory);
            Assert(discover.Count == 1 &&
                discover[0].PlanSha256 == key &&
                discover[0].Published == 1 &&
                discover[0].Pending == 1,
                "A restarted process must rediscover exactly one partial journal.");
            ProjectAiBatchRecoveryPreview recovery =
                await reopened.ReviewRecoveryAsync(project.ProjectDirectory, key);
            Assert(recovery.Files.Count == 2 &&
                recovery.Files.All(item => item.ReviewedTitle.EndsWith(
                    "retenue", StringComparison.Ordinal)),
                "Recovery review must show the exact previously approved titles.");
            await RejectAsync(() => reopened.ResumeAsync(
                other.ProjectDirectory, key),
                "A journal from another project must not be replayable.");

            await File.AppendAllTextAsync(secondPath,
                "\nModification externe pendant la reprise.\n");
            byte[] changed = await File.ReadAllBytesAsync(secondPath);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan),
                "A third-party edit must block further partial publication.");
            await RejectAsync(() => reopened.ResumeAsync(
                project.ProjectDirectory, key),
                "Recovery across restart must reject divergent source bytes.");
            Assert((await reopened.ListPendingBatchesAsync(
                project.ProjectDirectory)).Count == 0,
                "Divergent batches must be excluded from the safe recovery list.");
            Assert((await File.ReadAllBytesAsync(firstPath))
                    .SequenceEqual(partiallyApplied) &&
                (await File.ReadAllBytesAsync(secondPath)).SequenceEqual(changed),
                "A failed replay cannot overwrite a concurrent editor.");

            await File.WriteAllBytesAsync(secondPath, beforeTwo);
            string savedManifest = await File.ReadAllTextAsync(manifest);
            await File.WriteAllTextAsync(manifest, "{journal corrompu");
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan),
                "A damaged journal must fail closed.");
            await File.WriteAllTextAsync(manifest, savedManifest);

            string staged = Path.Combine(directory, "approved", "0001.md");
            byte[] originalStaged = await File.ReadAllBytesAsync(staged);
            await File.WriteAllTextAsync(staged, "corruption");
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan),
                "Corrupted snapshots must block replay.");
            await File.WriteAllBytesAsync(staged, originalStaged);

            ProjectAiBatchTitleCommitStatus resumed =
                await reopened.ResumeAsync(project.ProjectDirectory, key);
            Assert(resumed == ProjectAiBatchTitleCommitStatus.Applied &&
                !(await File.ReadAllBytesAsync(secondPath)).SequenceEqual(beforeTwo),
                "A human-triggered replay must publish only remaining files.");

            Assert((await reopened.ListPendingBatchesAsync(
                project.ProjectDirectory)).Count == 0,
                "Completed batches must not appear in recovery controls.");
            ProjectAiBatchTitleCommitStatus replayed =
                await service.CommitAsync(project.ProjectDirectory, plan);
            Assert(replayed == ProjectAiBatchTitleCommitStatus.AlreadyApplied,
                "Idempotent replay must not modify already published files.");
            ProjectRequirementCatalog finalCatalog =
                await registry.ListAsync(project.ProjectDirectory);
            Assert(finalCatalog.Requirements.Count == 2 &&
                finalCatalog.Requirements.Any(item =>
                    item.Card.Id == one.Card.Id &&
                    item.Card.Title == "Exigence alpha retenue" &&
                    item.Card.Validation == ProjectRequirementValidation.Draft) &&
                finalCatalog.Requirements.Any(item =>
                    item.Card.Id == two.Card.Id &&
                    item.Card.Title == "Exigence beta retenue" &&
                    item.Card.Verification ==
                        ProjectRequirementVerification.NotVerified),
                "Batch must preserve GUIDs, domain states and project ownership.");
            Assert((await File.ReadAllTextAsync(firstPath)).Contains(
                    "Contenu conservé alpha.", StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(secondPath)).Contains(
                    "Contenu conservé beta.", StringComparison.Ordinal),
                "Body Markdown must survive title-only rewrites.");

            ProjectAiPreparedReviewPlan changedApproval = plan with
            {
                Changes =
                [
                    plan.Changes[0] with { ReviewedTitle = "Titre non autorisé" },
                    plan.Changes[1]
                ]
            };
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, changedApproval),
                "New approval needs a fresh review against current source hashes.");

            ProjectAiPreparedReviewPlan duplicate = plan with
            {
                Changes = [plan.Changes[0], plan.Changes[0]]
            };
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, duplicate),
                "The same target cannot appear twice in a batch.");

            ProjectRequirementCatalog untouched =
                await registry.ListAsync(other.ProjectDirectory);
            Assert(untouched.Requirements.Count == 0,
                "No file or requirement may leak into another project.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates two untrusted but structurally valid title proposals.</summary>
    private static ProjectAiResponsePreview Preview(
        ProjectRequirementCatalog catalog)
    {
        ProjectAiImportCandidate[] candidates = catalog.Requirements
            .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
            .Select(item => new ProjectAiImportCandidate
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "update",
                Certainty = "hypothesis",
                TargetId = item.Card.Id,
                CandidateReference = item.Card.Reference,
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = item.Card.Title + " retenue"
                },
                BaseFields = new Dictionary<string, string>
                {
                    ["title"] = item.Card.Title
                }
            }).ToArray();
        return new ProjectAiResponsePreview(
            new ProjectAiResponseEnvelope
            {
                Schema = "nodalis.import.v1",
                ResponseId = Guid.NewGuid(),
                Proposals = candidates.ToList()
            },
            new string('B', 64));
    }

    /// <summary>Creates an isolated synthetic project manifest.</summary>
    private static ProjectCreationRequest NewProject(
        string application, Guid appId, string title) => new()
    {
        Name = title,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Application fictive",
            ParentDirectory = application,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Asserts refusal without swallowing unrelated failures.</summary>
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

    /// <summary>Fails one clear invariant with fictitious data only.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
