using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Tasks;

/// <summary>
/// Covers opt-in task IDs across Markdown edits, relocation, metadata,
/// source collisions, unsafe assignment and newly created project tasks.
/// </summary>
internal static class PersistentTaskIdentitySmokeTests
{
    /// <summary>Runs pure codec checks and workspace file regressions.</summary>
    /// <param name="root">Disposable test root.</param>
    /// <returns>Completed regression suite.</returns>
    public static async Task RunAsync(string root)
    {
        Guid id = Guid.NewGuid();
        string marker = TaskPersistentIdentityCodec.FormatMarker(id);
        string legacy = "- [ ] Tester le déploiement | Responsable: Alice";
        string annotated = TaskPersistentIdentityCodec.InsertMarker(legacy, id);
        Assert(annotated == legacy + " " + marker &&
            TaskPersistentIdentityCodec.ReadMarker(
                annotated, out string clean) == id && clean == legacy,
            "Marker must be invisible-to-metadata and preserve human content.");
        Assert(TaskPersistentIdentityCodec.InsertMarker(annotated, id) ==
                annotated &&
            TaskPersistentIdentityCodec.PreserveMarker(annotated,
                "- [x] Tester le déploiement | Responsable: Bob").EndsWith(
                    marker, StringComparison.Ordinal),
            "Editing checkbox and metadata must keep its canonical GUID.");
        await RejectAsync(() => Task.FromResult(
            TaskPersistentIdentityCodec.InsertMarker(annotated, Guid.NewGuid())),
            "An existing identity must never be reassigned.");

        string sample = "# Exemple\r\n" + annotated + "\r\n" +
            "```markdown\r\n- [ ] Fausse tâche " + marker +
            "\r\n```\r\n- [ ] Ancienne tâche\r\n";
        TaskPersistentIdentityScan scan = TaskPersistentIdentityCodec.Inspect(
            sample.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n'));
        Assert(scan.Entries.Count == 2 && scan.Issues.Count == 0 &&
            scan.Entries[0].Id == id &&
            scan.Entries[1].Id is null,
            "Read-only scanning must ignore fenced examples and legacy IDs.");
        Assert(TaskPersistentIdentityCodec.Inspect(
            [annotated, annotated]).Issues.Count == 1,
            "Duplicate stable task GUIDs must be diagnosed.");
        Assert(TaskPersistentIdentityCodec.Inspect(
            ["- [ ] Broken <!-- nodalis:task:not-a-guid -->"])
                .Issues.Count == 1,
            "Malformed explicit identifiers cannot be treated as valid.");
        await RejectAsync(() => Task.FromResult(
            TaskPersistentIdentityCodec.ReadMarker(
                "- [ ] Broken <!-- nodalis:task:not-a-guid -->",
                out string _)),
            "The codec must reject malformed IDs before rewriting.");
        Assert(TaskPersistentIdentityCodec.Inspect(
            ["- [ ] A", "- [ ] B"]).Entries
            .All(item => item.Id is null),
            "Legacy tasks must remain unmodified on reads.");

        string workspace = Path.Combine(root, "Persistent-task-v2");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Persistent Tasks");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Tasks");
            Directory.CreateDirectory(app);
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Identity",
                    Complexity = ProjectComplexity.Simple,
                    Target = new ProjectCreationTarget
                    {
                        ApplicationId = Guid.NewGuid(),
                        ApplicationName = "Tasks",
                        ParentDirectory = app,
                        DisplayName = "Tasks"
                    }
                });

            WorkspaceTaskService service = new(workspace);
            TaskItem created = await service.CreateTaskAsync(
                project.ProjectDirectory, "Vérifier les dépendances",
                new TaskMetadataUpdate
                {
                    Owner = "Équipe recette",
                    Priority = "Haute",
                    Tags = ["release", "tests"]
                });
            Assert(created.PersistentId is Guid createdId &&
                created.Id == createdId &&
                created.Text == "Vérifier les dépendances" &&
                created.Owner == "Équipe recette" &&
                created.Tags.Count == 2,
                "New project tasks must receive readable metadata and stable IDs.");
            string taskFile = Path.Combine(project.ProjectDirectory, "Tâches.md");
            string text = await File.ReadAllTextAsync(taskFile);
            Assert(text.Contains(
                TaskPersistentIdentityCodec.FormatMarker(created.Id),
                StringComparison.Ordinal),
                "Created task must have its identity persisted on the checkbox.");

            // An old task has no persisted GUID until the user approves just
            // this checkbox. Existing metadata and nearby text stay untouched.
            string legacyFile = Path.Combine(project.ProjectDirectory,
                "Anciennes tâches.md");
            string legacyContents = "# Legacy\r\n\r\n" +
                "- [ ] Documenter l'opération | Responsable: Alice\r\n" +
                "- [x] Examiner la sécurité\r\n";
            await File.WriteAllTextAsync(legacyFile, legacyContents);
            IReadOnlyList<TaskItem> first = await service.GetTasksAsync(
                project.ProjectDirectory, includeCompleted: true);
            TaskItem legacyTask = first.Single(task =>
                task.Text == "Documenter l'opération");
            Assert(legacyTask.PersistentId is null &&
                await File.ReadAllTextAsync(legacyFile) == legacyContents,
                "Discovering old checkboxes must not rewrite a legacy document.");

            Guid adoptedId = await service.AssignPersistentIdentityAsync(
                legacyTask);
            string afterAdoption = await File.ReadAllTextAsync(legacyFile);
            Assert(afterAdoption.Contains(
                    TaskPersistentIdentityCodec.FormatMarker(adoptedId),
                    StringComparison.Ordinal) &&
                afterAdoption.Contains("- [x] Examiner la sécurité",
                    StringComparison.Ordinal) &&
                afterAdoption.Contains("\r\n", StringComparison.Ordinal),
                "One approved marker insertion must preserve CRLF and other tasks.");
            await RejectAsync(() => service.AssignPersistentIdentityAsync(
                legacyTask),
                "Reusing a stale legacy source must refuse changing identity.");

            TaskItem adopted = (await service.GetTasksAsync(
                project.ProjectDirectory, includeCompleted: true))
                .Single(task => task.Text == "Documenter l'opération");
            Assert(adopted.Id == adoptedId &&
                adopted.PersistentId == adoptedId &&
                adopted.HasPersistentIdentity,
                "Adopted GUID must become the stable task identity.");
            await service.UpdateMetadataAsync(adopted, new TaskMetadataUpdate
            {
                Owner = "Responsable qualité",
                Status = "En cours",
                Tags = ["recette"]
            });
            TaskItem rewritten = (await service.GetTasksAsync(
                project.ProjectDirectory, includeCompleted: true))
                .Single(task => task.PersistentId == adoptedId);
            Assert(rewritten.Text == "Documenter l'opération" &&
                rewritten.Owner == "Responsable qualité" &&
                rewritten.Id == adoptedId &&
                (await File.ReadAllTextAsync(legacyFile)).Contains(
                    TaskPersistentIdentityCodec.FormatMarker(adoptedId),
                    StringComparison.Ordinal),
                "Metadata edits must preserve the same task GUID and hide marker.");

            // Inserting lines ahead of a task does not break its permanent
            // identity, and checkbox completion preserves the marker.
            string shifted = await File.ReadAllTextAsync(legacyFile);
            await File.WriteAllTextAsync(legacyFile,
                "# Nouveau contexte\r\n" + shifted);
            await service.SetCompletedAsync(rewritten, completed: true);
            TaskItem shiftedTask = (await service.GetTasksAsync(
                project.ProjectDirectory, includeCompleted: true))
                .Single(task => task.PersistentId == adoptedId);
            Assert(shiftedTask.Id == adoptedId &&
                shiftedTask.IsCompleted && shiftedTask.LineNumber >
                    rewritten.LineNumber,
                "Identity must survive relocation and completion toggling.");
            await RejectAsync(() => service.SetCompletedAsync(rewritten, false),
                "Stale task metadata must not overwrite subsequent edits.");

            // Moving the same Markdown checkbox to a different project-owned
            // file preserves its ID even when the link-index document changes.
            string movedFile = Path.Combine(project.ProjectDirectory,
                "Tâches déplacées.md");
            File.Move(legacyFile, movedFile);
            TaskItem moved = (await service.GetTasksAsync(
                project.ProjectDirectory, includeCompleted: true))
                .Single(task => task.PersistentId == adoptedId);
            Assert(moved.Id == adoptedId &&
                moved.SourceRelativePath.EndsWith(
                    "Tâches déplacées.md", StringComparison.OrdinalIgnoreCase) &&
                moved.Text == "Documenter l'opération",
                "Moving a task's source file must not change its stable GUID.");

            // Same GUID appearing in two separate files is ambiguous, so
            // neither occurrence is safe as a durable relation target.
            string collisionFile = Path.Combine(project.ProjectDirectory,
                "Collision.md");
            await File.WriteAllTextAsync(collisionFile,
                "- [ ] Copie humaine " +
                TaskPersistentIdentityCodec.FormatMarker(adoptedId) + "\n");
            TaskCollection duplicateCatalog = await service.RefreshAsync();
            Assert(duplicateCatalog.IdentityDiagnostics.Any(x =>
                    x.Contains(adoptedId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase)) &&
                duplicateCatalog.Tasks.Where(task =>
                    task.Text is "Documenter l'opération" or "Copie humaine")
                    .All(task => task.PersistentId is null),
                "Copied task GUIDs must lose eligible identity until resolved.");

            // The source conflict must be visible; nothing is repaired on
            // a refresh, nor can a forged task path be adopted elsewhere.
            Assert((await File.ReadAllTextAsync(collisionFile)).Contains(
                    TaskPersistentIdentityCodec.FormatMarker(adoptedId),
                    StringComparison.Ordinal),
                "Refresh must never silently replace duplicated IDs.");
            TaskItem external = shiftedTask with
            {
                PersistentId = null,
                SourceRelativePath = "../outside.md"
            };
            await RejectAsync(() => service.AssignPersistentIdentityAsync(
                external),
                "A forged external path must not be made persistent.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Expects a safety-related refusal without modifying source.</summary>
    /// <param name="action">Action that should throw.</param>
    /// <param name="message">Failure diagnostic.</param>
    /// <returns>Completed negative assertion.</returns>
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

    /// <summary>Reports one violated persistent-identity invariant.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Description.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
