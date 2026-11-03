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
/// End-to-end, synthetic Windows tests of the first one-file AI import commit.
/// The test never calls an AI provider, touches a real workspace or approves
/// any requirement status.
/// </summary>
internal static class ProjectAiTitleImportTransactionSmokeTests
{
    /// <summary>
    /// Tests metadata-only publication, stale SHA refusal, retry identity,
    /// snapshot recovery and isolation between two synthetic projects.
    /// </summary>
    /// <param name="root">Test suite's disposable work root.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-import-transaction-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(application);
            Guid applicationId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult firstProject = await creator.CreateAsync(
                Request("Projet fictif A", application, applicationId));
            ProjectCreationResult otherProject = await creator.CreateAsync(
                Request("Projet fictif B", application, applicationId));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry created = await registry.CreateAsync(
                firstProject.ProjectDirectory, "Titre ancien");

            string path = Path.Combine(firstProject.ProjectDirectory,
                "Documentation", "Exigences", created.Card.Reference + ".md");
            string initial = await File.ReadAllTextAsync(path);
            string withBody = initial + Environment.NewLine +
                "Texte libre à préserver.\n## Notes supplémentaires\n" +
                "Une référence : EXI-0001.\n";
            await File.WriteAllTextAsync(path, withBody, new UTF8Encoding(false));
            byte[] original = await File.ReadAllBytesAsync(path);
            ProjectRequirementEntry entry =
                (await registry.ListAsync(firstProject.ProjectDirectory))
                    .Requirements.Single(item => item.Card.Id == created.Card.Id);
            ProjectAiResponsePreview preview = Preview(entry);
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview,
                    await registry.ListAsync(firstProject.ProjectDirectory));
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
                [
                    new ProjectAiFieldDecision(
                        preview.Document.Proposals.Single().ProposalId,
                        "title", ProjectAiFieldDecisionKind.Accept)
                ]);
            Assert(plan.Changes.Count == 1, "A single accepted EXI title is required.");
            ProjectAiTitleImportTransactionService transaction = new(workspace);

            // No mutation is permissible once a Markdown file has changed
            // after its human-reviewed SHA was captured.
            await File.AppendAllTextAsync(path,
                "\nModification concurrente non approuvée.\n");
            byte[] concurrent = await File.ReadAllBytesAsync(path);
            await RejectAsync(() => transaction.CommitAsync(
                    firstProject.ProjectDirectory, plan),
                "A stale approved SHA must block publication.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(concurrent),
                "A rejected stale review must preserve all external changes.");
            await File.WriteAllBytesAsync(path, original);

            await RejectAsync(() => transaction.CommitAsync(
                    otherProject.ProjectDirectory, plan),
                "The same proposal cannot mutate another project.");
            Assert(!Directory.Exists(Path.Combine(
                    otherProject.ProjectDirectory, ".nodalis-ai-import")),
                "A foreign project must not receive an import journal.");

            ProjectAiTitleCommitStatus first = await transaction.CommitAsync(
                firstProject.ProjectDirectory, plan);
            Assert(first == ProjectAiTitleCommitStatus.Applied,
                "Exactly one approved EXI update must be published.");

            byte[] applied = await File.ReadAllBytesAsync(path);
            string markdown = new UTF8Encoding(false, true).GetString(applied);
            ProjectRequirementCard updated =
                ProjectRequirementMarkdownCodec.Parse(markdown);
            Assert(updated == created.Card with { Title = "Titre humain retenu" },
                "Title edits must preserve GUID, schema, dates and all three states.");
            Assert(markdown.Contains("## Notes supplémentaires",
                    StringComparison.Ordinal) &&
                markdown.Contains("Texte libre à préserver.",
                    StringComparison.Ordinal) &&
                markdown.Contains("Une référence : EXI-0001.",
                    StringComparison.Ordinal) &&
                markdown.Contains("# " + created.Card.Reference +
                    " — Titre humain retenu", StringComparison.Ordinal),
                "An import must not rewrite body sections or acceptance criteria.");

            string journalRoot = Path.Combine(firstProject.ProjectDirectory,
                ".nodalis-ai-import",
                plan.Changes[0].ProposalId.ToString("N"));
            string manifestPath = Path.Combine(journalRoot, "intent.json");
            Assert(File.Exists(manifestPath) &&
                File.Exists(Path.Combine(journalRoot, "original.md")) &&
                File.Exists(Path.Combine(journalRoot, "published.md")),
                "Recovery snapshots and the manifest must precede live publication.");
            ProjectAiTitleImportJournal? journal =
                JsonSerializer.Deserialize<ProjectAiTitleImportJournal>(
                    await File.ReadAllTextAsync(manifestPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert(journal is not null &&
                journal.ProjectId == firstProject.Project.Id &&
                journal.RequirementId == created.Card.Id &&
                journal.PublishedSha256 == Hash(applied) &&
                journal.OriginalSha256 == Hash(original) &&
                journal.RawResponseSha256 == plan.RawResponseSha256,
                "The journal must bind both exact byte revisions and identities.");

            ProjectAiTitleCommitStatus again = await transaction.CommitAsync(
                firstProject.ProjectDirectory, plan);
            Assert(again == ProjectAiTitleCommitStatus.AlreadyApplied &&
                (await File.ReadAllBytesAsync(path)).SequenceEqual(applied),
                "Replaying the exact same proposal must not duplicate the edit.");

            ProjectAiPreparedReviewPlan mutatedApproval = plan with
            {
                Changes =
                [
                    plan.Changes.Single() with
                    {
                        ReviewedTitle = "Valeur remplacée sans nouvelle proposition"
                    }
                ]
            };
            await RejectAsync(() => transaction.CommitAsync(
                firstProject.ProjectDirectory, mutatedApproval),
                "A reused proposal GUID with different approval must fail.");

            // Simulates interruption after the journal is durable but while the
            // live file still contains its original exact bytes.
            await File.WriteAllBytesAsync(path, original);
            ProjectAiTitleCommitStatus recovered = await transaction.CommitAsync(
                firstProject.ProjectDirectory, plan);
            Assert(recovered == ProjectAiTitleCommitStatus.Applied &&
                (await File.ReadAllBytesAsync(path)).SequenceEqual(applied),
                "The same durable journal must safely replay an interrupted publication.");

            await File.AppendAllTextAsync(path,
                "\nModification extérieure postérieure.\n");
            byte[] divergent = await File.ReadAllBytesAsync(path);
            await RejectAsync(() => transaction.CommitAsync(
                firstProject.ProjectDirectory, plan),
                "A third-party edit after publication cannot be overwritten on replay.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(divergent),
                "The journal may never replace a divergent local revision.");

            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            await RejectAsync(() => transaction.CommitAsync(
                firstProject.ProjectDirectory, plan, canceled.Token),
                "Canceled commit must never publish.");

            // The second project is a separate source of truth.
            ProjectRequirementCatalog otherCatalog =
                await registry.ListAsync(otherProject.ProjectDirectory);
            Assert(otherCatalog.Requirements.Count == 0,
                "No changes or imported artifacts may leak across projects.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates a synthetic AI response with an explicit base title.</summary>
    private static ProjectAiResponsePreview Preview(ProjectRequirementEntry entry)
    {
        Guid proposal = Guid.NewGuid();
        string raw = JsonSerializer.Serialize(new
        {
            schema = "nodalis.import.v1",
            responseId = Guid.NewGuid(),
            proposals = new[]
            {
                new
                {
                    proposalId = proposal,
                    kind = "requirement",
                    operation = "update",
                    certainty = "derived",
                    targetId = (Guid?)entry.Card.Id,
                    candidateReference = entry.Card.Reference,
                    baseFields = new { title = entry.Card.Title },
                    proposedFields = new { title = "Titre humain retenu" }
                }
            }
        });
        return ProjectAiResponseImport.Parse(raw);
    }

    /// <summary>Provides a real project manifest under a synthetic application.</summary>
    private static ProjectCreationRequest Request(
        string name, string application, Guid appId) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Application fictive",
            ParentDirectory = application,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Calculates a byte-identical snapshot fingerprint.</summary>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Asserts an expected safety refusal without mutating fixtures.</summary>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Fails one deterministic regression invariant.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
