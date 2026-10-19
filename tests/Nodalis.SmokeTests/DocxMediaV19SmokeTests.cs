using System.IO.Compression;
using System.Text;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxMediaV19SmokeTests
{
    /// <summary>Verifies internal media, selection, safe Markdown, deduplication and external warnings.</summary>
    public static async Task RunAsync(string root)
    {
        string path = Path.Combine(root, "docx-media-v19.docx");
        byte[] image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9fXzwAAAAASUVORK5CYII=");
        string document = """
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
             xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
             xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
             xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
              <w:body>
                <w:p><w:r><w:t>Avant </w:t></w:r>
                <w:r><w:drawing><wp:inline><wp:docPr id="1" name="Capture" descr="Capture [test]"/>
                  <a:graphic><a:graphicData><a:blip r:embed="rId1"/></a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r><w:r><w:t> après</w:t></w:r></w:p>
                <w:p><w:r><w:drawing><wp:inline><wp:docPr id="2" name="Capture répétée"/>
                  <a:graphic><a:graphicData><a:blip r:embed="rId2"/></a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r></w:p>
                <w:p><w:r><w:drawing><wp:inline><a:graphic><a:graphicData>
                <a:blip r:link="rId3"/></a:graphicData></a:graphic>
                </wp:inline></w:drawing></w:r></w:p>
                <w:p><w:object><w:r><w:t>Objet non exécutable</w:t></w:r></w:object></w:p>
              </w:body>
            </w:document>
            """;
        string relationships = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image2.png"/>
              <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="https://example.org/image.png" TargetMode="External"/>
            </Relationships>
            """;
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteXml(archive, "word/document.xml", document);
            WriteXml(archive, "word/_rels/document.xml.rels", relationships);
            using Stream one = archive.CreateEntry("word/media/image1.png").Open();
            one.Write(image);
            using Stream two = archive.CreateEntry("word/media/image2.png").Open();
            two.Write(image);
        }

        ParsedDocxDocument parsed = await new DocxParser().ParseAsync(path);
        Assert(parsed.Blocks.Count == 4, "All media and unsupported Word blocks must remain selectable.");
        DocxInlineMedia first = parsed.Blocks[0].Paragraph!.Media.Single();
        Assert(first.IsSupported && first.TextOffset == 6 &&
               first.AlternativeText.Contains("Capture [test]", StringComparison.Ordinal),
            "Image position, alt text and local relation must be preserved.");
        string markdown = DocxMarkdownConverter.ConvertBlocks([parsed.Blocks[0]]);
        Assert(markdown.IndexOf("Avant", StringComparison.Ordinal) <
               markdown.IndexOf("![", StringComparison.Ordinal) &&
               markdown.IndexOf("![", StringComparison.Ordinal) <
               markdown.IndexOf("après", StringComparison.Ordinal) &&
               markdown.Contains("Capture \\[test\\]", StringComparison.Ordinal),
            "Markdown images must remain inline in original paragraph order with escaped alt text.");
        Assert(parsed.Blocks[2].Paragraph!.Media.Single().Warning is not null &&
               parsed.Blocks[3].Paragraph!.Media.Single().Warning is not null,
            "Linked images and OLE objects must have explicit warnings.");

        IReadOnlyList<DocxInlineMedia> media = DocxSelectedMediaAssets.Select(parsed.Blocks.Take(2));
        Assert(media.Count == 1, "Binary-identical images must be deduplicated by fingerprint.");
        string project = Path.Combine(root, "media-v19-project");
        Directory.CreateDirectory(project);
        List<string> files = [];
        List<string> dirs = [];
        await DocxSelectedMediaAssets.CopyAsync(path, project, media, files, dirs);
        Assert(files.Count == 1 && File.Exists(files[0]),
            "Selected images must be materialized to the local media folder.");
        await DocxSelectedMediaAssets.CopyAsync(path, project, media, files, dirs);
        Assert(files.Count == 1, "Identical local media must be reused on repeated import.");
        Assert(DocxSelectedMediaAssets.Select(parsed.Blocks.Skip(2)).Count == 0,
            "Excluded images may not result in attachment copies.");

        string before = File.ReadAllText(path).Length.ToString();
        Assert(before.Length > 0, "Staged package must remain accessible.");
    }

    /// <summary>Writes XML fixture content to an in-memory ZIP entry.</summary>
    private static void WriteXml(ZipArchive archive, string path, string text)
    {
        using StreamWriter writer = new StreamWriter(
            archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>Fails the regression suite if any media invariant breaks.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
