using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic EXI B successor acceptance tests. The source retains evidence
/// while the successor receives independent, unverified criterion identities.
/// </summary>
internal static class ProjectRequirementSuccessorCriteriaSmokeTests
{
    /// <summary>Runs independent B/CA generation and recovery scenarios.</summary>
    /// <param name="root">Disposable CI test workspace parent.</param>
    /// <returns>Completed safety assertions.</returns>
    public static async Task RunAsync(string root)
    {
        await CheckReviewAndPublicationAsync(Path.Combine(root, "exi-b-ca-new"));
        await CheckCrashReplayAsync(Path.Combine(root, "exi-b-ca-prepared"),
            "prepared");
        await CheckCrashReplayAsync(Path.Combine(root, "exi-b-ca-created"),
            "target-created");
        await CheckBacklinksAsync(Path.Combine(root, "exi-b-ca-links"));
        await CheckBrokenIdentificationAsync(Path.Combine(root, "exi-b-ca-bad"));
    }

    /// <summary>Tests no implicit CA consent, provenance and clean draft.</summary>
    /// <param name="root">Isolated one-workspace fixture.</param>
    /// <returns>Verified new identities, no copied proofs and full undo.</returns>
    private static async Task CheckReviewAndPublicationAsync(string root)
    {
        (string source, string target, string originalPath,
            ProjectRequirementHandoverPreview preview, Guid[] originals) =
            await CreatePreviewAsync(root, identified: true);
        string original = await File.ReadAllTextAsync(originalPath);
        Assert(preview.CanPrepare &&
               preview.ProposedCriterionSuccession.Count == 2 &&
               preview.CriteriaForReview.Count == 2,
            "A source-owned B intention must offer two independently mapped CA.");
        ProjectRequirementSuccessorTransactionService transactions = new(root);
        bool refused = false;
        try
        {
            await transactions.ExecuteAsync(preview, approved: true);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Assert(refused && (await transactions.ListHistoryAsync()).Count == 0,
            "No B with CA may execute without per-criterion human consent.");
        refused = false;
        try
        {
            await transactions.ExecuteAsync(preview, approved: true,
                approvedCriteriaIds: [originals[0]],
                criteriaReviewRationale: "Only one CA checked.");
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Assert(refused && (await transactions.ListHistoryAsync()).Count == 0,
            "One checked CA never approves a second CA.");
        refused = false;
        try
        {
            await transactions.ExecuteAsync(preview, approved: true,
                approvedCriteriaIds: originals);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Assert(refused && (await transactions.ListHistoryAsync()).Count == 0,
            "CA succession always records an explicit written rationale.");

        ProjectRequirementSuccessorJournal completed =
            await transactions.ExecuteAsync(preview, approved: true,
                approvedCriteriaIds: originals,
                criteriaReviewRationale:
                    "Two independently requalified criteria, no inherited verdict.");
        string destination = Resolve(root, completed.TargetRelativePath);
        string created = await File.ReadAllTextAsync(destination);
        ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(
            created);
        ProjectRequirementCriterionScan newCriteria =
            ProjectRequirementCriterionCodec.Inspect(created);
        Assert(completed.Phase == "completed" &&
               completed.CriterionSuccession.Count == 2 &&
               completed.CriteriaReviewedUtc is not null &&
               completed.CriteriaReviewRationale.Contains(
                   "requalified", StringComparison.Ordinal) &&
               card.Id != preview.Intent.SourceObjectId &&
               card.Id == preview.ProposedId &&
               card.Validation == ProjectRequirementValidation.Draft &&
               card.Implementation ==
                   ProjectRequirementImplementation.NotStarted &&
               card.Verification ==
                   ProjectRequirementVerification.NotVerified &&
               newCriteria.Diagnostics.Count == 0 &&
               newCriteria.Criteria.Count == 2 &&
               newCriteria.Criteria.Select(item => item.Id!.Value)
                   .SequenceEqual(completed.CriterionSuccession.Select(item =>
                       item.SuccessorId)) &&
               completed.CriterionSuccession.All(item =>
                   item.SuccessorId != item.OriginalId &&
                   item.SuccessorId == ProjectRequirementCriterionSuccessorCodec
                       .DeriveId(completed.IntentId, item.OriginalId)) &&
               !created.Contains("EVIDENCE_CANARY_123",
                   StringComparison.Ordinal) &&
               !created.Contains("Verdict source CONFIRMÉ",
                   StringComparison.Ordinal) &&
               await File.ReadAllTextAsync(originalPath) == original,
            "B must reset states, map fresh CA IDs, omit evidence and keep source.");
        ProjectRequirementSuccessorJournal undone =
            await transactions.RollbackAsync(completed.TransactionId,
                approved: true);
        Assert(undone.Phase == "rolled-back" &&
               !File.Exists(destination) &&
               await File.ReadAllTextAsync(originalPath) == original &&
               (await transactions.ListPendingAsync()).Count == 0,
            "Only the unedited B successor can be withdrawn and source is intact.");
    }

    /// <summary>Replays journal before or after the new EXI CreateNew.</summary>
    /// <param name="root">One isolated workspace.</param>
    /// <param name="checkpoint">Named simulated crash checkpoint.</param>
    /// <returns>Recovered immutable B mapping and source.</returns>
    private static async Task CheckCrashReplayAsync(
        string root, string checkpoint)
    {
        (string source, string target, string originalPath,
            ProjectRequirementHandoverPreview preview, Guid[] originals) =
            await CreatePreviewAsync(root, identified: true);
        string original = await File.ReadAllTextAsync(originalPath);
        ProjectRequirementSuccessorTransactionService crashed =
            new(root, name =>
            {
                if (name == checkpoint)
                {
                    throw new InvalidOperationException("EXI B CA crash");
                }
            });
        bool interrupted = false;
        try
        {
            await crashed.ExecuteAsync(preview, approved: true,
                approvedCriteriaIds: originals,
                criteriaReviewRationale:
                    "Both CA require independent acceptance tests.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "EXI B CA crash")
        {
            interrupted = true;
        }

        ProjectRequirementSuccessorTransactionService service = new(root);
        Assert(interrupted &&
               (await service.ListPendingAsync()).Single()
                   .CriterionSuccession.Count == 2,
            "Crash must preserve exact CA mappings inside a pending B journal.");
        Assert(await service.RecoverPendingAsync(approved: true) == 1,
            "Explicit recovery must recognize or publish the same B CA draft.");
        ProjectRequirementSuccessorJournal completed =
            (await service.ListHistoryAsync()).Single();
        string created = await File.ReadAllTextAsync(
            Resolve(root, completed.TargetRelativePath));
        Assert(completed.Phase == "completed" &&
               ProjectRequirementCriterionCodec.Inspect(created).Criteria
                   .Select(item => item.Id)
                   .SequenceEqual(completed.CriterionSuccession.Select(item =>
                       (Guid?)item.SuccessorId)) &&
               await File.ReadAllTextAsync(originalPath) == original,
            "Recovery cannot silently regenerate or edit CA identifiers.");
        await service.RollbackAsync(completed.TransactionId, approved: true);
    }

    /// <summary>Verifies both GUID and local CA-code links block B rollback.</summary>
    /// <param name="root">Isolated source/target workspace.</param>
    /// <returns>Fail-closed successor retirement checks.</returns>
    private static async Task CheckBacklinksAsync(string root)
    {
        (string source, string target, string originalPath,
            ProjectRequirementHandoverPreview preview, Guid[] originals) =
            await CreatePreviewAsync(root, identified: true);
        ProjectRequirementSuccessorTransactionService service = new(root);
        ProjectRequirementSuccessorJournal completed =
            await service.ExecuteAsync(preview, approved: true,
                approvedCriteriaIds: originals,
                criteriaReviewRationale: "Both CA confirmed as new drafts.");
        string destination = Resolve(root, completed.TargetRelativePath);
        string link = Path.Combine(root, "Applications", "CrossProject.md");
        await File.WriteAllTextAsync(link,
            "Refers to new GUID " +
            completed.CriterionSuccession[0].SuccessorId.ToString("D"));
        bool refused = false;
        try
        {
            await service.RollbackAsync(completed.TransactionId,
                approved: true);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && File.Exists(destination) &&
               (await service.ListPendingAsync()).Count == 0,
            "New CA GUID backlinks must block withdrawal before journaling.");
        File.Delete(link);
        string targetNote = Path.Combine(target, "Fiche de test.md");
        await File.WriteAllTextAsync(targetNote,
            "Validation du critère CA-02 en cours.");
        refused = false;
        try
        {
            await service.RollbackAsync(completed.TransactionId,
                approved: true);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && File.Exists(destination) &&
               (await service.ListPendingAsync()).Count == 0,
            "Local CA-xx backlinks must block withdrawal.");
        File.Delete(targetNote);
        await service.RollbackAsync(completed.TransactionId, approved: true);
    }

    /// <summary>Refuses B with source criteria lacking original durable IDs.</summary>
    /// <param name="root">Synthetic isolated workspace.</param>
    /// <returns>Pure codec and B preview refusals.</returns>
    private static async Task CheckBrokenIdentificationAsync(string root)
    {
        (string source, string target, string originalPath,
            ProjectRequirementHandoverPreview preview, Guid[] originals) =
            await CreatePreviewAsync(root, identified: false);
        string markdown = await File.ReadAllTextAsync(originalPath);
        ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(
            markdown);
        bool refused = false;
        try
        {
            ProjectRequirementCriterionSuccessorCodec.Prepare(
                markdown, card, preview.Intent.TargetProjectId,
                "EXI-0002", Guid.NewGuid(), Guid.NewGuid(),
                preview.Intent.RecordedUtc);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && !preview.CanPrepare &&
               (await new ProjectRequirementSuccessorTransactionService(root)
                   .ListHistoryAsync()).Count == 0,
            "B cannot invent provenance for a CA lacking original stable GUID.");
    }

    /// <summary>Builds a source-owned B intention with two synthetic CA titles.</summary>
    /// <param name="root">Single test workspace root.</param>
    /// <param name="identified">Whether source criteria have block GUIDs.</param>
    /// <returns>Source, recipient, original and deterministic proposed B.</returns>
    private static async Task<(string Source, string Target, string OriginalPath,
        ProjectRequirementHandoverPreview Preview, Guid[] OriginalIds)>
        CreatePreviewAsync(string root, bool identified)
    {
        string source = Path.Combine(root, "Applications", "Synthetic",
            "Projets", "Source");
        string target = Path.Combine(root, "Applications", "Synthetic",
            "Projets", "Recipient");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid appId = Guid.NewGuid();
        ProjectManifest sourceOwner = new()
        {
            Id = Guid.NewGuid(), ApplicationId = appId,
            Name = "Source", InitialComplexity = ProjectComplexity.UltraSimple
        };
        ProjectManifest recipient = sourceOwner with
        {
            Id = Guid.NewGuid(), Name = "Recipient"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(sourceOwner));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(recipient));
        ProjectRequirementRegistryService registry = new(root);
        ProjectRequirementEntry entry = await registry.CreateAsync(
            source, "Exigence à transférer avec critères");
        await registry.CreateAsync(target, "Autre exigence");
        string file = Path.Combine(source, "Documentation", "Exigences",
            entry.Card.Reference + ".md");
        string original = await File.ReadAllTextAsync(file);
        string newline = original.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        original = original.Replace("## Dépendances et preuves",
            "### CA-01 — Contrôle source" + newline +
            (identified ? MarkdownBlockIdentityCodec.FormatMarker(ids[0]) +
                newline : "") +
            "EVIDENCE_CANARY_123 : résultat de campagne." + newline + newline +
            "### CA-02 — Confirmation source" + newline +
            (identified ? MarkdownBlockIdentityCodec.FormatMarker(ids[1]) +
                newline : "") +
            "Verdict source CONFIRMÉ." + newline + newline +
            "## Dépendances et preuves", StringComparison.Ordinal);
        await File.WriteAllTextAsync(file, original);
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverPreview candidate = await preflight.PrepareAsync(
            source, target, entry.Card.Id,
            ProjectHandoverMethod.LinkedSuccessor);
        ProjectHandoverIntentService intents = new(root);
        ProjectHandoverIntentSnapshot saved = await intents.RecordIntentAsync(
            candidate, await intents.ReadAsync(source), "Reviewer",
            "New CA GUIDs require new independent qualification.");
        ProjectRequirementHandoverPreview reviewed =
            await new ProjectRequirementHandoverPreviewService(root)
                .PrepareAsync(source, saved.Ledger.Intents.Single().Id);
        return (source, target, file, reviewed, ids);
    }

    /// <summary>Resolves canonical test paths without changing content.</summary>
    /// <param name="root">Disposable workspace root.</param>
    /// <param name="relative">One approved workspace-relative path.</param>
    /// <returns>Physical file path.</returns>
    private static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Signals a specific violated criterion succession rule.</summary>
    /// <param name="condition">Expected safety contract.</param>
    /// <param name="message">Failure diagnostics.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
