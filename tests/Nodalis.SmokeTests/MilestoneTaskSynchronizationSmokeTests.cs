using Nodalis.Core.Domain;
using Nodalis.Core.Milestones;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Regression for idempotent milestone-to-task mirrors and canonical dates.
/// Synthetic workspaces only; the user's documents are never used.
/// </summary>
internal static class MilestoneTaskSynchronizationSmokeTests
{
    /// <summary>Checks create/update/completion, manual safety and dates.</summary>
    /// <param name="root">Parent directory for temporary synthetic data.</param>
    /// <returns>Completed end-to-end file checks.</returns>
    public static async Task RunAsync(string root)
    {
        Assert(MilestoneDateCodec.TryParse("03/11/2026", out DateOnly french) &&
            french == new DateOnly(2026, 11, 3) &&
            MilestoneDateCodec.Format(french) == "03/11/2026" &&
            MilestoneDateCodec.TryParse("3/11/2026", out DateOnly pasted) &&
            pasted == french &&
            MilestoneDateCodec.TryParse("2026-11-03", out DateOnly iso) &&
            iso == french &&
            !MilestoneDateCodec.TryParse("31/02/2026", out DateOnly _),
            "All milestone writers and checkers must share one date contract.");

        string workspace = Path.Combine(root, "MilestoneMirrorSmoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Synthetic milestone mirror");
        string app = Path.Combine(workspace,
            WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
        Directory.CreateDirectory(app);
        ProjectCreationResult project = await new FileSystemProjectCreator(
            workspace).CreateAsync(new ProjectCreationRequest
            {
                Name = "Projet fictif",
                Complexity = ProjectComplexity.Complex,
                Target = new ProjectCreationTarget
                {
                    ApplicationId = Guid.NewGuid(),
                    ApplicationName = "Application fictive",
                    ParentDirectory = app,
                    DisplayName = "Application fictive"
                }
            });
        string taskPath = Path.Combine(project.ProjectDirectory, "Tâches.md");
        if (!File.Exists(taskPath))
        {
            await File.WriteAllTextAsync(taskPath, "# Tâches\n\n");
        }

        await File.AppendAllTextAsync(taskPath,
            "- [ ] Une tâche manuelle inchangée\n");
        string marker = "- [ ] Une tâche manuelle inchangée";
        WorkspaceMilestoneService milestones = new(workspace);
        MilestoneDraft original = new()
        {
            Name = "Livraison fictive",
            TargetDate = new DateOnly(2026, 11, 3),
            Status = "À faire"
        };
        MilestoneItem created = await milestones.AddAsync(
            project.ProjectDirectory, original);
        Guid persistentTaskId = MilestoneTaskSynchronizationService
            .TaskIdForMilestone(created.Id);
        string first = await File.ReadAllTextAsync(taskPath);
        Assert(first.Contains(TaskPersistentIdentityCodec.FormatMarker(
                   persistentTaskId), StringComparison.Ordinal) &&
               first.Contains("Jalon : Livraison fictive",
                   StringComparison.Ordinal) &&
               first.Contains("Échéance: 2026-11-03",
                   StringComparison.Ordinal) &&
               first.Contains(marker, StringComparison.Ordinal),
            "Creating a milestone must create exactly one identifiable " +
            "Markdown task without changing manual tasks.");

        MilestoneItem changed = await milestones.UpdateAsync(created,
            original with
            {
                Name = "Livraison fictive modifiée",
                TargetDate = new DateOnly(2026, 12, 4)
            });
        string second = await File.ReadAllTextAsync(taskPath);
        Assert(second.Contains("Jalon : Livraison fictive modifiée",
                   StringComparison.Ordinal) &&
               second.Contains("Échéance: 2026-12-04",
                   StringComparison.Ordinal) &&
               !second.Contains("Échéance: 2026-11-03",
                   StringComparison.Ordinal) &&
               second.Split(TaskPersistentIdentityCodec.FormatMarker(
                       persistentTaskId)).Length == 2 &&
               second.Contains(marker, StringComparison.Ordinal),
            "Editing the milestone must update the one existing task " +
            "and preserve its persistent identity.");

        MilestoneItem closed = await milestones.UpdateAsync(changed,
            original with
            {
                Name = changed.Name,
                TargetDate = changed.TargetDate,
                Status = "Terminée"
            });
        string third = await File.ReadAllTextAsync(taskPath);
        Assert(third.Contains("- [x] Jalon : Livraison fictive modifiée",
                   StringComparison.Ordinal) &&
               third.Contains(marker, StringComparison.Ordinal),
            "Completing a milestone must complete its dedicated task only.");

        bool changedAgain = await new MilestoneTaskSynchronizationService(
            workspace).SynchronizeAsync(project.ProjectDirectory, closed);
        Assert(!changedAgain &&
               await File.ReadAllTextAsync(taskPath) == third,
            "Repeated task synchronization must be strictly idempotent.");

        // A manually edited or unrelated checkbox may not be overwritten
        // merely because its title resembles a milestone.
        string lastTaskLine = third.Split('\n').Single(line =>
            line.Contains(TaskPersistentIdentityCodec.FormatMarker(
                persistentTaskId), StringComparison.Ordinal));
        await File.WriteAllTextAsync(taskPath,
            third.Replace(lastTaskLine,
                lastTaskLine.Replace("Jalon : ",
                    "Tâche personnalisée : ", StringComparison.Ordinal),
                StringComparison.Ordinal));
        bool refused = false;
        try
        {
            await new MilestoneTaskSynchronizationService(workspace)
                .SynchronizeAsync(project.ProjectDirectory, closed);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused,
            "A transformed or manually owned task must never be silently " +
            "overwritten by the milestone mirror.");
    }

    /// <summary>Throws if a synthetic behavior regresses.</summary>
    /// <param name="condition">Expected state.</param>
    /// <param name="description">Diagnostic without private project names.</param>
    private static void Assert(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }
}
