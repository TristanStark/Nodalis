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
/// Disposable Windows regression of a user-approved CA creation. No AI
/// provider or real user workspace is accessed, and every EXI is synthetic.
/// </summary>
internal static class ProjectAiCriterionCreateSmokeTests
{
    /// <summary>
    /// Checks local ID generation, reference exhaustion guards, stale input,
    /// cross-project isolation, audit/outcome, idempotency and fake replay.
    /// </summary>
    /// <param name="root">Disposable smoke-test suite directory.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-criterion-creation-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif de recette");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application démonstration");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                Request(app, appId, "Projet exemple source"));
            ProjectCreationResult foreign = await creator.CreateAsync(
                Request(app, appId, "Projet exemple autre"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry parent = await registry.CreateAsync(
                project.ProjectDirectory, "Fonctionnement cible");
            ProjectRequirementEntry another = await registry.CreateAsync(
                project.ProjectDirectory, "Autre exigence");

            string path = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", parent.Card.Reference + ".md");
            string otherPath = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", another.Card.Reference + ".md");
            Guid existingId = Guid.NewGuid();
            string original = await File.ReadAllTextAsync(path);
            string anchor = "## Critères d'acceptation" + Environment.NewLine +
                Environment.NewLine;
            string markdown = original.Replace(anchor, anchor +
                "### CA-01 — Critère déjà identifié" + Environment.NewLine +
                MarkdownBlockIdentityCodec.FormatMarker(existingId) +
                Environment.NewLine + Environment.NewLine,
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, markdown,
                new UTF8Encoding(false));
            ProjectRequirementCatalog catalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectRequirementEntry stored = catalog.Requirements.Single(
                item => item.Card.Id == parent.Card.Id);
            Guid proposalId = Guid.NewGuid();
            ProjectAiImportCandidate suggestion = new()
            {
                ProposalId = proposalId,
                Kind = "criterion",
                Operation = "create",
                TargetId = parent.Card.Id,
                CandidateReference = null,
                Certainty = "derived",
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Nouvelle condition de recette"
                }
            };
            ProjectAiResponsePreview preview = new(
                new ProjectAiResponseEnvelope
                {
                    Schema = "nodalis.import.v1",
                    ResponseId = Guid.NewGuid(),
                    Proposals = [suggestion]
                }, new string('D', 64));
            IReadOnlyList<ProjectAiRequirementReview> review =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            Assert(review.Single().Status ==
                ProjectAiRequirementMatchStatus.CriterionCreationDraft &&
                review.Single().Requirement?.Card.Id == parent.Card.Id,
                "AI creation must resolve an EXI parent, not a fabricated CA.");
            ProjectAiFieldDecision[] decisions =
            [
                new ProjectAiFieldDecision(proposalId, "title",
                    ProjectAiFieldDecisionKind.Accept)
            ];
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, review,
                    decisions);
            Assert(plan.CriterionCreations.Count == 1 &&
                plan.CriterionCreations.Single().ExpectedSha256 ==
                    stored.SourceSha256,
                "Creation plan must be inert and bound to the reviewed source.");

            ProjectAiCriterionCreateTransactionService service = new(workspace);
            Assert(await service.PreviewReferenceAsync(
                    project.ProjectDirectory, plan) == "CA-02",
                "Next code is local to the selected EXI and is not AI-assigned.");
            await RejectAsync(() => service.CommitAsync(
                foreign.ProjectDirectory, plan, "CA-02"),
                "Foreign project cannot receive a CA or import journal.");
            Assert(!Directory.Exists(Path.Combine(foreign.ProjectDirectory,
                    ".nodalis-ai-import")),
                "Project isolation must hold before touching the journal root.");

