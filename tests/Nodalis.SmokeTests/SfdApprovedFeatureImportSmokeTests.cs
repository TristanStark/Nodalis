using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Fictional FCT with/sans-IHM and ACT N:N import regressions.</summary>
internal static class SfdApprovedFeatureImportSmokeTests
{
    /// <summary>
    /// Validates exact ACT references, atomic registry N:N commit,
    /// same-run replay, recovery, second FCT and source conflict refusal.
    /// </summary>
    /// <param name="root">Disposable smoke-test parent.</param>
    /// <returns>Completed functionality review and publication checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-feature-publication");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Fonctionnalités fictives");
        string app = Path.Combine(workspace, "Applications", "Exemple");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Exemple",
            DisplayName = "Exemple",
            ParentDirectory = app
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Exemple de fonctionnalités",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        ProjectCreationResult separate = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet séparé",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        SfdDocumentRegistryService documents = new(workspace);
        SfdDocumentEntry process = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process,
            "Processus métier d'exemple");
        SfdDocumentEntry actOne = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Recevoir une demande fictive", process.Card.Id);
        SfdDocumentEntry actTwo = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Contrôler un fichier fictif", process.Card.Id);
        SfdDocumentEntry otherProcess = await documents.CreateAsync(
            separate.ProjectDirectory, SfdDocumentKind.Process,
            "Processus d'un autre projet");
        SfdDocumentEntry outside = await documents.CreateAsync(
            separate.ProjectDirectory, SfdDocumentKind.Activity,
            "ACT hors projet", otherProcess.Card.Id);
        SfdImportReviewService review = new(workspace);
        SfdApprovedFeatureImportService publisher = new(workspace);
        SfdActivityFunctionalityService links = new(workspace);

        string firstResponse = Json("feature-without-ui",
            "Extraire un fichier de démonstration", "withoutUi",
            [actOne.Card.Id, actTwo.Card.Id]);
        SfdImportReviewSnapshot initial = await review.ReviewAsync(
            project.ProjectDirectory, firstResponse);
        Require(initial.Items.Single().Status ==
                SfdImportReviewStatus.NewCandidate &&
            initial.Items.Single().ActivityRevisions.Count == 2 &&
            initial.Items.Single().ActivityRevisions.Values.All(value =>
                value.Length == 64) &&
            initial.ActivityFunctionalitySha256 is null,
            "FCT without IHM must review two real ACT GUIDs and their byte revisions.");

        SfdApprovedFeatureResult published = await publisher.PublishAsync(
            project.ProjectDirectory, firstResponse, initial, "feature-without-ui");
        SfdDocumentEntry card = (await documents.ListAsync(
            project.ProjectDirectory)).Entries.Single(entry =>
            entry.Card.Id == published.Id);
        Require(!published.AlreadyApplied &&
            published.Reference == "FCT-0001" &&
            published.LinkedActivityCount == 2 &&
            card.Card.FunctionalityMode == SfdFunctionalityMode.WithoutInterface &&
            card.Card.Certainty == SfdAssertionCertainty.ToConfirm &&
            !card.Card.ParentId.HasValue,
            "Approved FCT must retain no inferred parent and be typed WithoutInterface.");

        SfdActivityFunctionalitySnapshot linked = await links.ReadAsync(
            project.ProjectDirectory);
        Require(linked.Sha256?.Length == 64 &&
            linked.Registry.Links.Count == 2 &&
            linked.Registry.Links.All(item =>
                item.FunctionalityId == published.Id) &&
            linked.Registry.Links.Select(item => item.ActivityId).ToHashSet()
                .SetEquals([actOne.Card.Id, actTwo.Card.Id]),
            "One new FCT must connect to multiple ACT without cloning their Markdown.");

        SfdApprovedFeatureResult repeated = await publisher.PublishAsync(
            project.ProjectDirectory, firstResponse, initial,
            "feature-without-ui");
        Require(repeated.AlreadyApplied && repeated.Id == published.Id &&
            (await links.ReadAsync(project.ProjectDirectory)).Registry.Links.Count == 2,
            "Replaying the completed FCT/ACT relations must not duplicate links.");

        // Simulate interruption between FCT file move and link-registry save.
        // On replay, same FCT GUID should be reused and the exact links restored.
        string linkPath = Path.Combine(project.ProjectDirectory,
            SfdActivityFunctionalityService.FileName);
        File.Delete(linkPath);
        SfdApprovedFeatureResult resumed = await publisher.PublishAsync(
            project.ProjectDirectory, firstResponse, initial, "feature-without-ui");
        Require(!resumed.AlreadyApplied && resumed.Id == published.Id &&
            (await links.ReadAsync(project.ProjectDirectory)).Registry.Links.Count == 2,
            "Recovery must finish missing N:N relations without creating another FCT.");

        string secondResponse = Json("feature-with-ui",
            "Consulter le résultat fictif", "withUi", [actOne.Card.Id]);
        SfdImportReviewSnapshot secondReview = await review.ReviewAsync(
            project.ProjectDirectory, secondResponse);
        SfdApprovedFeatureResult second = await publisher.PublishAsync(
            project.ProjectDirectory, secondResponse, secondReview, "feature-with-ui");
        SfdActivityFunctionalitySnapshot twoFeatures = await links.ReadAsync(
            project.ProjectDirectory);
        Require(second.Reference == "FCT-0002" &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Single(
                entry => entry.Card.Id == second.Id).Card.FunctionalityMode ==
                SfdFunctionalityMode.WithInterface &&
            twoFeatures.Registry.Links.Count == 3 &&
            twoFeatures.Registry.Links.Count(item =>
                item.ActivityId == actOne.Card.Id) == 2,
            "N:N must allow one ACT supporting two FCT with differing UI categories.");

        string emptyResponse = Json("feature-draft",
            "Capacité non encore reliée", "withoutUi", []);
        SfdImportReviewSnapshot emptyReview = await review.ReviewAsync(
            project.ProjectDirectory, emptyResponse);
        Require(emptyReview.Items.Single().Status ==
            SfdImportReviewStatus.IncompleteDraft,
            "FCT without ACT is an incomplete draft, never a fabricated link.");
        SfdApprovedFeatureResult incomplete = await publisher.PublishAsync(
            project.ProjectDirectory, emptyResponse, emptyReview, "feature-draft");
        Require(incomplete.LinkedActivityCount == 0 &&
            (await links.ReadAsync(project.ProjectDirectory)).Registry.Links.Count == 3,
            "Explicitly approved incomplete FCT must not invent activity links.");

        await File.AppendAllTextAsync(actOne.AbsolutePath,
            "\n## Correction métier postérieure\n");
        SfdImportReviewSnapshot changed = await review.ReviewAsync(
            project.ProjectDirectory, secondResponse);
        Require(changed.PlanSha256 != secondReview.PlanSha256,
            "Modification d'une ACT doit invalider le plan FCT revu.");
        bool editRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                secondResponse, secondReview, "feature-with-ui");
        }
        catch (InvalidDataException)
        {
            editRefused = true;
        }

        Require(editRefused,
            "A modified ACT must block the replay rather than silently reuse links.");

        string foreign = Json("feature-external",
            "Fonctionnalité inter-projet invalide", "withoutUi", [outside.Card.Id]);
        SfdImportReviewSnapshot blocked = await review.ReviewAsync(
            project.ProjectDirectory, foreign);
        Require(blocked.Items.Single().Status ==
            SfdImportReviewStatus.NeedsResolution,
            "FCT source ACT from another project must require resolution.");
        bool foreignRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                foreign, blocked, "feature-external");
        }
        catch (InvalidDataException)
        {
            foreignRefused = true;
        }

        Require(foreignRefused, "Cross-project ACT/FCT publication must fail.");
    }

    /// <summary>Builds one strictly versioned fictional step-03 FCT response.</summary>
    /// <param name="key">Temporary FCT client key.</param>
    /// <param name="title">Fictional capability title.</param>
    /// <param name="mode">withUi or withoutUi.</param>
    /// <param name="activities">Real ACT GUIDs to relate.</param>
    /// <returns>Strict JSON payload for review.</returns>
    private static string Json(string key, string title, string mode, Guid[] activities) =>
        JsonSerializer.Serialize(new
        {
            schema = SfdImportContract.Schema,
            step = "sfd-03",
            runId = "fct-fictive-" + key,
            proposals = new object[]
            {
                new
                {
                    clientKey = key,
                    kind = "feature",
                    operation = "create",
                    certainty = "derived",
                    title,
                    description = "Démonstration uniquement, sans donnée réelle.",
                    uiMode = mode,
                    sourceKeys = Array.Empty<string>(),
                    activityKeys = activities.Select(id =>
                        id.ToString("D")).ToArray(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = Array.Empty<object>()
                }
            }
        });

    /// <summary>Validates a synthetic FCT publication invariant.</summary>
    /// <param name="condition">Expected true.</param>
    /// <param name="message">Diagnostic on failure.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
