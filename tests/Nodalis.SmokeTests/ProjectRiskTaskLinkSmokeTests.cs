using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Tasks;

/// <summary>
/// Human-reviewed, reversible links from risk-treatment measure versions to
/// uniquely identified same-project task checkboxes. No checkbox side effects.
/// </summary>
internal static class ProjectRiskTaskLinkSmokeTests
{
    /// <summary>Exercises strict risk-side history plus actual source resolution.</summary>
    /// <param name="root">Disposable workspace parent.</param>
    /// <returns>Completed regression checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-risk-task-links-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Risk task links");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Links");
            Directory.CreateDirectory(application);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                MakeProject("Current", appId, application));
            ProjectCreationResult foreignProject = await creator.CreateAsync(
                MakeProject("Foreign", appId, application));
            ProjectRiskRegistryService risks = new(workspace);
            WorkspaceTaskService tasks = new(workspace);

            ProjectRiskEntry risk = await risks.CreateAsync(
                project.ProjectDirectory, "Erreur pendant la bascule");
            ProjectRiskEntry planned = await risks.PlanTreatmentAsync(
                project.ProjectDirectory, risk,
                new ProjectRiskTreatmentPlan(
                    ProjectRiskTreatmentStrategy.Reduce,
                    "Responsable exploitation",
                    "Effectuer les contrôles et tests avant déploiement.",
                    "Corriger les anomalies et vérifier le retour arrière.",
                    "Restaurer la version précédente et informer les équipes."),
                "Comité de production",
                "Plan de réduction documenté et revu avant la recette.");
            string riskFile = Path.Combine(project.ProjectDirectory,
                "Analyse", "Risques", planned.Card.Reference + ".md");
            string before = await File.ReadAllTextAsync(riskFile);
            Assert(ProjectRiskTaskLinkMarkdownCodec.Inspect(before)
                    .Events.Count == 0 &&
                planned.TaskLinks.Count == 0 &&
                planned.LinkedTaskIds(ProjectRiskMeasureKind.Prevention).Count == 0,
                "A treatment plan never creates task links automatically.");

            TaskItem task = await tasks.CreateTaskAsync(
                project.ProjectDirectory, "Tester la procédure de retour arrière",
                new TaskMetadataUpdate { Owner = "Équipe recette" });
            TaskItem second = await tasks.CreateTaskAsync(
                project.ProjectDirectory, "Relire le guide d'exploitation",
                new TaskMetadataUpdate());
            TaskItem external = await tasks.CreateTaskAsync(
                foreignProject.ProjectDirectory,
                "Tâche strictement étrangère", new TaskMetadataUpdate());
            Assert(task.PersistentId is Guid && second.PersistentId is Guid &&
                external.PersistentId is Guid,
                "New project tasks must have persistent GUIDs.");
            IReadOnlyList<TaskItem> candidates = await risks.ListLinkableTasksAsync(
                project.ProjectDirectory);
            Assert(candidates.Any(item => item.PersistentId == task.PersistentId) &&
                candidates.Any(item => item.PersistentId == second.PersistentId) &&
                !candidates.Any(item => item.PersistentId == external.PersistentId),
                "Link candidates must only include project-owned stable tasks.");

            await RejectAsync(() => risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Prevention, external.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable projet",
                "Une tâche étrangère ne peut pas être liée au risque."),
                "Cross-project target GUIDs must be rejected.");
            await RejectAsync(() => risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Prevention, Guid.NewGuid(),
                ProjectRiskTaskLinkAction.Link, "Responsable projet",
                "Une tâche inconnue ne peut pas être liée au risque."),
                "Unresolved GUIDs cannot be linked.");
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTaskLinkMarkdownCodec.Append(before,
                    ProjectRiskMeasureKind.Prevention,
                    task.PersistentId!.Value, ProjectRiskTaskLinkAction.Unlink,
                    "Responsable qualité",
                    "Une relation absente ne peut pas être supprimée.")),
                "Unlink cannot invent a previously linked relationship.");

            string taskFile = Path.Combine(project.ProjectDirectory, "Tâches.md");
            string originalTaskSource = await File.ReadAllTextAsync(taskFile);
            ProjectRiskEntry linked = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Prevention, task.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable qualité",
                "La tâche de recette contribue directement à la prévention.");
            Assert(linked.TaskLinks.Count == 1 &&
                linked.TaskLinks[0].TreatmentId == planned.Treatments[^1].Id &&
                linked.TaskLinks[0].TaskId == task.PersistentId &&
                linked.TaskLinks[0].PreviousId is null &&
                linked.LinkedTaskIds(ProjectRiskMeasureKind.Prevention)
                    .SequenceEqual(new[] { task.PersistentId!.Value }) &&
                linked.LinkedTaskIds(ProjectRiskMeasureKind.Mitigation).Count == 0 &&
                linked.Reviews.Count == 0 &&
                linked.MeasureEvents.Count == 0 &&
                await File.ReadAllTextAsync(taskFile) == originalTaskSource,
                "Explicit linking must change only the risk Markdown and one slot.");

            await RejectAsync(() => risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, planned,
                ProjectRiskMeasureKind.Mitigation, second.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable",
                "Tentative de liaison sur un snapshot de risque périmé."),
                "A stale source SHA must be refused.");
            await RejectAsync(() => risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, linked,
                ProjectRiskMeasureKind.Prevention, task.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable",
                "Un doublon ne doit pas créer un événement historique."),
                "Duplicate links must not append events.");

            ProjectRiskEntry anotherLink = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, linked,
                ProjectRiskMeasureKind.Mitigation, second.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable exploitation",
                "La documentation limite l'impact d'une défaillance.");
            Assert(anotherLink.TaskLinks.Count == 2 &&
                anotherLink.LinkedTaskIds(ProjectRiskMeasureKind.Prevention)
                    .Contains(task.PersistentId!.Value) &&
                anotherLink.LinkedTaskIds(ProjectRiskMeasureKind.Mitigation)
                    .Contains(second.PersistentId!.Value),
                "Distinct measures must retain independent task relations.");

            string source = await File.ReadAllTextAsync(riskFile);
            int linksPosition = source.IndexOf(
                "<!-- nodalis:risk-task-links:start -->",
                StringComparison.Ordinal);
            int planPosition = source.IndexOf(
                "<!-- nodalis:risk-treatment:start -->",
                StringComparison.Ordinal);
            Assert(linksPosition >= 0 && linksPosition < planPosition &&
                ProjectRiskTaskLinkMarkdownCodec.Inspect(source)
                    .Events.Count == 2,
                "The link journal must remain ahead of all other histories.");
            string lastLine = source.Split('\n').Single(line =>
                line.Contains("\"Id\":\"" +
                    anotherLink.TaskLinks[^1].Id.ToString("D") +
                    "\"", StringComparison.OrdinalIgnoreCase));
            await RejectAsync(() => Task.FromResult(
                ProjectRiskTaskLinkMarkdownCodec.Inspect(
                    source.Replace(lastLine + "\n", string.Empty,
                        StringComparison.Ordinal))),
                "Removing the last link event must break the head anchor.");

            // Moving the source file must not break a link to the task ID.
            string movedTaskFile = Path.Combine(project.ProjectDirectory,
                "Tâches de recette.md");
            File.Move(taskFile, movedTaskFile);
            TaskItem movedTask = (await risks.ListLinkableTasksAsync(
                project.ProjectDirectory)).Single(item =>
                item.PersistentId == task.PersistentId);
            Assert(movedTask.SourceRelativePath.EndsWith(
                    "Tâches de recette.md", StringComparison.OrdinalIgnoreCase) &&
                linked.LinkedTaskIds(ProjectRiskMeasureKind.Prevention)
                    .Contains(movedTask.PersistentId!.Value),
                "File moves do not alter the stable relation key.");

            ProjectRiskEntry removed = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, anotherLink,
                ProjectRiskMeasureKind.Prevention, movedTask.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Unlink, "Responsable qualité",
                "Le risque est désormais traité autrement après revue.");
            Assert(removed.TaskLinks.Count == 3 &&
                removed.TaskLinks[^1].PreviousId ==
                    anotherLink.TaskLinks[^1].Id &&
                removed.LinkedTaskIds(ProjectRiskMeasureKind.Prevention).Count == 0 &&
                removed.LinkedTaskIds(ProjectRiskMeasureKind.Mitigation)
                    .Contains(second.PersistentId!.Value),
                "Unlink must append a reversible event without touching other slots.");

            // Re-link of the same ID is allowed only after explicit unlink.
            ProjectRiskEntry relinked = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, removed,
                ProjectRiskMeasureKind.Prevention, movedTask.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable qualité",
                "Le lien de prévention est rétabli sur avis humain.");
            File.Delete(movedTaskFile);
            ProjectRiskEntry orphanUnlinked = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, relinked,
                ProjectRiskMeasureKind.Prevention, movedTask.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Unlink, "Responsable qualité",
                "La source a été supprimée : suppression explicite du lien.");
            Assert(orphanUnlinked.TaskLinks.Count == 5 &&
                orphanUnlinked.LinkedTaskIds(ProjectRiskMeasureKind.Prevention)
                    .Count == 0,
                "A missing task source can still be explicitly unlinked.");

            ProjectRiskEntry revised = await risks.PlanTreatmentAsync(
                project.ProjectDirectory, orphanUnlinked,
                new ProjectRiskTreatmentPlan(
                    ProjectRiskTreatmentStrategy.Transfer,
                    "Responsable prestataire",
                    "Consigner les contrôles préalables au contrat.",
                    "Transférer une partie des conséquences par SLA.",
                    "Avertir le prestataire et activer le plan de secours."),
                "Comité de production",
                "Une nouvelle version du plan remplace les mesures antérieures.");
            Assert(revised.TaskLinks.Count == 5 &&
                revised.LinkedTaskIds(ProjectRiskMeasureKind.Mitigation).Count == 0 &&
                revised.TaskLinks.Any(item =>
                    item.TreatmentId == planned.Treatments[^1].Id),
                "New treatment revision must not inherit old linked tasks.");

            TaskItem newlyAdded = await tasks.CreateTaskAsync(
                project.ProjectDirectory, "Nouvelle recette après avenant",
                new TaskMetadataUpdate());
            ProjectRiskEntry relinkNewPlan = await risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, revised,
                ProjectRiskMeasureKind.Contingency, newlyAdded.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Comité de production",
                "Nouvelle tâche assignée au plan de contingence révisé.");
            Assert(relinkNewPlan.TaskLinks.Count == 6 &&
                relinkNewPlan.TaskLinks[^1].TreatmentId == revised.Treatments[^1].Id &&
                relinkNewPlan.LinkedTaskIds(ProjectRiskMeasureKind.Contingency)
                    .Contains(newlyAdded.PersistentId!.Value) &&
                relinkNewPlan.Card.Initial == new ProjectRiskRating(null, null) &&
                relinkNewPlan.Card.Qualification == ProjectRiskQualification.ToReview &&
                relinkNewPlan.MeasureEvents.Count == 0,
                "Links do not change scores, statuses or measure completion.");

            // A copy-pasted stable ID elsewhere in the workspace invalidates
            // new link attempts, without deleting any existing audit history.
            string duplicate = Path.Combine(foreignProject.ProjectDirectory,
                "Copied checkbox.md");
            await File.WriteAllTextAsync(duplicate,
                "- [ ] Copie accidentelle " +
                TaskPersistentIdentityCodec.FormatMarker(
                    newlyAdded.PersistentId!.Value) + "\n");
            ProjectRiskEntry reloaded = (await risks.ListAsync(
                project.ProjectDirectory)).Risks.Single();
            await RejectAsync(() => risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, reloaded,
                ProjectRiskMeasureKind.Mitigation, newlyAdded.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Link, "Responsable",
                "GUID collidé dans un autre document du workspace."),
                "Copied task GUID cannot form a new cross-project ambiguous link.");

            // Stale risk Markdown and broken link heads are never repaired.
            string beforeConcurrent = await File.ReadAllTextAsync(riskFile);
            string edited = beforeConcurrent.Replace(
                "## Contexte et conséquences",
                "## Contexte et conséquences\n\nAvis externe conservé.",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(riskFile, edited);
            await RejectAsync(() => risks.ChangeTaskLinkAsync(
                project.ProjectDirectory, reloaded,
                ProjectRiskMeasureKind.Contingency, newlyAdded.PersistentId!.Value,
                ProjectRiskTaskLinkAction.Unlink, "Responsable",
                "Le risque a changé depuis le chargement du registre."),
                "A changed risk source requires explicit reload before unlink.");
            Assert(await File.ReadAllTextAsync(riskFile) == edited,
                "Refused link update cannot erase concurrent free-form notes.");

            string broken = edited.Replace(
                "<!-- nodalis:risk-task-links-head:",
                "<!-- nodalis:risk-task-links-corrupted:",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(riskFile, broken);
            ProjectRiskCatalog invalid = await risks.ListAsync(
                project.ProjectDirectory);
            Assert(invalid.Diagnostics.Count > 0 &&
                !invalid.Risks.Any(item => item.Card.Id == relinkNewPlan.Card.Id),
                "Corrupted link audit must be diagnosed, not silently migrated.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Creates an isolated project fixture.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="appId">Application GUID.</param>
    /// <param name="application">Application directory.</param>
    /// <returns>Project request.</returns>
    private static ProjectCreationRequest MakeProject(
        string name, Guid appId, string application) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Links",
            ParentDirectory = application,
            DisplayName = "Links"
        }
    };

    /// <summary>Requires a command to refuse an invalid link without writing.</summary>
    /// <param name="action">Expected invalid action.</param>
    /// <param name="message">Diagnostic.</param>
    /// <returns>Completed refusal check.</returns>
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

    /// <summary>Fails on a broken relation invariant.</summary>
    /// <param name="condition">Requirement.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
