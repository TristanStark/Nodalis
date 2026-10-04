using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

public sealed class DocxParser
{
    private static readonly XNamespace WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XNamespace OfficeRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    private static readonly XNamespace DublinCoreNamespace =
        "http://purl.org/dc/elements/1.1/";

    private static readonly XNamespace DublinCoreTermsNamespace =
        "http://purl.org/dc/terms/";

    /// <summary>
    /// Performs the <c>ParseAsync</c> operation.
    /// </summary>
    /// <param name="docxPath">The <c>docxPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public Task<ParsedDocxDocument> ParseAsync(
            string docxPath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        string fullPath = Path.GetFullPath(docxPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Le fichier DOCX est introuvable.",
                fullPath);
        }

        return Task.Run(
            () => Parse(
                fullPath,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>Parse</c> operation.
    /// </summary>
    /// <param name="fullPath">The <c>fullPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static ParsedDocxDocument Parse(
            string fullPath,
            CancellationToken cancellationToken)
    {
        try
        {
            using global::System.IO.Compression.ZipArchive archive = ZipFile.OpenRead(fullPath);

            cancellationToken.ThrowIfCancellationRequested();

            global::System.IO.Compression.ZipArchiveEntry documentEntry = archive.GetEntry(
                "word/document.xml")
                ?? throw new InvalidDataException(
                    "Le DOCX ne contient pas word/document.xml.");

            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxRelationship> relationships = ReadRelationships(
                archive,
                cancellationToken);

            global::System.Collections.Generic.IReadOnlyDictionary<string, global::Nodalis.Infrastructure.Importing.DocxParser.StyleInfo> styles = ReadStyles(
                archive,
                cancellationToken);

            global::Nodalis.Core.Importing.DocxDocumentMetadata metadata = ReadMetadata(
                archive,
                cancellationToken);

            global::System.Collections.Generic.List<string> headers = ReadHeaders(
                archive,
                cancellationToken);

            XDocument documentXml;

            using (global::System.IO.Stream stream = documentEntry.Open())
            {
                documentXml = XDocument.Load(
                    stream,
                    LoadOptions.PreserveWhitespace);
            }

            global::System.Xml.Linq.XElement body = documentXml.Root?
                .Element(
                    WordNamespace + "body")
                ?? throw new InvalidDataException(
                    "Le document Word ne contient pas de corps exploitable.");

            global::System.Collections.Generic.Dictionary<string, global::Nodalis.Core.Importing.DocxRelationship> relationshipMap = relationships.ToDictionary(
                relationship => relationship.Id,
                StringComparer.Ordinal);

            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxBlock> blocks = new List<DocxBlock>();

            foreach (global::System.Xml.Linq.XElement element in body.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (element.Name ==
                    WordNamespace + "p")
                {
                    blocks.Add(
                        new DocxBlock
                        {
                            Kind = DocxBlockKind.Paragraph,
                            Paragraph = ParseParagraph(
                                element,
                                styles,
                                relationshipMap)
                        });

                    continue;
                }

                if (element.Name ==
                    WordNamespace + "tbl")
                {
                    blocks.Add(
                        new DocxBlock
                        {
                            Kind = DocxBlockKind.Table,
                            Table = ParseTable(
                                element)
                        });
                }
            }

            return new ParsedDocxDocument
            {
                Metadata = metadata,
                Blocks = blocks,
                Headers = headers,
                Relationships = relationships
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XmlException)
        {
            throw new InvalidDataException(
                $"Le DOCX '{Path.GetFileName(fullPath)}' est illisible ou invalide.",
                exception);
        }
    }

    /// <summary>
    /// Performs the <c>ParseParagraph</c> operation.
    /// </summary>
    /// <param name="paragraph">The <c>paragraph</c> value.</param>
    /// <param name="styles">The <c>styles</c> value.</param>
    /// <param name="relationships">The <c>relationships</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static DocxParagraph ParseParagraph(
            XElement paragraph,
            IReadOnlyDictionary<string, StyleInfo> styles,
            IReadOnlyDictionary<string, DocxRelationship> relationships)
    {
        global::System.Xml.Linq.XElement? properties = paragraph.Element(
            WordNamespace + "pPr");

        string? styleId = properties?
            .Element(
                WordNamespace + "pStyle")?
            .Attribute(
                WordNamespace + "val")?
            .Value;

        styles.TryGetValue(
            styleId ?? string.Empty,
            out global::Nodalis.Infrastructure.Importing.DocxParser.StyleInfo? style);

        global::System.Xml.Linq.XElement? numbering = properties?
            .Element(
                WordNamespace + "numPr");

        int? numberingId = ParseIntegerAttribute(
            numbering?
                .Element(
                    WordNamespace + "numId"),
            "val");

        int? listLevel = ParseIntegerAttribute(
            numbering?
                .Element(
                    WordNamespace + "ilvl"),
            "val");

        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxHyperlink> hyperlinks = new List<DocxHyperlink>();

        foreach (global::System.Xml.Linq.XElement hyperlink in paragraph.Elements(
                     WordNamespace + "hyperlink"))
        {
            string text = ExtractText(
                hyperlink)
                .Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            string? relationshipId = hyperlink
                .Attribute(
                    OfficeRelationshipNamespace + "id")?
                .Value;

            if (!string.IsNullOrWhiteSpace(relationshipId) &&
                relationships.TryGetValue(
                    relationshipId,
                    out global::Nodalis.Core.Importing.DocxRelationship? relationship))
            {
                hyperlinks.Add(
                    new DocxHyperlink
                    {
                        Text = text,
                        Target = relationship.Target
                    });

                continue;
            }

            string? anchor = hyperlink
                .Attribute(
                    WordNamespace + "anchor")?
                .Value;

            if (!string.IsNullOrWhiteSpace(anchor))
            {
                hyperlinks.Add(
                    new DocxHyperlink
                    {
                        Text = text,
                        Target = $"#{anchor}"
                    });
            }
        }

        return new DocxParagraph
        {
            Text = ExtractText(
                paragraph)
                .TrimEnd(),
            StyleId = styleId,
            StyleName = style?.Name,
            HeadingLevel =
                style?.HeadingLevel ??
                InferHeadingLevel(
                    styleId,
                    style?.Name),
            IsListItem = numberingId is not null,
            NumberingId = numberingId,
            ListLevel = listLevel,
            Hyperlinks = hyperlinks
        };
    }

    /// <summary>
    /// Performs the <c>ParseTable</c> operation.
    /// </summary>
    /// <param name="table">The <c>table</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static DocxTable ParseTable(XElement table)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxTableRow> rows = new List<DocxTableRow>();

        foreach (global::System.Xml.Linq.XElement row in table.Elements(
                     WordNamespace + "tr"))
        {
            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxTableCell> cells = new List<DocxTableCell>();

            foreach (global::System.Xml.Linq.XElement cell in row.Elements(
                         WordNamespace + "tc"))
            {
                string[] paragraphs = cell.Elements(
                        WordNamespace + "p")
                    .Select(ExtractText)
                    .Select(text => text.Trim())
                    .Where(text =>
                        !string.IsNullOrWhiteSpace(text))
                    .ToArray();

                cells.Add(
                    new DocxTableCell
                    {
                        Text = string.Join(
                            Environment.NewLine,
                            paragraphs)
                    });
            }

            rows.Add(
                new DocxTableRow
                {
                    Cells = cells
                });
        }

        return new DocxTable
        {
            Rows = rows
        };
    }

    /// <summary>
    /// Performs the <c>ReadStyles</c> operation.
    /// </summary>
    /// <param name="archive">The <c>archive</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IReadOnlyDictionary<string, StyleInfo> ReadStyles(
            ZipArchive archive,
            CancellationToken cancellationToken)
    {
        global::System.IO.Compression.ZipArchiveEntry? entry = archive.GetEntry(
            "word/styles.xml");

        if (entry is null)
        {
            return new Dictionary<string, StyleInfo>(
                StringComparer.OrdinalIgnoreCase);
        }

        cancellationToken.ThrowIfCancellationRequested();

        XDocument xml;

        using (global::System.IO.Stream stream = entry.Open())
        {
            xml = XDocument.Load(stream);
        }

        global::System.Collections.Generic.Dictionary<string, global::Nodalis.Infrastructure.Importing.DocxParser.StyleInfo> result = new Dictionary<string, StyleInfo>(
            StringComparer.OrdinalIgnoreCase);

        foreach (global::System.Xml.Linq.XElement styleElement in xml.Descendants(
                     WordNamespace + "style"))
        {
            string? styleId = styleElement
                .Attribute(
                    WordNamespace + "styleId")?
                .Value;

            if (string.IsNullOrWhiteSpace(styleId))
            {
                continue;
            }

            string? type = styleElement
                .Attribute(
                    WordNamespace + "type")?
                .Value;

            if (!string.IsNullOrWhiteSpace(type) &&
                !string.Equals(
                    type,
                    "paragraph",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? name = styleElement
                .Element(
                    WordNamespace + "name")?
                .Attribute(
                    WordNamespace + "val")?
                .Value;

            int? outlineLevel = ParseIntegerAttribute(
                styleElement
                    .Element(
                        WordNamespace + "pPr")?
                    .Element(
                        WordNamespace + "outlineLvl"),
                "val");

            result[styleId] =
                new StyleInfo(
                    name,
                    outlineLevel is int level
                        ? level + 1
                        : InferHeadingLevel(
                            styleId,
                            name));
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>ReadRelationships</c> operation.
    /// </summary>
    /// <param name="archive">The <c>archive</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<DocxRelationship> ReadRelationships(
            ZipArchive archive,
            CancellationToken cancellationToken)
    {
        global::System.IO.Compression.ZipArchiveEntry? entry = archive.GetEntry(
            "word/_rels/document.xml.rels");

        if (entry is null)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        XDocument xml;

        using (global::System.IO.Stream stream = entry.Open())
        {
            xml = XDocument.Load(stream);
        }

        return xml.Descendants(
                PackageRelationshipNamespace + "Relationship")
            .Select(element =>
                new DocxRelationship
                {
                    Id =
                        element.Attribute("Id")?.Value ??
                        string.Empty,
                    Type =
                        element.Attribute("Type")?.Value ??
                        string.Empty,
                    Target =
                        element.Attribute("Target")?.Value ??
                        string.Empty,
                    IsExternal = string.Equals(
                        element.Attribute("TargetMode")?.Value,
                        "External",
                        StringComparison.OrdinalIgnoreCase)
                })
            .Where(relationship =>
                !string.IsNullOrWhiteSpace(
                    relationship.Id))
            .ToList();
    }

    /// <summary>
    /// Performs the <c>ReadHeaders</c> operation.
    /// </summary>
    /// <param name="archive">The <c>archive</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> ReadHeaders(
            ZipArchive archive,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.List<string> result = new List<string>();

        foreach (global::System.IO.Compression.ZipArchiveEntry entry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith(
                             "word/header",
                             StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(
                             ".xml",
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry =>
                         entry.FullName,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            XDocument xml;

            using (global::System.IO.Stream stream = entry.Open())
            {
                xml = XDocument.Load(stream);
            }

            string text = string.Join(
                    Environment.NewLine,
                    xml.Descendants(
                            WordNamespace + "p")
                        .Select(ExtractText)
                        .Select(value => value.Trim())
                        .Where(value =>
                            !string.IsNullOrWhiteSpace(value)))
                .Trim();

            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>ReadMetadata</c> operation.
    /// </summary>
    /// <param name="archive">The <c>archive</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static DocxDocumentMetadata ReadMetadata(
            ZipArchive archive,
            CancellationToken cancellationToken)
    {
        global::System.IO.Compression.ZipArchiveEntry? entry = archive.GetEntry(
            "docProps/core.xml");

        if (entry is null)
        {
            return new DocxDocumentMetadata();
        }

        cancellationToken.ThrowIfCancellationRequested();

        XDocument xml;

        using (global::System.IO.Stream stream = entry.Open())
        {
            xml = XDocument.Load(stream);
        }

        global::System.Xml.Linq.XElement? root = xml.Root;

        return new DocxDocumentMetadata
        {
            Title = Value(
                root,
                DublinCoreNamespace + "title"),
            Subject = Value(
                root,
                DublinCoreNamespace + "subject"),
            Creator = Value(
                root,
                DublinCoreNamespace + "creator"),
            Description = Value(
                root,
                DublinCoreNamespace + "description"),
            Keywords = Value(
                root,
                CorePropertiesNamespace + "keywords"),
            Category = Value(
                root,
                CorePropertiesNamespace + "category"),
            CreatedUtc = ParseDate(
                Value(
                    root,
                    DublinCoreTermsNamespace + "created")),
            ModifiedUtc = ParseDate(
                Value(
                    root,
                    DublinCoreTermsNamespace + "modified"))
        };
    }

    /// <summary>
    /// Performs the <c>ExtractText</c> operation.
    /// </summary>
    /// <param name="element">The <c>element</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string ExtractText(XElement element)
    {
        global::System.Text.StringBuilder builder = new System.Text.StringBuilder();

        foreach (global::System.Xml.Linq.XElement node in element.Descendants())
        {
            if (node.Name ==
                WordNamespace + "t")
            {
                builder.Append(
                    node.Value);
                continue;
            }

            if (node.Name ==
                WordNamespace + "tab")
            {
                builder.Append('\t');
                continue;
            }

            if (node.Name ==
                    WordNamespace + "br" ||
                node.Name ==
                    WordNamespace + "cr")
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Performs the <c>ParseIntegerAttribute</c> operation.
    /// </summary>
    /// <param name="element">The <c>element</c> value.</param>
    /// <param name="attributeName">The <c>attributeName</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static int? ParseIntegerAttribute(
            XElement? element,
            string attributeName)
    {
        string? value = element?
            .Attribute(
                WordNamespace + attributeName)?
            .Value;

        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int result)
            ? result
            : null;
    }

    /// <summary>
    /// Performs the <c>InferHeadingLevel</c> operation.
    /// </summary>
    /// <param name="styleId">The <c>styleId</c> value.</param>
    /// <param name="styleName">The <c>styleName</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static int? InferHeadingLevel(
            string? styleId,
            string? styleName)
    {
        foreach (string? candidate in new[]
                 {
                     styleName,
                     styleId
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string normalized = candidate
                .Trim()
                .Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal);

            if (!normalized.StartsWith(
                    "Heading",
                    StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith(
                    "Titre",
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            string digits = new string(
                normalized
                    .Reverse()
                    .TakeWhile(char.IsDigit)
                    .Reverse()
                    .ToArray());

            if (int.TryParse(
                    digits,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int level) &&
                level is >= 1 and <= 9)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>
    /// Performs the <c>Value</c> operation.
    /// </summary>
    /// <param name="root">The <c>root</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string? Value(
            XElement? root,
            XName name)
    {
        string? value = root?
            .Element(name)?
            .Value
            .Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    /// <summary>
    /// Performs the <c>ParseDate</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static DateTimeOffset? ParseDate(
            string? value) =>
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out global::System.DateTimeOffset result)
                ? result
                : null;

    private sealed record StyleInfo(
        string? Name,
        int? HeadingLevel);
}
