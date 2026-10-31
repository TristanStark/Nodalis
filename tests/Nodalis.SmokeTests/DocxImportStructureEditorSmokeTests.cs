using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxImportStructureEditorSmokeTests
{
    /// <summary>
    /// Ensures that heading corrections alter the validated preview without touching the source.
    /// </summary>
    public static void Run()
    {
        DocxBlock first = Paragraph("Développement", 2);
        DocxBlock second = Paragraph("Implémentation");
        DocxBlock third = Paragraph("Sous-partie");
        DocxBlock fourth = Paragraph("Résultat attendu");

        DocxImportSectionPreview section = new DocxImportSectionPreview
        {
            Index = 0,
            SourceHeading = "Projet",
            SuggestedTargetSection = "Technique",
            BlockCount = 4,
            MarkdownPreview = string.Empty,
            Blocks =
            [
                Preview(1, first),
                Preview(2, second),
                Preview(3, third),
                Preview(4, fourth)
            ]
        };

        int? previous = DocxImportStructureEditor.ChangeHeading(section, 3, 3);
        Assert(previous is null && section.Blocks[2].Block.Paragraph?.HeadingLevel == 3,
            "Explicit title promotion must keep the chosen level.");
        Assert(section.Blocks[3].ParentBlockIndex == 3,
            "The promoted heading must own subsequent paragraphs.");
        Assert(section.Blocks[2].ParentBlockIndex == 1,
            "The promoted title must remain below the prior heading.");
        Assert(third.Paragraph?.HeadingLevel is null,
            "Correcting a preview must not mutate a parsed source block.");

        string markdown = DocxImportSelectionRenderer.Render(section, [1, 2, 3, 4]);
        Assert(markdown.Contains("### Sous-partie", StringComparison.Ordinal),
            "The final Markdown plan must reflect the corrected heading.");

        DocxImportStructureEditor.ChangeHeading(section, 3, previous);
        Assert(section.Blocks[2].Block.Paragraph?.HeadingLevel is null &&
               section.Blocks[3].ParentBlockIndex == 1,
            "Undoing a heading edit must restore the original hierarchy.");

        bool tableRejected = false;
        try
        {
            DocxImportStructureEditor.ChangeHeading(section, 999, 1);
        }
        catch (InvalidDataException)
        {
            tableRejected = true;
        }

        Assert(tableRejected, "A missing block must not be edited.");
    }

    /// <summary>Creates a fixture Word paragraph.</summary>
    private static DocxBlock Paragraph(string text, int? level = null) =>
        new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = text, HeadingLevel = level }
        };

    /// <summary>Creates a selectable fixture block.</summary>
    private static DocxImportBlockPreview Preview(int index, DocxBlock block) =>
        new DocxImportBlockPreview
        {
            BlockIndex = index,
            Block = block,
            KindLabel = block.Paragraph?.HeadingLevel is int level ? "Titre " + level : "Paragraphe",
            DisplayText = block.Paragraph?.Text ?? string.Empty,
            MarkdownPreview = string.Empty
        };

    /// <summary>Fails the smoke suite for a violated invariant.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