            byte[] baseBytes = await File.ReadAllBytesAsync(path);
            await File.AppendAllTextAsync(path,
                "\nModification concurrente fictive");
            byte[] concurrent = await File.ReadAllBytesAsync(path);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "CA-02"),
                "Stale EXI source must fail before reserving a reference.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(
                concurrent), "Rejected import cannot overwrite changed files.");
            await File.WriteAllBytesAsync(path, baseBytes);

            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "CA-03"),
                "An unconfirmed future CA code cannot be used.");
            byte[] afterFailedCode = await File.ReadAllBytesAsync(path);
            Assert(afterFailedCode.SequenceEqual(baseBytes),
                "Incorrect CA code cannot change the original EXI.");
            string otherOriginal = await File.ReadAllTextAsync(otherPath);
            ProjectAiReviewHistoryService history = new(workspace);
            string key = "criterion-create-" + proposalId.ToString("N");
            Guid auditId = await history.SaveAsync(project.ProjectDirectory,
                preview, review, decisions, plan, archiveRaw: false,
                rawResponse: null, action: "criterion-create",
                transactionKey: key);
            ProjectAiReviewAudit audit =
                await history.ReadAsync(project.ProjectDirectory, auditId);
            Assert(audit.Fields.Single().Decision == "Accept" &&
                audit.Fields.Single().ExistingRequirementId == parent.Card.Id &&
                audit.Fields.Single().ExistingCriterionId is null &&
                !audit.RawArchived,
                "Audit must reflect human consent without inventing a CA ID.");

            ProjectAiTitleCommitStatus result = await service.CommitAsync(
                project.ProjectDirectory, plan, "CA-02");
            await history.RecordOutcomeAsync(
                project.ProjectDirectory, auditId, result.ToString());
            Assert(result == ProjectAiTitleCommitStatus.Applied,
                "Approved CA creation should publish exactly one heading.");
            byte[] publishedBytes = await File.ReadAllBytesAsync(path);
            string published = new UTF8Encoding(false, true)
                .GetString(publishedBytes);
            ProjectRequirementCriterionScan scan =
                ProjectRequirementCriterionCodec.Inspect(published);
            Assert(scan.Criteria.Count == 2 &&
                scan.Criteria[0].Id == existingId &&
                scan.Criteria[1].Reference == "CA-02" &&
                scan.Criteria[1].Id is Guid newId &&
                newId != existingId &&
                scan.Criteria[1].LocallyIdentified &&
                scan.Criteria[1].Title == "Nouvelle condition de recette" &&
                ProjectRequirementMarkdownCodec.Parse(published) == parent.Card &&
                (await File.ReadAllTextAsync(otherPath)) == otherOriginal,
                "New CA must have a new Nodalis GUID without changing other fields.");
            Assert((await history.ReadOutcomeAsync(
                    project.ProjectDirectory, auditId))?.Result == "Applied",
                "Outcome must be recorded separately from human intent.");
            Assert(await service.CommitAsync(project.ProjectDirectory,
                    plan, "CA-02") == ProjectAiTitleCommitStatus.AlreadyApplied,
                "Confirmed creation must be idempotent with the same journal.");

            // A completed receipt must NOT resurrect a criterion after a
            // human deliberately restores the initial Markdown.
            await File.WriteAllBytesAsync(path, baseBytes);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "CA-02"),
                "Completed CA creation must not reapply after manual rollback.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(baseBytes),
                "Manual rollback remains intact after rejected retry.");
            await File.WriteAllBytesAsync(path, publishedBytes);

            await File.AppendAllTextAsync(path,
                "\nAnnotation humaine postérieure");
            byte[] later = await File.ReadAllBytesAsync(path);
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "CA-02"),
                "Later EXI edits must never be overwritten by old AI intent.");
            Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(later),
                "Live EXI must stay intact after divergent recovery.");
            await File.WriteAllBytesAsync(path, publishedBytes);

            string intentPath = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", key, "intent.json");
            string issued = await File.ReadAllTextAsync(intentPath);
            string publishedPath = Path.Combine(
                Path.GetDirectoryName(intentPath)!, "published.md");
            string approvedSnapshot = await File.ReadAllTextAsync(publishedPath);
            string malicious = approvedSnapshot +
                "\n## Section étrangère\nTexte non validé.\n";
            ProjectAiCriterionCreateJournal journal =
                JsonSerializer.Deserialize<ProjectAiCriterionCreateJournal>(
                    issued, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidOperationException("Synthetic journal absent.");
            await File.WriteAllTextAsync(publishedPath, malicious,
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(intentPath,
                JsonSerializer.Serialize(journal with
                {
                    PublishedSha256 = Convert.ToHexString(SHA256.HashData(
                        new UTF8Encoding(false).GetBytes(malicious)))
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await RejectAsync(() => service.CommitAsync(
                project.ProjectDirectory, plan, "CA-02"),
                "Forging both journal and snapshot SHA cannot bypass a review.");
            Assert((await File.ReadAllBytesAsync(path))
                .SequenceEqual(publishedBytes),
                "Forged snapshot can never alter the existing EXI.");
            await File.WriteAllTextAsync(intentPath, issued);
            await File.WriteAllTextAsync(publishedPath, approvedSnapshot,
                new UTF8Encoding(false));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates a synthetic project without personal identifiers.</summary>
    /// <param name="application">Temporary parent application.</param>
    /// <param name="applicationId">Random application GUID.</param>
    /// <param name="name">Synthetic project name.</param>
    /// <returns>Valid project creation request.</returns>
    private static ProjectCreationRequest Request(
        string application, Guid applicationId, string name) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Application démonstration",
            ParentDirectory = application,
            DisplayName = "Application démonstration"
        }
    };

    /// <summary>Expects a fail-closed asynchronous rejection.</summary>
    /// <param name="action">Unsafe operation under regression test.</param>
    /// <param name="message">Safety invariant.</param>
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

    /// <summary>Throws if an expected invariant is false.</summary>
    /// <param name="condition">Truth to enforce.</param>
    /// <param name="message">Diagnostic of test failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
