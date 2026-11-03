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
/// End-to-end synthetic test of mixed human-approved EXI creation and
/// title updates. Never uses external AI, real client data or screenshots.
/// </summary>
internal static class ProjectAiMixedImportSmokeTests
{
    /// <summary>
    /// Verifies no partial preflight writes, project isolation, reference
    /// allocation, restart recovery, receipts and idempotent replay.
    /// </summary>
    /// <param name="root">The suite's disposable work directory.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "AI-mixed-exi-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(application);
            Guid applicationId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult currentProject = await creator.CreateAsync(
                NewProject(application, applicationId, "Projet exemple alpha"));
            ProjectCreationResult foreignProject = await creator.CreateAsync(
                NewProject(application, applicationId, "Projet exemple beta"));
            string project = currentProject.ProjectDirectory;
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry first = await registry.CreateAsync(
                project, "Exigence actuelle un");
            ProjectRequirementEntry second = await registry.CreateAsync(
                project, "Exigence actuelle deux");
            string path1 = Path.Combine(project, "Documentation", "Exigences",
                first.Card.Reference + ".md");
            string path2 = Path.Combine(project, "Documentation", "Exigences",
                second.Card.Reference + ".md");
            await File.AppendAllTextAsync(path1,
                "\n## Notes métier\nConserver ces données confidentielles.\n");
            await File.AppendAllTextAsync(path2,
                "\n## Notes métier\nConserver ces données fictives.\n");
            byte[] before1 = await File.ReadAllBytesAsync(path1);
            byte[] before2 = await File.ReadAllBytesAsync(path2);

