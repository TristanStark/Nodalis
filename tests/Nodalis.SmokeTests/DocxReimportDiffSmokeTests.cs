using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxReimportDiffSmokeTests
{
    /// <summary>
    /// Covers the conflict matrix while ensuring no local files are touched.
    /// </summary>
    public static void Run()
    {
        string original = "## Tests\n\nCas initial";
        string updated = "## Tests\n\nCas nouveau";
        DocxImportManifestFragment baseline = new DocxImportManifestFragment
        {
            FragmentId = Guid.NewGuid(),
            SourceSectionIndex = 4,
            HeadingBlockIndex = 12,
            SourceHeading = "Tests",
            TargetSection = "Tests",
            DestinationRelativePath = "Applications/A/Projets/P/Tests/import.md",
            SelectedBlockIndexes = [12, 13],
            BaseMarkdown = original,
            FragmentSha256 = DocxImportManifestStore.ComputeSha256(original),
            DestinationSha256 = DocxImportManifestStore.ComputeSha256(original)
        };

        Assert(DocxReimportDiffEngine.Compare(baseline, original, original).Kind ==
               DocxReimportChangeKind.Unchanged,
            "Identical source, base and local destination are unchanged.");
        Assert(DocxReimportDiffEngine.Compare(baseline, updated, original).Kind ==
               DocxReimportChangeKind.WordChanged,
            "Only the updated Word fragment should be marked WordChanged.");
        Assert(DocxReimportDiffEngine.Compare(baseline, original, original + "\nNote locale").Kind ==
               DocxReimportChangeKind.NodalisChanged,
            "An extra Nodalis note must be protected from the next import.");

        DocxReimportChange both = DocxReimportDiffEngine.Compare(
            baseline, updated, original + "\nNote locale");
        Assert(both.Kind == DocxReimportChangeKind.BothChanged &&
               both.RequiresExplicitResolution && both.WordChanged && both.NodalisChanged,
            "Concurrent changes must require an explicit decision.");

        DocxReimportChange wordDeleted = DocxReimportDiffEngine.Compare(
            baseline, null, original);
        Assert(wordDeleted.Kind == DocxReimportChangeKind.WordDeleted &&
               wordDeleted.RequiresExplicitResolution,
            "A removed Word section must not delete Nodalis content.");

        DocxReimportChange localMissing = DocxReimportDiffEngine.Compare(
            baseline, original, null);
        Assert(localMissing.Kind == DocxReimportChangeKind.DestinationMissing &&
               localMissing.RequiresExplicitResolution,
            "A missing Nodalis file must never be recreated without approval.");

        DocxReimportChange moved = DocxReimportDiffEngine.Compare(
            baseline, original, original, "Tests renommés", 17);
        Assert(moved.Kind == DocxReimportChangeKind.SourceMovedOrRenamed &&
               moved.SourceStructureChanged && moved.RequiresExplicitResolution,
            "A renamed/moved section cannot be silently matched by its old index.");

        DocxReimportChange renamedAndEdited = DocxReimportDiffEngine.Compare(
            baseline, updated, original, "Recette");
        Assert(renamedAndEdited.Kind == DocxReimportChangeKind.WordChanged &&
               renamedAndEdited.RequiresExplicitResolution,
            "Changed content with a new title still requires explicit identity confirmation.");

        DocxImportManifestFragment mergedDestination = baseline with
        {
            DestinationSha256 = DocxImportManifestStore.ComputeSha256(
                "# Import\n\n" + original + "\n\n## Autre section\n\nTexte")
        };
        DocxReimportChange otherFragmentEdited = DocxReimportDiffEngine.Compare(
            mergedDestination, original,
            "# Import\n\n" + original + "\n\n## Autre section\n\nTexte modifié");
        Assert(otherFragmentEdited.Kind == DocxReimportChangeKind.NodalisChanged,
            "Editing any part of a merged Nodalis destination must prevent a false unchanged status.");
    }

    /// <summary>Fails the smoke suite when a conflict is misclassified.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
