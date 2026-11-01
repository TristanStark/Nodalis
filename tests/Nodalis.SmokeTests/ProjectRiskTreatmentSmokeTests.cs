using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Tests audited human treatment strategies and revisions separately from
/// business qualification, scores, lifecycle and free-form Markdown.
/// </summary>
internal static class ProjectRiskTreatmentSmokeTests
{
    /// <summary>Runs the full project-scoped risk treatment regression.</summary>
    /// <param name="root">Disposable test directory.</param>
    /// <returns>Completed scenario.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-treatment-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Risk treatment");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Treatment");
            Directory.CreateDirectory(app);
            FileSystemProjectCreator creator = new(workspace);
            Guid appId = Guid.NewGuid();
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject("Treatment main", appId, app));
            ProjectCreationResult foreign = await creator.CreateAsync(
                NewProject("Treatment foreign", appId, app));
            ProjectRiskRegistryService service = new(workspace);
            ProjectRiskEntry created = await service.CreateAsync(
                project.ProjectDirectory, "Risque de défaillance applicative");
            string path = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", created.Card.Reference + ".md");
            string original = await File.ReadAllTextAsync(path);
            Assert(ProjectRiskTreatmentMarkdownCodec.Inspect(original)
                    .Events.Count == 0 &&
                created.CurrentTreatment is null,
                "Creating a risk must not create an implicit treatment.");
            ProjectRiskTreatmentPlan firstPlan = Plan(
                ProjectRiskTreatmentStrategy.Reduce);
            ProjectRiskTreatmentPlan invalid = firstPlan with
            {
                Mitigation = " ",
            };
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTreatmentMarkdownCodec.Append(original,
                    invalid, "Responsable",
                    "Le plan est incomplet sans atténuation.")),
                "All three treatment fields must be explicitly completed.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTreatmentMarkdownCodec.Append(original,
                    firstPlan with { Strategy = (ProjectRiskTreatmentStrategy)42 },
                    "Responsable", "Stratégie inconnue.")),
                "Unknown treatment strategy must be rejected.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTreatmentMarkdownCodec.Append(original,
                    firstPlan, "Responsable", "court")),
                "Plan revision requires substantive human justification.");

            ProjectRiskEntry first = await service.PlanTreatmentAsync(
                project.ProjectDirectory, created, firstPlan,
                "Responsable de recette",
                "Plan de réduction revu avant mise en production.");
            Assert(first.Treatments.Count == 1 &&
                first.Treatments[0].PreviousId is null &&
                first.Treatments[0].RiskId == created.Card.Id &&
                first.Treatments[0].Plan == firstPlan &&
                first.CurrentTreatment == firstPlan &&
                first.Card.Initial == new ProjectRiskRating(null, null) &&
                first.Card.Residual == new ProjectRiskRating(null, null) &&
                first.Card.Qualification == ProjectRiskQualification.ToReview &&
                first.Card.Lifecycle == ProjectRiskLifecycle.Open,
                "First human plan must not change risk scores or status.");
            string afterFirst = await File.ReadAllTextAsync(path);
            Assert(afterFirst.Contains(
                    "<!-- nodalis:risk-treatment:start -->",
                    StringComparison.Ordinal) &&
                afterFirst.Contains("## Plan de contingence",
                    StringComparison.Ordinal) &&
                ProjectRiskTreatmentMarkdownCodec.Inspect(afterFirst)
                    .Events.Count == 1,
                "The canonical Markdown source must preserve its free sections.");

            await RejectAsync(() => service.PlanTreatmentAsync(
                project.ProjectDirectory, created,
                Plan(ProjectRiskTreatmentStrategy.Accept), "Responsable",
                "Le snapshot du précédent plan est obsolète."),
                "An outdated risk SHA must not overwrite a treatment.");
            await RejectAsync(() => service.PlanTreatmentAsync(
                project.ProjectDirectory, first, firstPlan, "Responsable",
                "Ce plan identique ne constitue pas une révision."),
                "Duplicate plan must not create a false historical revision.");
            Assert(await File.ReadAllTextAsync(path) == afterFirst,
                "No-op or rejected edits must not modify source bytes.");

            // Verify both journals can be appended AFTER an existing treatment
            // without changing the plan history, in correct block order.
            ProjectRiskEntry qualified = await service.QualifyAsync(
                project.ProjectDirectory, first,
                ProjectRiskQualification.Validated, "Comité",
                "Risque confirmé par les preuves d'exploitation.");
            ProjectRiskEntry rated = await service.ReviewAsync(
                project.ProjectDirectory, qualified,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(3, 3),
                "Expert fonctionnel",
                "Impact et probabilité évalués avec la recette.");
            string withThree = await File.ReadAllTextAsync(path);
            int treatPosition = withThree.IndexOf(
                "<!-- nodalis:risk-treatment:start -->",
                StringComparison.Ordinal);
            int qualifyPosition = withThree.IndexOf(
                "<!-- nodalis:risk-qualification:start -->",
                StringComparison.Ordinal);
            int scorePosition = withThree.IndexOf(
                "<!-- nodalis:risk-history:start -->",
                StringComparison.Ordinal);
            Assert(treatPosition >= 0 && treatPosition < qualifyPosition &&
                qualifyPosition < scorePosition,
                "Treatment must precede qualification and immutable rating events.");

            ProjectRiskTreatmentPlan revisedPlan = Plan(
                ProjectRiskTreatmentStrategy.Transfer) with
            {
                Owner = "Équipe exploitation",
                Mitigation = "Transférer une part du risque via contrat de service.",
                Contingency = "Basculer vers l'hébergement de secours documenté."
            };
            ProjectRiskEntry second = await service.PlanTreatmentAsync(
                project.ProjectDirectory, rated, revisedPlan,
                "Responsable exploitation",
                "Contrat externe validé par le comité de pilotage.");
            Assert(second.Treatments.Count == 2 &&
                second.Treatments[0] == first.Treatments[0] &&
                second.Treatments[1].PreviousId == first.Treatments[0].Id &&
                second.Treatments[1].Plan == revisedPlan &&
                second.Card.Initial == new ProjectRiskRating(3, 3) &&
                second.Card.Residual == new ProjectRiskRating(null, null) &&
                second.Card.Qualification == ProjectRiskQualification.Validated &&
                second.Qualifications.Count == 1 &&
                second.Reviews.Count == 1 &&
                second.CurrentTreatment == revisedPlan,
                "Revising a plan must preserve prior plans and both other journals.");

            ProjectRiskEntry residual = await service.ReviewAsync(
                project.ProjectDirectory, second,
                ProjectRiskReviewKind.Residual, new ProjectRiskRating(1, 2),
                "Responsable recette",
                "Mesures vérifiées et indicateurs suivis en recette.");
            ProjectRiskEntry unqualified = await service.QualifyAsync(
                project.ProjectDirectory, residual,
                ProjectRiskQualification.ToReview, "Comité",
                "Le changement de stratégie doit être requalifié.");
            string finalSource = await File.ReadAllTextAsync(path);
            Assert(unqualified.Treatments.Count == 2 &&
                unqualified.Qualifications.Count == 2 &&
                unqualified.Reviews.Count == 2 &&
                unqualified.Card.Initial == new ProjectRiskRating(3, 3) &&
                unqualified.Card.Residual == new ProjectRiskRating(1, 2) &&
                unqualified.Card.Qualification == ProjectRiskQualification.ToReview &&
                (await service.ListAsync(project.ProjectDirectory))
                    .Risks.Single().Treatments.Count == 2,
                "All three journals stay independent after subsequent updates.");

            // Editing or dropping a chained historical event must be detected.
            string secondLine = finalSource.Split('\n').Single(line =>
                line.Contains("\"Id\":\"" + second.Treatments[1].Id.ToString("D") +
                    "\"", StringComparison.OrdinalIgnoreCase));
            string corrupted = finalSource.Replace(secondLine + "\n",
                string.Empty, StringComparison.Ordinal);
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTreatmentMarkdownCodec.Inspect(corrupted)),
                "A silently removed treatment revision must not be valid.");
            string firstLine = finalSource.Split('\n').Single(line =>
                line.Contains("\"Id\":\"" + first.Treatments[0].Id.ToString("D") +
                    "\"", StringComparison.OrdinalIgnoreCase));
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTreatmentMarkdownCodec.Inspect(
                    finalSource.Replace(firstLine + "\n",
                        string.Empty, StringComparison.Ordinal))),
                "Removing a predecessor must break the revision chain.");
            Assert(await File.ReadAllTextAsync(path) == finalSource,
                "Negative history checks must not mutate the real risk.");

            // A concurrent Markdown change requires explicit human reload.
            string edited = finalSource.Replace(
                "## Contexte et conséquences",
                "## Contexte et conséquences\n\nNote ajoutée par l'équipe.",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, edited);
            await RejectAsync(() => service.PlanTreatmentAsync(
                project.ProjectDirectory, unqualified,
                Plan(ProjectRiskTreatmentStrategy.Avoid),
                "Comité", "La fiche a changé depuis la revue."),
                "A stale SHA must not overwrite the risk source.");
            Assert(await File.ReadAllTextAsync(path) == edited,
                "Concurrent edits must remain intact.");
            ProjectRiskEntry reloaded = (await service.ListAsync(
                project.ProjectDirectory)).Risks.Single();
            ProjectRiskEntry third = await service.PlanTreatmentAsync(
                project.ProjectDirectory, reloaded,
                Plan(ProjectRiskTreatmentStrategy.Avoid), "Comité",
                "Le nouveau périmètre impose d'éviter ce risque.");
            Assert(third.Treatments.Count == 3 &&
                third.Treatments[^1].PreviousId == second.Treatments[^1].Id &&
                (await File.ReadAllTextAsync(path)).Contains(
                    "Note ajoutée par l'équipe.", StringComparison.Ordinal),
                "A revised plan appends an event without deleting free text.");

            // The initial plan can also be created when both other journals
            // are already present, not just when the risk is still new.
            ProjectRiskEntry other = await service.CreateAsync(
                foreign.ProjectDirectory, "Autre plan");
            ProjectRiskEntry otherRated = await service.ReviewAsync(
                foreign.ProjectDirectory, other,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(2, 2),
                "Expert", "Évaluation du risque étranger indépendant.");
            ProjectRiskEntry otherQualified = await service.QualifyAsync(
                foreign.ProjectDirectory, otherRated,
                ProjectRiskQualification.Validated, "Responsable",
                "Qualification métier avant création du plan.");
            ProjectRiskEntry otherPlanned = await service.PlanTreatmentAsync(
                foreign.ProjectDirectory, otherQualified,
                Plan(ProjectRiskTreatmentStrategy.Accept),
                "Responsable", "Acceptation documentée avec surveillance.");
            Assert(otherPlanned.Treatments.Count == 1 &&
                otherPlanned.Reviews.Count == 1 &&
                otherPlanned.Qualifications.Count == 1,
                "Treatment must also insert ahead of existing audit blocks.");
            await RejectAsync(() => service.PlanTreatmentAsync(
                project.ProjectDirectory, otherPlanned,
                Plan(ProjectRiskTreatmentStrategy.Reduce),
                "Responsable", "Risque d'un autre projet."),
                "Project ownership prevents a cross-project treatment.");

            // Manual malformed history is diagnosed, never adopted as a plan.
            string sourceWithHistory = await File.ReadAllTextAsync(path);
            string invalidSource = sourceWithHistory.Replace(
                "<!-- nodalis:risk-treatment:start -->",
                "<!-- nodalis:risk-treatment:missing -->",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, invalidSource);
            ProjectRiskCatalog invalidCatalog = await service.ListAsync(
                project.ProjectDirectory);
            Assert(invalidCatalog.Diagnostics.Count > 0 &&
                !invalidCatalog.Risks.Any(item =>
                    item.Card.Id == third.Card.Id),
                "A malformed plan must be excluded, not silently repaired.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Builds a documented risk project fixture.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="appId">Application GUID.</param>
    /// <param name="folder">Owning application folder.</param>
    /// <returns>Project creation request.</returns>
    private static ProjectCreationRequest NewProject(
        string name, Guid appId, string folder) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Treatment",
            ParentDirectory = folder,
            DisplayName = "Treatment"
        }
    };

    /// <summary>Creates a complete human treatment plan for each strategy.</summary>
    /// <param name="strategy">Explicit strategy.</param>
    /// <returns>Complete plan for the smoke fixture.</returns>
    private static ProjectRiskTreatmentPlan Plan(
        ProjectRiskTreatmentStrategy strategy) => new(
        strategy,
        "Responsable de recette",
        "Vérification préventive des versions avant mise en service.",
        "Déployer un correctif vérifié dans l'environnement de recette.",
        "Revenir à la version validée et avertir les équipes d'exploitation.");

    /// <summary>Checks that one invalid operation is blocked safely.</summary>
    /// <param name="action">Rejected operation.</param>
    /// <param name="message">Diagnostic.</param>
    /// <returns>Completed test.</returns>
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

    /// <summary>Fails if an important risk invariant regresses.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
