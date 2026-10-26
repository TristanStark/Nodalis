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
              <w:style w:type="paragraph" w:styleId="Heading4">
                <w:name w:val="Heading 4"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="CustomFrench">
                <w:name w:val="Titre 3"/>
              </w:style>
            </w:styles>
            """;
        const string document = """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>Page de garde sans titre Word</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="CustomB"/></w:pPr><w:r><w:t>Architecture</w:t></w:r></w:p>
                <w:p><w:r><w:t>Paragraphe descriptif</w:t></w:r></w:p>
                <w:p><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:r><w:t>Tests principaux</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading2"/><w:outlineLvl w:val="9"/></w:pPr><w:r><w:t>Corps forcé</w:t></w:r></w:p>
                <w:p><w:r><w:rPr><w:b/><w:sz w:val="36"/></w:rPr><w:t>Recette candidats</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="CycleA"/></w:pPr><w:r><w:t>Style cyclique</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Suppressed"/></w:pPr><w:r><w:t>Style explicitement non-titre</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="CustomFrench"/></w:pPr><w:r><w:t>Partie française</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="7"/></w:numPr></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="36"/></w:rPr><w:t>Étape numérotée</w:t></w:r></w:p>
                <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Résultat tabulaire</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                <w:p><w:pPr><w:pStyle w:val="Heading4"/></w:pPr><w:r><w:t>Sous-section anglaise</w:t></w:r></w:p>
                <w:p><w:r><w:t>Détail imbriqué</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="CustomFrench"/></w:pPr><w:r><w:t>Partie française</w:t></w:r></w:p>
                <w:p><w:r><w:t>Après titre répété</w:t></w:r></w:p>
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
        Assert(blocks.Count == 15, "Source paragraphs, lists and tables must not disappear.");
        Assert(blocks[0].Paragraph?.HeadingLevel is null,
            "An unstyled cover page must remain outside the first heading section.");
        Assert(blocks[1].Paragraph?.HeadingLevel == 2,
            "Custom Word styles must inherit outline levels through basedOn chains.");
        Assert(blocks[3].Paragraph?.HeadingLevel == 1,
            "Direct paragraph outlineLvl must override all styles.");
        Assert(blocks[4].Paragraph?.HeadingLevel is null,
            "Direct body-text outlineLvl=9 must suppress the heading style.");
        Assert(blocks[5].Paragraph?.HeadingLevel is null &&
               blocks[5].Paragraph?.SuggestedHeadingLevel == 1 &&
               !string.IsNullOrWhiteSpace(blocks[5].Paragraph?.HeadingSuggestionReason),
            "An unstyled bold/large paragraph may only be suggested, not promoted.");
        Assert(blocks[6].Paragraph?.HeadingLevel is null,
            "Cyclic basedOn references must not introduce invalid headings.");
        Assert(blocks[7].Paragraph?.HeadingLevel is null,
            "An explicit style outlineLvl=9 must suppress even a Heading-style name.");
        Assert(blocks[8].Paragraph?.HeadingLevel == 3 &&
               blocks[11].Paragraph?.HeadingLevel == 4,
            "French Titre and English Heading style names must preserve nested heading levels.");
        Assert(blocks[9].Paragraph is { IsListItem: true, NumberingId: 7, ListLevel: 1 } &&
               blocks[9].Paragraph?.SuggestedHeadingLevel is null,
            "A visually prominent numbered list item must remain a list, not an inferred heading.");
        Assert(blocks[10].Kind == DocxBlockKind.Table &&
               blocks[10].Table?.Rows[0].Cells[0].Text == "Résultat tabulaire",
            "A table must be retained between list items and nested headings.");

        ImportDocumentTree tree = DocxStructuredImportReader.FromParsed(
            parsed, Path.GetFileName(path), new string('C', 64));
        Assert(tree.Roots.Count == 3 &&
               tree.Roots[0].Kind == ImportDocumentNodeKind.Paragraph &&
               tree.Roots[1].HeadingLevel == 2 &&
               tree.Roots[2].HeadingLevel == 1,
            "The cover page and heading hierarchy must preserve exact source order.");
        Assert(tree.Roots[2].Children.Any(child =>
                child.SuggestedHeadingLevel == 1 &&
                child.Kind == ImportDocumentNodeKind.Paragraph),
            "An unaccepted suggestion must remain a paragraph with visible evidence.");
        List<ImportDocumentNode> frenchSections = tree.Roots[2].Children
            .Where(child => child.Text == "Partie française")
            .ToList();
        Assert(frenchSections.Count == 2 &&
               frenchSections[0].SourceId != frenchSections[1].SourceId &&
               frenchSections[0].HeadingLevel == 3 &&
               frenchSections[1].HeadingLevel == 3,
            "Repeated headings must remain distinct sibling sections, with stable source IDs.");
        Assert(frenchSections[0].Children.Count == 3 &&
               frenchSections[0].Children[0].Kind == ImportDocumentNodeKind.ListItem &&
               frenchSections[0].Children[1].Kind == ImportDocumentNodeKind.Table &&
               frenchSections[0].Children[2].HeadingLevel == 4 &&
               frenchSections[0].Children[2].Children.Single().Text == "Détail imbriqué",
            "Lists, tables and nested EN headings must stay in original reading order.");
        Assert(frenchSections[1].Children.Single().Text == "Après titre répété",
            "A repeated heading must own only its own subsequent content.");
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
