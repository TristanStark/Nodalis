using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxFineSelectionSmokeTests
{
    /// <summary>
    /// Verifies nested DOCX section mapping and deterministic rendering of a fine-grained block selection.
    /// </summary>
    /// <param name="root">The initialized smoke-test workspace root.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        global::Nodalis.Core.Importing.DocxBlock sectionHeading =
            Paragraph(
                "Documentation technique",
                headingLevel: 1);
        global::Nodalis.Core.Importing.DocxBlock nestedHeading =
            Paragraph(
                "Sous-section",
                headingLevel: 2);
        global::Nodalis.Core.Importing.DocxBlock emptyParagraph =
            Paragraph(
                string.Empty);
        global::Nodalis.Core.Importing.DocxBlock listItem =
            Paragraph(
                "Point imbriqué",
                isListItem: true,
                listLevel: 1);
        global::Nodalis.Core.Importing.DocxBlock table =
            new DocxBlock
            {
                Kind =
                    DocxBlockKind.Table,
                Table =
                    new DocxTable
                    {
                        Rows =
                        [
                            new DocxTableRow
                            {
                                Cells =
                                [
                                    new DocxTableCell
                                    {
                                        Text =
                                            "Clé"
                                    },
                                    new DocxTableCell
                                    {
                                        Text =
                                            "Valeur"
                                    }
                                ]
                            },
                            new DocxTableRow
                            {
                                Cells =
                                [
                                    new DocxTableCell
                                    {
                                        Text =
                                            "A"
                                    },
                                    new DocxTableCell
                                    {
                                        Text =
                                            "B"
                                    }
                                ]
                            }
                        ]
                    }
            };
        global::Nodalis.Core.Importing.DocxBlock nextSectionHeading =
            Paragraph(
                "Tests / Recette",
                headingLevel: 1);
        global::Nodalis.Core.Importing.DocxBlock nextSectionBody =
            Paragraph(
                "Cas nominal");

        global::Nodalis.Core.Importing.ParsedDocxDocument document =
            new ParsedDocxDocument
            {
                Blocks =
                [
                    sectionHeading,
                    nestedHeading,
                    emptyParagraph,
                    listItem,
                    table,
                    nextSectionHeading,
                    nextSectionBody
                ]
            };

        global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer analyzer =
            new DocxImportAnalyzer(
                root);

        global::Nodalis.Core.Importing.DocxImportAnalysis analysis =
            await analyzer.AnalyzeAsync(
                document,
                "hierarchie.docx");

        global::Nodalis.Core.Importing.DocxMappedSection technical =
            analysis.MappedSections.Single(section =>
                string.Equals(
                    section.TargetSection,
                    "Technique",
                    StringComparison.OrdinalIgnoreCase));

        Assert(
            technical.Blocks.Count == 4 &&
            ReferenceEquals(
                technical.Blocks[0],
                nestedHeading) &&
            ReferenceEquals(
                technical.Blocks[1],
                emptyParagraph) &&
            ReferenceEquals(
                technical.Blocks[2],
                listItem) &&
            ReferenceEquals(
                technical.Blocks[3],
                table),
            "A nested heading, empty paragraph, list item and table must remain ordered inside the mapped parent section.");

        global::Nodalis.Core.Importing.DocxMappedSection tests =
            analysis.MappedSections.Single(section =>
                string.Equals(
                    section.TargetSection,
                    "Tests",
                    StringComparison.OrdinalIgnoreCase));

        Assert(
            tests.Blocks.Count == 1 &&
            ReferenceEquals(
                tests.Blocks[0],
                nextSectionBody),
            "A following mapped top-level heading must start a new section instead of remaining nested.");

        global::Nodalis.Core.Importing.DocxImportSectionPreview preview =
            new DocxImportSectionPreview
            {
                Index =
                    0,
                SourceHeading =
                    technical.SourceHeading,
                SuggestedTargetSection =
                    technical.TargetSection,
                BlockCount =
                    4,
                Blocks =
                [
                    PreviewBlock(
                        1,
                        nestedHeading,
                        "Titre 2",
                        "Sous-section",
                        null),
                    PreviewBlock(
                        2,
                        emptyParagraph,
                        "Paragraphe",
                        "(paragraphe vide)",
                        1),
                    PreviewBlock(
                        3,
                        listItem,
                        "Liste",
                        "Point imbriqué",
                        1),
                    PreviewBlock(
                        4,
                        table,
                        "Tableau",
                        "Tableau",
                        1)
                ],
                MarkdownPreview =
                    string.Empty
            };

        string rendered =
            DocxImportSelectionRenderer.Render(
                preview,
                new[]
                {
                    1,
                    2,
                    4
                });

        int headingPosition =
            rendered.IndexOf(
                "## Sous-section",
                StringComparison.Ordinal);
        int tablePosition =
            rendered.IndexOf(
                "| Clé | Valeur |",
                StringComparison.Ordinal);

        Assert(
            headingPosition >= 0 &&
            tablePosition > headingPosition &&
            !rendered.Contains(
                "Point imbriqué",
                StringComparison.Ordinal) &&
            rendered.Contains(
                "| --- | --- |",
                StringComparison.Ordinal),
            "Fine DOCX selection must preserve selected nested structure and a complete valid table while excluding an unselected list item.");

        string listOnly =
            DocxImportSelectionRenderer.Render(
                preview,
                new[]
                {
                    3
                });

        Assert(
            listOnly.Contains(
                "  - Point imbriqué",
                StringComparison.Ordinal) &&
            !listOnly.Contains(
                "| Clé | Valeur |",
                StringComparison.Ordinal),
            "A list item must remain individually selectable without pulling an unselected table into Markdown.");
    }

    /// <summary>
    /// Creates one paragraph block for a synthetic DOCX selection scenario.
    /// </summary>
    /// <param name="text">The paragraph text.</param>
    /// <param name="headingLevel">The optional heading level.</param>
    /// <param name="isListItem">Whether the paragraph is a list item.</param>
    /// <param name="listLevel">The optional list indentation level.</param>
    /// <returns>The paragraph block.</returns>
    private static DocxBlock Paragraph(
            string text,
            int? headingLevel = null,
            bool isListItem = false,
            int? listLevel = null) =>
            new DocxBlock
            {
                Kind =
                    DocxBlockKind.Paragraph,
                Paragraph =
                    new DocxParagraph
                    {
                        Text =
                            text,
                        HeadingLevel =
                            headingLevel,
                        IsListItem =
                            isListItem,
                        NumberingId =
                            isListItem
                                ? 1
                                : null,
                        ListLevel =
                            listLevel
                    }
            };

    /// <summary>
    /// Creates one selectable preview block for renderer verification.
    /// </summary>
    /// <param name="blockIndex">The original block index.</param>
    /// <param name="block">The source block.</param>
    /// <param name="kindLabel">The display kind.</param>
    /// <param name="displayText">The display text.</param>
    /// <param name="parentBlockIndex">The optional parent heading index.</param>
    /// <returns>The preview block.</returns>
    private static DocxImportBlockPreview PreviewBlock(
            int blockIndex,
            DocxBlock block,
            string kindLabel,
            string displayText,
            int? parentBlockIndex) =>
            new DocxImportBlockPreview
            {
                BlockIndex =
                    blockIndex,
                Block =
                    block,
                KindLabel =
                    kindLabel,
                DisplayText =
                    displayText,
                MarkdownPreview =
                    DocxMarkdownConverter.ConvertBlocks(
                        new[]
                        {
                            block
                        }),
                ParentBlockIndex =
                    parentBlockIndex
            };

    /// <summary>
    /// Throws when a fine-selection smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">The condition to verify.</param>
    /// <param name="message">The failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
