using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic RIS B creation, CAS, human undo, backlink guard and restart after
/// deterministic faults. No real project or production data is used.
/// </summary>
internal static class ProjectRiskSuccessorTransactionSmokeTests
{
    /// <summary>Runs only within a disposable workspace fixture.</summary>
    /// <param name="root">Isolated temporary smoke test workspace.</param>
    /// <returns>All transaction assertions complete.</returns>
    public static async Task RunAsync(string root)
    {
        string application = Path.Combine(root, "Applications",
            "RiskTransactionFixture");
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid applicationId = Guid.NewGuid();
        ProjectManifest owner = new()
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId,
            Name = "Source", InitialComplexity = ProjectComplexity.Complex
        };
        ProjectManifest recipient = owner with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(owner));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(recipient));

        ProjectRiskRegistryService risks = new(root);
        ProjectRiskEntry first = await risks.CreateAsync(
            source, "Synthetic transaction risk");
        string sourceFile = Path.Combine(source, "Analyse", "Risques",
            first.Card.Reference + ".md");
        await File.AppendAllTextAsync(sourceFile,
            "\nCANARY_RIS_HISTORY_NOT_INHERITED\n");
        string before = await File.ReadAllTextAsync(sourceFile);

        ProjectRiskHandoverPreview firstPreview = await PrepareAsync(
            root, source, target, first.Card.Id);
        ProjectRiskSuccessorTransactionService service = new(root);
        bool missingApprovalRejected = false;
        try
        {
            await service.ExecuteAsync(firstPreview, approved: false);
        }
        catch (InvalidOperationException)
        {
            missingApprovalRejected = true;
        }

        Assert(missingApprovalRejected &&
               !Directory.Exists(Path.Combine(target, "Analyse", "Risques")),
            "A RIS successor must not be created without separate approval.");

        ProjectRiskSuccessorJournal created = await service.ExecuteAsync(
            firstPreview, approved: true);
        string targetFile = Path.Combine(target, "Analyse", "Risques",
            created.TargetReference + ".md");
        string published = await File.ReadAllTextAsync(targetFile);
        ProjectRiskCard successor = ProjectRiskMarkdownCodec.Parse(published);
        Assert(created.Phase == "completed" &&
               successor.Id == firstPreview.TargetId &&
               successor.Id != first.Card.Id &&
               successor.ProjectId == recipient.Id &&
               successor.Qualification == ProjectRiskQualification.ToReview &&
               successor.Lifecycle == ProjectRiskLifecycle.Open &&
               successor.Initial == new ProjectRiskRating(null, null) &&
               successor.Residual == new ProjectRiskRating(null, null) &&
               !published.Contains("CANARY_RIS_HISTORY_NOT_INHERITED",
                   StringComparison.Ordinal) &&
               await File.ReadAllTextAsync(sourceFile) == before,
            "RIS B must publish a clean, distinct recipient and keep source bytes.");
        Assert((await service.ListHistoryAsync()).Any(item =>
                   item.TransactionId == created.TransactionId &&
                   item.Phase == "completed") &&
               (await service.ListPendingAsync()).Count == 0,
            "Journal must describe exact successful creation.");

        bool secondExecutionRejected = false;
        try
        {
            await service.ExecuteAsync(firstPreview, approved: true);
        }
        catch (InvalidOperationException)
        {
            secondExecutionRejected = true;
        }

        Assert(secondExecutionRejected,
            "An approved RIS B intention must be single-use.");

        await File.AppendAllTextAsync(targetFile, "\nEdited by human.\n");
        bool editedRollbackRejected = false;
        try
        {
            await service.RollbackAsync(created.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            editedRollbackRejected = true;
        }

        Assert(editedRollbackRejected &&
               File.Exists(targetFile), "Edited successor must never be withdrawn.");
        await File.WriteAllTextAsync(targetFile, published);

        string note = Path.Combine(target, "Notes.md");
        await File.WriteAllTextAsync(note,
            "Reference to new risk " + successor.Id.ToString("D") + "\n");
        bool linkedRollbackRejected = false;
        try
        {
            await service.RollbackAsync(created.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            linkedRollbackRejected = true;
        }

        Assert(linkedRollbackRejected && File.Exists(targetFile),
            "A linked successor must not be withdrawn leaving dangling GUID.");
        File.Delete(note);
        ProjectRiskSuccessorJournal rolledBack = await service.RollbackAsync(
            created.TransactionId, approved: true);
        Assert(rolledBack.Phase == "rolled-back" &&
               !File.Exists(targetFile) &&
               await File.ReadAllTextAsync(sourceFile) == before,
            "Human rollback must withdraw only the unmodified successor.");

        ProjectRiskEntry second = await risks.CreateAsync(
            source, "Synthetic recoverable risk");
        ProjectRiskHandoverPreview secondPreview = await PrepareAsync(
            root, source, target, second.Card.Id);
        ProjectRiskSuccessorTransactionService interrupt = new(root,
            stage =>
            {
                if (stage == "prepared")
                {
                    throw new InvalidOperationException(
                        "Synthetic crash before CreateNew");
                }
            });
        bool interrupted = false;
        try
        {
            await interrupt.ExecuteAsync(secondPreview, approved: true);
        }
        catch (InvalidOperationException)
        {
            interrupted = true;
        }

        Assert(interrupted &&
               (await service.ListPendingAsync()).Count == 1,
            "Prepared journal must survive before publication.");
        int recovered = await service.RecoverPendingAsync(approved: true);
        Assert(recovered == 1 &&
               (await service.ListPendingAsync()).Count == 0 &&
               (await risks.ListAsync(target)).Risks.Any(item =>
                   item.Card.Id == secondPreview.TargetId),
            "Explicit recovery must finish the exact approved B snapshot.");

        ProjectRiskEntry third = await risks.CreateAsync(
            source, "Synthetic post-CreateNew risk");
        ProjectRiskHandoverPreview thirdPreview = await PrepareAsync(
            root, source, target, third.Card.Id);
        ProjectRiskSuccessorTransactionService afterCreate = new(root,
            stage =>
            {
                if (stage == "target-created")
                {
                    throw new InvalidOperationException(
                        "Synthetic crash after CreateNew");
                }
            });
        bool afterCreateInterrupted = false;
        try
        {
            await afterCreate.ExecuteAsync(thirdPreview, approved: true);
        }
        catch (InvalidOperationException)
        {
            afterCreateInterrupted = true;
        }

        Assert(afterCreateInterrupted &&
               (await service.ListPendingAsync()).Count == 1,
            "Interrupted commit must keep a recoverable prepared journal.");
        Assert(await service.RecoverPendingAsync(approved: true) == 1 &&
               (await service.ListPendingAsync()).Count == 0,
            "Recovery must accept an exact existing file after CreateNew.");

        Directory.Delete(application, recursive: true);
    }

    /// <summary>Creates one real signed RIS B intention and reads its proposal.</summary>
    /// <param name="root">Workspace root.</param>
    /// <param name="source">Original active project.</param>
    /// <param name="target">Recipient active project.</param>
    /// <param name="riskId">Selected original stable RIS identity.</param>
    /// <returns>Fresh immutable B proposal.</returns>
    private static async Task<ProjectRiskHandoverPreview> PrepareAsync(
        string root, string source, string target, Guid riskId)
    {
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverIntentService intentions = new(root);
        ProjectHandoverPreview plan = await preflight.PrepareAsync(
            source, target, riskId, ProjectHandoverMethod.LinkedSuccessor);
        ProjectHandoverIntentSnapshot snapshot =
            await intentions.ReadAsync(source);
        ProjectHandoverIntentSnapshot updated =
            await intentions.RecordIntentAsync(plan, snapshot,
                "Synthetic reviewer",
                "Human-reviewed continuation with independent identity.");
        Guid intentId = updated.Ledger.Intents[^1].Id;
        return await new ProjectRiskHandoverPreviewService(root)
            .PrepareAsync(source, intentId);
    }

    /// <summary>Stops on missing immutable transaction invariants.</summary>
    /// <param name="condition">Expected outcome.</param>
    /// <param name="message">Reason for smoke failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
