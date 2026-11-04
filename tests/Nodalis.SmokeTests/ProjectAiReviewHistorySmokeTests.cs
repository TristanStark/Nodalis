using System.Text.Json;
using Nodalis.Core.Importing;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic persistent review history tests. They verify that a confirmation
/// never silently archives a raw model response and that all dispositions,
/// including pending and ignored fields, survive a service restart.
/// </summary>
internal static class ProjectAiReviewHistorySmokeTests
{
    /// <summary>Exercises audit save, privacy, ownership and completion links.</summary>
    /// <param name="root">Disposable smoke test workspace root.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "ai-review-history-fixture");
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
                Request(app, appId, "Projet exemple A"));
            ProjectCreationResult other = await creator.CreateAsync(
                Request(app, appId, "Projet exemple B"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry created = await registry.CreateAsync(
                project.ProjectDirectory, "Titre initial");
            ProjectRequirementCatalog catalog = await registry.ListAsync(
                project.ProjectDirectory);

            Guid proposal1 = Guid.NewGuid();
            Guid proposal2 = Guid.NewGuid();
            Guid responseId = Guid.NewGuid();
            string raw = JsonSerializer.Serialize(new
            {
                schema = "nodalis.import.v1",
                responseId,
                promptKey = "FRT-100-EXEMPLE",
                proposals = new object[]
                {
                    new
                    {
                        proposalId = proposal1,
                        kind = "requirement",
                        operation = "update",
                        certainty = "derived",
                        targetId = created.Card.Id,
                        candidateReference = created.Card.Reference,
                        baseFields = new { title = created.Card.Title },
                        proposedFields = new
                        {
                            title = "Titre accepté",
                            description = "Valeur IA confidentielle et ignorée"
                        }
                    },
                    new
                    {
                        proposalId = proposal2,
                        kind = "risk",
                        operation = "create",
                        certainty = "hypothesis",
                        proposedFields = new { title = "Risque laissé à revoir" }
                    }
                }
            });
            ProjectAiResponsePreview preview = ProjectAiResponseImport.Parse(raw);
            IReadOnlyList<ProjectAiRequirementReview> reviews =
                ProjectAiRequirementReviewService.Compare(preview, catalog);
            ProjectAiFieldDecision[] choices =
            [
                new(proposal1, "title", ProjectAiFieldDecisionKind.Accept),
                new(proposal1, "description", ProjectAiFieldDecisionKind.Ignore),
                new(proposal2, "title", ProjectAiFieldDecisionKind.Review)
            ];
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(preview, reviews, choices);
            ProjectAiReviewHistoryService service = new(workspace);
            string auditRoot = Path.Combine(project.ProjectDirectory,
                ".nodalis-ai-import", "reviews");
            Assert(!Directory.Exists(auditRoot),
                "Parsing and comparing cannot archive decisions implicitly.");

            await RejectAsync(() => service.SaveAsync(
                other.ProjectDirectory, preview, reviews, choices, plan,
                false, null, "review-only", null),
                "Never archive project A decisions in project B.");
            Assert(!Directory.Exists(Path.Combine(other.ProjectDirectory,
                    ".nodalis-ai-import")),
                "Foreign project refusal must occur before any audit directory.");

            Guid savedId = await service.SaveAsync(
                project.ProjectDirectory, preview, reviews, choices, plan,
                false, raw, "review-only", null);
            string savedFolder = Path.Combine(auditRoot, savedId.ToString("N"));
            Assert(File.Exists(Path.Combine(savedFolder, "review.json")) &&
                !File.Exists(Path.Combine(savedFolder, "response.txt")),
                "A raw response supplied without opt-in must never be archived.");

            ProjectAiReviewHistoryService restarted = new(workspace);
            ProjectAiReviewAudit saved = await restarted.ReadAsync(
                project.ProjectDirectory, savedId);
            Assert(saved.ReviewId == savedId &&
                saved.ProjectId == project.Project.Id &&
                saved.ResponseId == responseId &&
                saved.RawResponseSha256 == preview.RawSha256 &&
                saved.PromptKey == "FRT-100-EXEMPLE" &&
                !saved.RawArchived && saved.RawArchiveSha256 is null &&
                saved.Action == "review-only" &&
                saved.Fields.Count == 3 &&
                saved.Fields.Count(field => field.Decision == "Accept") == 1 &&
                saved.Fields.Count(field => field.Decision == "Ignore") == 1 &&
                saved.Fields.Count(field => field.Decision == "Review") == 1,
                "All explicit dispositions must survive a restart.");
            Assert(saved.Fields.Single(field => field.Decision == "Ignore")
                .ApprovedValue is null &&
                saved.Fields.Single(field => field.Decision == "Review")
                .ApprovedValue is null &&
                saved.Fields.Single(field => field.Decision == "Accept")
                    .ApprovedValue == "Titre accepté",
                "Unapproved external values must not be stored in audit metadata.");
            Assert(!(await File.ReadAllTextAsync(Path.Combine(
                    savedFolder, "review.json"))).Contains(
                    "Valeur IA confidentielle", StringComparison.Ordinal),
                "The metadata must not contain an ignored proposed value.");

            await RejectAsync(() => service.SaveAsync(
                project.ProjectDirectory, preview, reviews, choices, plan,
                true, raw + " ", "review-only", null),
                "Raw opt-in must reject altered original bytes.");
            Guid rawId = await service.SaveAsync(
                project.ProjectDirectory, preview, reviews, choices, plan,
                true, raw, "review-only", null);
            string rawFolder = Path.Combine(auditRoot, rawId.ToString("N"));
            Assert(await File.ReadAllTextAsync(
                    Path.Combine(rawFolder, "response.txt")) == raw,
                "Opt-in must preserve the exact pasted original raw response.");
            ProjectAiReviewAudit archived = await restarted.ReadAsync(
                project.ProjectDirectory, rawId);
            Assert(archived.RawArchived &&
                archived.RawArchiveSha256 == preview.RawSha256,
                "Archived raw content must be integrity-bound to its response.");

            ProjectAiPreparedReviewPlan altered = plan with
            {
                Changes = []
            };
            await RejectAsync(() => service.SaveAsync(
                project.ProjectDirectory, preview, reviews, choices, altered,
                false, null, "review-only", null),
                "Altered plans are not valid human provenance records.");

            await RejectAsync(() => service.SaveAsync(
                project.ProjectDirectory, preview, reviews, choices, plan,
                false, null, "create", proposal1.ToString("N")),
                "A publication audit must match the actual type of approved plan.");

            Guid forPublication = await service.SaveAsync(
                project.ProjectDirectory, preview, reviews, choices, plan,
                false, null, "title", proposal1.ToString("N"));
            await service.RecordOutcomeAsync(
                project.ProjectDirectory, forPublication, "Applied");
            string outcomeFile = Path.Combine(auditRoot,
                forPublication.ToString("N"), "outcome.json");
            ProjectAiReviewOutcome? outcome =
                JsonSerializer.Deserialize<ProjectAiReviewOutcome>(
                    await File.ReadAllTextAsync(outcomeFile),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert(outcome is not null &&
                outcome.Result == "Applied" &&
                outcome.TransactionKey == proposal1.ToString("N") &&
                outcome.ReviewId == forPublication,
                "Applied outcomes must link back to the exact import journal.");
            await RejectAsync(() => service.RecordOutcomeAsync(
                project.ProjectDirectory, forPublication, "Failed"),
                "A recorded completion may not silently be overwritten.");
            await RejectAsync(() => service.RecordOutcomeAsync(
                project.ProjectDirectory, savedId, "Applied"),
                "Read-only review history must not claim publication.");

            IReadOnlyList<ProjectAiReviewAudit> list =
                await restarted.ListAsync(project.ProjectDirectory);
            Assert(list.Count == 3 &&
                list.Any(item => item.ReviewId == rawId) &&
                list.Any(item => item.ReviewId == forPublication),
                "New service instances must list persistent reviewed and published audits.");

            await File.AppendAllTextAsync(
                Path.Combine(rawFolder, "response.txt"), "\ncorruption");
            await RejectAsync(() => restarted.ReadAsync(
                project.ProjectDirectory, rawId),
                "Tampered raw archives must fail SHA verification.");
            Assert((await restarted.ListAsync(project.ProjectDirectory))
                    .All(item => item.ReviewId != rawId),
                "A corrupt raw audit must be excluded without destructive repair.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates an isolated project under one synthetic application.</summary>
    private static ProjectCreationRequest Request(
        string app, Guid id, string name) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = id,
            ApplicationName = "Application fictive",
            ParentDirectory = app,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Expected deterministic safety refusal.</summary>
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

    /// <summary>Checks one invariant on synthetic workspace files.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
