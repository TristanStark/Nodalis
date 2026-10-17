using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Kanban;
using Nodalis.Infrastructure.Persistence;

internal static class WorkspaceKanbanSmokeTests
{
    /// <summary>
    /// Verifies four-state projection and safe Markdown-backed board moves without task duplication.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string workspaceRoot =
            Path.Combine(
                root,
                "WorkspaceKanbanWorkspace");

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        await store.InitializeAsync(
            "Kanban Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application Kanban");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet Kanban");

        Directory.CreateDirectory(
            projectDirectory);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                new ApplicationManifest
                {
                    Id =
                        applicationId,
                    Name =
                        "Application Kanban"
                },
                jsonOptions));

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(
                new ProjectManifest
                {
                    Id =
                        projectId,
                    Name =
                        "Projet Kanban",
                    ApplicationId =
                        applicationId,
                    InitialComplexity =
                        ProjectComplexity.Simple,
                    Sections =
                        []
                },
                jsonOptions));

        string taskPath =
            Path.Combine(
                projectDirectory,
                "Tâches.md");

        await File.WriteAllTextAsync(
            taskPath,
            "# Tâches\n\n- [ ] Préparer la release | Responsable: Alice | Échéance: 2026-10-20 | Priorité: Haute | Statut: À faire | Tags: #release\n");

        WorkspaceKanbanService service =
            new WorkspaceKanbanService(
                workspaceRoot);

        IReadOnlyList<TaskItem> initial =
            await service.RefreshAsync();

        TaskItem task =
            initial.Single(item =>
                item.Text ==
                    "Préparer la release");

        Assert(
            WorkspaceKanbanService.GetColumnStatus(
                task) ==
                WorkspaceKanbanService.TodoStatus,
            "A task with À faire metadata must appear in the to-do column.");

        await service.MoveAsync(
            task,
            WorkspaceKanbanService.InProgressStatus);

        IReadOnlyList<TaskItem> inProgress =
            await service.RefreshAsync();

        TaskItem moved =
            inProgress.Single(item =>
                item.Text ==
                    "Préparer la release");

        Assert(
            inProgress.Count(item =>
                item.Text ==
                    "Préparer la release") ==
                1 &&
            WorkspaceKanbanService.GetColumnStatus(
                moved) ==
                WorkspaceKanbanService.InProgressStatus &&
            !moved.IsCompleted,
            "A board move must update the source status without duplicating the task.");

        await service.MoveAsync(
            moved,
            WorkspaceKanbanService.DoneStatus);

        IReadOnlyList<TaskItem> completed =
            await service.RefreshAsync();

        TaskItem done =
            completed.Single(item =>
                item.Text ==
                    "Préparer la release");

        Assert(
            WorkspaceKanbanService.GetColumnStatus(
                done) ==
                WorkspaceKanbanService.DoneStatus &&
            done.IsCompleted,
            "Moving a task to Terminé must synchronize the Markdown checkbox.");

        await service.MoveAsync(
            done,
            WorkspaceKanbanService.BlockedStatus);

        IReadOnlyList<TaskItem> reopened =
            await service.RefreshAsync();

        TaskItem blocked =
            reopened.Single(item =>
                item.Text ==
                    "Préparer la release");

        Assert(
            WorkspaceKanbanService.GetColumnStatus(
                blocked) ==
                WorkspaceKanbanService.BlockedStatus &&
            !blocked.IsCompleted,
            "Moving a completed task back to Bloqué must reopen the Markdown checkbox.");

        string markdown =
            await File.ReadAllTextAsync(
                taskPath);

        Assert(
            markdown.Contains(
                "Statut: Bloqué",
                StringComparison.CurrentCulture) &&
            markdown.Contains(
                "- [ ] Préparer la release",
                StringComparison.CurrentCulture),
            "The final board state must be represented directly in the Markdown source.");
    }

    /// <summary>
    /// Throws when a Kanban smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">Condition to verify.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
