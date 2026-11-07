using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic, isolated EXI A transfer/rollback smoke tests on the Windows
/// CI host. No production workspace or real business data is used.
/// </summary>
internal static class ProjectRequirementMoveTransactionSmokeTests
{
    /// <summary>Runs successful A and all failure checkpoints.</summary>
    /// <param name="root">Disposable smoke test root.</param>
    /// <returns>Completed fail-closed transaction regressions.</returns>
    public static async Task RunAsync(string root)
    {
        await CheckSuccessAsync(Path.Combine(root, "exi-a-success"));
        await CheckInterruptedAsync(Path.Combine(root, "exi-a-prepared"),
            "prepared");
        await CheckInterruptedAsync(Path.Combine(root, "exi-a-parked"),
            "source-parked");
        await CheckInterruptedAsync(Path.Combine(root, "exi-a-created"),
            "target-created");
        await CheckRollbackCrashAsync(Path.Combine(root, "exi-a-undo-move"),
            "target-withdrawn");
        await CheckRollbackCrashAsync(Path.Combine(root, "exi-a-undo-source"),
            "source-restored");
        await CheckEditedDestinationAsync(Path.Combine(root, "exi-a-edited"));
        await CheckExternalLinksAsync(Path.Combine(root, "exi-a-links"));
    }

