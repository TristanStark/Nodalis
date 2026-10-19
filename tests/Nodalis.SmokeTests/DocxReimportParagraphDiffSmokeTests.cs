using Nodalis.Infrastructure.Importing;

internal static class DocxReimportParagraphDiffSmokeTests
{
    /// <summary>
    /// Checks paragraph additions, removals, changed text, duplicate blocks,
    /// fenced code, Windows newlines, missing sources and safe local context.
    /// </summary>
    public static void Run()
    {
        const string baseline = "## Tests\n\nParagraphe stable\n\nParagraphe ancien";
        const string updated = "## Tests\n\nParagraphe stable\n\nParagraphe nouveau" +
            "\n\nParagraphe ajouté";
        DocxReimportParagraphComparison changed =
            DocxReimportParagraphDiffEngine.ComparePair(baseline, updated);
        Assert(changed.Available && !changed.Truncated &&
               changed.ChangedCount == 2 &&
               changed.Rows.Count(row =>
                   row.Kind == DocxReimportParagraphChangeKind.Modified) == 1 &&
               changed.Rows.Count(row =>
                   row.Kind == DocxReimportParagraphChangeKind.Added) == 1 &&
               changed.Rows.Single(row =>
                   row.Kind == DocxReimportParagraphChangeKind.Added).CurrentLine == 7,
            "Changed and added paragraphs must be separately visible with line numbers.");

        DocxReimportParagraphComparison windows =
            DocxReimportParagraphDiffEngine.ComparePair(
                baseline, baseline.Replace("\n", "\r\n"));
        Assert(windows.ChangedCount == 0 && windows.Rows.Count == 3,
            "CRLF-only changes are not substantive paragraph changes.");

        DocxReimportParagraphComparison removed =
            DocxReimportParagraphDiffEngine.ComparePair(
                baseline, "## Tests\n\nParagraphe stable");
        Assert(removed.Rows.Count(row =>
                   row.Kind == DocxReimportParagraphChangeKind.Removed) == 1,
            "Word paragraph deletions must remain explicit.");

        DocxReimportParagraphComparison duplicates =
            DocxReimportParagraphDiffEngine.ComparePair(
                "## A\n\nIdentique\n\nIdentique",
                "## A\n\nIdentique");
        Assert(duplicates.Rows.Count(row =>
                   row.Kind == DocxReimportParagraphChangeKind.Removed) == 1,
            "Repeated paragraphs must retain stable one-to-one alignment.");

        DocxReimportParagraphComparison fenced =
            DocxReimportParagraphDiffEngine.ComparePair(
                "## Technique\n\n```sql\nselect *\n\nfrom tests\n```",
                "## Technique\n\n```sql\nselect *\n\nfrom tests\n```");
        Assert(fenced.Rows.Count == 2 && fenced.ChangedCount == 0,
            "Blank lines inside Markdown fences must not split source code.");

        DocxReimportParagraphComparison limited =
            DocxReimportParagraphDiffEngine.ComparePair(
                "## A", new string('x', 300_000));
        Assert(limited.Truncated && limited.Available &&
               limited.Rows.Count == 0,
            "Large paragraphs must be truncated safely instead of performing unbounded diff.");

        DocxReimportParagraphComparison missing =
            DocxReimportParagraphDiffEngine.ComparePair(baseline, null);
        Assert(!missing.Available && !missing.Truncated,
            "Missing Word sources must not be interpreted as empty content.");

        DocxReimportReviewedFragment fragment = new DocxReimportReviewedFragment
        {
            Association = new DocxReimportSectionAssociation
            {
                FragmentId = Guid.NewGuid(),
                PreviousHeading = "Tests",
                PreviousSectionIndex = 0,
                CurrentSectionIndex = 0,
                Kind = DocxReimportAssociationKind.Manual
            },
            BaseMarkdown = baseline,
            LatestWordMarkdown = updated,
            CurrentNodalisMarkdown = "# Mon dossier\n\n" + baseline +
                "\n\n## Ma note locale\n\nNe pas effacer"
        };
        DocxReimportParagraphDiff localContext =
            DocxReimportParagraphDiffEngine.Compare(fragment);
        Assert(!localContext.LocalIncludesSurroundings &&
               localContext.Word.ChangedCount == 2 &&
               localContext.Nodalis.ChangedCount == 0,
            "Outside notes must not be misreported as edits to an unchanged fragment.");

        DocxReimportParagraphDiff editedLocally =
            DocxReimportParagraphDiffEngine.Compare(fragment with
            {
                CurrentNodalisMarkdown = "# Mon dossier\n\n" +
                    baseline.Replace("Paragraphe ancien", "Modifié en local") +
                    "\n\n## Ma note locale"
            });
        Assert(editedLocally.LocalIncludesSurroundings &&
               editedLocally.Nodalis.ChangedCount > 0,
            "Locally modified fragment must show an explicit whole-file fallback.");
    }

    /// <summary>Fails the smoke suite on misleading diff output.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
