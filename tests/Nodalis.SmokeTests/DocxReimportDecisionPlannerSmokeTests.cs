using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxReimportDecisionPlannerSmokeTests
{
    /// <summary>
    /// Ensures dry-runs preserve outside notes, reject merged/edited anchors,
    /// reject bad choices and never perform a disk write.
    /// </summary>
    public static void Run()
    {
        string baseline = "## Tests\n\nCas initial";
        string nextWord = "## Tests\n\nCas actualisé Word";
        string originalDestination = "# Import depuis spec.docx\n\n" +
            baseline + "\n\n## Notes locales\nNe pas effacer";
        DocxImportManifestFragment original = Fragment(
            "Projet/Tests/import.md", baseline, originalDestination);

        DocxReimportReviewedFragment candidate = new DocxReimportReviewedFragment
        {
            Association = new DocxReimportSectionAssociation
            {
                FragmentId = original.FragmentId,
                PreviousHeading = "Tests",
                PreviousSectionIndex = 0,
                CurrentSectionIndex = 0,
                Kind = DocxReimportAssociationKind.Manual
            },
            Change = new DocxReimportChange
            {
                FragmentId = original.FragmentId,
                DestinationRelativePath = original.DestinationRelativePath,
                Kind = DocxReimportChangeKind.WordChanged,
                WordChanged = true
            },
            BaseMarkdown = baseline,
            LatestWordMarkdown = nextWord,
            CurrentNodalisMarkdown = originalDestination
        };
        DocxImportManifest history = Manifest([original]);
        DocxReimportReview review = Review(history, [candidate]);

        DocxReimportDecisionPlan noDecision =
            DocxReimportDecisionPlanner.Build(review, history, []);
        Assert(!noDecision.AllFragmentsResolved && noDecision.Writes.Count == 0,
            "Word changes without explicit choices must not generate writes.");

        DocxReimportResolution acceptWord = new DocxReimportResolution
        {
            FragmentId = original.FragmentId,
            Action = DocxReimportResolutionAction.UseWord
        };
        DocxReimportDecisionPlan preview =
            DocxReimportDecisionPlanner.Build(review, history, [acceptWord]);
        Assert(preview.ReviewComplete && preview.Writes.Count == 1 &&
               preview.Writes[0].ProjectedMarkdown ==
                   originalDestination.Replace(baseline, nextWord,
                       StringComparison.Ordinal) &&
               preview.Writes[0].ProjectedMarkdown.Contains(
                   "## Notes locales\nNe pas effacer", StringComparison.Ordinal) &&
               preview.Writes[0].ExpectedCurrentSha256 ==
                   DocxImportManifestStore.ComputeSha256(originalDestination),
            "Word replacements may change only the exact imported fragment and must preserve the Markdown surroundings.");

        DocxReimportDecisionPlan manual = DocxReimportDecisionPlanner.Build(
            review, history,
            [
                new DocxReimportResolution
                {
                    FragmentId = original.FragmentId,
                    Action = DocxReimportResolutionAction.ManualMerge,
                    ManualMarkdown = "## Tests\n\nCas Word avec correction manuelle"
                }
            ]);
        Assert(manual.ReviewComplete && manual.Writes.Count == 1 &&
               manual.Writes[0].ProjectedMarkdown.Contains(
                   "correction manuelle", StringComparison.Ordinal),
            "A complete manual fragment can be previewed before any write.");

        DocxReimportDecisionPlan keep = DocxReimportDecisionPlanner.Build(
            review, history,
            [
                new DocxReimportResolution
                {
                    FragmentId = original.FragmentId,
                    Action = DocxReimportResolutionAction.KeepNodalis
                }
            ]);
        Assert(keep.ReviewComplete && keep.Writes.Count == 0,
            "Explicitly keeping Nodalis may never write.");

        string userModified = originalDestination.Replace(
            "Cas initial", "Cas révisé localement", StringComparison.Ordinal);
        DocxReimportReview concurrent = Review(history,
        [
            candidate with
            {
                CurrentNodalisMarkdown = userModified,
                RequiresExplicitResolution = true
            }
        ]);
        DocxReimportDecisionPlan blocked = DocxReimportDecisionPlanner.Build(
            concurrent, history, [acceptWord]);
        Assert(!blocked.AllFragmentsResolved && blocked.Writes.Count == 0,
            "A locally rewritten fragment must never be replaced using a stale anchor.");

        DocxImportManifestFragment merged = Fragment(
            original.DestinationRelativePath, "## Recette\n\nCas B",
            originalDestination);
        DocxImportManifest shared = Manifest([original, merged]);
        DocxReimportReview sharedReview = Review(shared,
        [
            candidate,
            candidate with
            {
                Association = candidate.Association with
                {
                    FragmentId = merged.FragmentId,
                    PreviousSectionIndex = 1
                },
                Change = candidate.Change with { FragmentId = merged.FragmentId },
                BaseMarkdown = merged.BaseMarkdown
            }
        ]);
        DocxReimportDecisionPlan mergedPlan =
            DocxReimportDecisionPlanner.Build(sharedReview, shared, [acceptWord]);
        Assert(mergedPlan.Writes.Count == 0 &&
               mergedPlan.Lines.Any(line => !line.Resolved &&
                   line.Diagnostic.Contains("partagent", StringComparison.Ordinal)),
            "A Word fragment in a multi-section destination must not trigger a whole-file overwrite.");

        DocxReimportDecisionPlan deleted = DocxReimportDecisionPlanner.Build(
            Review(history,
            [
                candidate with
                {
                    Association = candidate.Association with
                    {
                        CurrentSectionIndex = null,
                        Kind = DocxReimportAssociationKind.ConfirmedMissingFromWord
                    },
                    LatestWordMarkdown = null
                }
            ]), history,
            [
                new DocxReimportResolution
                {
                    FragmentId = original.FragmentId,
                    Action = DocxReimportResolutionAction.KeepHistorical
                }
            ]);
        Assert(deleted.ReviewComplete && deleted.Writes.Count == 0,
            "A section deleted in Word remains historic Nodalis content.");

        bool duplicatesRejected = false;
        try
        {
            DocxReimportDecisionPlanner.Build(review, history,
                [acceptWord, acceptWord]);
        }
        catch (InvalidDataException)
        {
            duplicatesRejected = true;
        }

        Assert(duplicatesRejected, "Duplicate decisions must be refused.");
    }

    /// <summary>Constructs a deterministic previous source fragment.</summary>
    private static DocxImportManifestFragment Fragment(
        string destination, string snapshot, string fullDestination) =>
        new DocxImportManifestFragment
        {
            FragmentId = Guid.NewGuid(),
            SourceSectionIndex = 0,
            SourceHeading = "Tests",
            TargetSection = "Tests",
            DestinationRelativePath = destination,
            FragmentSha256 = DocxImportManifestStore.ComputeSha256(snapshot),
            BaseMarkdown = snapshot,
            DestinationSha256 = DocxImportManifestStore.ComputeSha256(fullDestination)
        };

    /// <summary>Creates an immutable test import history.</summary>
    private static DocxImportManifest Manifest(List<DocxImportManifestFragment> fragments) =>
        new DocxImportManifest
        {
            ImportId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceFileName = "spec.docx",
            SourceSha256 = new string('a', 64),
            SourceCopyRelativePath = "Imports/Sources/spec.docx",
            Fragments = fragments
        };

    /// <summary>Creates a read-only review for an import history.</summary>
    private static DocxReimportReview Review(
        DocxImportManifest history, List<DocxReimportReviewedFragment> fragments) =>
        new DocxReimportReview
        {
            PreviousImportId = history.ImportId,
            ProjectId = history.ProjectId,
            Matching = new DocxReimportMatchReport
            {
                Associations = fragments.Select(fragment =>
                    fragment.Association).ToList()
            },
            Fragments = fragments
        };

    /// <summary>Fails a regression when a proposed write loses user data.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
