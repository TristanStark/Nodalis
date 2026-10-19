using System.IO.Compression;
using System.Text;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxHeadingHierarchySmokeTests
{
    /// <summary>
    /// Parses a synthetic DOCX package to test inheritance, direct outlines and reversible suggestions.
    /// </summary>
    public static async Task RunAsync(string root)
    {
        string path = Path.Combine(root, "docx-19-headings.docx");
        const string styles = """
            <?xml version="1.0" encoding="utf-8"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="Heading2">
                <w:name w:val="Heading 2"/>
                <w:pPr><w:outlineLvl w:val="1"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="CustomA">
                <w:name w:val="Architecture personnalisée"/>
                <w:basedOn w:val="Heading2"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="CustomB">
                <w:name w:val="Sous-partie personnalisée"/>
                <w:basedOn w:val="CustomA"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Suppressed">
                <w:name w:val="Heading 1"/>
                <w:basedOn w:val="Heading2"/>
                <w:pPr><w:outlineLvl w:val="9"/></w:pPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="CycleA">
                <w:basedOn w:val="CycleB"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="CycleB">
                <w:basedOn w:val="CycleA"/>
              </w:style>
            </w:styles>
            """;
        const string document = """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="CustomB"/></w:pPr><w:r><w:t>Architecture</w:t></w:r></w:p>
                <w:p><w:r><w:t>Paragraphe descriptif</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t>Tests principaux</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading2"/><w:outlineLvl w:val="9"/></w:pPr><w:r><w:t>Corps forcé</w:t></w:r></w:p>
                <w:p><w:r><w:rPr><w:b/><w:sz w:val="36"/></w:rPr><w:t>Recette candidats</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="CycleA"/></w:pPr><w:r><w:t>Style cyclique</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Suppressed"/></w:pPr><w:r><w:t>Style explicitement non-titre</w:t></w:r></w:p>
              </w:body>
            </w:document>
            """;

        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "word/styles.xml", styles);
            WriteEntry(zip, "word/document.xml", document);
        }

        ParsedDocxDocument parsed = await new DocxParser().ParseAsync(path);
        IReadOnlyList<DocxBlock> blocks = parsed.Blocks;
        Assert(blocks.Count == 7, "Source paragraphs must not disappear.");
        Assert(blocks[0].Paragraph?.HeadingLevel == 2,
            "Custom Word styles must inherit outline levels through basedOn chains.");
        Assert(blocks[2].Paragraph?.HeadingLevel == 1,
            "Direct paragraph outlineLvl must override all styles.");
        Assert(blocks[3].Paragraph?.HeadingLevel is null,
            "Direct body-text outlineLvl=9 must suppress the heading style.");
        Assert(blocks[4].Paragraph?.HeadingLevel is null &&
               blocks[4].Paragraph?.SuggestedHeadingLevel == 1 &&
               !string.IsNullOrWhiteSpace(blocks[4].Paragraph?.HeadingSuggestionReason),
            "An unstyled bold/large paragraph may only be suggested, not promoted.");
        Assert(blocks[5].Paragraph?.HeadingLevel is null,
            "Cyclic basedOn references must not introduce invalid headings.");
        Assert(blocks[6].Paragraph?.HeadingLevel is null,
            "An explicit style outlineLvl=9 must suppress even a Heading-style name.");

        ImportDocumentTree tree = DocxStructuredImportReader.FromParsed(
            parsed, Path.GetFileName(path), new string('C', 64));
        Assert(tree.Roots.Count == 2 &&
               tree.Roots[0].HeadingLevel == 2 &&
               tree.Roots[1].HeadingLevel == 1,
            "Parsed heading hierarchy must be projected without automatic heuristics.");
        Assert(tree.Roots[1].Children.Any(child =>
                child.SuggestedHeadingLevel == 1 &&
                child.Kind == ImportDocumentNodeKind.Paragraph),
            "Suggestion evidence must remain available for the future manual UI.");
    }

    /// <summary>
    /// Adds a tiny XML package member.
    /// </summary>
    private static void WriteEntry(ZipArchive archive, string name, string xml)
    {
        using StreamWriter writer = new StreamWriter(
            archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    /// <summary>
    /// Fails the regression suite on an invalid behavior.
    /// </summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
