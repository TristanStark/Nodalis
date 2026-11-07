using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// End-to-end task handover tests with deterministic interruption checkpoints.
/// All fixtures are synthetic and isolated from any real user workspace.
/// </summary>
internal static class ProjectHandoverTaskTransactionSmokeTests
{
    /// <summary>Checks publication, rollback, interrupted replay and data integrity.</summary>
    /// <param name="root">Disposable smoke test workspace.</param>
    /// <returns>Completed test suite.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Task-Handover-Transactions");
        Directory.CreateDirectory(workspace);
        try
        {
            await CheckMethodAsync(workspace,
                ProjectHandoverMethod.MoveOriginal, fault: false);
            await CheckMethodAsync(workspace,
                ProjectHandoverMethod.LinkedSuccessor, fault: false);
            await CheckMethodAsync(workspace,
                ProjectHandoverMethod.MoveOriginal, fault: true);
            await CheckMethodAsync(workspace,
                ProjectHandoverMethod.LinkedSuccessor, fault: true);
            await CheckUnreviewedBacklinkAsync(workspace);
            await CheckUnknownEditRecoveryAsync(workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>One independent two-project handover and reversible recovery.</summary>
    /// <param name="root">Synthetic test workspace root.</param>
    /// <param name="method">A or B handover choice.</param>
    /// <param name="fault">Whether to simulate a process crash after file one.</param>
    /// <returns>Completed scenario.</returns>
    private static async Task CheckMethodAsync(string root,
        ProjectHandoverMethod method, bool fault)
    {
        (string source, string target, Guid taskId,
            string originalSource, string originalTarget) =
            await CreateFixtureAsync(root, method + "-" +
                (fault ? "Interrupted" : "Normal"));
        try
        {
            ProjectHandoverIntent intention = await RecordAsync(root,
                source, target, taskId, method);
            ProjectHandoverTaskExecutionPlanner planner = new(root);
            ProjectHandoverTaskExecutionPlan plan = await planner.PrepareAsync(
                source, intention.Id);
            Assert(plan.CanExecute &&
                   plan.Intent.Id == intention.Id &&
                   plan.ProposedSource != originalSource &&
                   plan.ProposedTarget != originalTarget,
                "Human-approved task should produce exact two-file replacements.");

            ProjectHandoverTaskTransactionService executor =
                new(root, state =>
                {
                    if (fault && state == "first-published")
                    {
                        throw new InvalidOperationException(
                            "Synthetic process interruption after first publication");
                    }
                });
            bool approvalRejected = false;
            try
            {
                await executor.ExecuteAsync(plan, approved: false);
            }
            catch (InvalidOperationException)
            {
                approvalRejected = true;
            }

            Assert(approvalRejected &&
                   await File.ReadAllTextAsync(Path.Combine(
                       source, "Tâches.md")) == originalSource &&
                   await File.ReadAllTextAsync(Path.Combine(
                       target, "Tâches.md")) == originalTarget,
                "Unapproved transfer must not change source or target.");

            ProjectHandoverTaskJournal journal;
            if (fault)
            {
                bool interrupted = false;
                try
                {
                    await executor.ExecuteAsync(plan, approved: true);
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.Contains(
                        "Synthetic process interruption",
                        StringComparison.Ordinal))
                {
                    interrupted = true;
                }

                Assert(interrupted,
                    "Fault injection must stop between durable file publications.");
                ProjectHandoverTaskTransactionService fresh = new(root);
                IReadOnlyList<ProjectHandoverTaskJournal> pending =
                    await fresh.ListPendingAsync();
                Assert(pending.Count == 1 &&
                       pending[0].Phase == "prepared",
                    "Interrupted journal must remain visible to human recovery.");
                bool recoveryNotApproved = false;
                try
                {
                    await fresh.RecoverPendingAsync(approved: false);
                }
                catch (InvalidOperationException)
                {
                    recoveryNotApproved = true;
                }

                Assert(recoveryNotApproved,
                    "Interruption must never trigger a background recovery.");
                int count = await fresh.RecoverPendingAsync(approved: true);
                Assert(count == 1, "Explicit recovery must complete the two files.");
                journal = pending[0] with { Phase = "completed" };
            }
            else
            {
                journal = await executor.ExecuteAsync(plan, approved: true);
            }

            string sourceResult = await File.ReadAllTextAsync(
                Path.Combine(source, "Tâches.md"));
            string targetResult = await File.ReadAllTextAsync(
                Path.Combine(target, "Tâches.md"));
            string originalMarker = TaskPersistentIdentityCodec.FormatMarker(
                taskId);
            string successorMarker = TaskPersistentIdentityCodec.FormatMarker(
                intention.ProposedTargetObjectId);
            Assert(journal.Phase == "completed" &&
                   targetResult.Contains(successorMarker,
                       StringComparison.Ordinal) &&
                   sourceResult.Contains("Passation ",
                       StringComparison.Ordinal),
                "Executed transfer must persist target task and readable source trace.");
            if (method == ProjectHandoverMethod.MoveOriginal)
            {
                Assert(!sourceResult.Contains(originalMarker,
                           StringComparison.Ordinal) &&
                       targetResult.Contains(originalMarker,
                           StringComparison.Ordinal) &&
                       TaskPersistentIdentityCodec.Inspect(
                           sourceResult.Split('\n')).Entries.All(
                           entry => entry.Id != taskId),
                    "Mode A must leave only one active checkbox owner.");
            }
            else
            {
                Assert(sourceResult.Contains(originalMarker,
                           StringComparison.Ordinal) &&
                       targetResult.Contains(successorMarker,
                           StringComparison.Ordinal) &&
                       successorMarker != originalMarker,
                    "Mode B must keep original and distinct successor with provenance.");
            }

            ProjectHandoverTaskTransactionService restorer = new(root);
            ProjectHandoverTaskJournal restored = await restorer.RollbackAsync(
                journal.TransactionId, approved: true);
            Assert(restored.Phase == "rolled-back" &&
                   await File.ReadAllTextAsync(Path.Combine(
                       source, "Tâches.md")) == originalSource &&
                   await File.ReadAllTextAsync(Path.Combine(
                       target, "Tâches.md")) == originalTarget,
                "Explicit rollback must restore byte-exact original Markdown.");
            Assert((await restorer.ListPendingAsync()).Count == 0,
                "Completed rollback has no pending transaction.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(
                Path.GetDirectoryName(source)!)!, recursive: true);
        }
    }

    /// <summary>Checks that other project Markdown references block mode A.</summary>
    /// <param name="root">Synthetic workspace root.</param>
    /// <returns>Completed check.</returns>
    private static async Task CheckUnreviewedBacklinkAsync(string root)
    {
        (string source, string target, Guid taskId,
            string originalSource, string originalTarget) =
            await CreateFixtureAsync(root, "Links");
        try
        {
            string linkPath = Path.Combine(source, "Compte rendu.md");
            await File.WriteAllTextAsync(linkPath,
                "# Lien manuel vers " + taskId.ToString("D"));
            ProjectHandoverIntent intention = await RecordAsync(root,
                source, target, taskId, ProjectHandoverMethod.MoveOriginal);
            ProjectHandoverTaskExecutionPlan plan =
                await new ProjectHandoverTaskExecutionPlanner(root)
                    .PrepareAsync(source, intention.Id);
            Assert(!plan.CanExecute &&
                   plan.Warnings.Any(text =>
                       text.Contains("référence(s) externes", StringComparison.Ordinal)),
                "A must refuse unresolved backlinks without silently rewriting.");
            Assert(await File.ReadAllTextAsync(
                       Path.Combine(source, "Tâches.md")) == originalSource &&
                   await File.ReadAllTextAsync(
                       Path.Combine(target, "Tâches.md")) == originalTarget,
                "Link verification is read-only.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(
                Path.GetDirectoryName(source)!)!, recursive: true);
        }
    }

