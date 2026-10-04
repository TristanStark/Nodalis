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

    public Task<ParsedDocxDocument> ParseAsync(
        string docxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        var fullPath = Path.GetFullPath(docxPath);

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

    private static ParsedDocxDocument Parse(
        string fullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(fullPath);

            cancellationToken.ThrowIfCancellationRequested();

            var documentEntry = archive.GetEntry(
                "word/document.xml")
                ?? throw new InvalidDataException(
                    "Le DOCX ne contient pas word/document.xml.");

            var relationships = ReadRelationships(
                archive,
                cancellationToken);

            var styles = ReadStyles(
                archive,
                cancellationToken);

            var metadata = ReadMetadata(
                archive,
                cancellationToken);

            XDocument documentXml;

            using (var stream = documentEntry.Open())
            {
                documentXml = XDocument.Load(
                    stream,
                    LoadOptions.PreserveWhitespace);
            }

            var body = documentXml.Root?
                .Element(
                    WordNamespace + "body")
                ?? throw new InvalidDataException(
                    "Le document Word ne contient pas de corps exploitable.");

            var relationshipMap = relationships.ToDictionary(
                relationship => relationship.Id,
                StringComparer.Ordinal);

            var blocks = new List<DocxBlock>();

            foreach (var element in body.Elements())
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

    private static DocxParagraph ParseParagraph(
        XElement paragraph,
        IReadOnlyDictionary<string, StyleInfo> styles,
        IReadOnlyDictionary<string, DocxRelationship> relationships)
    {
        var properties = paragraph.Element(
            WordNamespace + "pPr");

        var styleId = properties?
            .Element(
                WordNamespace + "pStyle")?
            .Attribute(
                WordNamespace + "val")?
            .Value;

        styles.TryGetValue(
            styleId ?? string.Empty,
            out var style);

        var numbering = properties?
            .Element(
                WordNamespace + "numPr");

        var numberingId = ParseIntegerAttribute(
            numbering?
                .Element(
                    WordNamespace + "numId"),
            "val");

        var listLevel = ParseIntegerAttribute(
            numbering?
                .Element(
                    WordNamespace + "ilvl"),
            "val");

        var hyperlinks = new List<DocxHyperlink>();

        foreach (var hyperlink in paragraph.Elements(
                     WordNamespace + "hyperlink"))
        {
            var text = ExtractText(
                hyperlink)
                .Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var relationshipId = hyperlink
                .Attribute(
                    OfficeRelationshipNamespace + "id")?
                .Value;

            if (!string.IsNullOrWhiteSpace(relationshipId) &&
                relationships.TryGetValue(
                    relationshipId,
                    out var relationship))
            {
                hyperlinks.Add(
                    new DocxHyperlink
                    {
                        Text = text,
                        Target = relationship.Target
                    });

                continue;
            }

            var anchor = hyperlink
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

    private static DocxTable ParseTable(XElement table)
    {
        var rows = new List<DocxTableRow>();

        foreach (var row in table.Elements(
                     WordNamespace + "tr"))
        {
            var cells = new List<DocxTableCell>();

            foreach (var cell in row.Elements(
                         WordNamespace + "tc"))
            {
                var paragraphs = cell.Elements(
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

    private static IReadOnlyDictionary<string, StyleInfo> ReadStyles(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(
            "word/styles.xml");

        if (entry is null)
        {
            return new Dictionary<string, StyleInfo>(
                StringComparer.OrdinalIgnoreCase);
        }

        cancellationToken.ThrowIfCancellationRequested();

        XDocument xml;

        using (var stream = entry.Open())
        {
            xml = XDocument.Load(stream);
        }

        var result = new Dictionary<string, StyleInfo>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var styleElement in xml.Descendants(
                     WordNamespace + "style"))
        {
            var styleId = styleElement
                .Attribute(
                    WordNamespace + "styleId")?
                .Value;

            if (string.IsNullOrWhiteSpace(styleId))
            {
                continue;
            }

            var type = styleElement
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

            var name = styleElement
                .Element(
                    WordNamespace + "name")?
                .Attribute(
                    WordNamespace + "val")?
                .Value;

            var outlineLevel = ParseIntegerAttribute(
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

    private static List<DocxRelationship> ReadRelationships(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(
            "word/_rels/document.xml.rels");

        if (entry is null)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        XDocument xml;

        using (var stream = entry.Open())
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

    private static DocxDocumentMetadata ReadMetadata(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(
            "docProps/core.xml");

        if (entry is null)
        {
            return new DocxDocumentMetadata();
        }

        cancellationToken.ThrowIfCancellationRequested();

        XDocument xml;

        using (var stream = entry.Open())
        {
            xml = XDocument.Load(stream);
        }

        var root = xml.Root;

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

    private static string ExtractText(XElement element)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var node in element.Descendants())
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

    private static int? ParseIntegerAttribute(
        XElement? element,
        string attributeName)
    {
        var value = element?
            .Attribute(
                WordNamespace + attributeName)?
            .Value;

        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static int? InferHeadingLevel(
        string? styleId,
        string? styleName)
    {
        foreach (var candidate in new[]
                 {
                     styleName,
                     styleId
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = candidate
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

            var digits = new string(
                normalized
                    .Reverse()
                    .TakeWhile(char.IsDigit)
                    .Reverse()
                    .ToArray());

            if (int.TryParse(
                    digits,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var level) &&
                level is >= 1 and <= 9)
            {
                return level;
            }
        }

        return null;
    }

    private static string? Value(
        XElement? root,
        XName name)
    {
        var value = root?
            .Element(name)?
            .Value
            .Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static DateTimeOffset? ParseDate(
        string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out var result)
            ? result
            : null;

    private sealed record StyleInfo(
        string? Name,
        int? HeadingLevel);
}
