using System.IO.Compression;
using System.Xml.Linq;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Composites;

/// <summary>Release fixtures for independent offline HTML, Markdown and native DOCX exports.</summary>
internal static class CompositeExportsSmokeTests
{
    /// <summary>Verifies resolved snapshots, safe HTML escaping and valid ZIP packages.</summary>
    /// <returns>Completion of three filesystem exports.</returns>
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "Nodalis-Composite-Export-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Guid sourceId = Guid.NewGuid();
            string parent = Path.Combine(root, "Report.md");
            string child = Path.Combine(root, "Source.md");
            await File.WriteAllTextAsync(parent, "# Mon rapport\n");
            await File.WriteAllTextAsync(child, "# Recette\nRésultat <validé> & signé\n");
            LinkIndexCatalog index = new()
            {
                Targets =
                [
                    new LinkTargetEntry
                    {
                        Id = sourceId,
                        Kind = LinkTargetKind.Document,
                        RelativePath = "Source.md",
                        DisplayName = "Source",
                        QualifiedName = "Source"
                    }
                ]
            };
            CompositeDocumentExportService exporter = new(root, index);
            string markdown = "# Mon rapport\n![[" + sourceId.ToString("D") + "]]";
            string raw = Path.Combine(root, "references.md");
            string expanded = Path.Combine(root, "expanded.md");
            string html = Path.Combine(root, "report.html");
            string docx = Path.Combine(root, "report.docx");
            await exporter.ExportAsync(parent, markdown, raw,
                CompositeExportFormat.Markdown, CompositeExportMode.References);
            await exporter.ExportAsync(parent, markdown, expanded,
                CompositeExportFormat.Markdown, CompositeExportMode.Resolved);
            await exporter.ExportAsync(parent, markdown, html,
                CompositeExportFormat.Html, CompositeExportMode.Resolved);
            await exporter.ExportAsync(parent, markdown, docx,
                CompositeExportFormat.Docx, CompositeExportMode.Resolved);
            string rawText = await File.ReadAllTextAsync(raw);
            string rendered = await File.ReadAllTextAsync(expanded);
            string htmlText = await File.ReadAllTextAsync(html);
            Assert(rawText.Contains("![[") && !rawText.Contains("Résultat") &&
                rendered.Contains("Résultat <validé>") &&
                rendered.Contains(sourceId.ToString("D")),
                "Reference mode must preserve markers while resolved mode includes provenance.");
            Assert(htmlText.Contains("&lt;validé&gt; &amp; signé") &&
                !htmlText.Contains("Résultat <validé>"),
                "Standalone HTML must escape document content.");
            string figure = Path.Combine(root, "figure.png");
            await File.WriteAllBytesAsync(figure, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y3c5H0AAAAASUVORK5CYII="));
            string htmlRich = CompositeDocumentExportService.RenderHtml(
                "**Fort** et ![Figure](figure.png) avec <danger>",
                root, root);
            Assert(htmlRich.Contains("<strong>Fort</strong>") &&
                htmlRich.Contains("data:image/png;base64,") &&
                htmlRich.Contains("&lt;danger&gt;") &&
                !htmlRich.Contains("<danger>"),
                "Standalone HTML must preserve bold, embed safe local images and escape markup.");
            string outside = CompositeDocumentExportService.RenderHtml(
                "![Interdit](../private.png)", root, root);
            Assert(!outside.Contains("data:image/"),
                "Assets outside workspace must never be embedded.");

            using ZipArchive document = ZipFile.OpenRead(docx);
            Assert(document.GetEntry("[Content_Types].xml") is not null &&
                document.GetEntry("_rels/.rels") is not null &&
                document.GetEntry("word/document.xml") is not null,
                "DOCX must contain all required OPC parts.");
            ZipArchiveEntry wordPart = document.GetEntry("word/document.xml")!;
            using (Stream input = wordPart.Open())
            {
                XDocument word = XDocument.Load(input);
                XNamespace wordNamespace =
                    "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                Assert(word.Root?.Name == wordNamespace + "document" &&
                    word.Descendants(wordNamespace + "t")
                        .Any(node => node.Value.Contains("Résultat <validé>")),
                    "DOCX must preserve resolved source content as valid WordprocessingML.");
            }
            bool blockedSourceOverwrite = false;
            try
            {
                await exporter.ExportAsync(parent, markdown, child,
                    CompositeExportFormat.Markdown, CompositeExportMode.Resolved);
            }
            catch (InvalidOperationException)
            {
                blockedSourceOverwrite = true;
            }

            Assert(blockedSourceOverwrite, "Export must refuse overwriting a canonical source.");
            Assert(await File.ReadAllTextAsync(child) == "# Recette\nRésultat <validé> & signé\n",
                "Exports must not modify canonical source documents.");
            Console.WriteLine("[Composites 1.6] Markdown reference/resolved, HTML and DOCX exports passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Fails the current fixture with a descriptive message.</summary>
    /// <param name="condition">Assertion.</param>
    /// <param name="message">Expected behavior.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