    /// <summary>Refuses recovery when a human edited a partly published file.</summary>
    /// <param name="root">Synthetic workspace root.</param>
    /// <returns>Completed non-destructive rejection.</returns>
    private static async Task CheckUnknownEditRecoveryAsync(string root)
    {
        (string source, string target, Guid taskId,
            string originalSource, string originalTarget) =
            await CreateFixtureAsync(root, "UnexpectedEdit");
        try
        {
            ProjectHandoverIntent intention = await RecordAsync(root,
                source, target, taskId, ProjectHandoverMethod.MoveOriginal);
            ProjectHandoverTaskExecutionPlan plan =
                await new ProjectHandoverTaskExecutionPlanner(root)
                    .PrepareAsync(source, intention.Id);
            ProjectHandoverTaskTransactionService faulted = new(root,
                step =>
                {
                    if (step == "first-published")
                    {
                        throw new InvalidOperationException("Interruption attendue");
                    }
                });
            try
            {
                await faulted.ExecuteAsync(plan, approved: true);
            }
            catch (InvalidOperationException exception) when (
                exception.Message == "Interruption attendue")
            {
                // Journal remains prepared, first task file already published.
            }

            string sourceFile = Path.Combine(source, "Tâches.md");
            await File.AppendAllTextAsync(sourceFile, "\nÉdition concurrente");
            bool rejected = false;
            try
            {
                await new ProjectHandoverTaskTransactionService(root)
                    .RecoverPendingAsync(approved: true);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected && (await File.ReadAllTextAsync(sourceFile))
                   .Contains("Édition concurrente", StringComparison.Ordinal) &&
                   await File.ReadAllTextAsync(Path.Combine(target, "Tâches.md"))
                       == originalTarget,
                "A recovery must never overwrite an unknown human edit.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(
                Path.GetDirectoryName(source)!)!, recursive: true);
        }
    }

    /// <summary>Creates two distinct canonical project folders with task files.</summary>
    /// <param name="root">Synthetic workspace root.</param>
    /// <param name="name">Scenario-specific application name.</param>
    /// <returns>Source, target, stable task ID and exact source bytes.</returns>
    private static async Task<(string Source, string Target, Guid TaskId,
        string OriginalSource, string OriginalTarget)> CreateFixtureAsync(
        string root, string name)
    {
        string app = Path.Combine(root, "Applications", name);
        string source = Path.Combine(app, "Projets", "Source");
        string target = Path.Combine(app, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid applicationId = Guid.NewGuid();
        ProjectManifest manifest = new()
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            Name = "Source",
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(manifest));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(manifest with
            {
                Id = Guid.NewGuid(), Name = "Target"
            }));
        Guid taskId = Guid.NewGuid();
        string sourceText = "# Tâches\n\n- [ ] Activité fictive " +
            TaskPersistentIdentityCodec.FormatMarker(taskId) + "\n";
        string targetText = "# Tâches\n\n";
        await File.WriteAllTextAsync(Path.Combine(source, "Tâches.md"),
            sourceText);
        await File.WriteAllTextAsync(Path.Combine(target, "Tâches.md"),
            targetText);
        return (source, target, taskId, sourceText, targetText);
    }

    /// <summary>Persists one approved exact-source A/B handover intention.</summary>
    /// <param name="root">Synthetic workspace.</param>
    /// <param name="source">Original project.</param>
    /// <param name="target">Recipient project.</param>
    /// <param name="taskId">Stable original checkbox identity.</param>
    /// <param name="method">Approved strategy.</param>
    /// <returns>Persisted pending plan intention.</returns>
    private static async Task<ProjectHandoverIntent> RecordAsync(
        string root, string source, string target, Guid taskId,
        ProjectHandoverMethod method)
    {
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverPreview preview = await preflight.PrepareAsync(
            source, target, taskId, method);
        Assert(preview.CanRecord, "Fixture preflight should be permitted.");
        ProjectHandoverIntentService service = new(root);
        ProjectHandoverIntentSnapshot history = await service.ReadAsync(source);
        ProjectHandoverIntentSnapshot saved = await service.RecordIntentAsync(
            preview, history, "Reviewer", "Human-approved synthetic transfer.");
        return saved.Ledger.Intents.Single();
    }

    /// <summary>Fails the smoke suite with a concise diagnostic.</summary>
    /// <param name="condition">Required safety invariant.</param>
    /// <param name="message">Diagnostic on failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
