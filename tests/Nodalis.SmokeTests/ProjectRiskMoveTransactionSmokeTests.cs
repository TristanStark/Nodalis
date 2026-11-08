using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// End-to-end RIS A smoke fixture: stable GUID/history, incoming backlinks,
/// exact SHA rollback and replay after source parking/publication/withdrawal.
/// </summary>
internal static class ProjectRiskMoveTransactionSmokeTests
{
    /// <summary>Exercises only disposable synthetic workspace data.</summary>
    /// <param name="root">Temporary isolated Nodalis workspace.</param>
    /// <returns>All non-overwriting assertions passed.</returns>
    public static async Task RunAsync(string root)
    {
        string application = Path.Combine(root, "Applications",
            "RiskMoveFixture");
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid appId = Guid.NewGuid();
        ProjectManifest originalOwner = new()
        {
            Id = Guid.NewGuid(), ApplicationId = appId, Name = "Source",
            InitialComplexity = ProjectComplexity.Complex
        };
        ProjectManifest targetOwner = originalOwner with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(originalOwner));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(targetOwner));

        ProjectRiskRegistryService risks = new(root);
        ProjectRiskEntry initialRisk = await risks.CreateAsync(
            source, "Synthetic historical risk A");
        string originalPath = Path.Combine(source, "Analyse", "Risques",
            initialRisk.Card.Reference + ".md");
        await File.AppendAllTextAsync(originalPath,
            "\n## Historic human analysis\nCANARY_RIS_A_HISTORY_456\n");
        string original = await File.ReadAllTextAsync(originalPath);
        ProjectRiskHandoverPreview first = await PrepareAsync(
            root, source, target, initialRisk.Card.Id);
        Assert(first.CanPreview && first.MoveMarkdown is not null &&
               first.TargetId == initialRisk.Card.Id &&
               first.MoveMarkdown.Contains("CANARY_RIS_A_HISTORY_456",
                   StringComparison.Ordinal),
            "A review must preserve original GUID and human history.");

        ProjectRiskMoveTransactionService service = new(root);
        bool noConsent = false;
        try
        {
            await service.ExecuteAsync(first, approved: false);
        }
        catch (InvalidOperationException)
        {
            noConsent = true;
        }

        Assert(noConsent &&
               await File.ReadAllTextAsync(originalPath) == original,
            "No consent must leave the original risk untouched.");

        string sourceNote = Path.Combine(source, "Notes.md");
        await File.WriteAllTextAsync(sourceNote,
            "External risk link " + initialRisk.Card.Id.ToString("D"));
        bool linked = false;
        try
        {
            await service.ExecuteAsync(first, approved: true);
        }
        catch (InvalidDataException)
        {
            linked = true;
        }

        Assert(linked && File.Exists(originalPath),
            "Incoming RIS GUID backlink must block unsafe A execution.");
        File.Delete(sourceNote);

        ProjectRiskMoveJournal moved = await service.ExecuteAsync(
            first, approved: true);
        string targetFile = Path.Combine(target, "Analyse", "Risques",
            moved.TargetReference + ".md");
        string destination = await File.ReadAllTextAsync(targetFile);
        ProjectRiskCard newOwner = ProjectRiskMarkdownCodec.Parse(destination);
        Assert(moved.Phase == "completed" &&
               !File.Exists(originalPath) &&
               newOwner.Id == initialRisk.Card.Id &&
               newOwner.ProjectId == targetOwner.Id &&
               newOwner.Reference == moved.TargetReference &&
               destination.Contains("CANARY_RIS_A_HISTORY_456",
                   StringComparison.Ordinal) &&
               (await risks.ListAsync(target)).Risks.Any(item =>
                   item.Card.Id == initialRisk.Card.Id) &&
               !(await risks.ListAsync(source)).Risks.Any(item =>
                   item.Card.Id == initialRisk.Card.Id),
            "A must park original before publishing one owner with all history.");

        await File.AppendAllTextAsync(targetFile, "\nHuman edit after move\n");
        bool editedRejected = false;
        try
        {
            await service.RollbackAsync(moved.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            editedRejected = true;
        }

        Assert(editedRejected && !File.Exists(originalPath),
            "Edited recipient RIS must never be silently restored.");
        await File.WriteAllTextAsync(targetFile, destination);
        string targetNote = Path.Combine(target, "RiskLinks.md");
        await File.WriteAllTextAsync(targetNote,
            "Related risk " + initialRisk.Card.Id.ToString("D"));
        bool backlinkRejected = false;
        try
        {
            await service.RollbackAsync(moved.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            backlinkRejected = true;
        }

        Assert(backlinkRejected && File.Exists(targetFile),
            "Rollback must reject a newly linked recipient risk.");
        File.Delete(targetNote);
        ProjectRiskMoveJournal restored = await service.RollbackAsync(
            moved.TransactionId, approved: true);
        Assert(restored.Phase == "rolled-back" &&
               !File.Exists(targetFile) &&
               await File.ReadAllTextAsync(originalPath) == original &&
               (await service.ListPendingAsync()).Count == 0,
            "A rollback must restore exact original historical bytes.");

        bool replayRejected = false;
        try
        {
            await service.ExecuteAsync(first, approved: true);
        }
        catch (InvalidOperationException)
        {
            replayRejected = true;
        }

        Assert(replayRejected,
            "A reviewed intention must never execute twice after rollback.");

        ProjectRiskEntry secondRisk = await risks.CreateAsync(
            source, "Synthetic interrupted A risk");
        ProjectRiskHandoverPreview second = await PrepareAsync(
            root, source, target, secondRisk.Card.Id);
        ProjectRiskMoveTransactionService failAfterParking = new(root,
            stage =>
            {
                if (stage == "source-parked")
                {
                    throw new InvalidOperationException(
                        "Synthetic interruption after parking original");
                }
            });
        bool parkedFault = false;
        try
        {
            await failAfterParking.ExecuteAsync(second, approved: true);
        }
        catch (InvalidOperationException)
        {
            parkedFault = true;
        }

        Assert(parkedFault &&
               (await service.ListPendingAsync()).Count == 1 &&
               !(await risks.ListAsync(source)).Risks.Any(item =>
                   item.Card.Id == secondRisk.Card.Id),
            "Parked original must survive interruption without double ownership.");
        Assert(await service.RecoverPendingAsync(approved: true) == 1 &&
               (await service.ListPendingAsync()).Count == 0 &&
               (await risks.ListAsync(target)).Risks.Any(item =>
                   item.Card.Id == secondRisk.Card.Id),
            "Explicit A recovery must publish the SHA-exact parked risk.");

        ProjectRiskEntry thirdRisk = await risks.CreateAsync(
            source, "Synthetic post-publication A risk");
        ProjectRiskHandoverPreview third = await PrepareAsync(
            root, source, target, thirdRisk.Card.Id);
        ProjectRiskMoveTransactionService failAfterCreate = new(root,
            stage =>
            {
                if (stage == "target-created")
                {
                    throw new InvalidOperationException(
                        "Synthetic interruption after CreateNew");
                }
            });
        bool publishedFault = false;
        try
        {
            await failAfterCreate.ExecuteAsync(third, approved: true);
        }
        catch (InvalidOperationException)
        {
            publishedFault = true;
        }

        Assert(publishedFault &&
               (await service.ListPendingAsync()).Count == 1,
            "A completed file with pending journal must be safely recoverable.");
        Assert(await service.RecoverPendingAsync(approved: true) == 1,
            "Explicit recovery must recognize an already published exact RIS.");

        ProjectRiskMoveJournal latest =
            (await service.ListHistoryAsync()).Single(journal =>
                journal.IntentId == third.Intent.Id &&
                journal.Phase == "completed");
        ProjectRiskMoveTransactionService failAfterWithdrawal = new(root,
            stage =>
            {
                if (stage == "target-withdrawn")
                {
                    throw new InvalidOperationException(
                        "Synthetic interrupted rollback of A");
                }
            });
        bool undoFault = false;
        try
        {
            await failAfterWithdrawal.RollbackAsync(
                latest.TransactionId, approved: true);
        }
        catch (InvalidOperationException)
        {
            undoFault = true;
        }

        Assert(undoFault &&
               (await service.ListPendingAsync()).Count == 1,
            "Rollback must preserve its journal after target withdrawal.");
        Assert(await service.RecoverPendingAsync(approved: true) == 1 &&
               (await service.ListPendingAsync()).Count == 0 &&
               (await risks.ListAsync(source)).Risks.Any(item =>
                   item.Card.Id == thirdRisk.Card.Id) &&
               !(await risks.ListAsync(target)).Risks.Any(item =>
                   item.Card.Id == thirdRisk.Card.Id),
            "Recovery must restore the original A owner, without duplicates.");


        // An A transfer cannot carry a current-plan task association into
        // another owner, but historic link/unlink decisions remain visible.
        Guid planId = Guid.NewGuid();
        Guid linkedTaskId = Guid.NewGuid();
        ProjectRiskTaskLinkEvent link = new()
        {
            Id = Guid.NewGuid(),
            RiskId = first.TargetId,
            TreatmentId = planId,
            Kind = ProjectRiskMeasureKind.Prevention,
            TaskId = linkedTaskId,
            Action = ProjectRiskTaskLinkAction.Link,
            Actor = "Synthetic reviewer",
            Rationale = "Synthetic link for guard qualification",
            RecordedUtc = DateTimeOffset.UtcNow
        };
        ProjectRiskTaskLinkEvent unlink = link with
        {
            Id = Guid.NewGuid(),
            PreviousId = link.Id,
            Action = ProjectRiskTaskLinkAction.Unlink,
            RecordedUtc = link.RecordedUtc.AddMinutes(1)
        };
        Assert(ProjectRiskMoveOutboundGuard.HasActiveTaskLinks(
                   planId, [link]) &&
               !ProjectRiskMoveOutboundGuard.HasActiveTaskLinks(
                   planId, [link, unlink]) &&
               !ProjectRiskMoveOutboundGuard.HasActiveTaskLinks(
                   Guid.NewGuid(), [link]),
            "Only live links of the latest treatment block RIS A; " +
            "unlinked and older historical associations do not.");

        Directory.Delete(application, recursive: true);
    }

    /// <summary>Registers an explicit A intention and obtains its latest preview.</summary>
    /// <param name="root">Workspace.</param>
    /// <param name="source">Original project.</param>
    /// <param name="target">Receiving project.</param>
    /// <param name="riskId">Selected risk identity.</param>
    /// <returns>Human-signed in-memory A plan.</returns>
    private static async Task<ProjectRiskHandoverPreview> PrepareAsync(
        string root, string source, string target, Guid riskId)
    {
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverIntentService intentions = new(root);
        ProjectHandoverPreview candidate = await preflight.PrepareAsync(
            source, target, riskId, ProjectHandoverMethod.MoveOriginal);
        ProjectHandoverIntentSnapshot current =
            await intentions.ReadAsync(source);
        ProjectHandoverIntentSnapshot recorded =
            await intentions.RecordIntentAsync(candidate, current,
                "Synthetic reviewer", "Move original with manual requalification.");
        return await new ProjectRiskHandoverPreviewService(root)
            .PrepareAsync(source, recorded.Ledger.Intents[^1].Id);
    }

    /// <summary>Stops on broken safety invariant.</summary>
    /// <param name="value">Expected truth.</param>
    /// <param name="message">Diagnostic on failure.</param>
    private static void Assert(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
