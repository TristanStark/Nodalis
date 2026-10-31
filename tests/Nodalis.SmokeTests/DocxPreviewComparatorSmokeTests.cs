using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxPreviewComparatorSmokeTests
{
    /// <summary>Verifies the original stays inspectable even when the user excludes a block.</summary>
    public static void Run()
    {
        DocxBlock one = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph
            {
                Text = "Partie conservée"
            }
        };
        DocxBlock two = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph
            {
                Text = "Partie exclue",
                Media =
                [
                    new DocxInlineMedia
                    {
                        SourceId = "linked",
                        TextOffset = 0,
                        Warning = "Ressource externe bloquée"
                    }
                ]
            }
        };
        DocxImportSectionPreview section = new DocxImportSectionPreview
        {
            Index = 0,
            SourceHeading = "Tests",
            SuggestedTargetSection = "Tests",
            BlockCount = 2,
            Blocks =
            [
                new DocxImportBlockPreview
                {
                    BlockIndex = 0, Block = one, KindLabel = "Paragraphe",
                    DisplayText = one.Paragraph!.Text, MarkdownPreview = string.Empty
                },
                new DocxImportBlockPreview
                {
                    BlockIndex = 1, Block = two, KindLabel = "Paragraphe",
                    DisplayText = two.Paragraph!.Text, MarkdownPreview = string.Empty
                }
            ],
            MarkdownPreview = string.Empty
        };

        string original = DocxOriginalSectionRenderer.Render(section);
        string selected = DocxImportSelectionRenderer.Render(section, [0]);
        if (!original.Contains("Partie exclue", StringComparison.Ordinal) ||
            !original.Contains("Ressource externe bloquée", StringComparison.Ordinal) ||
            selected.Contains("Partie exclue", StringComparison.Ordinal) ||
            !selected.Contains("Partie conservée", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Le comparateur doit rendre visibles les exclusions et avertissements.");
        }
    }
}
