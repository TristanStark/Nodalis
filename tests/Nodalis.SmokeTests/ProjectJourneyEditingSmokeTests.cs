using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Protects guided v2 journey edits against identity loss and silent writes.</summary>
internal static class ProjectJourneyEditingSmokeTests
{
    /// <summary>
    /// Exercises pure edits, user-controlled persistence, stale snapshots and
    /// preservation of unrelated Markdown and source references.
    /// </summary>
    /// <param name="root">Disposable smoke-test parent.</param>
    /// <returns>Completed editor verification.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Journey-guided-editor-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Parcours v2 · édition");
        try
        {
            Guid applicationId = Guid.NewGuid();
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Test");
            Directory.CreateDirectory(application);
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult result = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Parcours édition",
                    Complexity = ProjectComplexity.Complex,
                    Target = new ProjectCreationTarget
                    {
                        ApplicationId = applicationId,
                        ApplicationName = "Test",
                        ParentDirectory = application,
                        DisplayName = "Test"
                    }
                });

            ProjectJourneyService service = new(workspace);
            ProjectJourneySnapshot first = await service.InitializeAsync(
                result.ProjectDirectory);
            string originalOverview = await File.ReadAllTextAsync(
                result.OverviewFilePath);
            ProjectJourneyDocument original = first.Document;
            ProjectJourneyStep exigences = original.Steps.Single(step =>
                step.TemplateKey == "exigences");
            Guid originalStepId = exigences.Id;
            Guid originalOperationId = exigences.Operations[0].Id;

            ProjectJourneyDocument renamed = ProjectJourneyEditor.RenameStep(
                original, exigences.Id, "Exigences et critères");
            Assert(renamed.Steps.Single(step => step.Id == exigences.Id).Name ==
                    "Exigences et critères" &&
                original.Steps.Single(step => step.Id == exigences.Id).Name ==
                    "Exigences",
                "Rename must not mutate the caller's prior snapshot.");
            ProjectJourneyDocument changed = ProjectJourneyEditor.SetStatus(
                renamed, exigences.Id, ProjectJourneyStepStatus.Completed);
            changed = ProjectJourneyEditor.MoveStep(changed, exigences.Id, 1);
            ProjectJourneyStep reparented = changed.Steps.Single(step =>
                step.Id == exigences.Id);
            Assert(reparented.Status == ProjectJourneyStepStatus.Completed &&
                reparented.Order == 2 &&
                changed.Steps.Select(step => step.Order).Distinct().Count() ==
                    changed.Steps.Count &&
                reparented.Operations[0].Id == originalOperationId,
                "Move and status change must preserve GUIDs and unique orders.");

            changed = ProjectJourneyEditor.AddStep(changed, "Revue d'exploitation");
            ProjectJourneyStep added = changed.Steps.Single(step =>
                step.Name == "Revue d'exploitation");
            Assert(added.Id != Guid.Empty &&
                added.TemplateKey is null &&
                changed.Steps.Select(step => step.Id).Distinct().Count() ==
                    changed.Steps.Count &&
                !original.Steps.Any(step => step.Id == added.Id),
                "Added stages must have fresh GUIDs and no implicit template provenance.");
            changed = ProjectJourneyEditor.AddOperation(
                changed, added.Id, "Vérifier les contraintes");
            changed = ProjectJourneyEditor.AddOperation(
                changed, added.Id, "Relire les procédures");
            ProjectJourneyStep withOperations = changed.Steps.Single(step =>
                step.Id == added.Id);
            Guid a = withOperations.Operations[0].Id;
            Guid b = withOperations.Operations[1].Id;
            Assert(a != b && a != added.Id,
                "Operations must receive distinct new GUIDs.");
            changed = ProjectJourneyEditor.MoveOperation(changed,
                added.Id, b, -1);
            Assert(changed.Steps.Single(step => step.Id == added.Id)
                .Operations[0].Id == b,
                "Operation reordering must preserve the two identities.");

            changed = ProjectJourneyEditor.RenameOperation(
                changed, added.Id, b, "Relire les procédures métiers");
            Assert(changed.Steps.Single(step => step.Id == added.Id)
                .Operations[0].Name == "Relire les procédures métiers",
                "Operation rename must retain its GUID and position.");

            // Existing references to evidence must survive reordering of the
            // containing stage and its repeatable operations.
            ProjectJourneyStep stage = changed.Steps.Single(step =>
                step.Id == added.Id);
            changed = changed with
            {
                Steps = changed.Steps.Select(step => step.Id == added.Id
                    ? step with
                    {
                        Operations =
                        [
                            step.Operations[0] with
                            {
                                SourcePaths = ["Applications/Test/Projets/document.md"],
                                GateIds = [Guid.NewGuid()],
                                MilestoneIds = [Guid.NewGuid()]
                            },
                            .. step.Operations.Skip(1)
                        ]
                    }
                    : step).ToList()
            };
            ProjectJourneyOperation referenced = changed.Steps.Single(step =>
                step.Id == added.Id).Operations[0];
            changed = ProjectJourneyEditor.MoveOperation(changed,
                added.Id, b, 1);
            ProjectJourneyOperation afterMove = changed.Steps.Single(step =>
                step.Id == added.Id).Operations.Single(item => item.Id == b);
            Assert(afterMove.SourcePaths.SequenceEqual(referenced.SourcePaths) &&
                afterMove.GateIds.SequenceEqual(referenced.GateIds) &&
                afterMove.MilestoneIds.SequenceEqual(referenced.MilestoneIds),
                "Moving an operation must retain its evidence, gate and milestone links.");

            changed = ProjectJourneyEditor.RemoveOperation(changed, added.Id, a);
            Assert(changed.Steps.Single(step => step.Id == added.Id)
                .Operations.Single().Id == b &&
                !original.Steps.Any(step => step.Id == added.Id),
                "Removing one operation must not alter other operations or older snapshots.");

            await RejectAsync(() => Task.FromResult(ProjectJourneyEditor.MoveStep(
                changed, added.Id, 2)),
                "Only one-place up/down moves are accepted.");
            await RejectAsync(() => Task.FromResult(ProjectJourneyEditor.SetStatus(
                changed, added.Id, (ProjectJourneyStepStatus)100)),
                "Unknown statuses must be rejected.");
            await RejectAsync(() => Task.FromResult(ProjectJourneyEditor.RemoveStep(
                changed, Guid.NewGuid())),
                "Unknown stages must be rejected.");

            ProjectJourneyDocument beforeRemoval = changed;
            changed = ProjectJourneyEditor.RemoveStep(changed, added.Id);
            Assert(changed.Steps.Count == original.Steps.Count &&
                changed.Steps.Select(step => step.Id).Distinct().Count() ==
                    changed.Steps.Count &&
                !changed.Steps.Any(step => step.Id == added.Id) &&
                beforeRemoval.Steps.Any(step => step.Id == added.Id),
                "Removing a stage must not mutate older snapshots or change survivor IDs.");

            ProjectJourneySnapshot persisted = await service.SaveAsync(
                result.ProjectDirectory, changed, first.Sha256);
            Assert(persisted.Document.Steps.Single(step =>
                    step.Id == originalStepId).Status ==
                    ProjectJourneyStepStatus.Completed &&
                await File.ReadAllTextAsync(result.OverviewFilePath) == originalOverview,
                "Only the journey JSON may change; the project overview stays identical.");

            ProjectJourneySnapshot restarted =
                (await new ProjectJourneyService(workspace).ReadAsync(
                    result.ProjectDirectory))!;
            Assert(restarted.Sha256 == persisted.Sha256 &&
                restarted.Document.Steps.Select(step => step.Id)
                    .SequenceEqual(changed.Steps.Select(step => step.Id)),
                "One committed edit must survive application restart with stable GUIDs.");

            await RejectAsync(() => service.SaveAsync(
                result.ProjectDirectory, original, first.Sha256),
                "A stale UI revision must not overwrite the user's latest edit.");
            Assert((await service.ReadAsync(result.ProjectDirectory))!.Sha256 ==
                restarted.Sha256,
                "Rejected stale writes must leave the disk snapshot intact.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Asserts that a rejected mutation did not succeed.</summary>
    /// <param name="action">Expected failure.</param>
    /// <param name="message">Diagnostic.</param>
    /// <returns>Completed negative assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool denied = false;
        try
        {
            await action();
        }
        catch (Exception error) when (error is InvalidDataException or
            InvalidOperationException or ArgumentException or
            KeyNotFoundException)
        {
            denied = true;
        }

        Assert(denied, message);
    }

    /// <summary>Raises a readable smoke test failure.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
