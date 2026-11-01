using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Exercises historical risk scores, explicit initial freeze, residual changes,
/// source edits, concurrency and project identity without WPF automation.
/// </summary>
internal static class ProjectRiskReviewSmokeTests
{
    /// <summary>Runs end-to-end canonical Markdown risk review regressions.</summary>
    /// <param name="root">Disposable regression root.</param>
    /// <returns>Completed smoke tests.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-review-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Risk review");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Review");
            Directory.CreateDirectory(application);
            FileSystemProjectCreator creator = new(workspace);
            Guid applicationId = Guid.NewGuid();
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject("Primary", applicationId, application));
            ProjectCreationResult foreign = await creator.CreateAsync(
                NewProject("Foreign", applicationId, application));
            ProjectRiskRegistryService service = new(workspace);

            ProjectRiskEntry created = await service.CreateAsync(
                project.ProjectDirectory, "Risque de disponibilité");
            ProjectRiskEntry initialSnapshot = (await service.ListAsync(
                project.ProjectDirectory)).Risks.Single();
            string riskPath = Path.Combine(project.ProjectDirectory, "Analyse",
                "Risques", created.Card.Reference + ".md");
            string original = await File.ReadAllTextAsync(riskPath);
            ProjectRiskReviewHistory unreviewed =
                ProjectRiskReviewMarkdownCodec.Inspect(original);
            Assert(unreviewed.Events.Count == 0 &&
                unreviewed.Card.Initial == new ProjectRiskRating(null, null) &&
                unreviewed.Card.Residual == new ProjectRiskRating(null, null),
                "A new risk must have no invented human rating.");

            await RejectAsync(() => Task.FromResult(
                ProjectRiskReviewMarkdownCodec.Append(original,
                    ProjectRiskReviewKind.Residual, new(1, 1),
                    "Reviewer", "Review before initial")),
                "Residual review must require a signed-off initial review.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskReviewMarkdownCodec.Append(original,
                    ProjectRiskReviewKind.Initial, new(null, null),
                    "Reviewer", "Missing both values")),
                "An unknown rating cannot be signed off as an evaluation.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskReviewMarkdownCodec.Append(original,
                    ProjectRiskReviewKind.Initial, new(1, null),
                    "Reviewer", "Incomplete risk matrix")),
                "A partial assessment cannot become an initial score.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskReviewMarkdownCodec.Append(original,
                    ProjectRiskReviewKind.Initial, new(2, 3),
                    "Reviewer", "brief")),
                "Review rationale must be substantive.");

            ProjectRiskEntry first = await service.ReviewAsync(
                project.ProjectDirectory, initialSnapshot,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(2, 3),
                "Responsable projet", "Qualification initiale argumentée.");
            Assert(first.Reviews.Count == 1 &&
                first.Reviews[0].Kind == ProjectRiskReviewKind.Initial &&
                first.Card.Initial == new ProjectRiskRating(2, 3) &&
                ProjectRiskMatrix.Evaluate(first.Card.Initial) ==
                    ProjectRiskSeverity.High &&
                first.Card.Residual == new ProjectRiskRating(null, null),
                "Initial rating is frozen, residual remains unassessed.");
            string afterFirst = await File.ReadAllTextAsync(riskPath);
            Assert(afterFirst.Contains("## Plan de contingence",
                    StringComparison.Ordinal) &&
                afterFirst.Contains("<!-- nodalis:risk-history:start -->",
                    StringComparison.Ordinal) &&
                ProjectRiskReviewMarkdownCodec.Inspect(afterFirst)
                    .Events.Single().Id == first.Reviews.Single().Id,
                "Risk source keeps free Markdown and audit markers.");

            await RejectAsync(() => service.ReviewAsync(
                project.ProjectDirectory, initialSnapshot,
                ProjectRiskReviewKind.Residual, new ProjectRiskRating(1, 1),
                "Reviewer", "Outdated snapshot must be denied."),
                "A stale risk selection must not overwrite the first review.");
            await RejectAsync(() => service.ReviewAsync(
                project.ProjectDirectory, first,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(1, 1),
                "Reviewer", "Initial rating must remain frozen."),
                "Initial review must not be recorded twice.");

            // A residual review is a separate human action and never
            // retroactively edits the first assessment.
            ProjectRiskEntry residual = await service.ReviewAsync(
                project.ProjectDirectory, first,
                ProjectRiskReviewKind.Residual, new ProjectRiskRating(1, 2),
                "Responsable recette",
                "Mesures appliquées et efficacité vérifiée.");
            Assert(residual.Reviews.Count == 2 &&
                residual.Reviews[0] == first.Reviews[0] &&
                residual.Reviews[1].Kind == ProjectRiskReviewKind.Residual &&
                residual.Card.Initial == new ProjectRiskRating(2, 3) &&
                residual.Card.Residual == new ProjectRiskRating(1, 2) &&
                ProjectRiskMatrix.Evaluate(residual.Card.Residual) ==
                    ProjectRiskSeverity.Low,
                "Residual rating may decrease only by independent human review.");
            string afterResidual = await File.ReadAllTextAsync(riskPath);
            Assert(afterResidual.Contains(
                "initial-probability: 2", StringComparison.Ordinal) &&
                afterResidual.Contains(
                    "initial-impact: 3", StringComparison.Ordinal),
                "Human residual requalification cannot rewrite frozen initial.");

            // YAML-only edits without matching signed-off events fail closed.
            string forgedScore = afterResidual.Replace(
                "initial-probability: 2", "initial-probability: 1",
                StringComparison.Ordinal);
            await RejectAsync(() => Task.FromResult(
                ProjectRiskReviewMarkdownCodec.Inspect(forgedScore)),
                "Edited historical score must disagree with immutable events.");
            string eventLine = afterResidual.Split('\n').First(line =>
                line.StartsWith("<!-- nodalis:risk-review:",
                    StringComparison.Ordinal));
            await RejectAsync(() => Task.FromResult(
                ProjectRiskReviewMarkdownCodec.Inspect(afterResidual.Replace(
                    eventLine + "\n", string.Empty, StringComparison.Ordinal))),
                "Removing a historical review must invalidate the timeline.");
            Assert(await File.ReadAllTextAsync(riskPath) == afterResidual,
                "Negative validation must not modify the original Markdown.");

            // A concurrent free-form edit is preserved; an already displayed
            // outdated snapshot must be reloaded before another review.
            string altered = afterResidual.Replace(
                "## Contexte et conséquences",
                "## Contexte et conséquences\n\nNote modifiée de l'extérieur.",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(riskPath, altered);
            await RejectAsync(() => service.ReviewAsync(
                project.ProjectDirectory, residual,
                ProjectRiskReviewKind.Residual, new ProjectRiskRating(3, 3),
                "Reviewer", "Concurrent changes must be reviewed."),
                "External Markdown modifications require user reloading.");
            Assert(await File.ReadAllTextAsync(riskPath) == altered,
                "Stale attempts must never overwrite free-form Markdown.");
            ProjectRiskEntry refreshed = (await service.ListAsync(
                project.ProjectDirectory)).Risks.Single();
            ProjectRiskEntry third = await service.ReviewAsync(
                project.ProjectDirectory, refreshed,
                ProjectRiskReviewKind.Residual, new ProjectRiskRating(3, 3),
                "Responsable exploitation",
                "Nouvel incident augmente le risque résiduel.");
            Assert(third.Reviews.Count == 3 &&
                third.Card.Initial == new ProjectRiskRating(2, 3) &&
                third.Card.Residual == new ProjectRiskRating(3, 3) &&
                third.Reviews[0] == first.Reviews[0] &&
                third.Reviews[1] == residual.Reviews[1] &&
                (await File.ReadAllTextAsync(riskPath))
                    .Contains("Note modifiée de l'extérieur.",
                        StringComparison.Ordinal),
                "Repeated residual reviews append and keep all original notes.");

            ProjectRiskEntry foreignEntry = await service.CreateAsync(
                foreign.ProjectDirectory, "Autre projet");
            await RejectAsync(() => service.ReviewAsync(
                project.ProjectDirectory, foreignEntry,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(1, 1),
                "Reviewer", "Foreign source should not be accepted."),
                "Review must reject a risk owned by another project.");

            // A manually forged current YAML score without an audit
            // causes the whole v2 candidate to be excluded, not silently
            // interpreted as human-approved low risk.
            string invalid = (await File.ReadAllTextAsync(riskPath)).Replace(
                "residual-impact: 3", "residual-impact: 2",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(riskPath, invalid);
            ProjectRiskCatalog invalidCatalog = await service.ListAsync(
                project.ProjectDirectory);
            Assert(!invalidCatalog.Risks.Any(item =>
                    item.Card.Id == third.Card.Id) &&
                invalidCatalog.Diagnostics.Count > 0,
                "A risk with mismatched manual score must be diagnosed.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Constructs one complex demo project with v2 risk folder.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="applicationId">Owner application.</param>
    /// <param name="directory">Application folder.</param>
    /// <returns>Project request.</returns>
    private static ProjectCreationRequest NewProject(
        string name, Guid applicationId, string directory) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Review",
            ParentDirectory = directory,
            DisplayName = "Review"
        }
    };

    /// <summary>Requires failures on forbidden risk review operations.</summary>
    /// <param name="action">Work expected to throw.</param>
    /// <param name="message">Why a rejection matters.</param>
    /// <returns>Completed negative test.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            ArgumentException or UnauthorizedAccessException or
            KeyNotFoundException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Throws when a regression is detected.</summary>
    /// <param name="condition">Required condition.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
