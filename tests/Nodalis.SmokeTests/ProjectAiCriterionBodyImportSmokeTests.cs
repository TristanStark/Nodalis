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
/// Disposable Windows fixture for detailed, human-approved CA body imports.
/// No user workspace, screenshots, external model or real project names.
/// </summary>
internal static class ProjectAiCriterionBodyImportSmokeTests
{
    /// <summary>
    /// Checks conflict detection, SHA CAS, project isolation, independent
    /// audit/outcome, idempotent retry and semantic journal tampering refusal.
    /// </summary>
    /// <param name="root">Disposable root used only for smoke testing.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-criterion-body-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(app);
            Guid applicationId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                Request(app, applicationId, "Projet fictif source"));
            ProjectCreationResult otherProject = await creator.CreateAsync(
                Request(app, applicationId, "Projet fictif étranger"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry created = await registry.CreateAsync(
                project.ProjectDirectory, "Besoin fictif");
            ProjectRequirementEntry separate = await registry.CreateAsync(
                project.ProjectDirectory, "Besoin distinct");
            string path = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", created.Card.Reference + ".md");
            string untouched = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", separate.Card.Reference + ".md");
            string otherContent = await File.ReadAllTextAsync(untouched);
            Guid caId = Guid.NewGuid();
            string newline = Environment.NewLine;
            string original = await File.ReadAllTextAsync(path);
            string anchor = "## Critères d'acceptation" + newline + newline;
            string markdown = original.Replace(anchor, anchor +
                "### CA-01 — Résultat observable" + newline +
                MarkdownBlockIdentityCodec.FormatMarker(caId) + newline +
                newline + "Corps CA initial" + newline + newline,
                StringComparison.Ordinal);
            Assert(markdown != original, "Fixture must create a real CA body.");
            await File.WriteAllTextAsync(path, markdown,
                new UTF8Encoding(false));

            ProjectRequirementCatalog catalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectRequirementEntry parent = catalog.Requirements.Single(
                item => item.Card.Id == created.Card.Id);
            ProjectRequirementCriterion criterion = parent.Criteria.Single();
            Assert(criterion.Id == caId && criterion.LocallyIdentified &&
                criterion.Body == "Corps CA initial",
                "Registry must supply body and source hash from one EXI snapshot.");

            Guid proposalId = Guid.NewGuid();
            ProjectAiImportCandidate candidate = new()
            {
                ProposalId = proposalId,
                Kind = "criterion",
                Operation = "update",
                TargetId = caId,
                CandidateReference = "CA-01",
                Certainty = "derived",
                BaseFields = new Dictionary<string, string>
                {
                    ["description"] = "Corps CA initial"
                },
                ProposedFields = new Dictionary<string, string>
                {
                    ["description"] = "Règle observable\n\n#### Justification\nPreuve"
                }
            };
            ProjectAiResponsePreview preview = new(
                new ProjectAiResponseEnvelope
                {
                    Schema = "nodalis.import.v1",
                    ResponseId = Guid.NewGuid(),
                    Proposals = [candidate]
                }, new string('A', 64));
            IReadOnlyList<ProjectAiRequirementReview> comparisons =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            Assert(comparisons.Single().Fields.Single().Kind ==
                ProjectAiFieldDifferenceKind.CandidateChange,
                "Baseline, canonical current and proposal must compare.");
            ProjectAiFieldDecision[] decisions =
            [
                new ProjectAiFieldDecision(proposalId, "description",
                    ProjectAiFieldDecisionKind.Accept)
            ];
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview,
                    comparisons, decisions);
            Assert(plan.CriterionBodies.Count == 1 &&
                plan.CriterionBodies[0].ExpectedSha256 == parent.SourceSha256 &&
                plan.Changes.Count == 0 && plan.Descriptions.Count == 0 &&
                plan.CriterionTitles.Count == 0,
                "A CA body is independently approved and SHA-bound.");

            ProjectAiCriterionBodyImportTransactionService transaction =
                new(workspace);
            await RejectAsync(() => transaction.CommitAsync(
                otherProject.ProjectDirectory, plan),
                "Import may not publish into a foreign project.");
            Assert(!Directory.Exists(Path.Combine(otherProject.ProjectDirectory,
                    ".nodalis-ai-import")),
                "Foreign project must not receive a publication journal.");

            byte[] originalBytes = await File.ReadAllBytesAsync(path);
            await File.AppendAllTextAsync(path,
                "\nModification concurrente synthétique");
            byte[] changed = await File.ReadAllBytesAsync(path);
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, plan),
                "Edited EXI source must refuse stale human review.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(changed),
                "Refused publication cannot overwrite concurrent Markdown.");
            await File.WriteAllBytesAsync(path, originalBytes);

            ProjectAiReviewHistoryService history = new(workspace);
            string key = "criterion-body-" + proposalId.ToString("N");
            Guid auditId = await history.SaveAsync(project.ProjectDirectory,
                preview, comparisons, decisions, plan, archiveRaw: false,
                rawResponse: null, action: "criterion-body",
                transactionKey: key);
            ProjectAiReviewAudit review =
                await history.ReadAsync(project.ProjectDirectory, auditId);
            Assert(review.Fields.Single().ExistingCriterionId == caId &&
                review.Fields.Single().Decision == "Accept" &&
                !review.RawArchived,
                "Audit must preserve human decision and stable CA identity.");

            ProjectAiTitleCommitStatus applied = await transaction.CommitAsync(
                project.ProjectDirectory, plan);
            await history.RecordOutcomeAsync(project.ProjectDirectory,
                auditId, applied.ToString());
            Assert(applied == ProjectAiTitleCommitStatus.Applied,
                "Human-approved detailed CA body should commit exactly once.");
            byte[] publishedBytes = await File.ReadAllBytesAsync(path);
            string published = new UTF8Encoding(false, true)
                .GetString(publishedBytes);
            Assert(ProjectRequirementCriterionBodyCodec.Extract(published, caId) ==
                "Règle observable\n\n#### Justification\nPreuve" &&
                ProjectRequirementCriterionCodec.Inspect(published).Criteria
                    .Single().Id == caId &&
                ProjectRequirementCriterionCodec.Inspect(published).Criteria
                    .Single().Title == "Résultat observable" &&
                ProjectRequirementMarkdownCodec.Parse(published) == created.Card &&
                (await File.ReadAllTextAsync(untouched)) == otherContent,
                "Only the CA content may change; GUID/title/status/other EXI stay.");
            Assert((await history.ReadOutcomeAsync(
                project.ProjectDirectory, auditId))?.Result == "Applied",
                "Human intent and business result are separately archived.");
            Assert(await transaction.CommitAsync(project.ProjectDirectory, plan) ==
                ProjectAiTitleCommitStatus.AlreadyApplied,
                "Same reviewed transaction must be idempotent.");

            ProjectAiPreparedReviewPlan different = plan with
            {
                CriterionBodies =
                [
                    plan.CriterionBodies.Single() with
                    {
                        ReviewedBody = "Choix humain différent"
                    }
                ]
            };
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, different),
                "Reused candidate ID cannot import a different CA body.");

            await File.AppendAllTextAsync(path,
                "\nNouvelle annotation humaine ultérieure");
            byte[] recent = await File.ReadAllBytesAsync(path);
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, plan),
                "A later human edit must not be overwritten from old intent.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(recent),
                "Divergent source remains intact after rejected retry.");
            await File.WriteAllBytesAsync(path, publishedBytes);

            string journalPath = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", key, "intent.json");
            string journalText = await File.ReadAllTextAsync(journalPath);
            string snapshotPath = Path.Combine(
                Path.GetDirectoryName(journalPath)!, "published.md");
            string snapshot = await File.ReadAllTextAsync(snapshotPath);
            string forged = snapshot + "\n## Contenu non approuvé\nInjection.";
            ProjectAiCriterionBodyImportJournal journal =
                JsonSerializer.Deserialize<ProjectAiCriterionBodyImportJournal>(
                    journalText,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidOperationException("No synthetic CA journal.");
            await File.WriteAllTextAsync(snapshotPath, forged,
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(journalPath,
                JsonSerializer.Serialize(journal with
                {
                    PublishedSha256 = Convert.ToHexString(SHA256.HashData(
                        new UTF8Encoding(false).GetBytes(forged)))
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await RejectAsync(() => transaction.CommitAsync(
                project.ProjectDirectory, plan),
                "Matching forged hashes cannot authorize a different EXI body.");
            Assert((await File.ReadAllBytesAsync(path))
                .SequenceEqual(publishedBytes),
                "Semantic replay must protect published CA against forged journal.");
            await File.WriteAllTextAsync(journalPath, journalText);
            await File.WriteAllTextAsync(snapshotPath, snapshot,
                new UTF8Encoding(false));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates a disposable synthetic project request.</summary>
    /// <param name="app">Parent application folder.</param>
    /// <param name="appId">Generated application GUID.</param>
    /// <param name="name">Synthetic project name.</param>
    /// <returns>Valid project request.</returns>
    private static ProjectCreationRequest Request(string app, Guid appId,
        string name) => new()
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

    /// <summary>Expects rejection of an unsafe business operation.</summary>
    /// <param name="action">Guarded asynchronous action.</param>
    /// <param name="message">Test invariant.</param>
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

    /// <summary>Throws when a smoke-test safety invariant is false.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
