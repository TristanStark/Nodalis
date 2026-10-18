using System.IO.Compression;
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
    private readonly string _workspaceRoot;
    private readonly LinkIndexCatalog _catalog;

    /// <summary>Creates a renderer-backed exporter using the canonical workspace index.</summary>
    /// <param name="workspaceRoot">Workspace folder.</param>
    /// <param name="catalog">Canonical references.</param>
    public CompositeDocumentExportService(string workspaceRoot, LinkIndexCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(catalog);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _catalog = catalog;
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
        if (string.Equals(full, Path.GetFullPath(parentPath),
                StringComparison.OrdinalIgnoreCase) ||
            _catalog.Targets.Any(source =>
                source.Kind == LinkTargetKind.Document &&
                string.Equals(full,
                    Path.GetFullPath(Path.Combine(_workspaceRoot,
                        source.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "L'export ne peut pas écraser un document source du workspace.");
        }

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
                    await File.WriteAllTextAsync(temporary,
                        RenderHtml(text, Path.GetDirectoryName(parentPath), _workspaceRoot),
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
    public static string RenderHtml(string markdown, string? baseDirectory = null,
        string? workspaceRoot = null)
    {
        int embeddedBytes = 0;
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
            string value = RenderInlineHtml(block.Text, baseDirectory, workspaceRoot,
                ref embeddedBytes);
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
                                .Append(RenderInlineHtml(cell, baseDirectory,
                                    workspaceRoot, ref embeddedBytes))
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

    /// <summary>Formats inline Markdown safely and embeds small workspace-local images.</summary>
    /// <param name="source">Inline Markdown text.</param>
    /// <param name="baseDirectory">Directory of the parent document.</param>
    /// <param name="workspaceRoot">Optional canonical root restricting images.</param>
    /// <param name="embeddedBytes">Total bytes embedded into this export.</param>
    /// <returns>Safe HTML with no external script resources.</returns>
    private static string RenderInlineHtml(string source, string? baseDirectory,
        string? workspaceRoot, ref int embeddedBytes)
    {
        StringBuilder output = new();
        foreach (MarkdownInline item in MarkdownInlineParser.Parse(source))
        {
            string label = EscapeHtml(item.Text);
            switch (item.Kind)
            {
                case MarkdownInlineKind.Bold:
                    output.Append("<strong>").Append(label).Append("</strong>");
                    break;
                case MarkdownInlineKind.Italic:
                    output.Append("<em>").Append(label).Append("</em>");
                    break;
                case MarkdownInlineKind.Code:
                    output.Append("<code>").Append(label).Append("</code>");
                    break;
                case MarkdownInlineKind.Image:
                    output.Append(RenderImage(item, baseDirectory, workspaceRoot,
                        ref embeddedBytes));
                    break;
                case MarkdownInlineKind.Link:
                    string href = item.Target ?? "";
                    if ((href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         href.StartsWith('#') ||
                         href.StartsWith("./", StringComparison.Ordinal)) &&
                        href.IndexOfAny(['"', '\r', '\n', '<', '>']) < 0)
                    {
                        output.Append("<a href=\"").Append(EscapeHtml(href))
                            .Append("\">").Append(label).Append("</a>");
                    }
                    else
                    {
                        output.Append(label);
                    }

                    break;
                case MarkdownInlineKind.InternalLink:
                    output.Append("<code>").Append(label).Append("</code>");
                    break;
                default:
                    output.Append(label);
                    break;
            }
        }

        return output.ToString();
    }

    /// <summary>Embeds one safe local PNG/JPEG/GIF/WebP asset with a bounded byte budget.</summary>
    /// <param name="image">Parsed Markdown image.</param>
    /// <param name="baseDirectory">Relative image origin.</param>
    /// <param name="workspaceRoot">Permitted filesystem boundary.</param>
    /// <param name="embeddedBytes">Bytes already included in this export.</param>
    /// <returns>Inlined image or harmless descriptive placeholder.</returns>
    private static string RenderImage(MarkdownInline image, string? baseDirectory,
        string? workspaceRoot, ref int embeddedBytes)
    {
        string label = EscapeHtml(image.Text);
        string? target = image.Target;
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(baseDirectory) ||
            string.IsNullOrWhiteSpace(workspaceRoot) ||
            Path.IsPathRooted(target) || target.Contains("://", StringComparison.Ordinal))
        {
            return "<span>[image : " + label + "]</span>";
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspaceRoot)) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(baseDirectory, target));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || !IsSafeLocalAssetPath(path, root))
            {
                return "<span>[image indisponible : " + label + "]</span>";
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            string? mime = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => null
            };
            FileInfo info = new(path);
            if (mime is null || info.Length > 2_000_000 ||
                info.Length > 20_000_000 - embeddedBytes)
            {
                return "<span>[image trop volumineuse ou non prise en charge : " +
                    label + "]</span>";
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length > 2_000_000 ||
                bytes.Length > 20_000_000 - embeddedBytes)
            {
                return "<span>[image trop volumineuse : " + label + "]</span>";
            }

            embeddedBytes += bytes.Length;
            return "<img alt=\"" + label + "\" src=\"data:" + mime +
                ";base64," + Convert.ToBase64String(bytes) + "\">";
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "<span>[image inaccessible : " + label + "]</span>";
        }
    }

    /// <summary>Rejects reparse points in a local asset or its workspace-relative parents.</summary>
    /// <param name="path">Candidate image path.</param>
    /// <param name="root">Canonical workspace root with trailing separator.</param>
    /// <returns>True only for non-reparse local assets.</returns>
    private static bool IsSafeLocalAssetPath(string path, string root)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        DirectoryInfo? parent = Directory.GetParent(path);
        while (parent is not null &&
            parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            parent = parent.Parent;
        }

        return true;
    }

    /// <summary>Escapes user-authored content without any network-related dependency.</summary>
    /// <param name="value">Raw user string.</param>
    /// <returns>HTML-safe text.</returns>
    private static string EscapeHtml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal)
             .Replace("\"", "&quot;", StringComparison.Ordinal)
             .Replace("'", "&#39;", StringComparison.Ordinal);

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
