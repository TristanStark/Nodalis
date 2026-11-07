using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>Real EXI B journal tests in fully synthetic disposable workspaces.</summary>
internal static class ProjectRequirementSuccessorTransactionSmokeTests
{
    /// <summary>Verifies exact creation, interruption replay and safe rollback.</summary>
    /// <param name="root">Unique smoke-test temporary root.</param>
    /// <returns>Completed independent transaction cases.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Exi-B-Transactions");
        Directory.CreateDirectory(workspace);
        try
        {
            await CheckSuccessAsync(workspace);
            await CheckInterruptedPublicationAsync(workspace, "prepared");
            await CheckInterruptedPublicationAsync(workspace, "target-created");
            await CheckInterruptedRollbackAsync(workspace);
            await CheckHumanEditAsync(workspace);
            await CheckStaleReferenceAsync(workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates a B successor and restores it without ever editing A.</summary>
    /// <param name="root">Test workspace.</param>
    /// <returns>Completed no-mutation and rollback checks.</returns>
    private static async Task CheckSuccessAsync(string root)
    {
        (string source, string target, string path,
            ProjectRequirementHandoverPreview preview) =
            await CreateApprovedPreviewAsync(root, "Success");
        string original = await File.ReadAllTextAsync(path);
        ProjectRequirementSuccessorTransactionService service = new(root);
        bool rejected = false;
        try
        {
            await service.ExecuteAsync(preview, approved: false);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected && (await service.ListPendingAsync()).Count == 0,
            "EXI creation requires a second explicit human approval.");
        Assert(preview.CanPrepare && preview.ProposedTargetRelativePath is not null,
            "The B preview must be a canonical, unblocked successor.");
        ProjectRequirementSuccessorJournal completed =
            await service.ExecuteAsync(preview, approved: true);
        string destination = Path.Combine(root, preview.ProposedTargetRelativePath!
            .Replace('/', Path.DirectorySeparatorChar));
        ProjectRequirementCard targetCard =
            ProjectRequirementMarkdownCodec.Parse(
                await File.ReadAllTextAsync(destination));
        Assert(completed.Phase == "completed" &&
               targetCard.Id == preview.ProposedId &&
               targetCard.ProjectId == preview.Intent.TargetProjectId &&
               targetCard.Validation == ProjectRequirementValidation.Draft &&
               (await File.ReadAllTextAsync(destination)).Contains(
                   "Passation B", StringComparison.Ordinal) &&
               await File.ReadAllTextAsync(path) == original,
            "B creates a distinct provenance-bearing draft, retaining source bytes.");

        Assert((await service.ListHistoryAsync()).Any(item =>
            item.TransactionId == completed.TransactionId) &&
            (await service.ListPendingAsync()).Count == 0,
            "A completed B transaction must remain durable and not pending.");
        bool duplicateRefused = false;
        try
        {
            await service.ExecuteAsync(preview, approved: true);
        }
        catch (InvalidOperationException)
        {
            duplicateRefused = true;
        }

        Assert(duplicateRefused,
            "A consumed B intention must never create a second successor.");
        ProjectRequirementSuccessorJournal rolledBack =
            await service.RollbackAsync(completed.TransactionId, approved: true);
        Assert(rolledBack.Phase == "rolled-back" &&
               !Path.Exists(destination) &&
               await File.ReadAllTextAsync(path) == original,
            "Explicit B rollback must retire only the new successor.");
        string retained = Path.Combine(root, "Imports",
            ".exi-successor-transactions",
            completed.TransactionId.ToString("N"), "withdrawn-successor.md");
        Assert(File.Exists(retained) &&
               ProjectRequirementMarkdownCodec.Parse(
                   await File.ReadAllTextAsync(retained)).Id ==
                   preview.ProposedId,
            "The withdrawn EXI remains available byte-exact in journal history.");
    }

    /// <summary>Simulates interruption before or after one-file creation.</summary>
    /// <param name="root">Test workspace.</param>
    /// <param name="fault">Named deterministic checkpoint.</param>
    /// <returns>Completed recovery and original preservation checks.</returns>
    private static async Task CheckInterruptedPublicationAsync(
        string root, string fault)
    {
        (string source, string target, string path,
            ProjectRequirementHandoverPreview preview) =
            await CreateApprovedPreviewAsync(root, "Interrupted-" + fault);
        string original = await File.ReadAllTextAsync(path);
        ProjectRequirementSuccessorTransactionService faulty =
            new(root, checkpoint =>
            {
                if (checkpoint == fault)
                {
                    throw new InvalidOperationException(
                        "Injected EXI interruption");
                }
            });
        bool stopped = false;
        try
        {
            await faulty.ExecuteAsync(preview, approved: true);
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "Injected EXI interruption")
        {
            stopped = true;
        }

        Assert(stopped, "The EXI transaction must stop at chosen checkpoint.");
        ProjectRequirementSuccessorTransactionService resumed = new(root);
        IReadOnlyList<ProjectRequirementSuccessorJournal> pending =
            await resumed.ListPendingAsync();
        Assert(pending.Count == 1 && pending[0].Phase == "prepared",
            "EXI interruption must leave exact readable pending journal.");
        bool refused = false;
        try
        {
            await resumed.RecoverPendingAsync(approved: false);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Assert(refused,
            "A transaction must not recover automatically after a restart.");
        int count = await resumed.RecoverPendingAsync(approved: true);
        string destination = Path.Combine(root, preview.ProposedTargetRelativePath!
            .Replace('/', Path.DirectorySeparatorChar));
        Assert(count == 1 && File.Exists(destination) &&
               (await resumed.ListPendingAsync()).Count == 0 &&
               await File.ReadAllTextAsync(path) == original,
            "Explicit recovery must create or recognize exact successor once.");
        await resumed.RollbackAsync(pending[0].TransactionId, approved: true);
    }

    /// <summary>Verifies recovery of a crash just after the rollback rename.</summary>
    /// <param name="root">Test workspace.</param>
    /// <returns>Completed rollback recovery.</returns>
    private static async Task CheckInterruptedRollbackAsync(string root)
    {
        (string source, string target, string path,
            ProjectRequirementHandoverPreview preview) =
            await CreateApprovedPreviewAsync(root, "Interrupted-Rollback");
        ProjectRequirementSuccessorTransactionService faulty =
            new(root, checkpoint =>
            {
                if (checkpoint == "target-withdrawn")
                {
                    throw new InvalidOperationException(
                        "Injected EXI rollback interruption");
                }
            });
        ProjectRequirementSuccessorJournal completed =
            await faulty.ExecuteAsync(preview, approved: true);
        bool interrupted = false;
        try
        {
            await faulty.RollbackAsync(completed.TransactionId, approved: true);
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "Injected EXI rollback interruption")
        {
            interrupted = true;
        }

        ProjectRequirementSuccessorTransactionService resumed = new(root);
        Assert(interrupted &&
               (await resumed.ListPendingAsync()).Single().Phase ==
                   "rollback-prepared",
            "Rollback crash must remain a distinct pending phase.");
        int count = await resumed.RecoverPendingAsync(approved: true);
        string destination = Path.Combine(root, preview.ProposedTargetRelativePath!
            .Replace('/', Path.DirectorySeparatorChar));
        Assert(count == 1 && !Path.Exists(destination) &&
               (await resumed.ListPendingAsync()).Count == 0,
            "A rollback interruption must reconcile the already-withdrawn file.");
    }

    /// <summary>Refuses rollback if a human edited the published successor.</summary>
    /// <param name="root">Test workspace.</param>
    /// <returns>Non-destructive human-edit refusal.</returns>
    private static async Task CheckHumanEditAsync(string root)
    {
        (string source, string target, string path,
            ProjectRequirementHandoverPreview preview) =
            await CreateApprovedPreviewAsync(root, "Human-Edit");
        ProjectRequirementSuccessorTransactionService service = new(root);
        ProjectRequirementSuccessorJournal completed =
            await service.ExecuteAsync(preview, approved: true);
        string destination = Path.Combine(root, preview.ProposedTargetRelativePath!
            .Replace('/', Path.DirectorySeparatorChar));
        await File.AppendAllTextAsync(destination, "\nNote humaine postérieure.");
        bool refused = false;
        try
        {
            await service.RollbackAsync(completed.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && File.Exists(destination) &&
               (await File.ReadAllTextAsync(destination)).Contains(
                   "Note humaine postérieure.", StringComparison.Ordinal) &&
               (await service.ListPendingAsync()).Count == 0,
            "An edited successor must never be overwritten or withdrawn.");
        // Fixture cleanup: restore exact staged bytes and roll back.
        await File.WriteAllTextAsync(destination, preview.ProposedMarkdown!);
        await service.RollbackAsync(completed.TransactionId, approved: true);
    }

    /// <summary>Refuses an EXI code claimed after a preview was approved.</summary>
    /// <param name="root">Test workspace.</param>
    /// <returns>Non-destructive reference-collision refusal.</returns>
    private static async Task CheckStaleReferenceAsync(string root)
    {
        (string source, string target, string path,
            ProjectRequirementHandoverPreview preview) =
            await CreateApprovedPreviewAsync(root, "Stale-Reference");
        await new ProjectRequirementRegistryService(root).CreateAsync(target,
            "Nouvelle exigence réservant le code");
        ProjectRequirementSuccessorTransactionService service = new(root);
        bool refused = false;
        try
        {
            await service.ExecuteAsync(preview, approved: true);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        string destination = Path.Combine(root, preview.ProposedTargetRelativePath!
            .Replace('/', Path.DirectorySeparatorChar));
        Assert(refused && File.Exists(destination) &&
               (await service.ListHistoryAsync()).Count == 0,
            "Stale EXI B approval must not overwrite an already used reference.");
    }

    /// <summary>Builds two real canonical projects and a persisted B intention.</summary>
    /// <param name="root">One disposable test workspace.</param>
    /// <param name="name">Independent scenario application folder.</param>
    /// <returns>Approved live B preview and synthetic original path.</returns>
    private static async Task<(string Source, string Target, string SourcePath,
        ProjectRequirementHandoverPreview Preview)> CreateApprovedPreviewAsync(
        string root, string name)
    {
        string application = Path.Combine(root, "Applications", name);
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid appId = Guid.NewGuid();
        ProjectManifest original = new()
        {
            Id = Guid.NewGuid(), ApplicationId = appId, Name = "Source",
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        ProjectManifest recipient = original with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(original));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(recipient));
        ProjectRequirementEntry entry =
            await new ProjectRequirementRegistryService(root).CreateAsync(
                source, "Exigence synthétique originale");
        ProjectHandoverPreview preflight =
            await new ProjectHandoverPreflightService(root).PrepareAsync(
                source, target, entry.Card.Id,
                ProjectHandoverMethod.LinkedSuccessor);
        ProjectHandoverIntentService intentions = new(root);
        ProjectHandoverIntentSnapshot intentHistory =
            await intentions.RecordIntentAsync(preflight,
                await intentions.ReadAsync(source), "Reviewer",
                "Human-approved synthetic successor with no inherited verdict.");
        Guid intentId = intentHistory.Ledger.Intents.Single().Id;
        ProjectRequirementHandoverPreview review =
            await new ProjectRequirementHandoverPreviewService(root)
                .PrepareAsync(source, intentId);
        Assert(review.CanPrepare &&
               review.Intent.Method == ProjectHandoverMethod.LinkedSuccessor,
            "Synthetic project should yield an unblocked EXI B preview.");
        return (source, target,
            Path.Combine(source, "Documentation", "Exigences",
                entry.Card.Reference + ".md"), review);
    }

    /// <summary>Signals an exact failed safety contract.</summary>
    /// <param name="condition">Required state.</param>
    /// <param name="message">Specific test failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
