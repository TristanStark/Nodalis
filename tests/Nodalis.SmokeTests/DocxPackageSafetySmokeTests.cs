using System.IO.Compression;
using System.Text;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

/// <summary>Regression checks for malicious ZIP/Open XML input before DOCX imports.</summary>
internal static class DocxPackageSafetySmokeTests
{
    private const string Document = """
        <?xml version="1.0" encoding="utf-8"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body><w:p><w:r><w:t>Contenu valide</w:t></w:r></w:p></w:body>
        </w:document>
        """;

    /// <summary>Exercises valid packages and rejects invalid metadata, XML and media operations.</summary>
    public static async Task RunAsync(string root)
    {
        string valid = Path.Combine(root, "docx-safety-valid.docx");
        WriteDocx(valid, Document, null);
        ParsedDocxDocument parsed = await new DocxParser().ParseAsync(valid);
        Assert(parsed.Blocks.Count == 1, "A normal DOCX remains readable.");

        string traversal = Path.Combine(root, "docx-safety-traversal.docx");
        WriteDocx(traversal, Document,
            archive => WriteEntry(archive, "word/../outside.txt", "non"));
        await MustRejectAsync(traversal, "ZIP path traversal");

        string absolute = Path.Combine(root, "docx-safety-absolute.docx");
        WriteDocx(absolute, Document,
            archive => WriteEntry(archive, "/outside.txt", "non"));
        await MustRejectAsync(absolute, "absolute ZIP path");

        string duplicate = Path.Combine(root, "docx-safety-duplicate.docx");
        WriteDocx(duplicate, Document,
            archive => WriteEntry(archive, "WORD/DOCUMENT.XML", Document));
        await MustRejectAsync(duplicate, "ambiguous duplicate XML entry");

        string symlink = Path.Combine(root, "docx-safety-symlink.docx");
        WriteDocx(symlink, Document, archive =>
        {
            ZipArchiveEntry link = archive.CreateEntry("word/media/image.png");
            link.ExternalAttributes = 0xA000 << 16;
        });
        await MustRejectAsync(symlink, "ZIP symbolic link");

        string doctype = Path.Combine(root, "docx-safety-dtd.docx");
        string unsafeXml = Document.Replace("<w:document",
            "<!DOCTYPE w:document [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><w:document",
            StringComparison.Ordinal);
        WriteDocx(doctype, unsafeXml, null);
        await MustRejectAsync(doctype, "DTD or external entity in DOCX XML");

        string bomb = Path.Combine(root, "docx-safety-compression.docx");
        WriteDocx(bomb, Document, archive =>
            WriteEntry(archive, "custom/huge.bin", new string('X', 3 * 1024 * 1024)));
        await MustRejectAsync(bomb, "suspicious compression ratio");

        string destination = Path.Combine(root, "docx-safety-output");
        Directory.CreateDirectory(destination);
        DocxInlineMedia selected = new DocxInlineMedia
        {
            SourceId = "rId1",
            ArchiveEntryPath = "word/media/image.png",
            Sha256 = new string('a', 64),
            MarkdownRelativePath = "../Media-Docx/" + new string('a', 64) + ".png"
        };
        bool refused = false;
        try
        {
            await DocxSelectedMediaAssets.CopyAsync(traversal, destination,
                [selected], new List<string>(), new List<string>());
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && !Directory.Exists(Path.Combine(destination, "Media-Docx")),
            "Malformed packages must be rejected before creating any media folder.");
        Assert(File.Exists(traversal), "Rejected DOCX source is never deleted.");
    }

    /// <summary>Creates a synthetic Word ZIP package with an optional extra entry.</summary>
    private static void WriteDocx(string path, string document, Action<ZipArchive>? extra)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "word/document.xml", document);
        extra?.Invoke(archive);
    }

    /// <summary>Writes one ZIP entry without changing its original path.</summary>
    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using StreamWriter writer = new StreamWriter(
            archive.CreateEntry(path, CompressionLevel.Optimal).Open(),
            new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Requires the parser to fail closed on an unsafe source.</summary>
    private static async Task MustRejectAsync(string path, string reason)
    {
        bool rejected = false;
        try
        {
            await new DocxParser().ParseAsync(path);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(rejected, "Unsafe DOCX should be rejected: " + reason);
    }

    /// <summary>Fails fast when a DOCX security invariant is violated.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
