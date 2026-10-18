using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Nodalis.Core.Composites;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;

namespace Nodalis.Infrastructure.Composites;

/// <summary>Offline export formats supported by composite documents.</summary>
public enum CompositeExportFormat { Markdown, Html, Docx }

/// <summary>Whether to export readable references or a resolved immutable snapshot.</summary>
public enum CompositeExportMode { References, Resolved }

/// <summary>Exports composite documents without modifying referenced sources or requiring NuGet.</summary>
public sealed class CompositeDocumentExportService
{
    private readonly CompositeWorkspaceRenderer _renderer;

    /// <summary>Creates a renderer-backed exporter using the canonical workspace index.</summary>
    /// <param name="workspaceRoot">Workspace folder.</param>
    /// <param name="catalog">Canonical references.</param>
    public CompositeDocumentExportService(string workspaceRoot, LinkIndexCatalog catalog)
    {
        _renderer = new CompositeWorkspaceRenderer(workspaceRoot, catalog);
    }

    /// <summary>Writes a Markdown/HTML/DOCX snapshot atomically to a user-chosen location.</summary>
    /// <param name="parentPath">Source document path.</param>
    /// <param name="parentMarkdown">Editor snapshot, including unsaved text.</param>
    /// <param name="destination">Explicit target path.</param>
    /// <param name="format">Requested output file format.</param>
    /// <param name="mode">Original references or resolved text.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Number of diagnosed include failures in the snapshot.</returns>
    public async Task<int> ExportAsync(string parentPath, string parentMarkdown,
        string destination, CompositeExportFormat format,
        CompositeExportMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(parentMarkdown);
        cancellationToken.ThrowIfCancellationRequested();
        CompositeRenderResult result = mode == CompositeExportMode.Resolved
            ? await _renderer.RenderAsync(parentPath, parentMarkdown, cancellationToken)
            : new CompositeRenderResult(parentMarkdown, [], []);
        string text = result.Markdown;
        string full = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            switch (format)
            {
                case CompositeExportFormat.Markdown:
                    await File.WriteAllTextAsync(temporary, text, Encoding.UTF8,
                        cancellationToken);
                    break;
                case CompositeExportFormat.Html:
                    await File.WriteAllTextAsync(temporary, RenderHtml(text),
                        Encoding.UTF8, cancellationToken);
                    break;
                case CompositeExportFormat.Docx:
                    await Task.Run(() => WriteDocx(temporary, text, cancellationToken),
                        cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return result.Diagnostics.Count;
    }

    /// <summary>Renders a standalone escaped HTML document without network resources.</summary>
    /// <param name="markdown">Resolved or reference-preserving Markdown.</param>
    /// <returns>Self-contained HTML markup.</returns>
    public static string RenderHtml(string markdown)
    {
        StringBuilder html = new();
        html.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        html.Append("<title>Document composite Nodalis</title>");
        html.Append("<style>body{font:15px/1.6 system-ui,sans-serif;max-width:900px;");
        html.Append("margin:32px auto;padding:0 20px;color:#222}pre{white-space:pre-wrap;");
        html.Append("background:#eee;padding:12px}blockquote{border-left:3px solid #777;");
        html.Append("padding-left:12px;color:#444}table{border-collapse:collapse}");
        html.Append("td,th{border:1px solid #bbb;padding:5px}</style></head><body>");
        foreach (MarkdownBlock block in MarkdownDocumentParser.Parse(markdown))
        {
            string value = WebUtility.HtmlEncode(block.Text);
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                    int level = Math.Clamp(block.Level, 1, 6);
                    html.Append("<h").Append(level).Append('>').Append(value)
                        .Append("</h").Append(level).Append('>');
                    break;
                case MarkdownBlockKind.CodeBlock:
                    html.Append("<pre>").Append(value).Append("</pre>");
                    break;
                case MarkdownBlockKind.Quote:
                    html.Append("<blockquote>").Append(value).Append("</blockquote>");
                    break;
                case MarkdownBlockKind.ChecklistItem:
                    html.Append("<p>").Append(block.IsChecked == true ? "☑ " : "☐ ")
                        .Append(value).Append("</p>");
                    break;
                case MarkdownBlockKind.Table:
                    html.Append("<table>");
                    for (int index = 0; index < block.TableRows.Count; index++)
                    {
                        html.Append("<tr>");
                        foreach (string cell in block.TableRows[index])
                        {
                            string tag = index == 0 ? "th" : "td";
                            html.Append('<').Append(tag).Append('>')
                                .Append(WebUtility.HtmlEncode(cell))
                                .Append("</").Append(tag).Append('>');
                        }

                        html.Append("</tr>");
                    }

                    html.Append("</table>");
                    break;
                default:
                    html.Append("<p>").Append(value).Append("</p>");
                    break;
            }
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>Creates a valid minimal Open Packaging Convention DOCX with native Word paragraphs.</summary>
    /// <param name="path">Temporary DOCX output location.</param>
    /// <param name="markdown">Source snapshot.</param>
    /// <param name="token">Cancellation request.</param>
    private static void WriteDocx(string path, string markdown, CancellationToken token)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace c = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace r = "http://schemas.openxmlformats.org/package/2006/relationships";
        List<XElement> children = [];
        foreach (MarkdownBlock block in MarkdownDocumentParser.Parse(markdown))
        {
            token.ThrowIfCancellationRequested();
            if (block.Kind == MarkdownBlockKind.Table)
            {
                XElement table = new(w + "tbl");
                foreach (List<string> row in block.TableRows)
                {
                    XElement tr = new(w + "tr");
                    foreach (string cell in row)
                    {
                        tr.Add(new XElement(w + "tc", new XElement(w + "p",
                            new XElement(w + "r", new XElement(w + "t",
                                new XAttribute(XNamespace.Xml + "space", "preserve"), cell)))));
                    }

                    table.Add(tr);
                }

                children.Add(table);
                continue;
            }

            XElement paragraph = new(w + "p");
            if (block.Kind == MarkdownBlockKind.Heading)
            {
                paragraph.Add(new XElement(w + "pPr",
                    new XElement(w + "pStyle",
                        new XAttribute(w + "val", "Heading" +
                            Math.Clamp(block.Level, 1, 6)))));
            }

            string value = block.Kind == MarkdownBlockKind.ChecklistItem
                ? (block.IsChecked == true ? "☑ " : "☐ ") + block.Text
                : block.Text;
            paragraph.Add(new XElement(w + "r",
                new XElement(w + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
            children.Add(paragraph);
        }

        XDocument document = new(new XElement(w + "document",
            new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
            new XElement(w + "body", children,
                new XElement(w + "sectPr"))));
        XDocument contentTypes = new(new XElement(c + "Types",
            new XElement(c + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType",
                    "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(c + "Default", new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(c + "Override", new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));
        XDocument relations = new(new XElement(r + "Relationships",
            new XElement(r + "Relationship", new XAttribute("Id", "rId1"),
                new XAttribute("Type",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "word/document.xml"))));
        using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write);
        using ZipArchive zip = new(output, ZipArchiveMode.Create);
        AddXml(zip, "[Content_Types].xml", contentTypes);
        AddXml(zip, "_rels/.rels", relations);
        AddXml(zip, "word/document.xml", document);
    }

    /// <summary>Writes one XML part into a standard Office ZIP container.</summary>
    /// <param name="archive">Open writable DOCX archive.</param>
    /// <param name="name">OPC part path.</param>
    /// <param name="document">Namespace-qualified XML.</param>
    private static void AddXml(ZipArchive archive, string name, XDocument document)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream stream = entry.Open();
        document.Save(stream);
    }
}
