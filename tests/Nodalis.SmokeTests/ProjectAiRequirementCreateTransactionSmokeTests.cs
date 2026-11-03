using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic, no-network regression for human-approved EXI creation from a
/// portable external proposal. No user's project names or real data appear.
/// </summary>
internal static class ProjectAiRequirementCreateTransactionSmokeTests
{
    /// <summary>Tests safe identity, draft-only creation, replay and corruption.</summary>
    /// <param name="root">Existing disposable smoke root.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-new-EXI-smoke");
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
                Request(app, appId, "Projet fictif A"));
            ProjectCreationResult other = await creator.CreateAsync(
                Request(app, appId, "Projet fictif B"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementCatalog initial = await registry.ListAsync(
                project.ProjectDirectory);
            Assert(initial.ProjectId == project.Project.Id &&
                initial.NextReference == "EXI-0001",
                "The canonical project GUID must be part of the review snapshot.");

            Guid proposalId = Guid.NewGuid();
            ProjectAiImportCandidate proposal = new()
            {
                ProposalId = proposalId,
                Kind = "requirement",
                Operation = "create",
                Certainty = "hypothesis",
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Titre de l'exigence proposée",
                    ["description"] = "À revoir par un humain"
                }
            };
            ProjectAiResponsePreview preview = Preview(proposal);
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview, initial);
            Assert(reviews.Single().Status ==
                ProjectAiRequirementMatchStatus.CreationDraft,
                "An AI create remains an uncommitted draft even if it claims certainty.");

            ProjectAiPreparedReviewPlan empty =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews, []);
            Assert(empty.Creations.Count == 0 && empty.PendingFields == 2,
                "No AI create can be approved by default.");
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
                [
                    new ProjectAiFieldDecision(proposalId, "title",
                        ProjectAiFieldDecisionKind.Edit,
                        "Titre approuvé manuellement"),
                    new ProjectAiFieldDecision(proposalId, "description",
                        ProjectAiFieldDecisionKind.Ignore)
                ]);
            Assert(plan.Creations.Count == 1 && plan.Changes.Count == 0 &&
                plan.Creations.Single().ProjectId == project.Project.Id &&
                plan.Creations.Single().ReviewedTitle == "Titre approuvé manuellement" &&
                plan.IgnoredFields == 1 &&
                plan.PendingFields == 0,
                "A partial human decision must preserve the project and title.");
            ProjectAiRequirementCreateTransactionService service = new(workspace);
            Assert(await service.GetJournaledReferenceAsync(
                project.ProjectDirectory, plan) is null,
                "Reading a fresh creation plan cannot make a journal.");

            await RejectAsync(() => service.CommitAsync(
                other.ProjectDirectory, plan, "EXI-0001"),
                "A project A approval must not be usable in project B.");
            Assert(!Directory.Exists(Path.Combine(other.ProjectDirectory,
                    ".nodalis-ai-import")),
                "Foreign creation must not even stage a transaction.");
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "EXI-0002"),
                "A reference not reserved in the human confirmation must fail.");

            ProjectAiRequirementCreateResult created =
                await service.CommitAsync(
                    project.ProjectDirectory, plan, "EXI-0001");
            string destination = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", "EXI-0001.md");
            byte[] originalBytes = await File.ReadAllBytesAsync(destination);
            ProjectRequirementCard card =
                ProjectRequirementMarkdownCodec.Parse(
                    new UTF8Encoding(false, true).GetString(originalBytes));
            Assert(created.Status == ProjectAiRequirementCreateStatus.Created &&
                created.Entry.Card == card &&
                card.ProjectId == project.Project.Id &&
                card.Id != Guid.Empty &&
                card.Id != proposalId &&
                card.Reference == "EXI-0001" &&
                card.Title == "Titre approuvé manuellement" &&
                card.Validation == ProjectRequirementValidation.Draft &&
                card.Implementation == ProjectRequirementImplementation.NotStarted &&
                card.Verification == ProjectRequirementVerification.NotVerified,
                "Nodalis must assign identity and create an unapproved draft.");

            string transactionRoot = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", proposalId.ToString("N"));
            string journalFile = Path.Combine(transactionRoot, "intent.json");
            string stagingFile = Path.Combine(transactionRoot, "published.md");
            string receiptFile = Path.Combine(transactionRoot, "applied.json");
            Assert(File.Exists(journalFile) && File.Exists(stagingFile) &&
                File.Exists(receiptFile) &&
                await service.GetJournaledReferenceAsync(
                    project.ProjectDirectory, plan) == "EXI-0001",
                "Intent, snapshot and idempotent receipt must persist.");

            ProjectAiRequirementCreateResult replay =
                await service.CommitAsync(project.ProjectDirectory, plan, "EXI-0001");
            Assert(replay.Status == ProjectAiRequirementCreateStatus.AlreadyCreated &&
                replay.Entry.Card.Id == card.Id &&
                (await File.ReadAllBytesAsync(destination))
                    .SequenceEqual(originalBytes),
                "Retry of the same response must not create a second EXI.");

            ProjectAiPreparedReviewPlan altered = plan with
            {
                Creations =
                [
                    plan.Creations.Single() with
                    {
                        ReviewedTitle = "Titre détourné"
                    }
                ]
            };
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, altered, "EXI-0001"),
                "An existing proposal ID cannot authorize a different title.");

            string originalJournal = await File.ReadAllTextAsync(journalFile);
            await File.WriteAllTextAsync(journalFile, "{non-json");
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "EXI-0001"),
                "Malformed journal must not crash or replace a requirement.");
            await File.WriteAllTextAsync(journalFile, originalJournal);

            byte[] originalSnapshot = await File.ReadAllBytesAsync(stagingFile);
            await File.WriteAllTextAsync(stagingFile, "corrupt");
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "EXI-0001"),
                "Corrupted immutable snapshot must block replay.");
            await File.WriteAllBytesAsync(stagingFile, originalSnapshot);

            // Simulate a crash after the intent and snapshot were made durable
            // but before the canonical EXI or applied receipt were published.
            File.Delete(receiptFile);
            File.Delete(destination);
            ProjectAiRequirementCreateResult resumed =
                await service.CommitAsync(
                    project.ProjectDirectory, plan, "EXI-0001");
            Assert(resumed.Status == ProjectAiRequirementCreateStatus.Created &&
                resumed.Entry.Card.Id == card.Id &&
                File.Exists(receiptFile) &&
                (await File.ReadAllBytesAsync(destination))
                    .SequenceEqual(originalBytes),
                "Interrupted creation must reuse its exact Nodalis GUID.");

            await File.AppendAllTextAsync(destination, "\nModification externe.\n");
            byte[] concurrentBytes = await File.ReadAllBytesAsync(destination);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "EXI-0001"),
                "An externally edited EXI cannot be silently replaced.");
            Assert((await File.ReadAllBytesAsync(destination))
                    .SequenceEqual(concurrentBytes),
                "All external modifications must be preserved.");

            File.Delete(destination);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "EXI-0001"),
                "A previously published and deleted EXI cannot be resurrected.");
            Assert(!File.Exists(destination),
                "A deleted EXI remains deleted after a refused import.");
            await File.WriteAllBytesAsync(destination, originalBytes);

            // New proposal but stale reference: the first EXI still occupies it.
            ProjectAiImportCandidate next = proposal with
            {
                ProposalId = Guid.NewGuid()
            };
            ProjectAiResponsePreview nextPreview = Preview(next);
            ProjectAiPreparedReviewPlan nextPlan =
                ProjectAiImportDecisionPlanner.Prepare(nextPreview,
                    ProjectAiRequirementReviewService.Compare(nextPreview,
                        await registry.ListAsync(project.ProjectDirectory)),
                    [
                        new ProjectAiFieldDecision(next.ProposalId, "title",
                            ProjectAiFieldDecisionKind.Accept)
                    ]);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, nextPlan, "EXI-0001"),
                "Concurrent EXI reference allocation must be detected.");

            ProjectAiImportCandidate suppliedId = proposal with
            {
                ProposalId = Guid.NewGuid(),
                TargetId = Guid.NewGuid()
            };
            ProjectAiResponsePreview foreign = Preview(suppliedId);
            IReadOnlyList<ProjectAiRequirementReview> foreignReviews =
                ProjectAiRequirementReviewService.Compare(foreign,
                    await registry.ListAsync(project.ProjectDirectory));
            Assert(foreignReviews.Single().Status ==
                ProjectAiRequirementMatchStatus.CreateCollision,
                "An AI cannot assign the GUID of a new requirement.");
            await RejectAsync(() => Task.FromResult(
                ProjectAiImportDecisionPlanner.Prepare(foreign,
                    foreignReviews,
                    [
                        new ProjectAiFieldDecision(suppliedId.ProposalId,
                            "title", ProjectAiFieldDecisionKind.Accept)
                    ])), "Reject approval of an external official GUID.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Provides one untrusted external response fingerprint.</summary>
    private static ProjectAiResponsePreview Preview(
        ProjectAiImportCandidate candidate) => new(
        new ProjectAiResponseEnvelope
        {
            Schema = "nodalis.import.v1",
            ResponseId = Guid.NewGuid(),
            Proposals = [candidate]
        },
        new string('D', 64));

    /// <summary>Builds a project under an entirely synthetic application.</summary>
    private static ProjectCreationRequest Request(
        string app, Guid appId, string name) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Application fictive",
            ParentDirectory = app,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Expects a safety exception without writing extra state.</summary>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Asserts one deterministic invariant.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
