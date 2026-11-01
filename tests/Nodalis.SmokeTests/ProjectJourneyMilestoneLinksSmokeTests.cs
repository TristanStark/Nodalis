using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Exercises journey links to canonical milestone GUIDs without ever
/// manufacturing, migrating or changing a milestone or gate decision.
/// </summary>
internal static class ProjectJourneyMilestoneLinksSmokeTests
{
    /// <summary>Runs the complete stable-ID, scope and non-owning relation scenario.</summary>
    /// <param name="root">Smoke fixture root.</param>
    /// <returns>Completed regression test.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Journey-milestone-links-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Parcours v2 · relations");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Test");
            Directory.CreateDirectory(application);
            Guid applicationId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                CreateRequest("Projet avec jalons", applicationId, application));
            ProjectCreationResult otherProject = await creator.CreateAsync(
                CreateRequest("Autre projet", applicationId, application));

            WorkspaceMilestoneService milestones = new(workspace);
            string milestonePath =
                await milestones.GetMilestoneFileForContextAsync(
                    project.ProjectDirectory) ??
                throw new InvalidOperationException("Jalons du projet introuvables.");
            string otherMilestonePath =
                await milestones.GetMilestoneFileForContextAsync(
                    otherProject.ProjectDirectory) ??
                throw new InvalidOperationException("Jalons du second projet introuvables.");
            Guid validId = Guid.NewGuid();
            Guid duplicateId = Guid.NewGuid();
            Guid outsideId = Guid.NewGuid();
            Guid existingGateId = Guid.NewGuid();

            string heading = "# Jalons\n\n" +
                "| Jalon | Date cible | Statut | Description | Lien | Id | Dépend de |\n" +
                "| --- | --- | --- | --- | --- | --- | --- |\n";
            string stableRow = "| Validation existante | 15/11/2026 | En cours | " +
                "Test de recette | | " + validId.ToString("D") + " | |\n";
            string duplicateRow1 = "| Dup A | 16/11/2026 | À faire | | | " +
                duplicateId.ToString("D") + " | |\n";
            string duplicateRow2 = "| Dup B | 17/11/2026 | À faire | | | " +
                duplicateId.ToString("D") + " | |\n";
            string legacyRow = "| Ancien sans ID | 18/11/2026 | À faire | | | | |\n";
            string stableSource = heading + stableRow + duplicateRow1 +
                duplicateRow2 + legacyRow;
            await File.WriteAllTextAsync(milestonePath, stableSource);
            await File.WriteAllTextAsync(otherMilestonePath,
                heading + "| Hors du projet | 19/11/2026 | À faire | | | " +
                outsideId.ToString("D") + " | |\n");

            ProjectJourneyMilestoneCatalogService catalogService = new(workspace);
            ProjectJourneyMilestoneCatalog catalog = await catalogService.ListAsync(
                project.ProjectDirectory);
            Assert(catalog.Candidates.Count == 1 &&
                catalog.Candidates[0].Id == validId &&
                catalog.Candidates[0].Name == "Validation existante" &&
                catalog.Candidates[0].LineNumber == 5 &&
                catalog.Candidates[0].SourceRelativePath.EndsWith(
                    "Jalons.md", StringComparison.OrdinalIgnoreCase) &&
                catalog.LegacyWithoutStableId == 1 &&
                catalog.DuplicateIds.SequenceEqual([duplicateId]) &&
                !catalog.Candidates.Any(item => item.Id == outsideId),
                "Only unique, physically persisted project GUIDs must be selectable.");
            Assert(await File.ReadAllTextAsync(milestonePath) == stableSource,
                "Browsing relations must never migrate legacy milestone IDs.");

            await RejectAsync(() => catalogService.RequireCurrentAsync(
                project.ProjectDirectory, duplicateId),
                "A duplicate GUID must not be available for a new relation.");
            await RejectAsync(() => catalogService.RequireCurrentAsync(
                project.ProjectDirectory, outsideId),
                "A foreign project's milestone must not be linked.");
            await RejectAsync(() => catalogService.RequireCurrentAsync(
                project.ProjectDirectory, Guid.NewGuid()),
                "Unknown milestone IDs must not be accepted.");
            Assert((await catalogService.RequireCurrentAsync(
                project.ProjectDirectory, validId)).LineNumber == 5,
                "The selected canonical milestone row must remain navigable.");

            ProjectJourneyService journeyService = new(workspace);
            ProjectJourneySnapshot initial = await journeyService.InitializeAsync(
                project.ProjectDirectory);
            ProjectJourneyStep stage = initial.Document.Steps.Single(item =>
                item.TemplateKey == "tests");
            ProjectJourneyOperation operation = stage.Operations[0];
            ProjectJourneyDocument proposed = ProjectJourneyEditor.AddMilestoneLink(
                initial.Document, stage.Id, operation.Id, validId);
            ProjectJourneyOperation linked = proposed.Steps.Single(item =>
                item.Id == stage.Id).Operations.Single(item =>
                item.Id == operation.Id);
            Assert(linked.MilestoneIds.SequenceEqual([validId]) &&
                linked.Id == operation.Id &&
                operation.MilestoneIds.Count == 0,
                "Linking must be pure and preserve operation GUID and initial snapshot.");

            proposed = ProjectJourneyEditor.AddMilestoneLink(
                proposed, stage.Id, operation.Id, validId);
            Assert(proposed.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == operation.Id)
                    .MilestoneIds.Count == 1,
                "Adding the same relation twice must not duplicate it.");

            ProjectJourneyOperation originalOperation = proposed.Steps.Single(item =>
                item.Id == stage.Id).Operations.Single(item => item.Id == operation.Id);
            ProjectJourneyDocument withGate = proposed with
            {
                Steps = proposed.Steps.Select(item =>
                    item.Id == stage.Id
                        ? item with
                        {
                            Operations = item.Operations.Select(op =>
                                op.Id == operation.Id
                                    ? op with { GateIds = [existingGateId] }
                                    : op).ToList()
                        }
                        : item).ToList()
            };
            Assert(originalOperation.GateIds.Count == 0,
                "Seeding a previously known gate reference must not change other snapshots.");
            ProjectJourneySnapshot saved = await journeyService.SaveAsync(
                project.ProjectDirectory, withGate, initial.Sha256);
            ProjectJourneySnapshot reloaded =
                (await new ProjectJourneyService(workspace).ReadAsync(
                    project.ProjectDirectory))!;
            Assert(saved.Sha256 == reloaded.Sha256 &&
                reloaded.Document.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == operation.Id)
                    .MilestoneIds.SequenceEqual([validId]) &&
                reloaded.Document.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == operation.Id)
                    .GateIds.SequenceEqual([existingGateId]),
                "Relations must survive a restart without duplicating source records.");

            ProjectJourneyDocument withoutGate = ProjectJourneyEditor.RemoveGateLink(
                reloaded.Document, stage.Id, operation.Id, existingGateId);
            ProjectJourneyDocument withoutMilestone =
                ProjectJourneyEditor.RemoveMilestoneLink(withoutGate,
                    stage.Id, operation.Id, validId);
            ProjectJourneyOperation unlinked = withoutMilestone.Steps.Single(item =>
                item.Id == stage.Id).Operations.Single(item => item.Id == operation.Id);
            Assert(unlinked.GateIds.Count == 0 &&
                unlinked.MilestoneIds.Count == 0 &&
                unlinked.Id == operation.Id &&
                await File.ReadAllTextAsync(milestonePath) == stableSource,
                "Unlinking must neither modify milestone Markdown nor gate decisions.");

            await RejectAsync(() => Task.FromResult(
                ProjectJourneyEditor.RemoveMilestoneLink(
                    withoutMilestone, stage.Id, operation.Id, validId)),
                "Removing an absent milestone link must be refused.");
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyEditor.RemoveGateLink(
                    withoutGate, stage.Id, operation.Id, existingGateId)),
                "Removing an absent gate link must be refused.");
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyEditor.AddMilestoneLink(
                    reloaded.Document, stage.Id, operation.Id, Guid.Empty)),
                "Empty relationship GUIDs must be refused.");

            // The linked milestone can disappear later. The journey retains
            // its non-owning reference until the user explicitly unlinks it.
            await File.WriteAllTextAsync(milestonePath, heading + legacyRow);
            await RejectAsync(() => catalogService.RequireCurrentAsync(
                project.ProjectDirectory, validId),
                "A deleted canonical milestone must no longer resolve.");
            Assert((await journeyService.ReadAsync(
                    project.ProjectDirectory))!.Document.Steps.Single(item =>
                    item.Id == stage.Id).Operations.Single(item =>
                    item.Id == operation.Id).MilestoneIds.Contains(validId),
                "External milestone changes must not silently mutate journey links.");
            await RejectAsync(() => journeyService.SaveAsync(
                project.ProjectDirectory, withoutMilestone, initial.Sha256),
                "Stale revisions must not overwrite existing journey relations.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Creates one disposable project without changing workspace settings.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="applicationId">Owning application ID.</param>
    /// <param name="application">Owning application path.</param>
    /// <returns>Creation request.</returns>
    private static ProjectCreationRequest CreateRequest(
        string name, Guid applicationId, string application) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Test",
            ParentDirectory = application,
            DisplayName = "Test"
        }
    };

    /// <summary>Checks that unsafe relation operations fail as expected.</summary>
    /// <param name="action">Expected failing operation.</param>
    /// <param name="label">Assertion diagnostic.</param>
    /// <returns>Completed check.</returns>
    private static async Task RejectAsync(Func<Task> action, string label)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is ArgumentException or
            IOException or InvalidDataException or InvalidOperationException or
            KeyNotFoundException)
        {
            rejected = true;
        }

        Assert(rejected, label);
    }

    /// <summary>Throws an explicit smoke failure.</summary>
    /// <param name="condition">Expected contract.</param>
    /// <param name="label">Diagnostic.</param>
    private static void Assert(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException(label);
        }
    }
}
