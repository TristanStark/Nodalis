using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Windows smoke for human-confirmed CA title import. All projects and
/// external model proposals are synthetic, ephemeral and isolated.
/// </summary>
internal static class ProjectAiCriterionTitleImportSmokeTests
{
    /// <summary>
    /// Covers GUID resolution, cross-project isolation, stale SHA,
    /// cross-document GUID collision, idempotency and journal tampering.
    /// </summary>
    /// <param name="root">Disposable test suite directory.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-criterion-title-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace de recette fictif");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(application);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                Request(application, appId, "Projet fictif source"));
            ProjectCreationResult other = await creator.CreateAsync(
                Request(application, appId, "Projet fictif étranger"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry one = await registry.CreateAsync(
                project.ProjectDirectory, "Fonction fictive A");
            ProjectRequirementEntry two = await registry.CreateAsync(
                project.ProjectDirectory, "Fonction fictive B");
            Guid caId = Guid.NewGuid();
            Guid otherCaId = Guid.NewGuid();
            string onePath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", one.Card.Reference + ".md");
            string twoPath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", two.Card.Reference + ".md");
            string oneSource = AttachCriterion(
                await File.ReadAllTextAsync(onePath), caId, "Critère local");
            string twoSource = AttachCriterion(
                await File.ReadAllTextAsync(twoPath), otherCaId, "Autre critère");
            await File.WriteAllTextAsync(onePath, oneSource,
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(twoPath, twoSource,
                new UTF8Encoding(false));

            ProjectRequirementCatalog catalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectRequirementEntry first = catalog.Requirements.Single(
                entry => entry.Card.Id == one.Card.Id);
            ProjectAiImportCandidate proposal = new()
            {
                ProposalId = Guid.NewGuid(),
                Kind = "criterion",
                Operation = "update",
                TargetId = caId,
                CandidateReference = "CA-01",
                Certainty = "hypothesis",
                BaseFields = new Dictionary<string, string>
                {
                    ["title"] = "Critère local"
                },
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Critère contrôlé"
                }
            };
            ProjectAiResponsePreview preview = new(
                new ProjectAiResponseEnvelope
                {
                    Schema = "nodalis.import.v1",
                    ResponseId = Guid.NewGuid(),
                    Proposals = [proposal]
                }, new string('C', 64));
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            Assert(reviews.Single().Criterion?.Id == caId &&
                reviews.Single().Requirement?.Card.Id == first.Card.Id &&
                reviews.Single().Status ==
                    ProjectAiRequirementMatchStatus.CriterionGuidMatch,
                "CA GUID must resolve within the selected EXI, never by code alone.");
            ProjectAiFieldDecision[] decisions =
            [
                new ProjectAiFieldDecision(proposal.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Accept)
            ];
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
                    decisions);
            Assert(plan.CriterionTitles.Count == 1 && plan.Changes.Count == 0 &&
                plan.Descriptions.Count == 0 && plan.Creations.Count == 0,
                "Approved CA must be a separate, one-file plan.");

            ProjectAiCriterionTitleImportTransactionService engine = new(workspace);
            await RejectAsync(() => engine.CommitAsync(
                other.ProjectDirectory, plan),
                "CA from another project cannot be imported.");
            Assert(!Directory.Exists(Path.Combine(other.ProjectDirectory,
                ".nodalis-ai-import")),
                "Foreign projects may not receive audit or business files.");

            byte[] original = await File.ReadAllBytesAsync(onePath);
            await File.AppendAllTextAsync(onePath, "\nModification externe");
            byte[] concurrent = await File.ReadAllBytesAsync(onePath);
            await RejectAsync(() => engine.CommitAsync(
                project.ProjectDirectory, plan),
                "Stale SHA must block importing a CA title.");
            Assert((await File.ReadAllBytesAsync(onePath)).SequenceEqual(
                concurrent), "A rejected CA import preserves concurrent edits.");
            await File.WriteAllBytesAsync(onePath, original);

            // Even though each local CA is identifiable, duplicate GUID
            // across EXI documents is not a unique target.
            string collision = AttachCriterion(
                await File.ReadAllTextAsync(twoPath), caId, "Conflit d'identité");
            await File.WriteAllTextAsync(twoPath, collision,
                new UTF8Encoding(false));
            await RejectAsync(() => engine.CommitAsync(
                project.ProjectDirectory, plan),
                "Reusing CA GUID in a second EXI must block publication.");
            await File.WriteAllTextAsync(twoPath, twoSource,
                new UTF8Encoding(false));

            ProjectAiReviewHistoryService history = new(workspace);
            string key = "criterion-" + proposal.ProposalId.ToString("N");
            Guid reviewId = await history.SaveAsync(project.ProjectDirectory,
                preview, reviews, decisions, plan, archiveRaw: false,
                rawResponse: null, action: "criterion-title",
                transactionKey: key);
            Assert((await history.ReadAsync(project.ProjectDirectory, reviewId))
                .Fields.Single().ExistingCriterionId == caId,
                "Immutable human review must retain canonical criterion GUID.");

            ProjectAiTitleCommitStatus applied = await engine.CommitAsync(
                project.ProjectDirectory, plan);
            await history.RecordOutcomeAsync(project.ProjectDirectory,
                reviewId, applied.ToString());
            Assert(applied == ProjectAiTitleCommitStatus.Applied,
                "An explicitly approved CA title must publish atomically.");
            byte[] published = await File.ReadAllBytesAsync(onePath);
            string markdown = new UTF8Encoding(false, true).GetString(published);
            Assert(ProjectRequirementMarkdownCodec.Parse(markdown) == one.Card &&
                ProjectRequirementCriterionCodec.Inspect(markdown).Criteria
                    .Single().Id == caId &&
                ProjectRequirementCriterionCodec.Inspect(markdown).Criteria
                    .Single().Title == "Critère contrôlé" &&
                markdown.Contains(MarkdownBlockIdentityCodec.FormatMarker(caId),
                    StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(twoPath)) == twoSource,
                "Only the chosen CA heading changes; all IDs and other EXI stay.");
            Assert((await history.ReadOutcomeAsync(
                project.ProjectDirectory, reviewId))?.Result == "Applied",
                "Review history and confirmed business outcome remain separate.");
            Assert(await engine.CommitAsync(project.ProjectDirectory, plan) ==
                ProjectAiTitleCommitStatus.AlreadyApplied,
                "A retry of exactly the same authorization is idempotent.");

            await File.AppendAllTextAsync(onePath, "\nModification ultérieure");
            byte[] later = await File.ReadAllBytesAsync(onePath);
            await RejectAsync(() => engine.CommitAsync(
                project.ProjectDirectory, plan),
                "Later human content cannot be overwritten from an old journal.");
            Assert((await File.ReadAllBytesAsync(onePath)).SequenceEqual(later),
                "Diverged document remains unchanged after rejected retry.");
            await File.WriteAllBytesAsync(onePath, published);

            ProjectAiPreparedReviewPlan different = plan with
            {
                CriterionTitles =
                [
                    plan.CriterionTitles.Single() with
                    {
                        ReviewedTitle = "Autre titre non approuvé"
                    }
                ]
            };
            await RejectAsync(() => engine.CommitAsync(
                project.ProjectDirectory, different),
                "Reusing one proposal ID with a different CA choice is forbidden.");

            string intentPath = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", key, "intent.json");
            string originalIntent = await File.ReadAllTextAsync(intentPath);
            string publishedPath = Path.Combine(
                Path.GetDirectoryName(intentPath)!, "published.md");
            string genuinePublished = await File.ReadAllTextAsync(publishedPath);
            string forgedContent = genuinePublished +
                "\n## Section injectée\nContenu non approuvé\n";
            ProjectAiCriterionTitleJournal intent =
                JsonSerializer.Deserialize<ProjectAiCriterionTitleJournal>(
                    originalIntent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidOperationException("Missing synthetic CA journal.");
            await File.WriteAllTextAsync(publishedPath, forgedContent,
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(intentPath,
                JsonSerializer.Serialize(intent with
                {
                    PublishedSha256 = Convert.ToHexString(SHA256.HashData(
                        new UTF8Encoding(false).GetBytes(forgedContent)))
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await RejectAsync(() => engine.CommitAsync(
                project.ProjectDirectory, plan),
                "Even coherent forged SHA and snapshot must fail semantic replay.");
            Assert((await File.ReadAllBytesAsync(onePath))
                .SequenceEqual(published),
                "Forged CA replay never mutates the live requirement.");
            await File.WriteAllTextAsync(intentPath, originalIntent);
            await File.WriteAllTextAsync(publishedPath, genuinePublished,
                new UTF8Encoding(false));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Adds one canonical CA heading to an existing EXI.</summary>
    /// <param name="markdown">Original EXI Markdown.</param>
    /// <param name="id">Existing stable block GUID.</param>
    /// <param name="title">Synthetic local criterion title.</param>
    /// <returns>Same EXI with exactly one additional CA.</returns>
    private static string AttachCriterion(string markdown, Guid id,
        string title)
    {
        string newline = Environment.NewLine;
        string marker = "## Critères d'acceptation" + newline + newline;
        if (!markdown.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Synthetic EXI section missing.");
        }

        return markdown.Replace(marker, marker + "### CA-01 — " + title +
            newline + MarkdownBlockIdentityCodec.FormatMarker(id) +
            newline + newline, StringComparison.Ordinal);
    }

    /// <summary>Creates an isolated application project.</summary>
    /// <param name="app">Synthetic app directory.</param>
    /// <param name="appId">Generated app GUID.</param>
    /// <param name="name">Nonpersonal project name.</param>
    /// <returns>Project creation request.</returns>
    private static ProjectCreationRequest Request(string app,
        Guid appId, string name) => new()
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

    /// <summary>Expects a fail-closed transaction refusal.</summary>
    /// <param name="action">Guarded asynchronous action.</param>
    /// <param name="message">Safety invariant to enforce.</param>
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

    /// <summary>Enforces a synthetic test invariant.</summary>
    /// <param name="condition">Expected truth.</param>
    /// <param name="message">Test failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
