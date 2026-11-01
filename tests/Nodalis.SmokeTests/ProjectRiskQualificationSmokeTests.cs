using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Human-only qualification history must not change the scores, lifecycle,
/// unrelated project files or free Markdown text.
/// </summary>
internal static class ProjectRiskQualificationSmokeTests
{
    /// <summary>Runs project-local business qualification regression scenarios.</summary>
    /// <param name="root">Disposable test workspace parent.</param>
    /// <returns>Completed source and history checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-qual-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Qualification métier RIS");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Quality");
            Directory.CreateDirectory(application);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                MakeRequest("Current", appId, application));
            ProjectCreationResult foreign = await creator.CreateAsync(
                MakeRequest("Foreign", appId, application));
            ProjectRiskRegistryService service = new(workspace);
            ProjectRiskEntry created = await service.CreateAsync(
                project.ProjectDirectory, "Absence de procédure");
            string riskFile = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", created.Card.Reference + ".md");
            string source = await File.ReadAllTextAsync(riskFile);
            ProjectRiskQualificationHistory original =
                ProjectRiskQualificationMarkdownCodec.Inspect(source);
            Assert(original.Events.Count == 0 &&
                original.Card.Qualification ==
                    ProjectRiskQualification.ToReview &&
                original.Card.Lifecycle == ProjectRiskLifecycle.Open,
                "A new risk has no implied human business qualification.");

            await RejectAsync(() => Task.FromResult(
                ProjectRiskQualificationMarkdownCodec.Append(
                    source, ProjectRiskQualification.ToReview,
                    "Expert", "Aucun changement d'état.")),
                "Repeated ToReview status must not create a fake decision.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskQualificationMarkdownCodec.Append(
                    source, ProjectRiskQualification.Validated,
                    "Expert", "court")),
                "A human reason is mandatory and must be meaningful.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskQualificationMarkdownCodec.Append(
                    source, (ProjectRiskQualification)99,
                    "Expert", "Ce statut n'existe pas.")),
                "Unknown statuses must not silently fall back to validated.");

            ProjectRiskEntry first = await service.QualifyAsync(
                project.ProjectDirectory, created,
                ProjectRiskQualification.Validated,
                "Responsable qualité",
                "Existence du risque confirmée par la recette.");
            Assert(first.Card.Qualification ==
                    ProjectRiskQualification.Validated &&
                first.Qualifications.Count == 1 &&
                first.Qualifications[0].Previous ==
                    ProjectRiskQualification.ToReview &&
                first.Qualifications[0].Status ==
                    ProjectRiskQualification.Validated &&
                first.Qualifications[0].RiskId == created.Card.Id &&
                first.Card.Initial == new ProjectRiskRating(null, null) &&
                first.Card.Residual == new ProjectRiskRating(null, null) &&
                first.Card.Lifecycle == ProjectRiskLifecycle.Open,
                "Qualification métier must not infer scores or lifecycle.");

            string afterFirst = await File.ReadAllTextAsync(riskFile);
            Assert(afterFirst.Contains(
                    "<!-- nodalis:risk-qualification:start -->",
                    StringComparison.Ordinal) &&
                afterFirst.Contains("## Plan de contingence",
                    StringComparison.Ordinal) &&
                ProjectRiskQualificationMarkdownCodec.Inspect(afterFirst)
                    .Events.Count == 1 &&
                ProjectRiskReviewMarkdownCodec.Inspect(afterFirst)
                    .Events.Count == 0,
                "A qualified unscored risk must have a valid independent audit.");

            await RejectAsync(() => service.QualifyAsync(
                project.ProjectDirectory, created,
                ProjectRiskQualification.Rejected,
                "Autre responsable", "Avis périmé après validation."),
                "A stale source selection must not overwrite a qualification.");
            await RejectAsync(() => service.QualifyAsync(
                project.ProjectDirectory, first,
                ProjectRiskQualification.Validated,
                "Responsable", "Ne change pas le statut."),
                "A repeated status is not a new event.");
            Assert(await File.ReadAllTextAsync(riskFile) == afterFirst,
                "Rejected qualification must never rewrite canonical source.");

            // Scores and business qualification may be reviewed in either order.
            ProjectRiskEntry rated = await service.ReviewAsync(
                project.ProjectDirectory, first,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(3, 3),
                "Expert technique",
                "Évaluation initiale indépendante du statut métier.");
            string withRating = await File.ReadAllTextAsync(riskFile);
            Assert(ProjectRiskReviewMarkdownCodec.Inspect(withRating)
                    .Events.Count == 1 &&
                ProjectRiskQualificationMarkdownCodec.Inspect(withRating)
                    .Events.Count == 1 &&
                withRating.IndexOf(
                    "<!-- nodalis:risk-qualification:start -->",
                    StringComparison.Ordinal) <
                withRating.IndexOf(
                    "<!-- nodalis:risk-history:start -->",
                    StringComparison.Ordinal),
                "Business decisions precede score history in canonical Markdown.");

            ProjectRiskEntry rejected = await service.QualifyAsync(
                project.ProjectDirectory, rated,
                ProjectRiskQualification.Rejected,
                "Comité de pilotage",
                "Nouvel élément contradictoire examiné explicitement.");
            Assert(rejected.Qualifications.Count == 2 &&
                rejected.Qualifications[0] == first.Qualifications[0] &&
                rejected.Qualifications[1].Previous ==
                    ProjectRiskQualification.Validated &&
                rejected.Qualifications[1].Status ==
                    ProjectRiskQualification.Rejected &&
                rejected.Card.Qualification ==
                    ProjectRiskQualification.Rejected &&
                rejected.Card.Initial == new ProjectRiskRating(3, 3) &&
                rejected.Reviews.Count == 1 &&
                rejected.Reviews[0] == rated.Reviews[0],
                "Requalification must preserve frozen initial and all decisions.");

            ProjectRiskEntry residual = await service.ReviewAsync(
                project.ProjectDirectory, rejected,
                ProjectRiskReviewKind.Residual, new ProjectRiskRating(1, 2),
                "Expert technique",
                "Mesures vérifiées après action et étude d'impact.");
            ProjectRiskEntry returned = await service.QualifyAsync(
                project.ProjectDirectory, residual,
                ProjectRiskQualification.ToReview,
                "Responsable qualité",
                "Le périmètre a changé, nouvelle qualification requise.");
            string finalSource = await File.ReadAllTextAsync(riskFile);
            Assert(returned.Card.Qualification ==
                    ProjectRiskQualification.ToReview &&
                returned.Card.Initial == new ProjectRiskRating(3, 3) &&
                returned.Card.Residual == new ProjectRiskRating(1, 2) &&
                returned.Qualifications.Count == 3 &&
                returned.Reviews.Count == 2 &&
                ProjectRiskQualificationMarkdownCodec.Inspect(finalSource)
                    .Events.Count == 3 &&
                ProjectRiskReviewMarkdownCodec.Inspect(finalSource)
                    .Events.Count == 2,
                "An explicit ToReview transition must not touch human scores.");
            Assert((await service.ListAsync(project.ProjectDirectory)).Risks
                    .Single().Qualifications.Count == 3,
                "The read-only registry must project complete business history.");

            // YAML tampering and event deletion invalidate the candidate.
            string forged = finalSource.Replace(
                "qualification: ToReview", "qualification: Validated",
                StringComparison.Ordinal);
            await RejectAsync(() => Task.FromResult(
                ProjectRiskQualificationMarkdownCodec.Inspect(forged)),
                "A changed YAML status without a matching event is not valid.");
            string eventLine = finalSource.Split('\n').First(line =>
                line.StartsWith("<!-- nodalis:risk-qualification-event:",
                    StringComparison.Ordinal));
            await RejectAsync(() => Task.FromResult(
                ProjectRiskQualificationMarkdownCodec.Inspect(
                    finalSource.Replace(
                        eventLine + "\n", string.Empty,
                        StringComparison.Ordinal))),
                "Removing an immutable decision must invalidate replay.");

            string unrelatedEdited = finalSource.Replace(
                "## Contexte et conséquences",
                "## Contexte et conséquences\n\nNote humaine externe.",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(riskFile, unrelatedEdited);
            await RejectAsync(() => service.QualifyAsync(
                project.ProjectDirectory, returned,
                ProjectRiskQualification.Validated,
                "Expert", "Modification concurrente non revue."),
                "A concurrent free-text edit requires a fresh read.");
            Assert(await File.ReadAllTextAsync(riskFile) == unrelatedEdited,
                "A rejected save must retain the concurrent Markdown note.");
            ProjectRiskEntry fresh = (await service.ListAsync(
                project.ProjectDirectory)).Risks.Single();
            ProjectRiskEntry approved = await service.QualifyAsync(
                project.ProjectDirectory, fresh,
                ProjectRiskQualification.Validated,
                "Expert", "Modification externe relue avant la décision.");
            Assert(approved.Qualifications.Count == 4 &&
                (await File.ReadAllTextAsync(riskFile)).Contains(
                    "Note humaine externe.", StringComparison.Ordinal),
                "Requalification after explicit reload retains free-form notes.");

            ProjectRiskEntry other = await service.CreateAsync(
                foreign.ProjectDirectory, "Risque étranger");
            await RejectAsync(() => service.QualifyAsync(
                project.ProjectDirectory, other,
                ProjectRiskQualification.Validated,
                "Comité", "Risque d'un projet extérieur."),
                "Project boundary must reject foreign business decisions.");

            string tampered = (await File.ReadAllTextAsync(riskFile)).Replace(
                "qualification: Validated", "qualification: Rejected",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(riskFile, tampered);
            ProjectRiskCatalog invalid = await service.ListAsync(
                project.ProjectDirectory);
            Assert(!invalid.Risks.Any(item =>
                    item.Card.Id == approved.Card.Id) &&
                invalid.Diagnostics.Count > 0,
                "A tampered business status is diagnosed, not accepted as valid.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates one isolated complex project fixture.</summary>
    /// <param name="name">Project label.</param>
    /// <param name="appId">Application GUID.</param>
    /// <param name="application">Application root.</param>
    /// <returns>New project request.</returns>
    private static ProjectCreationRequest MakeRequest(
        string name, Guid appId, string application) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Quality",
            ParentDirectory = application,
            DisplayName = "Quality"
        }
    };

    /// <summary>Requires a rejected invalid operation.</summary>
    /// <param name="action">Expected failure.</param>
    /// <param name="message">Diagnostic.</param>
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

    /// <summary>Checks one regression invariant.</summary>
    /// <param name="condition">Required condition.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