            ProjectRequirementCatalog catalog = await registry.ListAsync(project);
            ProjectAiResponsePreview preview = Preview(catalog);
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
                    preview.Document.Proposals.Select(item =>
                        new ProjectAiFieldDecision(item.ProposalId, "title",
                            ProjectAiFieldDecisionKind.Accept)).ToArray());
            Assert(plan.Changes.Count == 2 && plan.Creations.Count == 2 &&
                plan.Creations.All(item => item.ProjectId ==
                    currentProject.Project.Id),
                "All mixed operations must be explicitly approved in one project.");

            ProjectAiMixedImportTransactionService service = new(workspace);
            IReadOnlyList<string> proposed = await service.ProposeReferencesAsync(
                project, plan);
            Assert(proposed.SequenceEqual(["EXI-0003", "EXI-0004"]),
                "Nodalis allocates consecutive unreserved EXI codes.");
            Assert(!Directory.Exists(Path.Combine(project, ".nodalis-ai-import")),
                "Read-only mixed plan preview must not stage a journal.");
            string key = ProjectAiMixedImportTransactionService.PlanFingerprint(plan);
            ProjectAiPreparedReviewPlan reordered = plan with
            {
                Changes = plan.Changes.Reverse().ToArray(),
                Creations = plan.Creations.Reverse().ToArray()
            };
            Assert(key == ProjectAiMixedImportTransactionService
                .PlanFingerprint(reordered),
                "Plan identity must be independent of approved row order.");

            await RejectAsync(() => service.CommitAsync(
                foreignProject.ProjectDirectory, plan, proposed),
                "Mixed approval cannot be committed in a different project.");
            Assert(!Directory.Exists(Path.Combine(
                    foreignProject.ProjectDirectory, ".nodalis-ai-import")),
                "Cross-project refusal must happen before any staged mutation.");
            await RejectAsync(() => service.CommitAsync(
                project, plan, ["EXI-0003", "EXI-0005"]),
                "A confirmation must match all Nodalis-assigned codes.");

            await File.AppendAllTextAsync(path2,
                "\nModification extérieure après comparaison.\n");
            byte[] concurrent = await File.ReadAllBytesAsync(path2);
            await RejectAsync(() => service.CommitAsync(
                project, plan, proposed),
                "Any stale existing EXI must reject the entire mixed preflight.");
            Assert((await File.ReadAllBytesAsync(path1)).SequenceEqual(before1) &&
                (await File.ReadAllBytesAsync(path2)).SequenceEqual(concurrent) &&
                !File.Exists(Path.Combine(project, "Documentation", "Exigences",
                    "EXI-0003.md")),
                "A failed preflight must not publish any update or creation.");
            await File.WriteAllBytesAsync(path2, before2);

            int checkpoint = 0;
            await RejectAsync(() => service.CommitWithCheckpointAsync(
                project, plan, proposed, CancellationToken.None, count =>
                {
                    checkpoint = count;
                    if (count == 1)
                    {
                        throw new InvalidOperationException(
                            "Arrêt fictif après première mise à jour.");
                    }
                }), "An injected interruption must leave an incomplete batch.");
            Assert(checkpoint == 1 &&
                !(await File.ReadAllBytesAsync(path1)).SequenceEqual(before1) &&
                (await File.ReadAllBytesAsync(path2)).SequenceEqual(before2),
                "Only the first update may be published at the first checkpoint.");

            string transaction = Path.Combine(project, ".nodalis-ai-import",
                "mixed-" + key);
            string manifestPath = Path.Combine(transaction, "intent.json");
            Assert(File.Exists(manifestPath) &&
                File.Exists(Path.Combine(transaction, "approved", "0003.md")) &&
                File.Exists(Path.Combine(transaction, "original", "0001.md")) &&
                !File.Exists(Path.Combine(project, "Documentation", "Exigences",
                    "EXI-0003.md")),
                "All original and approved snapshots must precede any publication.");
            ProjectAiMixedImportTransactionService reopened = new(workspace);
            IReadOnlyList<ProjectAiMixedRecoveryPreview> found =
                await reopened.ListPendingAsync(project);
            Assert(found.Count == 1 && found[0].PlanSha256 == key &&
                found[0].Published == 1 && found[0].Pending == 3,
                "After restart the journal must show one of four actions completed.");
            Assert((await reopened.ProposeReferencesAsync(
                project, plan)).SequenceEqual(proposed),
                "Restart must honor the same reviewed EXI references.");
            await RejectAsync(() => reopened.ResumeAsync(
                foreignProject.ProjectDirectory, key),
                "A different project may not replay the retained journal.");

            await File.AppendAllTextAsync(path2,
                "\nTexte concurrent non approuvé.\n");
            byte[] changed = await File.ReadAllBytesAsync(path2);
            await RejectAsync(() => reopened.ResumeAsync(project, key),
                "Restart must refuse every divergent EXI.");
            Assert((await File.ReadAllBytesAsync(path2)).SequenceEqual(changed) &&
                (await reopened.ListPendingAsync(project)).Count == 0,
                "Divergent journals are excluded from safe automatic recovery.");
            await File.WriteAllBytesAsync(path2, before2);

            int resumedSteps = 0;
            await RejectAsync(() => reopened.CommitWithCheckpointAsync(
                project, plan, proposed, CancellationToken.None, count =>
                {
                    resumedSteps = count;
                    if (count == 2)
                    {
                        throw new InvalidOperationException(
                            "Arrêt fictif après création d'un brouillon.");
                    }
                }), "A second interruption must preserve the first new identity.");
            Assert(resumedSteps == 2,
                "Replay should publish second update and then first creation.");
            ProjectAiMixedJournal journal =
                JsonSerializer.Deserialize<ProjectAiMixedJournal>(
                    await File.ReadAllTextAsync(manifestPath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            ProjectAiMixedEntry[] newEntries = journal.Entries.Where(item =>
                item.Operation == "create").ToArray();
            Assert(newEntries.Length == 2 &&
                newEntries.All(item => item.RequirementId != Guid.Empty) &&
                newEntries.Select(item => item.RequirementId).Distinct().Count() == 2,
                "The journal must own two unique Nodalis-generated GUIDs.");
            string createdPath = Path.Combine(project, "Documentation",
                "Exigences", newEntries[0].Reference + ".md");
            byte[] createdBytes = await File.ReadAllBytesAsync(createdPath);
            string receipt = Path.Combine(transaction, "receipts",
                newEntries[0].Slot + ".json");
            Assert(File.Exists(receipt),
                "A persisted creation receipt must follow successful creation.");

            File.Delete(createdPath);
            await RejectAsync(() => reopened.ResumeAsync(project, key),
                "A previously published then deleted EXI may not be recreated.");
            Assert(!File.Exists(createdPath),
                "Deleted documents must never silently reappear.");
            await File.WriteAllBytesAsync(createdPath, createdBytes);

            string goodJournal = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(manifestPath, "{corrompu");
            await RejectAsync(() => reopened.ResumeAsync(project, key),
                "An unreadable journal cannot authorize any further publication.");
            await File.WriteAllTextAsync(manifestPath, goodJournal);

            string approvedPath = Path.Combine(transaction, "approved", "0003.md");
            byte[] originalApproved = await File.ReadAllBytesAsync(approvedPath);
            string forged = new UTF8Encoding(false, true).GetString(originalApproved) +
                "\n## État métier injecté\n";
            byte[] forgedBytes = new UTF8Encoding(false).GetBytes(forged);
            await File.WriteAllBytesAsync(approvedPath, forgedBytes);
            ProjectAiMixedJournal alteredJournal = journal with
            {
                Entries = journal.Entries.Select(item =>
                    item.Slot == "0003.md"
                        ? item with { PublishedSha256 = Hash(forgedBytes) }
                        : item).ToList()
            };
            await File.WriteAllTextAsync(manifestPath,
                JsonSerializer.Serialize(alteredJournal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await RejectAsync(() => reopened.ResumeAsync(project, key),
                "A forged journal hash cannot authorize non-title Markdown.");
            await File.WriteAllBytesAsync(approvedPath, originalApproved);
            await File.WriteAllTextAsync(manifestPath, goodJournal);

            ProjectAiMixedCommitStatus resumed = await reopened.ResumeAsync(
                project, key);
            Assert(resumed == ProjectAiMixedCommitStatus.Applied,
                "The remaining second new draft must publish after full revalidation.");
            ProjectAiMixedCommitStatus again = await reopened.ResumeAsync(
                project, key);
            Assert(again == ProjectAiMixedCommitStatus.AlreadyApplied &&
                (await reopened.ListPendingAsync(project)).Count == 0,
                "Exact replay must not allocate new identities or duplicate files.");

            ProjectRequirementCatalog finalCatalog =
                await registry.ListAsync(project);
            Assert(finalCatalog.Requirements.Count == 4 &&
                finalCatalog.Requirements.Any(item => item.Card.Id == first.Card.Id &&
                    item.Card.Title == "Exigence actuelle un revue") &&
                finalCatalog.Requirements.Any(item => item.Card.Id == second.Card.Id &&
                    item.Card.Title == "Exigence actuelle deux revue") &&
                newEntries.All(record => finalCatalog.Requirements.Any(item =>
                    item.Card.Id == record.RequirementId &&
                    item.Card.Reference == record.Reference &&
                    item.Card.Title == record.ReviewedTitle &&
                    item.Card.Validation == ProjectRequirementValidation.Draft &&
                    item.Card.Implementation ==
                        ProjectRequirementImplementation.NotStarted &&
                    item.Card.Verification ==
                        ProjectRequirementVerification.NotVerified)),
                "Existing IDs remain stable and creations are unapproved drafts.");
            Assert((await File.ReadAllTextAsync(path1)).Contains(
                    "Conserver ces données confidentielles.", StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(path2)).Contains(
                    "Conserver ces données fictives.", StringComparison.Ordinal),
                "Mixed title updates must preserve all non-title Markdown.");

            ProjectAiPreparedReviewPlan changedPlan = plan with
            {
                Creations =
                [
                    plan.Creations[0] with
                    {
                        ReviewedTitle = "Titre humain différent"
                    },
                    plan.Creations[1]
                ]
            };
            await RejectAsync(() => service.CommitAsync(
                project, changedPlan, proposed),
                "A changed plan cannot reuse stale, previously approved references.");
            IReadOnlyList<string> freshCodes =
                await service.ProposeReferencesAsync(project, changedPlan);
            Assert(freshCodes.SequenceEqual(["EXI-0005", "EXI-0006"]),
                "Fresh code preview must reflect created requirements.");
            await RejectAsync(() => service.CommitAsync(
                project, changedPlan, freshCodes),
                "A proposal journaled in another batch cannot generate " +
                "another official requirement identity.");
            Assert(!Directory.Exists(Path.Combine(project,
                    ".nodalis-ai-import", "mixed-" +
                    ProjectAiMixedImportTransactionService.PlanFingerprint(changedPlan))),
                "Duplicate proposal refusal must happen before new journal staging.");
            Assert((await registry.ListAsync(
                foreignProject.ProjectDirectory)).Requirements.Count == 0,
                "All mixed mutations must remain isolated to their owner.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Untrusted external proposals with 2 changes and 2 creations.</summary>
    private static ProjectAiResponsePreview Preview(
        ProjectRequirementCatalog catalog)
    {
        List<ProjectAiImportCandidate> candidates = [];
        foreach (ProjectRequirementEntry entry in catalog.Requirements)
        {
            candidates.Add(new ProjectAiImportCandidate
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "update",
                Certainty = "derived",
                TargetId = entry.Card.Id,
                CandidateReference = entry.Card.Reference,
                BaseFields = new Dictionary<string, string>
                {
                    ["title"] = entry.Card.Title
                },
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = entry.Card.Title + " revue"
                }
            });
        }

        candidates.AddRange(
        [
            new ProjectAiImportCandidate
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "create",
                Certainty = "hypothesis",
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Nouvelle exigence fictive alpha"
                }
            },
            new ProjectAiImportCandidate
            {
                ProposalId = Guid.NewGuid(),
                Kind = "requirement",
                Operation = "create",
                Certainty = "derived",
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Nouvelle exigence fictive beta"
                }
            }
        ]);
        return new ProjectAiResponsePreview(
            new ProjectAiResponseEnvelope
            {
                Schema = "nodalis.import.v1",
                ResponseId = Guid.NewGuid(),
                Proposals = candidates
            },
            new string('E', 64));
    }

    /// <summary>Creates a synthetic project under the single fake application.</summary>
    private static ProjectCreationRequest NewProject(
        string application, Guid appId, string name) => new()
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

    /// <summary>Expected fail-closed refusal, not an unhandled crash.</summary>
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

    /// <summary>Computes an exact portable digest of already bounded bytes.</summary>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Asserts an invariant with synthetic names only.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
