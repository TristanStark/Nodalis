using Nodalis.Core.Domain;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Validates explicit v2 risk creation, Markdown IDs, collision handling,
/// non-migration of legacy documents and unassessed 3x3 risk matrix scores.
/// </summary>
internal static class ProjectRiskRegistrySmokeTests
{
    /// <summary>Runs a complete project-local risk registry regression.</summary>
    /// <param name="root">Disposable test root.</param>
    /// <returns>Completed test suite.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-v2-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Risk registry v2");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Risks");
            Directory.CreateDirectory(app);
            FileSystemProjectCreator creator = new(workspace);
            Guid applicationId = Guid.NewGuid();
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject("Risks main", applicationId, app));
            ProjectCreationResult foreign = await creator.CreateAsync(
                NewProject("Risks other", applicationId, app));
            ProjectRiskRegistryService service = new(workspace);
            string directory = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques");

            ProjectRiskCatalog empty = await service.ListAsync(
                project.ProjectDirectory);
            Assert(empty.Risks.Count == 0 && empty.Diagnostics.Count == 0 &&
                empty.NextReference == "RIS-0001" && !Directory.Exists(directory),
                "A read must not create risk directories or migrate legacy data.");

            Assert(ProjectRiskMatrix.Evaluate(new(null, null)) ==
                    ProjectRiskSeverity.Unassessed &&
                ProjectRiskMatrix.Evaluate(new(1, 1)) ==
                    ProjectRiskSeverity.Low &&
                ProjectRiskMatrix.Evaluate(new(1, 2)) ==
                    ProjectRiskSeverity.Low &&
                ProjectRiskMatrix.Evaluate(new(3, 1)) ==
                    ProjectRiskSeverity.Moderate &&
                ProjectRiskMatrix.Evaluate(new(2, 2)) ==
                    ProjectRiskSeverity.Moderate &&
                ProjectRiskMatrix.Evaluate(new(2, 3)) ==
                    ProjectRiskSeverity.High &&
                ProjectRiskMatrix.Evaluate(new(3, 3)) ==
                    ProjectRiskSeverity.High,
                "Unknown and the 1..3 x 1..3 classification are independent.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMatrix.Evaluate(new(3, null))),
                "Incomplete assessments must never be interpreted as low.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMatrix.Evaluate(new(0, 2))),
                "Out-of-range scores must be rejected.");
            await RejectAsync(() => service.CreateAsync(
                project.ProjectDirectory, "Newline\ninjected"),
                "Multiline title must not inject frontmatter.");
            Assert(!Directory.Exists(directory),
                "Rejected creation must not create risk folders.");

            // Existing legacy risk notes are never repurposed as v2 risk data.
            Directory.CreateDirectory(directory);
            string legacyFile = Path.Combine(directory, "RIS-0001.md");
            const string legacy = "# Legacy risk\n\nDo not convert.\n";
            await File.WriteAllTextAsync(legacyFile, legacy);
            ProjectRiskCatalog before = await service.ListAsync(
                project.ProjectDirectory);
            Assert(before.Diagnostics.Count == 1 &&
                before.Risks.Count == 0 && before.NextReference == "RIS-0002",
                "Legacy files must be diagnostics and reserve their filenames.");
            ProjectRiskEntry created = await service.CreateAsync(
                project.ProjectDirectory, "Retard de déploiement");
            Assert(created.Card.Reference == "RIS-0002" &&
                created.Card.Id != Guid.Empty &&
                created.Card.ProjectId != Guid.Empty &&
                created.Card.Qualification == ProjectRiskQualification.ToReview &&
                created.Card.Lifecycle == ProjectRiskLifecycle.Open &&
                ProjectRiskMatrix.Evaluate(created.Card.Initial) ==
                    ProjectRiskSeverity.Unassessed &&
                ProjectRiskMatrix.Evaluate(created.Card.Residual) ==
                    ProjectRiskSeverity.Unassessed &&
                await File.ReadAllTextAsync(legacyFile) == legacy,
                "Creation must persist a new, explicitly unassessed risk.");

            string firstPath = Path.Combine(directory, "RIS-0002.md");
            string original = await File.ReadAllTextAsync(firstPath);
            ProjectRiskCard parsed = ProjectRiskMarkdownCodec.Parse(original);
            Assert(parsed == created.Card || parsed.Id == created.Card.Id &&
                parsed.Reference == created.Card.Reference &&
                parsed.CreatedUtc == created.Card.CreatedUtc &&
                parsed.Title == created.Card.Title,
                "Stable GUID, reference and metadata must survive a round-trip.");
            ProjectRiskEntry second = await service.CreateAsync(
                project.ProjectDirectory, "Baisse de disponibilité");
            Assert(second.Card.Reference == "RIS-0003" &&
                second.Card.Id != created.Card.Id,
                "Separate risk files require distinct IDs and readable codes.");

            ProjectRiskCatalog after = await service.ListAsync(
                project.ProjectDirectory);
            Assert(after.Risks.Count == 2 &&
                after.NextReference == "RIS-0004" &&
                after.Risks.Select(item => item.Card.Id).Distinct().Count() == 2,
                "Registry should index both canonical project-local risks.");

            // Duplicate GUIDs must not be silently reassigned by scanning.
            string duplicatePath = Path.Combine(directory, "copied-risk.md");
            await File.WriteAllTextAsync(duplicatePath, original);
            ProjectRiskCatalog withDuplicate = await service.ListAsync(
                project.ProjectDirectory);
            Assert(withDuplicate.Risks.Count == 1 &&
                withDuplicate.Diagnostics.Count >= 3 &&
                withDuplicate.Risks[0].Card.Id == second.Card.Id,
                "Duplicated GUID/references must exclude ambiguous records.");
            File.Delete(duplicatePath);

            // A valid-looking card from another project is not linked to this one.
            string alienPath = Path.Combine(directory, "RIS-0090.md");
            ProjectRiskCard foreignCard = parsed with
            {
                Id = Guid.NewGuid(),
                ProjectId = foreign.ProjectId,
                Reference = "RIS-0090",
                Title = "Risque d'un autre projet"
            };
            await File.WriteAllTextAsync(alienPath,
                ProjectRiskMarkdownCodec.Format(foreignCard));
            ProjectRiskCatalog withAlien = await service.ListAsync(
                project.ProjectDirectory);
            Assert(withAlien.Risks.Count == 2 &&
                withAlien.Diagnostics.Count == 2 &&
                withAlien.NextReference == "RIS-0091",
                "Foreign risks must be diagnosed and never rewritten.");
            File.Delete(alienPath);

            // The source remains editable Markdown and parsing must fail closed
            // if ratings are only partially reviewed or metadata are repeated.
            ProjectRiskCard partial = parsed with
            {
                Initial = new ProjectRiskRating(3, null)
            };
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMarkdownCodec.Format(partial)),
                "A partial initial rating must never be written.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMarkdownCodec.Parse(original.Replace(
                    "reference: RIS-0002",
                    "reference: RIS-0002\nreference: RIS-0003",
                    StringComparison.Ordinal))),
                "Duplicate metadata fields must be rejected.");

            await RejectAsync(() => service.ListAsync(
                Path.Combine(root, "Outside-project")),
                "A project outside the configured workspace must be rejected.");
            Assert(await File.ReadAllTextAsync(firstPath) == original,
                "Browsing and negative tests must never mutate the risk source.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Builds a project fixture without global workspace changes.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="applicationId">Owning application GUID.</param>
    /// <param name="app">Application folder.</param>
    /// <returns>Project request.</returns>
    private static ProjectCreationRequest NewProject(
        string name, Guid applicationId, string app) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Risks",
            ParentDirectory = app,
            DisplayName = "Risks"
        }
    };

    /// <summary>Expects an invalid operation to be denied.</summary>
    /// <param name="action">Operation under test.</param>
    /// <param name="message">Failure explanation.</param>
    /// <returns>Completed negative check.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or UnauthorizedAccessException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Fails the smoke test if the contract breaks.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Reason.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
