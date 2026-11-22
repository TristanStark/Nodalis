using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Only fictional PM→ACT journal publication and recovery regressions.</summary>
internal static class SfdApprovedActivityImportSmokeTests
{
    /// <summary>Qualifies parent GUID/bytes, same-run mapping, replay and refusals.</summary>
    /// <param name="root">Disposable smoke-test directory.</param>
    /// <returns>Completed parent-bound ACT publication checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-act-publication");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync(
            "Workspace ACT fictif");
        string app = Path.Combine(workspace, "Applications", "Exemples");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(), ApplicationName = "Exemples",
            ParentDirectory = app, DisplayName = "Exemples"
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "SFD exemple",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        ProjectCreationResult other = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Autre SFD exemple",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        SfdDocumentRegistryService documents = new(workspace);
        SfdImportReviewService reviewer = new(workspace);
        SfdApprovedProcessImportService pmPublisher = new(workspace);
        SfdApprovedActivityImportService actPublisher = new(workspace);
        string response = SfdImportContract.ExampleJson;

        SfdImportReviewSnapshot initial = await reviewer.ReviewAsync(
            project.ProjectDirectory, response);
        SfdImportReviewItem pending = initial.Items.Single(item =>
            item.ClientKey == "act-demo");
        Require(pending.Status == SfdImportReviewStatus.NewCandidate &&
            pending.ResolvedParentId is null,
            "The ACT is merely a proposal before its same-run parent is approved.");

        bool missingParentRefused = false;
        try
        {
            await actPublisher.PublishAsync(project.ProjectDirectory,
                response, initial, "act-demo");
        }
        catch (InvalidDataException)
        {
            missingParentRefused = true;
        }

        Require(missingParentRefused &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 0,
            "Publication ACT must fail until the PM has actually been published.");

        SfdApprovedProcessResult pm = await pmPublisher.PublishAsync(
            project.ProjectDirectory, response, initial, "pm-demo");
        Require(pm.Id != Guid.Empty,
            "One explicitly approved parent must have a stable Nodalis GUID.");
        SfdImportReviewSnapshot afterPm = await reviewer.ReviewAsync(
            project.ProjectDirectory, response);
        SfdImportReviewItem resolved = afterPm.Items.Single(item =>
            item.ClientKey == "act-demo");
        Require(resolved.ResolvedParentId == pm.Id &&
            resolved.ResolvedParentSha256?.Length == 64 &&
            resolved.Status == SfdImportReviewStatus.NewCandidate,
            "The original AI parentKey must resolve to its published GUID and PM SHA.");

        SfdApprovedActivityResult first = await actPublisher.PublishAsync(
            project.ProjectDirectory, response, afterPm, "act-demo");
        SfdDocumentCatalog current = await documents.ListAsync(
            project.ProjectDirectory);
        Require(!first.AlreadyApplied &&
            first.Id != Guid.Empty && first.Reference == "PM-0001-ACT-001" &&
            first.ParentProcessId == pm.Id &&
            current.Diagnostics.Count == 0 &&
            current.Entries.Count == 2 &&
            current.Entries.Single(item => item.Card.Id == first.Id)
                .Card.ParentId == pm.Id &&
            current.Entries.Single(item => item.Card.Id == first.Id)
                .Card.Certainty == SfdAssertionCertainty.ToConfirm,
            "ACT must be created under the real PM, with GUID and unconfirmed status.");

        SfdApprovedActivityResult replay = await actPublisher.PublishAsync(
            project.ProjectDirectory, response, afterPm, "act-demo");
        Require(replay.AlreadyApplied && replay.Id == first.Id &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 2,
            "Replaying the original approved response cannot duplicate an ACT.");

        string journal = Path.Combine(project.ProjectDirectory, ".sfd-ai-import");
        string activityIntent = Directory.GetFiles(journal,
            "intent-activity.json", SearchOption.AllDirectories).Single();
        Require(File.Exists(activityIntent) &&
            File.ReadAllText(activityIntent).Contains(first.Id.ToString("D"),
                StringComparison.OrdinalIgnoreCase),
            "The ACT intent must retain the exact stable identity.");

        // Simulate power loss before the final atomic directory move.
        string staging = Path.Combine(Path.GetDirectoryName(activityIntent)!,
            "staged-activity");
        Directory.Move(Path.GetDirectoryName(first.AbsolutePath)!, staging);
        Require((await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 1,
            "Uncommitted ACT stage must not be visible as a canonical ACT.");
        SfdApprovedActivityResult resumed = await actPublisher.PublishAsync(
            project.ProjectDirectory, response, afterPm, "act-demo");
        Require(!resumed.AlreadyApplied && resumed.Id == first.Id &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 2,
            "Recovering the staged ACT must preserve its GUID and PM association.");

        await File.AppendAllTextAsync(first.AbsolutePath,
            "\n## Correction manuelle de l'activité fictive\n");
        bool changedActivityRefused = false;
        try
        {
            await actPublisher.PublishAsync(project.ProjectDirectory,
                response, afterPm, "act-demo");
        }
        catch (InvalidDataException)
        {
            changedActivityRefused = true;
        }

        Require(changedActivityRefused &&
            File.ReadAllText(first.AbsolutePath).Contains(
                "Correction manuelle", StringComparison.Ordinal),
            "A changed ACT must never be overwritten on repeated approval.");

        // An external change to the parent invalidates its review fingerprint.
        string existingParent = await File.ReadAllTextAsync(pm.AbsolutePath);
        await File.AppendAllTextAsync(pm.AbsolutePath,
            "\n## Déplacement de périmètre simulé\n");
        SfdImportReviewSnapshot newReview = await reviewer.ReviewAsync(
            project.ProjectDirectory, response);
        Require(newReview.PlanSha256 != afterPm.PlanSha256 &&
            newReview.Items.Single(item => item.ClientKey == "act-demo")
                .ResolvedParentSha256 != resolved.ResolvedParentSha256,
            "A manually edited PM must invalidate the reviewed ACT plan.");
        bool parentChangedRefused = false;
        try
        {
            await actPublisher.PublishAsync(project.ProjectDirectory,
                response, afterPm, "act-demo");
        }
        catch (InvalidDataException)
        {
            parentChangedRefused = true;
        }

        Require(parentChangedRefused,
            "Publishing an ACT after a parent modification must be refused.");
        await File.WriteAllTextAsync(pm.AbsolutePath, existingParent);

        // A second publication can target a pre-existing PM only by its GUID.
        SfdDocumentEntry manualParent = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process, "Autre PM fictif");
        string directResponse = JsonSerializer.Serialize(new
        {
            schema = SfdImportContract.Schema, step = "sfd-01",
            runId = "with-existing-parent",
            proposals = new object[]
            {
                new
                {
                    clientKey = "act-direct", kind = "businessActivity",
                    operation = "create", certainty = "toConfirm",
                    title = "Activité déjà contextualisée", description = "ACT fictive.",
                    parentKey = manualParent.Card.Id.ToString("D"),
                    sourceKeys = Array.Empty<string>(),
                    activityKeys = Array.Empty<string>(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = Array.Empty<object>()
                }
            }
        });
        SfdImportReviewSnapshot directReview = await reviewer.ReviewAsync(
            project.ProjectDirectory, directResponse);
        Require(directReview.Items.Single().ResolvedParentId ==
                manualParent.Card.Id,
            "An ACT can reference an existing same-project PM by GUID.");
        SfdApprovedActivityResult direct = await actPublisher.PublishAsync(
            project.ProjectDirectory, directResponse, directReview, "act-direct");
        Require(direct.Reference == "PM-0002-ACT-001" &&
            direct.ParentProcessId == manualParent.Card.Id,
            "The second PM must restart ACT numbering at 001.");

        // Cross-project GUID must be rejected even when it is structurally a PM.
        SfdDocumentEntry foreign = await documents.CreateAsync(
            other.ProjectDirectory, SfdDocumentKind.Process, "PM hors projet");
        string foreignResponse = directResponse.Replace(
            manualParent.Card.Id.ToString("D"), foreign.Card.Id.ToString("D"),
            StringComparison.Ordinal);
        SfdImportReviewSnapshot foreignReview = await reviewer.ReviewAsync(
            project.ProjectDirectory, foreignResponse);
        Require(foreignReview.Items.Single().Status ==
                SfdImportReviewStatus.NeedsResolution &&
            foreignReview.Items.Single().ResolvedParentId is null,
            "A PM GUID in another SFD project cannot be published as local parent.");

        bool foreignRefused = false;
        try
        {
            await actPublisher.PublishAsync(project.ProjectDirectory,
                foreignResponse, foreignReview, "act-direct");
        }
        catch (InvalidDataException)
        {
            foreignRefused = true;
        }

        Require(foreignRefused,
            "Cross-project parent references must fail closed.");
    }

    /// <summary>Verifies one entirely fictional SFD publication invariant.</summary>
    /// <param name="condition">Expected state.</param>
    /// <param name="message">Failure description.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