    /// <summary>Verifies original GUID and byte-exact manual undo.</summary>
    /// <param name="root">Isolated workspace.</param>
    /// <returns>Completed round-trip assertion.</returns>
    private static async Task CheckSuccessAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed) =
            await CreatePreviewAsync(root);
        string original = await File.ReadAllTextAsync(file);
        ProjectRequirementMoveTransactionService service = new(root);
        bool rejected = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: false);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected && (await service.ListHistoryAsync()).Count == 0,
            "A mutation cannot start without a second human confirmation.");
        ProjectRequirementMoveJournal moved =
            await service.ExecuteAsync(reviewed, approved: true);
        string destination = Resolve(root, moved.TargetRelativePath);
        ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(
            await File.ReadAllTextAsync(destination));
        Assert(moved.Phase == "completed" && !File.Exists(file) &&
               card.Id == reviewed.Intent.SourceObjectId &&
               card.ProjectId == reviewed.Intent.TargetProjectId &&
               card.Reference == reviewed.ProposedReference &&
               (await File.ReadAllTextAsync(destination)).Contains(
                   "Passation A", StringComparison.Ordinal),
            "A must park the original before publishing the same GUID in target.");
        bool repeated = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidOperationException)
        {
            repeated = true;
        }

        Assert(repeated, "An executed A intent must not be consumed twice.");
        ProjectRequirementMoveJournal undone =
            await service.RollbackAsync(moved.TransactionId, approved: true);
        string history = Path.Combine(root, "Imports", ".exi-move-transactions",
            moved.TransactionId.ToString("N"), "withdrawn-destination.md");
        Assert(undone.Phase == "rolled-back" &&
               File.Exists(file) && !File.Exists(destination) &&
               await File.ReadAllTextAsync(file) == original &&
               File.Exists(history) &&
               ProjectRequirementMarkdownCodec.Parse(
                   await File.ReadAllTextAsync(history)).Id == card.Id &&
               (await service.ListPendingAsync()).Count == 0,
            "A rollback restores exactly the original and retains target history.");
    }

    /// <summary>Restarts from staged, parked or already-created snapshots.</summary>
    /// <param name="root">Isolated workspace.</param>
    /// <param name="checkpoint">Crash-injection checkpoint.</param>
    /// <returns>Recovered A and intact original after rollback.</returns>
    private static async Task CheckInterruptedAsync(
        string root, string checkpoint)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed) =
            await CreatePreviewAsync(root);
        string original = await File.ReadAllTextAsync(file);
        ProjectRequirementMoveTransactionService faulty = new(root, name =>
        {
            if (name == checkpoint)
            {
                throw new InvalidOperationException("EXI-A crash injection");
            }
        });
        bool stopped = false;
        try
        {
            await faulty.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "EXI-A crash injection")
        {
            stopped = true;
        }

        ProjectRequirementMoveTransactionService recovered = new(root);
        ProjectRequirementMoveJournal pending =
            (await recovered.ListPendingAsync()).Single();
        Assert(stopped && pending.Phase == "prepared",
            "Source-park or publication interruption must leave a durable journal.");
        bool refused = false;
        try
        {
            await recovered.RecoverPendingAsync(approved: false);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Assert(refused, "Interrupted A cannot auto-recover without approval.");
        int count = await recovered.RecoverPendingAsync(approved: true);
        ProjectRequirementMoveJournal completed =
            (await recovered.ListHistoryAsync()).Single();
        Assert(count == 1 && completed.Phase == "completed" &&
               !File.Exists(file) &&
               File.Exists(Resolve(root, completed.TargetRelativePath)),
            "Approved recovery must finish from an exact staged/parked A snapshot.");
        await recovered.RollbackAsync(completed.TransactionId, approved: true);
        Assert(await File.ReadAllTextAsync(file) == original,
            "A recovered transaction remains byte-exactly reversible.");
    }

    /// <summary>Replays undo after removing target or restoring source.</summary>
    /// <param name="root">Isolated workspace.</param>
    /// <param name="checkpoint">Rollback crash point.</param>
    /// <returns>Recovered original with no duplicate active owner.</returns>
    private static async Task CheckRollbackCrashAsync(
        string root, string checkpoint)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed) =
            await CreatePreviewAsync(root);
        string original = await File.ReadAllTextAsync(file);
        ProjectRequirementMoveTransactionService faulty = new(root, name =>
        {
            if (name == checkpoint)
            {
                throw new InvalidOperationException("EXI-A undo injection");
            }
        });
        ProjectRequirementMoveJournal moved =
            await faulty.ExecuteAsync(reviewed, approved: true);
        bool stopped = false;
        try
        {
            await faulty.RollbackAsync(moved.TransactionId, approved: true);
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "EXI-A undo injection")
        {
            stopped = true;
        }

        ProjectRequirementMoveTransactionService recovered = new(root);
        Assert(stopped &&
               (await recovered.ListPendingAsync()).Single().Phase ==
                   "rollback-prepared",
            "Interrupted A rollback must remain a pending durable journal.");
        Assert(await recovered.RecoverPendingAsync(approved: true) == 1 &&
               File.Exists(file) &&
               !File.Exists(Resolve(root, moved.TargetRelativePath)) &&
               await File.ReadAllTextAsync(file) == original,
            "Approved rollback recovery returns the original, no active duplicates.");
    }

    /// <summary>Refuses overwriting a human-edited destination on rollback.</summary>
    /// <param name="root">Isolated workspace.</param>
    /// <returns>Safe rejection before rollback journal mutation.</returns>
    private static async Task CheckEditedDestinationAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed) =
            await CreatePreviewAsync(root);
        ProjectRequirementMoveTransactionService service = new(root);
        ProjectRequirementMoveJournal moved =
            await service.ExecuteAsync(reviewed, approved: true);
        string destination = Resolve(root, moved.TargetRelativePath);
        await File.AppendAllTextAsync(destination, "\nHuman edit.");
        bool refused = false;
        try
        {
            await service.RollbackAsync(moved.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && File.Exists(destination) &&
               (await service.ListPendingAsync()).Count == 0 &&
               !File.Exists(file),
            "Editing a moved requirement must forbid destructive automatic undo.");
    }

    /// <summary>Rejects incoming GUID and human-reference links before A.</summary>
    /// <param name="root">Isolated workspace.</param>
    /// <returns>Verified non-destructive reference preflight.</returns>
    private static async Task CheckExternalLinksAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed) =
            await CreatePreviewAsync(root);
        string external = Path.Combine(root, "Applications", "External.md");
        ProjectRequirementMoveTransactionService service = new(root);
        await File.WriteAllTextAsync(external,
            "Related GUID: " + reviewed.Intent.SourceObjectId.ToString("D"));
        bool guidRefused = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidDataException)
        {
            guidRefused = true;
        }

        Assert(guidRefused && File.Exists(file) &&
               (await service.ListHistoryAsync()).Count == 0,
            "EXI A must refuse a foreign reference to its original GUID.");
        await File.WriteAllTextAsync(external,
            "Related EXI: " + reviewed.Intent.SourceReference);
        bool codeRefused = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidDataException)
        {
            codeRefused = true;
        }

        Assert(codeRefused && File.Exists(file) &&
               (await service.ListHistoryAsync()).Count == 0,
            "EXI A must refuse ambiguous human EXI-xxxx references.");
        await File.WriteAllTextAsync(external,
            "Future target: " + reviewed.ProposedReference);
        bool destinationCodeRefused = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidDataException)
        {
            destinationCodeRefused = true;
        }

        Assert(destinationCodeRefused && File.Exists(file) &&
               (await service.ListHistoryAsync()).Count == 0,
            "A proposed EXI destination code must not capture existing links.");
        File.Delete(external);
        string arbitrary = Path.Combine(root, "Applications", "Notes",
            "Passations.md");
        Directory.CreateDirectory(Path.GetDirectoryName(arbitrary)!);
        await File.WriteAllTextAsync(arbitrary,
            "External reference: " + reviewed.Intent.SourceReference);
        bool sameNameRefused = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidDataException)
        {
            sameNameRefused = true;
        }

        Assert(sameNameRefused && File.Exists(file) &&
               (await service.ListHistoryAsync()).Count == 0,
            "Arbitrary user Passations.md documents must still be inspected.");
    }

    /// <summary>Builds two valid synthetic projects and a source-owned A intent.</summary>
    /// <param name="root">Test workspace.</param>
    /// <returns>Original and unblocked A preview.</returns>
    private static async Task<(string Source, string Target, string File,
        ProjectRequirementHandoverPreview Preview)> CreatePreviewAsync(
        string root)
    {
        string application = Path.Combine(root, "Applications", "Synthetic");
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid app = Guid.NewGuid();
        ProjectManifest sourceManifest = new()
        {
            Id = Guid.NewGuid(), ApplicationId = app,
            Name = "Source", InitialComplexity = ProjectComplexity.UltraSimple
        };
        ProjectManifest targetManifest = sourceManifest with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(sourceManifest));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(targetManifest));
        ProjectRequirementRegistryService registry = new(root);
        ProjectRequirementEntry requirement =
            await registry.CreateAsync(source, "Migration sûre");
        await registry.CreateAsync(target, "Exigence indépendante");
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverPreview reviewed = await preflight.PrepareAsync(
            source, target, requirement.Card.Id,
            ProjectHandoverMethod.MoveOriginal);
        ProjectHandoverIntentService service = new(root);
        ProjectHandoverIntentSnapshot saved = await service.RecordIntentAsync(
            reviewed, await service.ReadAsync(source), "Reviewer",
            "A conservera le GUID avec requalification des références.");
        ProjectRequirementHandoverPreview candidate =
            await new ProjectRequirementHandoverPreviewService(root)
                .PrepareAsync(source, saved.Ledger.Intents.Single().Id);
        Assert(candidate.CanPrepare &&
               candidate.ProposedId == requirement.Card.Id &&
               candidate.ProposedReference == "EXI-0002",
            "Preflight must produce original GUID and fresh target reference. " +
            candidate.FormatReview());
        return (source, target, Path.Combine(source, "Documentation",
            "Exigences", requirement.Card.Reference + ".md"), candidate);
    }

    /// <summary>Maps a canonical workspace-relative path to local tests.</summary>
    /// <param name="root">Workspace root.</param>
    /// <param name="relative">Canonical path to inspect.</param>
    /// <returns>Physical disposable path.</returns>
    private static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Fails with a specific violated invariant.</summary>
    /// <param name="condition">Required state.</param>
    /// <param name="message">Assertion details.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
