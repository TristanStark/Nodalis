using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Acceptance-criteria handover regressions: only fully stable CA may move A,
/// only explicit per-CA review can authorize it, and B never clones old CA.
/// </summary>
internal static class ProjectRequirementMoveCriteriaSmokeTests
{
    /// <summary>Runs independent synthetic CA custody and link safety cases.</summary>
    /// <param name="root">Disposable workspace parent.</param>
    /// <returns>Completed test assertions.</returns>
    public static async Task RunAsync(string root)
    {
        await CheckStableMoveAsync(Path.Combine(root, "exi-ca-move"));
        await CheckCrashRecoveryAsync(Path.Combine(root, "exi-ca-crash"));
        await CheckCriterionLinksAsync(Path.Combine(root, "exi-ca-links"));
        await CheckUnsafeCriteriaAsync(Path.Combine(root, "exi-ca-unsafe"));
    }

    /// <summary>Preserves both CA GUIDs and original Markdown byte-for-byte.</summary>
    /// <param name="root">Independent test workspace.</param>
    /// <returns>Successful A and rollback with human CA review.</returns>
    private static async Task CheckStableMoveAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed, Guid[] ids) =
            await PrepareAsync(root, withCriteria: true);
        string original = await File.ReadAllTextAsync(file);
        Assert(reviewed.CanPrepare &&
               reviewed.CriteriaForReview.Count == 2 &&
               reviewed.CriteriaForReview.All(item => item.LocallyIdentified),
            "Identified CA must be visible and transferable only through A.");
        ProjectRequirementMoveTransactionService service = new(root);
        bool rejected = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected && (await service.ListHistoryAsync()).Count == 0,
            "No implicit CA approval can authorize any A write.");
        rejected = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: [ids[0]],
                criteriaReviewRationale: "Only one criterion verified.");
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected && (await service.ListHistoryAsync()).Count == 0,
            "Every CA must be reviewed independently, not just one.");
        rejected = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: ids);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected && (await service.ListHistoryAsync()).Count == 0,
            "A CA review must be justified and persisted.");

        ProjectRequirementMoveJournal completed =
            await service.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: ids,
                criteriaReviewRationale:
                    "CA-01 and CA-02 IDs checked against linked evidence.");
        string destination = Resolve(root, completed.TargetRelativePath);
        string moved = await File.ReadAllTextAsync(destination);
        ProjectRequirementCriterionScan originalCriteria =
            ProjectRequirementCriterionCodec.Inspect(original);
        ProjectRequirementCriterionScan movedCriteria =
            ProjectRequirementCriterionCodec.Inspect(moved);
        Assert(completed.Phase == "completed" &&
               completed.ReviewedCriteria.Count == 2 &&
               completed.CriteriaReviewRationale.Contains(
                   "CA-01", StringComparison.Ordinal) &&
               completed.CriteriaReviewedUtc is not null &&
               !File.Exists(file) &&
               movedCriteria.Criteria.Select(item => item.Id)
                   .SequenceEqual(originalCriteria.Criteria.Select(item => item.Id)) &&
               movedCriteria.Criteria.Select(item => item.Title)
                   .SequenceEqual(originalCriteria.Criteria.Select(item => item.Title)) &&
               movedCriteria.Criteria.All(item => item.LocallyIdentified),
            "A retains verified CA GUIDs, headings and human review history.");

        ProjectRequirementMoveJournal reversed =
            await service.RollbackAsync(completed.TransactionId, approved: true);
        Assert(reversed.Phase == "rolled-back" &&
               await File.ReadAllTextAsync(file) == original &&
               !File.Exists(destination),
            "A rollback with reviewed CA must restore exact source bytes.");
    }

    /// <summary>Proves human-approved crash recovery retains original CA IDs.</summary>
    /// <param name="root">Synthetic workspace.</param>
    /// <returns>Completed recovered transaction and rollback.</returns>
    private static async Task CheckCrashRecoveryAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed, Guid[] ids) =
            await PrepareAsync(root, withCriteria: true);
        ProjectRequirementMoveTransactionService faulty =
            new(root, checkpoint =>
            {
                if (checkpoint == "source-parked")
                {
                    throw new InvalidOperationException("Injected CA interruption");
                }
            });
        bool interrupted = false;
        try
        {
            await faulty.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: ids,
                criteriaReviewRationale: "Both CA IDs explicitly validated.");
        }
        catch (InvalidOperationException ex) when (
            ex.Message == "Injected CA interruption")
        {
            interrupted = true;
        }

        ProjectRequirementMoveTransactionService recovered = new(root);
        Assert(interrupted &&
               (await recovered.ListPendingAsync()).Single()
                   .ReviewedCriteria.Count == 2,
            "A crash must keep all approved CA identities in the journal.");
        Assert(await recovered.RecoverPendingAsync(approved: true) == 1,
            "A pending CA transfer must complete after human recovery.");
        ProjectRequirementMoveJournal completed =
            (await recovered.ListHistoryAsync()).Single();
        ProjectRequirementCriterionScan targetCriteria =
            ProjectRequirementCriterionCodec.Inspect(
                await File.ReadAllTextAsync(
                    Resolve(root, completed.TargetRelativePath)));
        Assert(targetCriteria.Criteria.Select(item => item.Id)
            .SequenceEqual(ids.Select(item => (Guid?)item)),
            "Recovered CA transfer must not regenerate stable criterion IDs.");
        await recovered.RollbackAsync(completed.TransactionId, approved: true);
    }

    /// <summary>Rejects CA GUID and local CA-xx references before or after A.</summary>
    /// <param name="root">Synthetic workspace.</param>
    /// <returns>Fail-closed link tests and reversible valid move.</returns>
    private static async Task CheckCriterionLinksAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed, Guid[] ids) =
            await PrepareAsync(root, withCriteria: true);
        ProjectRequirementMoveTransactionService service = new(root);
        string external = Path.Combine(root, "Applications", "Foreign.md");
        await File.WriteAllTextAsync(external,
            "External CA GUID " + ids[0].ToString("D"));
        bool blocked = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: ids,
                criteriaReviewRationale: "All CA identified.");
        }
        catch (InvalidDataException)
        {
            blocked = true;
        }

        Assert(blocked && File.Exists(file) &&
               (await service.ListHistoryAsync()).Count == 0,
            "Any cross-project CA GUID backlink must block an EXI A transfer.");
        File.Delete(external);
        string sourceNotes = Path.Combine(source, "Notes CA.md");
        await File.WriteAllTextAsync(sourceNotes,
            "Refer to CA-01 in our implementation.");
        blocked = false;
        try
        {
            await service.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: ids,
                criteriaReviewRationale: "Both CA identified.");
        }
        catch (InvalidDataException)
        {
            blocked = true;
        }

        Assert(blocked && File.Exists(file) &&
               (await service.ListHistoryAsync()).Count == 0,
            "Ambiguous CA-01 text in source project requires requalification.");
        File.Delete(sourceNotes);
        ProjectRequirementMoveJournal moved =
            await service.ExecuteAsync(reviewed, approved: true,
                reviewedCriteriaIds: ids,
                criteriaReviewRationale: "Both CA identifiers reviewed.");
        string link = Path.Combine(target, "Lien CA.md");
        await File.WriteAllTextAsync(link, "Evidence mentions CA-02.");
        blocked = false;
        try
        {
            await service.RollbackAsync(moved.TransactionId, approved: true);
        }
        catch (InvalidDataException)
        {
            blocked = true;
        }

        Assert(blocked &&
               (await service.ListPendingAsync()).Count == 0 &&
               File.Exists(Resolve(root, moved.TargetRelativePath)),
            "A CA backlink created after relocation must prevent rollback.");
        File.Delete(link);
        await service.RollbackAsync(moved.TransactionId, approved: true);
    }

    /// <summary>Disallows malformed criteria, B cloning and non-CA block IDs.</summary>
    /// <param name="root">Synthetic workspace.</param>
    /// <returns>Pure preflight refusal assertions.</returns>
    private static async Task CheckUnsafeCriteriaAsync(string root)
    {
        (string source, string target, string file,
            ProjectRequirementHandoverPreview reviewed, Guid[] ids) =
            await PrepareAsync(root, withCriteria: false);
        ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(
            await File.ReadAllTextAsync(file));
        string canonical = await File.ReadAllTextAsync(file);
        string newline = canonical.Contains("\r\n",
            StringComparison.Ordinal) ? "\r\n" : "\n";
        string heading = "## Dépendances et preuves";
        string noGuid = canonical.Replace(heading,
            "### CA-01 — Sans identifiant" + newline +
            "Texte test." + newline + newline + heading,
            StringComparison.Ordinal);
        bool rejected = false;
        try
        {
            ProjectRequirementHandoverCodec.Prepare(noGuid, card,
                reviewed.Intent.TargetProjectId, "EXI-0002",
                card.Id, Guid.NewGuid(), ProjectHandoverMethod.MoveOriginal);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(rejected, "Mode A must refuse criteria lacking stable GUIDs.");
        Guid criterionId = Guid.NewGuid();
        string identified = canonical.Replace(heading,
            "### CA-01 — Identifié" + newline +
            MarkdownBlockIdentityCodec.FormatMarker(criterionId) +
            newline + "Accepté." + newline + newline + heading,
            StringComparison.Ordinal);
        bool refusedB = false;
        try
        {
            ProjectRequirementHandoverCodec.Prepare(identified, card,
                reviewed.Intent.TargetProjectId, "EXI-0002",
                Guid.NewGuid(), Guid.NewGuid(),
                ProjectHandoverMethod.LinkedSuccessor);
        }
        catch (InvalidDataException)
        {
            refusedB = true;
        }

        Assert(refusedB,
            "A B successor may not clone the original stable CA IDs.");
        ProjectRequirementCriterionScan scan =
            ProjectRequirementCriterionCodec.Inspect(identified);
        Assert(scan.Criteria.Count == 1 && scan.Diagnostics.Count == 0 &&
               scan.Criteria[0].Id == criterionId,
            "Fixture must be recognized as one properly identified CA.");
        string newBlock = identified.Replace(heading,
            "### Justification source" + newline +
            MarkdownBlockIdentityCodec.FormatMarker(Guid.NewGuid()) +
            newline + "Independent source block." + newline + heading,
            StringComparison.Ordinal);
        rejected = false;
        try
        {
            ProjectRequirementHandoverCodec.Prepare(newBlock, card,
                reviewed.Intent.TargetProjectId, "EXI-0002",
                card.Id, Guid.NewGuid(), ProjectHandoverMethod.MoveOriginal);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(rejected,
            "Only specifically acknowledged CA block IDs may move in mode A.");
    }

    /// <summary>Builds synthetic projects and an immutable A intent.</summary>
    /// <param name="root">One isolated test workspace.</param>
    /// <param name="withCriteria">Creates two fully identified CA when true.</param>
    /// <returns>Human-approved intent, source file and stable CA GUIDs.</returns>
    private static async Task<(string Source, string Target, string File,
        ProjectRequirementHandoverPreview Preview, Guid[] Ids)> PrepareAsync(
        string root, bool withCriteria)
    {
        string source = Path.Combine(root, "Applications", "Synthetic",
            "Projets", "Source");
        string target = Path.Combine(root, "Applications", "Synthetic",
            "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid appId = Guid.NewGuid();
        ProjectManifest owner = new()
        {
            Id = Guid.NewGuid(), ApplicationId = appId, Name = "Source",
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        ProjectManifest recipient = owner with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(owner));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(recipient));
        ProjectRequirementRegistryService registry = new(root);
        ProjectRequirementEntry original =
            await registry.CreateAsync(source, "Exigence à critères");
        await registry.CreateAsync(target, "Exigence indépendante");
        string file = Path.Combine(source, "Documentation", "Exigences",
            original.Card.Reference + ".md");
        Guid[] ids = withCriteria ?
            [Guid.NewGuid(), Guid.NewGuid()] : [];
        if (withCriteria)
        {
            string markdown = await File.ReadAllTextAsync(file);
            string nl = markdown.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n" : "\n";
            markdown = markdown.Replace("## Dépendances et preuves",
                "### CA-01 — Premier critère" + nl +
                MarkdownBlockIdentityCodec.FormatMarker(ids[0]) + nl +
                "Résultat attendu : précis et vérifiable." + nl + nl +
                "### CA-02 — Second critère" + nl +
                MarkdownBlockIdentityCodec.FormatMarker(ids[1]) + nl +
                "Résultat attendu : indépendant." + nl + nl +
                "## Dépendances et preuves", StringComparison.Ordinal);
            await File.WriteAllTextAsync(file, markdown);
        }

        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverPreview candidate = await preflight.PrepareAsync(
            source, target, original.Card.Id, ProjectHandoverMethod.MoveOriginal);
        ProjectHandoverIntentService intents = new(root);
        ProjectHandoverIntentSnapshot recorded =
            await intents.RecordIntentAsync(candidate,
                await intents.ReadAsync(source), "Reviewer",
                "Stable CA identity review before moving original.");
        ProjectRequirementHandoverPreview review =
            await new ProjectRequirementHandoverPreviewService(root)
                .PrepareAsync(source, recorded.Ledger.Intents.Single().Id);
        Assert(review.CanPrepare,
            "Synthetic A preview should support valid stable CA. " +
            review.FormatReview());
        return (source, target, file, review, ids);
    }

    /// <summary>Maps portable source references to filesystem paths.</summary>
    /// <param name="root">Isolated workspace root.</param>
    /// <param name="relative">Canonical workspace-relative target.</param>
    /// <returns>Physical test file location.</returns>
    private static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Reports any broken invariant of the CA handover model.</summary>
    /// <param name="condition">Expected true safety invariant.</param>
    /// <param name="message">Assertion explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
