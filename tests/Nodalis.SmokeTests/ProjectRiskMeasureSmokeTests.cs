using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Regression for explicit treatment-measure execution, evidence, unique plan
/// versions, source revisions and independent business/rating histories.
/// </summary>
internal static class ProjectRiskMeasureSmokeTests
{
    /// <summary>Exercises one project risk and multiple treatment versions.</summary>
    /// <param name="root">Disposable test root.</param>
    /// <returns>Finished test cases.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-measure-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Risk measure progress");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Measures");
            Directory.CreateDirectory(app);
            FileSystemProjectCreator creator = new(workspace);
            Guid appId = Guid.NewGuid();
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject("Current", appId, app));
            ProjectCreationResult other = await creator.CreateAsync(
                NewProject("Foreign", appId, app));
            ProjectRiskRegistryService service = new(workspace);
            ProjectRiskEntry created = await service.CreateAsync(
                project.ProjectDirectory, "Défaillance de la production");
            string path = Path.Combine(project.ProjectDirectory, "Analyse",
                "Risques", created.Card.Reference + ".md");
            string initialSource = await File.ReadAllTextAsync(path);
            Assert(ProjectRiskMeasureMarkdownCodec.Inspect(initialSource)
                    .Events.Count == 0 &&
                created.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.Pending,
                "A risk never invents the progress of unplanned measures.");

            await RejectAsync(() => service.UpdateMeasureAsync(
                project.ProjectDirectory, created,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.Completed,
                "Responsable",
                "Tentative de réalisation sans plan validé.",
                "Compte rendu de recette."),
                "Measure progress must require a current treatment plan.");

            ProjectRiskEntry planned = await service.PlanTreatmentAsync(
                project.ProjectDirectory, created, Plan(
                    ProjectRiskTreatmentStrategy.Reduce),
                "Pilote recette", "Mesures préventives approuvées.");
            Assert(planned.MeasureEvents.Count == 0 &&
                planned.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.Pending &&
                planned.CurrentTreatment is not null,
                "A plan does not automatically start its measures.");

            string afterPlan = await File.ReadAllTextAsync(path);
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMeasureMarkdownCodec.Append(afterPlan,
                    ProjectRiskMeasureKind.Prevention,
                    ProjectRiskMeasureStatus.Completed,
                    "Responsable", "Mesure de prévention terminée.", null)),
                "A completed measure must include human-entered evidence.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMeasureMarkdownCodec.Append(afterPlan,
                    (ProjectRiskMeasureKind)100,
                    ProjectRiskMeasureStatus.InProgress,
                    "Responsable", "Mesure en cours de réalisation.", null)),
                "Unknown measure slots must be rejected.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMeasureMarkdownCodec.Append(afterPlan,
                    ProjectRiskMeasureKind.Prevention,
                    (ProjectRiskMeasureStatus)100,
                    "Responsable", "État non pris en charge.", null)),
                "Unknown states must not be accepted.");

            ProjectRiskEntry started = await service.UpdateMeasureAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.InProgress,
                "Responsable technique",
                "Les vérifications ont été démarrées explicitement.", null);
            Guid originalPlan = planned.Treatments[^1].Id;
            Assert(started.MeasureEvents.Count == 1 &&
                started.MeasureEvents[0].TreatmentId == originalPlan &&
                started.MeasureEvents[0].PreviousId is null &&
                started.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.InProgress &&
                started.MeasureStatus(ProjectRiskMeasureKind.Mitigation) ==
                    ProjectRiskMeasureStatus.Pending &&
                started.Card.Initial == new ProjectRiskRating(null, null) &&
                started.Card.Qualification == ProjectRiskQualification.ToReview,
                "Only one explicit prevention slot changed.");

            await RejectAsync(() => service.UpdateMeasureAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.Blocked, "Responsable",
                "Source périmée depuis le démarrage de la mesure.", null),
                "Old snapshots must not overwrite the measure history.");
            await RejectAsync(() => service.UpdateMeasureAsync(
                project.ProjectDirectory, started,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.InProgress, "Responsable",
                "Transition inchangée sans aucun nouvel événement.", null),
                "No-op measure status must not generate an audit event.");

            ProjectRiskEntry completed = await service.UpdateMeasureAsync(
                project.ProjectDirectory, started,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.Completed, "Responsable technique",
                "Tests de prévention effectués et vérifiés.",
                "PV de recette n° 42 et contrôle signé du 01/11/2026.");
            Assert(completed.MeasureEvents.Count == 2 &&
                completed.MeasureEvents[1].PreviousId ==
                    completed.MeasureEvents[0].Id &&
                completed.MeasureEvents[1].PreviousStatus ==
                    ProjectRiskMeasureStatus.InProgress &&
                completed.MeasureEvents[1].Status ==
                    ProjectRiskMeasureStatus.Completed &&
                completed.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.Completed &&
                completed.MeasureStatus(ProjectRiskMeasureKind.Contingency) ==
                    ProjectRiskMeasureStatus.Pending &&
                completed.Card.Initial == new ProjectRiskRating(null, null) &&
                completed.Card.Residual == new ProjectRiskRating(null, null),
                "Completed measure needs evidence and never lowers risk rating.");

            string sourceWithProgress = await File.ReadAllTextAsync(path);
            Assert(sourceWithProgress.IndexOf(
                    "<!-- nodalis:risk-measures:start -->",
                    StringComparison.Ordinal) <
                sourceWithProgress.IndexOf(
                    "<!-- nodalis:risk-treatment:start -->",
                    StringComparison.Ordinal) &&
                ProjectRiskMeasureMarkdownCodec.Inspect(sourceWithProgress)
                    .Events.Count == 2,
                "Measure audit must remain ahead of the treatment plan.");

            string secondEventLine = sourceWithProgress.Split('\n').Single(line =>
                line.Contains("\"Id\":\"" +
                    completed.MeasureEvents[^1].Id.ToString("D") +
                    "\"", StringComparison.OrdinalIgnoreCase));
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMeasureMarkdownCodec.Inspect(
                    sourceWithProgress.Replace(secondEventLine + "\n",
                        string.Empty, StringComparison.Ordinal))),
                "Removing the final event must conflict with its protected head.");
            string firstEventLine = sourceWithProgress.Split('\n').Single(line =>
                line.Contains("\"Id\":\"" +
                    completed.MeasureEvents[0].Id.ToString("D") +
                    "\"", StringComparison.OrdinalIgnoreCase));
            await RejectAsync(() => Task.FromResult(
                ProjectRiskMeasureMarkdownCodec.Inspect(
                    sourceWithProgress.Replace(firstEventLine + "\n",
                        string.Empty, StringComparison.Ordinal))),
                "Removing an earlier event must break the PreviousId chain.");

            // Other independent human review commands must preserve the audit.
            ProjectRiskEntry qualified = await service.QualifyAsync(
                project.ProjectDirectory, completed,
                ProjectRiskQualification.Validated, "Comité recette",
                "Risque maintenu au registre malgré la mesure réalisée.");
            ProjectRiskEntry rated = await service.ReviewAsync(
                project.ProjectDirectory, qualified,
                ProjectRiskReviewKind.Initial, new ProjectRiskRating(3, 3),
                "Comité recette",
                "Criticité initiale déterminée hors état d'exécution.");
            Assert(rated.MeasureEvents.Count == 2 &&
                rated.Card.Initial == new ProjectRiskRating(3, 3) &&
                rated.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.Completed,
                "Review and qualification cannot erase execution events.");

            ProjectRiskEntry revised = await service.PlanTreatmentAsync(
                project.ProjectDirectory, rated,
                Plan(ProjectRiskTreatmentStrategy.Transfer),
                "Responsable exploitation",
                "Le transfert contractuel change les mesures du plan.");
            Guid newPlan = revised.Treatments[^1].Id;
            Assert(newPlan != originalPlan &&
                revised.MeasureEvents.Count == 2 &&
                revised.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.Pending &&
                revised.MeasureStatus(ProjectRiskMeasureKind.Mitigation) ==
                    ProjectRiskMeasureStatus.Pending &&
                revised.Card.Initial == new ProjectRiskRating(3, 3),
                "Revision starts new unassessed slots, preserving old evidence.");

            ProjectRiskEntry blocked = await service.UpdateMeasureAsync(
                project.ProjectDirectory, revised,
                ProjectRiskMeasureKind.Mitigation,
                ProjectRiskMeasureStatus.Blocked, "Responsable contrats",
                "Accord prestataire nécessaire avant mise en service.", null);
            Assert(blocked.MeasureEvents.Count == 3 &&
                blocked.MeasureEvents[^1].TreatmentId == newPlan &&
                blocked.MeasureEvents[^1].PreviousId ==
                    completed.MeasureEvents[^1].Id &&
                blocked.MeasureStatus(ProjectRiskMeasureKind.Mitigation) ==
                    ProjectRiskMeasureStatus.Blocked &&
                blocked.MeasureStatus(ProjectRiskMeasureKind.Prevention) ==
                    ProjectRiskMeasureStatus.Pending &&
                blocked.Card.Qualification == ProjectRiskQualification.Validated,
                "Progress of a newer plan must not inherit old completion.");

            // Human change to free text must be kept, while stale state fails.
            string beforeExternal = await File.ReadAllTextAsync(path);
            string edited = beforeExternal.Replace(
                "## Contexte et conséquences",
                "## Contexte et conséquences\n\nNouvelle note externe.",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, edited);
            await RejectAsync(() => service.UpdateMeasureAsync(
                project.ProjectDirectory, blocked,
                ProjectRiskMeasureKind.Mitigation,
                ProjectRiskMeasureStatus.InProgress, "Responsable contrats",
                "Document modifié depuis la dernière lecture.", null),
                "Concurrent Markdown edits require explicit reload.");
            Assert(await File.ReadAllTextAsync(path) == edited,
                "Rejected progress changes must retain external text.");

            ProjectRiskEntry fresh = (await service.ListAsync(
                project.ProjectDirectory)).Risks.Single();
            ProjectRiskEntry resumed = await service.UpdateMeasureAsync(
                project.ProjectDirectory, fresh,
                ProjectRiskMeasureKind.Mitigation,
                ProjectRiskMeasureStatus.InProgress, "Responsable contrats",
                "La nouvelle note a été relue et le contrat est lancé.", null);
            Assert(resumed.MeasureEvents.Count == 4 &&
                resumed.MeasureEvents[^1].PreviousStatus ==
                    ProjectRiskMeasureStatus.Blocked &&
                (await File.ReadAllTextAsync(path)).Contains(
                    "Nouvelle note externe.", StringComparison.Ordinal),
                "User-driven unblock retains history and source body.");

            ProjectRiskEntry foreign = await service.CreateAsync(
                other.ProjectDirectory, "Risque externe");
            ProjectRiskEntry foreignPlan = await service.PlanTreatmentAsync(
                other.ProjectDirectory, foreign,
                Plan(ProjectRiskTreatmentStrategy.Accept), "Comité",
                "Plan externe approuvé pour contrôle isolé.");
            await RejectAsync(() => service.UpdateMeasureAsync(
                project.ProjectDirectory, foreignPlan,
                ProjectRiskMeasureKind.Prevention,
                ProjectRiskMeasureStatus.InProgress, "Comité",
                "Écriture d'un autre projet interdite.", null),
                "Foreign projects must never accept execution events.");

            string finalSource = await File.ReadAllTextAsync(path);
            string brokenHead = finalSource.Replace(
                "<!-- nodalis:risk-measures-head:",
                "<!-- nodalis:risk-measures-unknown:",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, brokenHead);
            ProjectRiskCatalog malformed = await service.ListAsync(
                project.ProjectDirectory);
            Assert(malformed.Diagnostics.Count > 0 &&
                !malformed.Risks.Any(item => item.Card.Id == resumed.Card.Id),
                "Broken execution log must be diagnosed, not silently migrated.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates a complex fixture with existing legacy risk table.</summary>
    /// <param name="name">Project label.</param>
    /// <param name="appId">Application owner GUID.</param>
    /// <param name="folder">Application directory.</param>
    /// <returns>Creation request.</returns>
    private static ProjectCreationRequest NewProject(
        string name, Guid appId, string folder) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Measures",
            ParentDirectory = folder,
            DisplayName = "Measures"
        }
    };

    /// <summary>Builds one complete mandatory treatment plan.</summary>
    /// <param name="strategy">Human-chosen strategy.</param>
    /// <returns>Complete plan.</returns>
    private static ProjectRiskTreatmentPlan Plan(
        ProjectRiskTreatmentStrategy strategy) => new(
        strategy, "Responsable projet",
        "Prévenir les anomalies par des contrôles avant déploiement.",
        "Réduire les conséquences par une phase de recette dédiée.",
        "Activer le plan de retour arrière selon la procédure approuvée.");

    /// <summary>Requires one invalid action to be safely rejected.</summary>
    /// <param name="action">Command to verify.</param>
    /// <param name="message">Diagnostic for failure.</param>
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
            ArgumentException or UnauthorizedAccessException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Fails on any regression.</summary>
    /// <param name="condition">Invariant to protect.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
