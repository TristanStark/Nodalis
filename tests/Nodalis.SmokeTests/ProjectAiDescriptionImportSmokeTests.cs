using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Exercises reviewed EXI description publication against synthetic Windows
/// projects only. No AI provider, personal workspace, or screenshot is used.
/// </summary>
internal static class ProjectAiDescriptionImportSmokeTests
{
    /// <summary>
    /// Proves consent, metadata preservation, stale-SHA refusal, foreign-project
    /// isolation, retry idempotency and tampered journal refusal.
    /// </summary>
    /// <param name="root">Disposable smoke-test root.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-description-import-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(application);
            Guid applicationId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                CreateRequest(application, applicationId, "Projet fictif A"));
            ProjectCreationResult foreign = await creator.CreateAsync(
                CreateRequest(application, applicationId, "Projet fictif B"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry created = await registry.CreateAsync(
                project.ProjectDirectory, "Exigence fictive");
            string documentPath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", created.Card.Reference + ".md");
            string initial = await File.ReadAllTextAsync(documentPath);
            string decorated = initial.Replace(
                "## Description vérifiable" + Environment.NewLine +
                    Environment.NewLine,
                "## Description vérifiable" + Environment.NewLine +
                    Environment.NewLine + "Description locale initiale" +
                    Environment.NewLine + Environment.NewLine,
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(documentPath, decorated,
                new UTF8Encoding(false));

            ProjectRequirementCatalog catalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectRequirementEntry entry = catalog.Requirements.Single();
            Assert(entry.Description == "Description locale initiale",
                "The canonical inventory must expose the local description.");
            ProjectAiImportCandidate candidate = new()
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "update",
                Certainty = "hypothesis",
                TargetId = entry.Card.Id,
                CandidateReference = entry.Card.Reference,
                BaseFields = new Dictionary<string, string>
                {
                    ["description"] = "Description locale initiale"
                },
                ProposedFields = new Dictionary<string, string>
                {
                    ["description"] = "Description humaine\n\n### Condition"
                }
            };
            ProjectAiResponsePreview preview = new(
                new ProjectAiResponseEnvelope
                {
                    Schema = "nodalis.import.v1",
                    ResponseId = Guid.NewGuid(),
                    Proposals = [candidate]
                },
                new string('D', 64));
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            ProjectAiFieldDecision[] decisions =
            [
                new ProjectAiFieldDecision(candidate.ProposalId,
                    "description", ProjectAiFieldDecisionKind.Accept)
            ];
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
                    decisions);
            Assert(plan.Descriptions.Count == 1 && plan.Changes.Count == 0 &&
                plan.Creations.Count == 0,
                "Description must never silently enter a title import plan.");
            ProjectAiDescriptionImportTransactionService transaction =
                new(workspace);

            byte[] original = await File.ReadAllBytesAsync(documentPath);
            await File.AppendAllTextAsync(documentPath, "\nAjout externe");
            byte[] concurrent = await File.ReadAllBytesAsync(documentPath);
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, plan),
                "Stale EXI must block description publication.");
            Assert((await File.ReadAllBytesAsync(documentPath))
                .SequenceEqual(concurrent),
                "No concurrent content may be overwritten.");
            await File.WriteAllBytesAsync(documentPath, original);

            await RejectAsync(() => transaction.CommitAsync(
                foreign.ProjectDirectory, plan),
                "An EXI from another project must never be writable.");
            Assert(!Directory.Exists(Path.Combine(foreign.ProjectDirectory,
                    ".nodalis-ai-import")),
                "Foreign project must remain free of import journals.");

            ProjectAiReviewHistoryService history = new(workspace);
            Guid auditId = await history.SaveAsync(project.ProjectDirectory,
                preview, reviews, decisions, plan, archiveRaw: false,
                rawResponse: null, action: "description",
                transactionKey: "description-" + candidate.ProposalId.ToString("N"));
            Assert((await history.ReadAsync(project.ProjectDirectory, auditId))
                    .Fields.Single().Decision == "Accept",
                "Human acceptance must be journaled separately from the EXI.");

            ProjectAiTitleCommitStatus result = await transaction.CommitAsync(
                project.ProjectDirectory, plan);
            await history.RecordOutcomeAsync(project.ProjectDirectory, auditId,
                result.ToString());
            Assert(result == ProjectAiTitleCommitStatus.Applied,
                "One human-approved description should publish atomically.");
            byte[] published = await File.ReadAllBytesAsync(documentPath);
            string updated = new UTF8Encoding(false, true).GetString(published);
            Assert(ProjectRequirementMarkdownCodec.Parse(updated) == created.Card &&
                ProjectRequirementDescriptionCodec.Extract(updated) ==
                    "Description humaine\n\n### Condition" &&
                updated.Contains("## Critères d'acceptation",
                    StringComparison.Ordinal) &&
                updated.Contains("## Dépendances et preuves",
                    StringComparison.Ordinal),
                "Description publication must preserve all metadata and other sections.");
            Assert((await history.ReadOutcomeAsync(
                    project.ProjectDirectory, auditId))?.Result == "Applied",
                "Published result must be traceable separately from consent.");
            Assert(await transaction.CommitAsync(project.ProjectDirectory, plan) ==
                ProjectAiTitleCommitStatus.AlreadyApplied,
                "An identical approved transaction must not publish twice.");

            await File.AppendAllTextAsync(documentPath,
                "\nModification humaine ultérieure");
            byte[] changed = await File.ReadAllBytesAsync(documentPath);
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, plan),
                "A divergent post-publication EXI cannot be silently restored.");
            Assert((await File.ReadAllBytesAsync(documentPath))
                .SequenceEqual(changed),
                "Refused retry must preserve later human edits.");
            await File.WriteAllBytesAsync(documentPath, published);

            ProjectAiPreparedReviewPlan changedPlan = plan with
            {
                Descriptions =
                [
                    plan.Descriptions.Single() with
                    {
                        ReviewedDescription = "Autre choix"
                    }
                ]
            };
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, changedPlan),
                "Same proposal GUID may not commit a different choice.");

            string journal = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", "description-" +
                candidate.ProposalId.ToString("N"), "intent.json");
            string saved = await File.ReadAllTextAsync(journal);
            await File.AppendAllTextAsync(journal, "\nmalveillance");
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, plan),
                "Tampered JSON intent must fail closed.");
            await File.WriteAllTextAsync(journal, saved);
            Assert((await File.ReadAllBytesAsync(documentPath))
                .SequenceEqual(published),
                "Recovery failure must not corrupt published requirements.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates a project within a disposable synthetic application.</summary>
    /// <param name="app">Parent application directory.</param>
    /// <param name="appId">Generated application identity.</param>
    /// <param name="name">Synthetic name.</param>
    /// <returns>Valid project-creation request.</returns>
    private static ProjectCreationRequest CreateRequest(
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

    /// <summary>Expects an explicit fail-closed refusal.</summary>
    /// <param name="action">Asynchronous guarded business write.</param>
    /// <param name="message">Invariant to enforce.</param>
    private static async Task RejectAsync(
        Func<Task> action, string message)
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

    /// <summary>Throws if a synthetic invariant is false.</summary>
    /// <param name="condition">Regression assertion.</param>
    /// <param name="message">Failure reason.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
