using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Tasks;

/// <summary>
/// Verifies read-only reverse navigation from a durable task GUID to all
/// current, past-version and deliberately removed risk-measure relations.
/// </summary>
internal static class ProjectRiskTaskBacklinkSmokeTests
{
    /// <summary>Checks the full project-backed reverse lookup without writes.</summary>
    /// <param name="root">Disposable workspace parent.</param>
    /// <returns>Completed catalog assertions.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-backlinks-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Risk task backlinks");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Backlinks");
            Directory.CreateDirectory(application);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                CreateProject("Current", appId, application));
            ProjectCreationResult other = await creator.CreateAsync(
                CreateProject("Foreign", appId, application));

            WorkspaceTaskService tasks = new(workspace);
            ProjectRiskRegistryService riskService = new(workspace);
            ProjectRiskTaskBacklinkService resolver = new(workspace);
            TaskItem task = await tasks.CreateTaskAsync(
                project.ProjectDirectory, "Tester la procédure de bascule",
                new TaskMetadataUpdate());
            TaskItem foreign = await tasks.CreateTaskAsync(
                other.ProjectDirectory, "Tâche d'un projet voisin",
                new TaskMetadataUpdate());
            Guid id = task.PersistentId!.Value;
            ProjectRiskTaskBacklinkCatalog empty =
                await resolver.ListAsync(task);
            Assert(empty.TaskId == id && empty.Links.Count == 0 &&
                empty.Diagnostics.Count == 0,
                "A task does not acquire automatic risk links.");

            ProjectRiskEntry risk = await riskService.CreateAsync(
                project.ProjectDirectory, "Bascule incomplète");
            ProjectRiskEntry plan = await riskService.PlanTreatmentAsync(
                project.ProjectDirectory, risk, Treatment(
                    ProjectRiskTreatmentStrategy.Reduce),
                "Responsable de projet",
                "Le plan de prévention est validé par le comité.");
            ProjectRiskEntry linked = await riskService.ChangeTaskLinkAsync(
                project.ProjectDirectory, plan,
                ProjectRiskMeasureKind.Prevention, id,
                ProjectRiskTaskLinkAction.Link, "Équipe recette",
                "Contrôle préventif réalisé via cette tâche technique.");

            string riskFile = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", linked.Card.Reference + ".md");
            string taskFile = Path.Combine(project.ProjectDirectory, "Tâches.md");
            string beforeRisk = await File.ReadAllTextAsync(riskFile);
            string beforeTask = await File.ReadAllTextAsync(taskFile);
            ProjectRiskTaskBacklinkCatalog first = await resolver.ListAsync(task);
            Assert(first.TaskId == id && first.Links.Count == 1 &&
                first.Links[0].RiskId == linked.Card.Id &&
                first.Links[0].RiskRelativePath == linked.RelativePath &&
                first.Links[0].TreatmentId == plan.Treatments[^1].Id &&
                first.Links[0].Kind == ProjectRiskMeasureKind.Prevention &&
                first.Links[0].IsCurrentPlan && first.Links[0].IsLinked &&
                first.Links[0].Status.Contains("actuel",
                    StringComparison.OrdinalIgnoreCase) &&
                await File.ReadAllTextAsync(riskFile) == beforeRisk &&
                await File.ReadAllTextAsync(taskFile) == beforeTask,
                "Backlinks are read-only and point at verified canonical RIS.");

            ProjectRiskTaskBacklinkCatalog foreignLookup =
                await resolver.ListAsync(foreign);
            Assert(foreignLookup.Links.Count == 0,
                "A task of another project must never inherit backlinks.");
            Assert((await resolver.ListAsync(task with
                {
                    PersistentId = null
                })).Links.Count == 0,
                "A derived-only task identity must not navigate to any RIS.");

            // A risk link remains stable if the task file moves, even if the
            // old UI snapshot still contains its original document path.
            string movedFile = Path.Combine(project.ProjectDirectory,
                "Tâches de recette.md");
            File.Move(taskFile, movedFile);
            ProjectRiskTaskBacklinkCatalog moved = await resolver.ListAsync(task);
            Assert(moved.Links.Count == 1 &&
                moved.Links[0].RiskId == linked.Card.Id &&
                moved.TaskId == id,
                "A task Markdown source file move keeps the same backlink.");

            ProjectRiskEntry removed = await riskService.ChangeTaskLinkAsync(
                project.ProjectDirectory, linked,
                ProjectRiskMeasureKind.Prevention, id,
                ProjectRiskTaskLinkAction.Unlink, "Responsable de projet",
                "Ce contrôle n'est plus rattaché au plan de prévention.");
            ProjectRiskTaskBacklinkCatalog historical =
                await resolver.ListAsync(task);
            Assert(historical.Links.Count == 1 &&
                !historical.Links[0].IsLinked &&
                historical.Links[0].IsCurrentPlan &&
                historical.Links[0].Status.Contains("Délié",
                    StringComparison.Ordinal),
                "Explicitly unlinked relations remain visible as history.");

            ProjectRiskEntry relinked = await riskService.ChangeTaskLinkAsync(
                project.ProjectDirectory, removed,
                ProjectRiskMeasureKind.Prevention, id,
                ProjectRiskTaskLinkAction.Link, "Responsable de projet",
                "Le contrôle préventif est réintégré après réexamen.");
            ProjectRiskEntry revised = await riskService.PlanTreatmentAsync(
                project.ProjectDirectory, relinked,
                Treatment(ProjectRiskTreatmentStrategy.Transfer),
                "Comité de pilotage",
                "Une nouvelle stratégie crée une version de traitement.");
            ProjectRiskTaskBacklinkCatalog superseded =
                await resolver.ListAsync(task);
            Assert(superseded.Links.Count == 1 &&
                superseded.Links[0].IsLinked &&
                !superseded.Links[0].IsCurrentPlan &&
                superseded.Links[0].Status.Contains("ancien",
                    StringComparison.Ordinal),
                "Revised plans expose previous relations as historical only.");

            ProjectRiskEntry currentLink = await riskService.ChangeTaskLinkAsync(
                project.ProjectDirectory, revised,
                ProjectRiskMeasureKind.Contingency, id,
                ProjectRiskTaskLinkAction.Link, "Responsable d'exploitation",
                "La même tâche aide également au plan de contingence.");
            ProjectRiskTaskBacklinkCatalog multiple =
                await resolver.ListAsync(task);
            Assert(multiple.Links.Count == 2 &&
                multiple.Links.Any(item =>
                    item.IsCurrentPlan && item.IsLinked &&
                    item.Kind == ProjectRiskMeasureKind.Contingency) &&
                multiple.Links.Any(item =>
                    !item.IsCurrentPlan && item.IsLinked &&
                    item.Kind == ProjectRiskMeasureKind.Prevention),
                "Backlinks distinguish current and superseded measure versions.");

            // Existing task ID copied into another Markdown checkbox becomes
            // ambiguous across the workspace, so no actionable backlinks.
            string duplicate = Path.Combine(other.ProjectDirectory,
                "Duplicated task.md");
            await File.WriteAllTextAsync(duplicate,
                "- [ ] Copie accidentelle " +
                TaskPersistentIdentityCodec.FormatMarker(id) + "\n");
            ProjectRiskTaskBacklinkCatalog ambiguous =
                await resolver.ListAsync(task);
            Assert(ambiguous.TaskId is null &&
                ambiguous.Links.Count == 0 &&
                ambiguous.Diagnostics.Count > 0,
                "Copied GUID must not resolve to any actionable RIS link.");

            File.Delete(duplicate);
            ProjectRiskTaskBacklinkCatalog restored =
                await resolver.ListAsync(task);
            Assert(restored.TaskId == id && restored.Links.Count == 2,
                "Resolving a duplicate explicitly restores the same task GUID.");

            // An actual task deletion must not silently navigate to the
            // unrelated original source path or a different project.
            File.Delete(movedFile);
            ProjectRiskTaskBacklinkCatalog missing = await resolver.ListAsync(task);
            Assert(missing.TaskId is null && missing.Links.Count == 0 &&
                missing.Diagnostics.Count > 0,
                "An orphan task source must not navigate to a phantom task.");

            Assert((await File.ReadAllTextAsync(riskFile)).Contains(
                    currentLink.TaskLinks[^1].Id.ToString("D"),
                    StringComparison.OrdinalIgnoreCase),
                "Navigation never alters the saved risk audit history.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Builds one isolated, nonarchived project.</summary>
    /// <param name="name">Project label.</param>
    /// <param name="applicationId">Stable application GUID.</param>
    /// <param name="application">Containing application folder.</param>
    /// <returns>Project creation request.</returns>
    private static ProjectCreationRequest CreateProject(
        string name, Guid applicationId, string application) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Backlinks",
            ParentDirectory = application,
            DisplayName = "Backlinks"
        }
    };

    /// <summary>Creates a complete explicit human treatment plan.</summary>
    /// <param name="strategy">Chosen strategy.</param>
    /// <returns>Reviewed plan fixture.</returns>
    private static ProjectRiskTreatmentPlan Treatment(
        ProjectRiskTreatmentStrategy strategy) => new(
        strategy,
        "Responsable d'exploitation",
        "Vérifier toutes les procédures avant déploiement.",
        "Réduire les conséquences via une procédure de secours.",
        "Restaurer la version précédente et avertir le service.");

    /// <summary>Raises for any violated backlink invariant.</summary>
    /// <param name="condition">Required assertion.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
