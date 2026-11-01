using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Verifies gate links reference real canonical milestone GUIDs.</summary>
internal static class ProjectGateMilestoneLinksSmokeTests
{
    /// <summary>Runs positive, forged-reference, stale-link and unlink checks.</summary>
    /// <param name="root">Disposable test root.</param>
    /// <returns>Completed regression suite.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Gate-milestone-links-v2");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("Gates et jalons");
        try
        {
            string app = Path.Combine(workspace, WorkspaceLayout.ApplicationsDirectoryName, "Test");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject("Projet de recette", appId, app));
            ProjectCreationResult other = await creator.CreateAsync(
                NewProject("Projet externe", appId, app));

            WorkspaceMilestoneService milestones = new(workspace);
            string path = await milestones.GetMilestoneFileForContextAsync(
                project.ProjectDirectory) ??
                throw new InvalidOperationException("Jalons absents.");
            string outside = await milestones.GetMilestoneFileForContextAsync(
                other.ProjectDirectory) ??
                throw new InvalidOperationException("Jalons externes absents.");
            Guid goodId = Guid.NewGuid();
            Guid duplicate = Guid.NewGuid();
            Guid foreign = Guid.NewGuid();
            const string heading = "# Jalons\n\n" +
                "| Jalon | Date cible | Statut | Description | Lien | Id | Dépend de |\n" +
                "| --- | --- | --- | --- | --- | --- | --- |\n";
            string markdown = heading +
                "| Validation | 12/11/2026 | À faire | | | " + goodId + " | |\n" +
                "| Doublon A | 12/11/2026 | À faire | | | " + duplicate + " | |\n" +
                "| Doublon B | 12/11/2026 | À faire | | | " + duplicate + " | |\n" +
                "| Hérité sans ID | 12/11/2026 | À faire | | | | |\n";
            await File.WriteAllTextAsync(path, markdown);
            await File.WriteAllTextAsync(outside, heading +
                "| Autre projet | 12/11/2026 | À faire | | | " + foreign + " | |\n");
            ProjectJourneyMilestoneCatalogService catalog = new(workspace);
            ProjectJourneyMilestoneCatalog candidates =
                await catalog.ListAsync(project.ProjectDirectory);
            Assert(candidates.Candidates.Count == 1 &&
                candidates.Candidates[0].Id == goodId &&
                candidates.DuplicateIds.SequenceEqual([duplicate]) &&
                candidates.LegacyWithoutStableId == 1,
                "Only unambiguous persisted project GUIDs are eligible.");

            ProjectGateRegistryService gates = new(workspace);
            ProjectGateRegistrySnapshot initial =
                await gates.ReadAsync(project.ProjectDirectory);
            ProjectGateRegistry created =
                ProjectGateEditor.CreateGate(initial.Registry, "Contrôle recette");
            Guid gateId = created.Gates.Single().Id;
            ProjectGateRegistry linked = ProjectGateEditor.LinkMilestone(
                created, gateId, goodId);
            ProjectGateRegistry twice = ProjectGateEditor.LinkMilestone(
                linked, gateId, goodId);
            Assert(created.Gates.Single().MilestoneIds.Count == 0 &&
                twice.Gates.Single().MilestoneIds.SequenceEqual([goodId]),
                "A repeated link must be pure and idempotent.");

            await RejectAsync(() => gates.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.LinkMilestone(linked, gateId, duplicate),
                initial.Sha256), "Ambiguous GUID was accepted.");
            await RejectAsync(() => gates.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.LinkMilestone(linked, gateId, foreign),
                initial.Sha256), "Foreign GUID was accepted.");
            await RejectAsync(() => gates.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.LinkMilestone(linked, gateId, Guid.NewGuid()),
                initial.Sha256), "Invented GUID was accepted.");

            ProjectGateRegistrySnapshot saved = await gates.SaveAsync(
                project.ProjectDirectory, linked, initial.Sha256);
            Assert(saved.Registry.Gates.Single().MilestoneIds.SequenceEqual([goodId]) &&
                (await gates.ReadAsync(project.ProjectDirectory))
                    .Registry.Gates.Single().MilestoneIds.SequenceEqual([goodId]) &&
                await File.ReadAllTextAsync(path) == markdown,
                "Confirmed link must persist without modifying its source.");

            string replacement = heading +
                "| Autre jalon | 16/11/2026 | À faire | | | " + Guid.NewGuid() + " | |\n";
            await File.WriteAllTextAsync(path, replacement);
            ProjectGateRegistry modified = ProjectGateEditor.AddCriterion(
                saved.Registry, gateId, "Contrôle supplémentaire");
            ProjectGateRegistrySnapshot changed = await gates.SaveAsync(
                project.ProjectDirectory, modified, saved.Sha256);
            Assert(changed.Registry.Gates.Single().MilestoneIds.Contains(goodId),
                "Unrelated gate edits must not delete a stale historical link.");

            ProjectGateRegistry secondGate = ProjectGateEditor.CreateGate(
                changed.Registry, "Nouveau contrôle");
            await RejectAsync(() => gates.SaveAsync(project.ProjectDirectory,
                ProjectGateEditor.LinkMilestone(
                    secondGate, secondGate.Gates.Last().Id, goodId),
                changed.Sha256), "A stale GUID was linked to a new gate.");

            ProjectGateRegistry unlinked = ProjectGateEditor.UnlinkMilestone(
                changed.Registry, gateId, goodId);
            Assert(unlinked.Gates.Single().MilestoneIds.Count == 0 &&
                changed.Registry.Gates.Single().MilestoneIds.SequenceEqual([goodId]),
                "Unlink must not mutate an earlier registry snapshot.");
            ProjectGateRegistrySnapshot cleared = await gates.SaveAsync(
                project.ProjectDirectory, unlinked, changed.Sha256);
            Assert(cleared.Registry.Gates.Single().MilestoneIds.Count == 0 &&
                await File.ReadAllTextAsync(path) == replacement,
                "Unlink must change the gate only, never a canonical milestone.");
            await RejectAsync(() => Task.FromResult(ProjectGateEditor.UnlinkMilestone(
                cleared.Registry, gateId, goodId)),
                "Removing a missing association must not succeed silently.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates an isolated Complex project.</summary>
    /// <param name="name">Distinct display name.</param>
    /// <param name="appId">Application identity.</param>
    /// <param name="directory">Application location.</param>
    /// <returns>Project creation request.</returns>
    private static ProjectCreationRequest NewProject(
        string name, Guid appId, string directory) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Test",
            ParentDirectory = directory,
            DisplayName = "Test"
        }
    };

    /// <summary>Checks that a rejected operation really failed.</summary>
    /// <param name="action">Operation expected to fail.</param>
    /// <param name="message">Assertion message.</param>
    /// <returns>Completed rejection assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or KeyNotFoundException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Throws when a regression breaks.</summary>
    /// <param name="condition">Required invariant.</param>
    /// <param name="message">Assertion message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
