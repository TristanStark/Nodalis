using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Fictional, explicit baseline/current/proposed field-update regressions.</summary>
internal static class SfdApprovedFieldUpdateSmokeTests
{
    /// <summary>
    /// Verifies frozen baseline, 3-way conflict classification, separate
    /// field decisions, revision CAS, replay, and preservation of manual text.
    /// </summary>
    /// <param name="root">Disposable test parent.</param>
    /// <returns>Completed synthetic regression checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-three-way-updates");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync(
            "Démonstration fictive des versions");
        string app = Path.Combine(workspace, "Applications", "Fictive");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Fictive",
            DisplayName = "Fictive",
            ParentDirectory = app
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet SFD fictif",
                Complexity = ProjectComplexity.Simple,
                Kind = ProjectKind.WithSpecifications,
                Target = target
            });
        ProjectCreationResult foreign = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "SFD différent",
                Complexity = ProjectComplexity.Simple,
                Kind = ProjectKind.WithSpecifications,
                Target = target
            });

        SfdDocumentRegistryService documents = new(workspace);
        SfdDocumentEntry process = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process,
            "Recevoir une demande fictive");
        SfdDocumentEntry activity = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Vérifier une donnée fictive", process.Card.Id);
        SfdDocumentEntry foreignProcess = await documents.CreateAsync(
            foreign.ProjectDirectory, SfdDocumentKind.Process,
            "Traiter une autre donnée fictive");
        SfdImportReviewService review = new(workspace);
        SfdDocumentBaselineService baselines = new(workspace);
        SfdApprovedFieldUpdateService publisher = new(workspace);
        string proposed = Json("businessProcess", process.Card.Id,
            "Gérer les demandes fictives", "Description IA fictive et testable.");

        SfdImportReviewSnapshot first = await review.ReviewAsync(
            project.ProjectDirectory, proposed);
        SfdImportReviewItem beforeBase = first.Items.Single();
        Require(beforeBase.Status == SfdImportReviewStatus.UpdateCandidate &&
            beforeBase.ThreeWay is { HasBaseline: false } &&
            beforeBase.ExistingSha256?.Length == 64,
            "Read-only review must not invent a baseline for PM updates.");
        bool withoutBaseRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                proposed, first, "update-fictif", true, false);
        }
        catch (InvalidDataException)
        {
            withoutBaseRefused = true;
        }

        Require(withoutBaseRefused,
            "A 3-way update must refuse to publish without a captured baseline.");

        SfdDocumentBaseline baseline = await baselines.CaptureAsync(
            project.ProjectDirectory, process.Card.Id, beforeBase.ExistingSha256!);
        Require(baseline.TargetId == process.Card.Id &&
            baseline.BaseSha256 == beforeBase.ExistingSha256 &&
            baseline.Markdown.Contains("## Finalité et périmètre",
                StringComparison.Ordinal) &&
            (await baselines.ReadAsync(project.ProjectDirectory,
                process.Card.Id))?.BaseSha256 == baseline.BaseSha256,
            "User-authorized baseline capture must preserve exact source bytes.");

        SfdImportReviewSnapshot frozen = await review.ReviewAsync(
            project.ProjectDirectory, proposed);
        SfdThreeWayPreview threeWay = frozen.Items.Single().ThreeWay!;
        Require(threeWay.HasBaseline && !threeWay.HasConflict &&
            threeWay.Fields.Count == 2 &&
            threeWay.Fields.All(field =>
                field.Status == SfdThreeWayFieldStatus.ProposedChange),
            "Title and AI-draft description must be independently proposed changes.");

        string original = await File.ReadAllTextAsync(process.AbsolutePath);
        SfdApprovedFieldUpdateResult titleOnly = await publisher.PublishAsync(
            project.ProjectDirectory, proposed, frozen, "update-fictif",
            acceptTitle: true, acceptDescription: false);
        string afterTitle = await File.ReadAllTextAsync(process.AbsolutePath);
        SfdDocumentCard renamed = SfdDocumentMarkdownCodec.Parse(afterTitle);
        Require(!titleOnly.AlreadyApplied &&
            titleOnly.TargetId == process.Card.Id &&
            renamed.Title == "Gérer les demandes fictives" &&
            renamed.Reference == process.Card.Reference &&
            renamed.Id == process.Card.Id &&
            renamed.ParentId == process.Card.ParentId &&
            renamed.Certainty == process.Card.Certainty &&
            afterTitle.Contains("## Finalité et périmètre",
                StringComparison.Ordinal) &&
            !afterTitle.Contains("Description IA fictive et testable.",
                StringComparison.Ordinal),
            "Title-only approval may not change description, GUID or manual Markdown.");

        SfdApprovedFieldUpdateResult repeatedTitle = await publisher.PublishAsync(
            project.ProjectDirectory, proposed, frozen, "update-fictif",
            acceptTitle: true, acceptDescription: false);
        Require(repeatedTitle.AlreadyApplied &&
            await File.ReadAllTextAsync(process.AbsolutePath) == afterTitle,
            "Replaying identical title decision must not apply it twice.");

        SfdImportReviewSnapshot afterTitleReview = await review.ReviewAsync(
            project.ProjectDirectory, proposed);
        Require(afterTitleReview.Items.Single().ThreeWay!.Fields.Single(field =>
                field.Name == "title").Status ==
                    SfdThreeWayFieldStatus.AlreadyCurrent &&
            afterTitleReview.Items.Single().ThreeWay!.Fields.Single(field =>
                field.Name == "description").Status ==
                    SfdThreeWayFieldStatus.ProposedChange,
            "Three-way comparison must distinguish already-applied and pending fields.");

        SfdApprovedFieldUpdateResult descriptionOnly = await publisher.PublishAsync(
            project.ProjectDirectory, proposed, afterTitleReview, "update-fictif",
            acceptTitle: false, acceptDescription: true);
        string afterDescription = await File.ReadAllTextAsync(process.AbsolutePath);
        Require(!descriptionOnly.AlreadyApplied &&
            SfdDocumentMarkdownCodec.Parse(afterDescription).Title ==
                "Gérer les demandes fictives" &&
            afterDescription.Contains("## Description proposée par IA — à qualifier",
                StringComparison.Ordinal) &&
            afterDescription.Contains("> Description IA fictive et testable.",
                StringComparison.Ordinal),
            "Description-only decision must preserve the separately approved title.");

        SfdImportReviewSnapshot completed = await review.ReviewAsync(
            project.ProjectDirectory, proposed);
        Require(completed.Items.Single().ThreeWay!.Fields.All(field =>
                field.Status == SfdThreeWayFieldStatus.AlreadyCurrent),
            "Both accepted fields must become AlreadyCurrent against the same base.");

        // A non-managed user section may change after the baseline; the
        // three-way diff can still safely propose untouched title/AI fields.
        string withManualNotes = afterDescription +
            "\n## Notes humaines non gérées par l'IA\n" +
            "Ce passage doit être conservé lors des éditions ultérieures.\n";
        await File.WriteAllTextAsync(process.AbsolutePath, withManualNotes);
        string revised = Json("businessProcess", process.Card.Id,
            "Traiter les demandes fictives vérifiées", "Nouvelle description fictive.");
        SfdImportReviewSnapshot reviewedManual = await review.ReviewAsync(
            project.ProjectDirectory, revised);
        Require(reviewedManual.Items.Single().ThreeWay!.Fields.All(field =>
                field.Status == SfdThreeWayFieldStatus.Conflict),
            "Independent changes to both fields must show genuine three-way conflicts.");

        bool conflictRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                revised, reviewedManual, "update-fictif", true, true);
        }
        catch (InvalidDataException)
        {
            conflictRefused = true;
        }

        Require(conflictRefused &&
            (await File.ReadAllTextAsync(process.AbsolutePath))
                .Contains("Notes humaines non gérées", StringComparison.Ordinal),
            "Conflicting AI proposal must never overwrite human-edited fields.");

        // A second ACT receives a fresh explicit baseline; an unrelated
        // human-added section must survive an approved description change.
        string activityProposal = Json("businessActivity", activity.Card.Id,
            activity.Card.Title, "Description contrôlée pour une ACT.",
            process.Card.Id);
        SfdImportReviewSnapshot actInitial = await review.ReviewAsync(
            project.ProjectDirectory, activityProposal);
        await baselines.CaptureAsync(project.ProjectDirectory,
            activity.Card.Id, actInitial.Items.Single().ExistingSha256!);
        await File.AppendAllTextAsync(activity.AbsolutePath,
            "\n## Journal métier écrit par un humain\n" +
            "Contenu hors section IA, ne jamais supprimer.\n");
        SfdImportReviewSnapshot actReview = await review.ReviewAsync(
            project.ProjectDirectory, activityProposal);
        Require(actReview.Items.Single().ThreeWay!.Fields.Single(field =>
                field.Name == "description").Status ==
                    SfdThreeWayFieldStatus.ProposedChange,
            "A change outside managed fields must not be a false conflict.");
        SfdApprovedFieldUpdateResult actUpdated = await publisher.PublishAsync(
            project.ProjectDirectory, activityProposal, actReview,
            "update-fictif", false, true);
        Require(!actUpdated.AlreadyApplied &&
            File.ReadAllText(activity.AbsolutePath).Contains(
                "Journal métier écrit par un humain", StringComparison.Ordinal) &&
            SfdDocumentMarkdownCodec.Parse(
                File.ReadAllText(activity.AbsolutePath)).ParentId ==
                process.Card.Id,
            "A safe ACT description edit must preserve human notes and PM parent.");

        // A previous reviewed SHA must be rejected after external edit.
        SfdImportReviewSnapshot stale = await review.ReviewAsync(
            project.ProjectDirectory, Json("businessActivity", activity.Card.Id,
                "Activité fictive renommée", "Suite factice", process.Card.Id));
        await File.AppendAllTextAsync(activity.AbsolutePath,
            "\n## Modification concurrente fictive\n");
        bool staleRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                Json("businessActivity", activity.Card.Id,
                    "Activité fictive renommée", "Suite factice",
                    process.Card.Id),
                stale, "update-fictif", true, false);
        }
        catch (InvalidDataException)
        {
            staleRefused = true;
        }

        Require(staleRefused,
            "External edits between review and update must invalidate the SHA plan.");

        string foreignUpdate = Json("businessProcess",
            foreignProcess.Card.Id, "Titre dangereux inter-projet",
            "Ne jamais publier ailleurs.");
        SfdImportReviewSnapshot foreignReview = await review.ReviewAsync(
            project.ProjectDirectory, foreignUpdate);
        Require(foreignReview.Items.Single().Status ==
            SfdImportReviewStatus.Rejected,
            "Updates to another project's GUID must remain rejected.");
        // Literal HTML-like values and ampersands must round-trip through
        // the inert, AI-owned Markdown quote without creating false reimport
        // differences or executable HTML.
        SfdDocumentEntry special = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process,
            "Règle de démonstration des caractères spéciaux");
        string specialJson = Json("businessProcess", special.Card.Id,
            special.Card.Title, "Valeur <client> & littéral &lt;");
        SfdImportReviewSnapshot specialBase = await review.ReviewAsync(
            project.ProjectDirectory, specialJson);
        await baselines.CaptureAsync(project.ProjectDirectory,
            special.Card.Id, specialBase.Items.Single().ExistingSha256!);
        SfdImportReviewSnapshot specialReview = await review.ReviewAsync(
            project.ProjectDirectory, specialJson);
        await publisher.PublishAsync(project.ProjectDirectory, specialJson,
            specialReview, "update-fictif", false, true);
        string specialMarkdown = await File.ReadAllTextAsync(
            special.AbsolutePath);
        Require(specialMarkdown.Contains("&lt;client&gt; &amp;",
                StringComparison.Ordinal) &&
            (await review.ReviewAsync(project.ProjectDirectory, specialJson))
                .Items.Single().ThreeWay!.Fields.Single(entry =>
                    entry.Name == "description").Status ==
                    SfdThreeWayFieldStatus.AlreadyCurrent,
            "Escaped AI draft Markdown must preserve literal ampersands on rereview.");

        // Two managed description headings are ambiguous; never choose a
        // section arbitrarily and erase the other's human contents.
        await File.AppendAllTextAsync(special.AbsolutePath,
            "\n## Description proposée par IA — à qualifier\n\n> doublon\n");
        bool duplicateSectionRefused = false;
        try
        {
            await review.ReviewAsync(project.ProjectDirectory, specialJson);
        }
        catch (InvalidDataException)
        {
            duplicateSectionRefused = true;
        }

        Require(duplicateSectionRefused,
            "Duplicate AI-managed description sections must block unsafe diffing.");

        Require(original != afterTitle &&
            (await baselines.ReadAsync(project.ProjectDirectory,
                process.Card.Id))?.Markdown == original,
            "Frozen baseline must never be updated by later approvals.");
    }

    /// <summary>Builds strict fictional SFD 01 update JSON.</summary>
    /// <param name="kind">PM or ACT role.</param>
    /// <param name="id">Existing target GUID.</param>
    /// <param name="title">Proposed title.</param>
    /// <param name="description">Proposed AI-owned section.</param>
    /// <param name="parentId">Exact pre-existing parent for ACT.</param>
    /// <returns>External-response-shaped synthetic JSON.</returns>
    private static string Json(string kind, Guid id, string title,
        string description, Guid? parentId = null) =>
        JsonSerializer.Serialize(new
        {
            schema = SfdImportContract.Schema,
            step = "sfd-01",
            runId = "revision-fictive",
            proposals = new[]
            {
                new
                {
                    clientKey = "update-fictif",
                    kind,
                    operation = "update",
                    certainty = "toConfirm",
                    title,
                    description,
                    targetId = id,
                    parentKey = parentId?.ToString("D"),
                    sourceKeys = Array.Empty<string>(),
                    activityKeys = Array.Empty<string>(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = Array.Empty<object>()
                }
            }
        });

    /// <summary>Checks one fake project-state invariant.</summary>
    /// <param name="condition">Expected state.</param>
    /// <param name="message">Diagnostic on mismatch.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
