using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Meetings;
using Nodalis.Infrastructure.Meetings;
using Nodalis.Infrastructure.Persistence;

internal static class MeetingExtractionSmokeTests
{
    /// <summary>
    /// Verifies deterministic preview, selective materialization, duplicate protection, backlinks, and source preservation.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string workspaceRoot =
            Path.Combine(
                root,
                "MeetingExtractionWorkspace");

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        await store.InitializeAsync(
            "Meeting Extraction Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application Réunion");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet Réunion");

        Directory.CreateDirectory(
            projectDirectory);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        ApplicationManifest application =
            new ApplicationManifest
            {
                Id =
                    applicationId,
                Name =
                    "Application Réunion"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id =
                    projectId,
                Name =
                    "Projet Réunion",
                ApplicationId =
                    applicationId,
                InitialComplexity =
                    ProjectComplexity.Simple,
                Sections =
                    []
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                application,
                jsonOptions));

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(
                project,
                jsonOptions));

        WorkspaceMeetingService meetingService =
            new WorkspaceMeetingService(
                workspaceRoot);

        MeetingCreationResult meeting =
            await meetingService.CreateAsync(
                projectDirectory,
                new MeetingDraft
                {
                    Title =
                        "Comité extraction",
                    Date =
                        new DateOnly(
                            2026,
                            10,
                            6),
                    Decisions =
                        "Adopter le format cible\nConserver le mode local",
                    Actions =
                        "Préparer la validation\nDocumenter le flux"
                });

        string meetingBefore =
            await File.ReadAllTextAsync(
                meeting.FilePath);

        WorkspaceMeetingExtractionService extraction =
            new WorkspaceMeetingExtractionService(
                workspaceRoot);

        MeetingExtractionPreview preview =
            await extraction.PreparePreviewAsync(
                meeting.FilePath);

        Assert(
            preview.Actions.Count ==
                2 &&
            preview.Decisions.Count ==
                2 &&
            preview.MeetingDate ==
                new DateOnly(
                    2026,
                    10,
                    6),
            "Meeting extraction preview must deterministically detect known Actions and Décisions sections.");

        Assert(
            await File.ReadAllTextAsync(
                meeting.FilePath) ==
                meetingBefore,
            "Preparing a meeting extraction preview must not modify the source report.");

        Guid actionId =
            preview.Actions[0].Task.Id;
        Guid decisionId =
            preview.Decisions[0].Id;

        MeetingExtractionRequest request =
            new MeetingExtractionRequest
            {
                ActionIds =
                    new HashSet<Guid>
                    {
                        actionId
                    },
                DecisionIds =
                    new HashSet<Guid>
                    {
                        decisionId
                    }
            };

        MeetingExtractionResult result =
            await extraction.ApplyAsync(
                preview,
                request);

        Assert(
            result.PromotedActionCount ==
                1 &&
            result.CreatedDecisionCount ==
                1 &&
            result.SkippedDuplicateCount ==
                0,
            "Only explicitly selected meeting candidates must be materialized.");

        Assert(
            File.Exists(
                Path.Combine(
                    projectDirectory,
                    "Tâches.md")),
            "Selected meeting actions must be promoted to the project task document.");

        string decisionsDirectory =
            Path.Combine(
                projectDirectory,
                "Décisions");

        Assert(
            Directory.Exists(
                decisionsDirectory) &&
            Directory.EnumerateFiles(
                    decisionsDirectory,
                    "*.md",
                    SearchOption.TopDirectoryOnly)
                .Count() ==
                1,
            "Selected meeting decisions must create exactly one Decision Record.");

        string meetingAfter =
            await File.ReadAllTextAsync(
                meeting.FilePath);

        Assert(
            meetingAfter.Contains(
                "Tâche projet : [[",
                StringComparison.Ordinal),
            "Promoted meeting actions must keep a backlink to the created project task.");

        MeetingExtractionPreview after =
            await extraction.PreparePreviewAsync(
                meeting.FilePath);

        Assert(
            after.Decisions.Single(candidate =>
                    candidate.Id ==
                    decisionId)
                .IsDuplicate,
            "A decision already created from the meeting must be detected as a duplicate.");

        MeetingExtractionResult duplicateAttempt =
            await extraction.ApplyAsync(
                preview,
                request);

        Assert(
            duplicateAttempt.PromotedActionCount ==
                0 &&
            duplicateAttempt.CreatedDecisionCount ==
                0 &&
            duplicateAttempt.SkippedDuplicateCount ==
                2,
            "Reapplying the same approved extraction must not create duplicate tasks or decisions.");
    }

    /// <summary>
    /// Throws when an extraction smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">Condition to verify.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
