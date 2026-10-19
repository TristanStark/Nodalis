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

        string paragraphText = ExtractText(paragraph).TrimEnd();
        int? directOutline = ParseIntegerAttribute(
            properties?.Element(WordNamespace + "outlineLvl"),
            "val");

        // The direct outline setting overrides both the style and its ancestors.
        // Level 9 means regular body text in Word, not heading level 10.
        int? headingLevel = directOutline is int outline
            ? outline is >= 0 and <= 8 ? outline + 1 : null
            : style is not null
                ? style.HeadingLevel
                : InferHeadingLevel(styleId, null);

        (int? suggestedLevel, string? suggestionReason) =
            headingLevel is null && numberingId is null
                ? SuggestUnstyledHeading(paragraph, paragraphText)
                : (null, null);

        return new DocxParagraph
        {
            Text = paragraphText,
            StyleId = styleId,
            StyleName = style?.Name,
            HeadingLevel = headingLevel,
            SuggestedHeadingLevel = suggestedLevel,
            HeadingSuggestionReason = suggestionReason,
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
    private static DocxTable ParseTable(XElement table, int nestingDepth = 0)
    {
        if (nestingDepth > 4)
        {
            string fallbackText = ExtractText(table);
            return new DocxTable
            {
                Rows =
                [
                    new DocxTableRow
                    {
                        Cells = [new DocxTableCell { Text = fallbackText }]
                    }
                ],
                Warnings = ["Tableau imbriqué au-delà de quatre niveaux : représentation textuelle conservée."]
            };
        }

        List<DocxTableRow> rows = [];
        List<string> warnings = [];
        XElement[] rawRows = table.Elements(WordNamespace + "tr").ToArray();

        if (rawRows.Length > 15000)
        {
            throw new InvalidDataException("Table DOCX anormalement volumineuse.");
        }

        foreach (XElement row in rawRows)
        {
            List<DocxTableCell> cells = [];
            XElement[] rawCells = row.Elements(WordNamespace + "tc").ToArray();
            if (rawCells.Length > 256)
            {
                throw new InvalidDataException("Une table DOCX contient trop de cellules par ligne.");
            }

            foreach (XElement cell in rawCells)
            {
                XElement? properties = cell.Element(WordNamespace + "tcPr");
                int columnSpan = ParseIntegerAttribute(
                    properties?.Element(WordNamespace + "gridSpan"), "val") ?? 1;
                if (columnSpan is < 1 or > 256)
                {
                    throw new InvalidDataException("Fusion de colonnes Word invalide.");
                }

                XElement? verticalProperty = properties?.Element(WordNamespace + "vMerge");
                string? verticalValue = verticalProperty?
                    .Attribute(WordNamespace + "val")?.Value;
                DocxVerticalMerge vertical = verticalProperty is null
                    ? DocxVerticalMerge.None
                    : string.Equals(verticalValue, "restart", StringComparison.OrdinalIgnoreCase)
                        ? DocxVerticalMerge.Start
                        : DocxVerticalMerge.Continue;

                List<DocxTableCellPart> ordered = [];
                foreach (XElement child in cell.Elements())
                {
                    if (child.Name == WordNamespace + "p")
                    {
                        ordered.Add(new DocxTableCellPart { Text = ExtractText(child) });
                    }
                    else if (child.Name == WordNamespace + "tbl")
                    {
                        ordered.Add(new DocxTableCellPart
                        {
                            NestedTable = ParseTable(child, nestingDepth + 1)
                        });
                    }
                }

                List<string> paragraphs = ordered
                    .Where(part => part.Text is not null)
                    .Select(part => part.Text!)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                List<DocxTable> nested = ordered
                    .Where(part => part.NestedTable is not null)
                    .Select(part => part.NestedTable!)
                    .ToList();

                if (columnSpan > 1)
                {
                    warnings.Add("Cellules fusionnées horizontalement : HTML local plutôt que tableau Markdown.");
                }

                if (vertical != DocxVerticalMerge.None)
                {
                    warnings.Add("Fusion verticale Word : structure signalée, fidélité du rendu à vérifier.");
                }

                if (nested.Count > 0)
                {
                    warnings.Add("Tableau imbriqué conservé dans une représentation HTML locale.");
                }

                cells.Add(new DocxTableCell
                {
                    Text = string.Join(Environment.NewLine, paragraphs),
                    ColumnSpan = columnSpan,
                    VerticalMerge = vertical,
                    NestedTables = nested,
                    Parts = ordered
                });
            }

            if (cells.Sum(cell => cell.ColumnSpan) > 512)
            {
                throw new InvalidDataException("La largeur d'une table DOCX dépasse la limite de sécurité.");
            }

            bool header = row.Element(WordNamespace + "trPr")?
                .Element(WordNamespace + "tblHeader") is not null;

            rows.Add(new DocxTableRow { Cells = cells, IsHeader = header });
        }

        if (rows.Select(row => row.Cells.Sum(cell => cell.ColumnSpan))
                .Distinct().Count() > 1)
        {
            warnings.Add("Largeurs de lignes Word différentes : rendu HTML de secours.");
        }

        return new DocxTable
        {
            Rows = rows,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList()
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
        ZipArchiveEntry? entry = archive.GetEntry("word/styles.xml");
        if (entry is null)
        {
            return new Dictionary<string, StyleInfo>(StringComparer.OrdinalIgnoreCase);
        }

        cancellationToken.ThrowIfCancellationRequested();
        XDocument xml;
        using (Stream stream = entry.Open())
        {
            xml = XDocument.Load(stream);
        }

        Dictionary<string, UnresolvedStyle> definitions =
            new Dictionary<string, UnresolvedStyle>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement element in xml.Descendants(WordNamespace + "style"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? id = element.Attribute(WordNamespace + "styleId")?.Value;
            string? type = element.Attribute(WordNamespace + "type")?.Value;
            if (string.IsNullOrWhiteSpace(id) ||
                (!string.IsNullOrWhiteSpace(type) &&
                 !string.Equals(type, "paragraph", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            definitions[id] = new UnresolvedStyle(
                element.Element(WordNamespace + "name")?.Attribute(WordNamespace + "val")?.Value,
                element.Element(WordNamespace + "basedOn")?.Attribute(WordNamespace + "val")?.Value,
                ParseIntegerAttribute(
                    element.Element(WordNamespace + "pPr")?.Element(WordNamespace + "outlineLvl"),
                    "val"));
        }

        Dictionary<string, StyleInfo> resolved =
            new Dictionary<string, StyleInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in definitions.Keys)
        {
            ResolveStyle(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return resolved;

        StyleInfo ResolveStyle(string id, HashSet<string> ancestry)
        {
            if (resolved.TryGetValue(id, out StyleInfo? cached))
            {
                return cached;
            }

            if (!definitions.TryGetValue(id, out UnresolvedStyle? definition))
            {
                return new StyleInfo(null, null);
            }

            if (!ancestry.Add(id))
            {
                // A malformed basedOn cycle cannot inherit an outline level.
                return new StyleInfo(definition.Name, null);
            }

            int? inherited = null;
            if (!string.IsNullOrWhiteSpace(definition.BasedOn))
            {
                inherited = ResolveStyle(definition.BasedOn, ancestry).HeadingLevel;
            }

            ancestry.Remove(id);
            int? heading = definition.OutlineLevel is int level
                ? level is >= 0 and <= 8 ? level + 1 : null
                : InferHeadingLevel(id, definition.Name) ?? inherited;

            StyleInfo result = new StyleInfo(definition.Name, heading);
            resolved[id] = result;
            return result;
        }
    }

    /// <summary>
    /// Suggests (never automatically applies) a level for obvious but unstyled visual headings.
    /// </summary>
    /// <param name="paragraph">Word paragraph XML element.</param>
    /// <param name="text">Extracted visible text.</param>
    /// <returns>A reversible candidate and human-readable explanation.</returns>
    private static (int? Level, string? Reason) SuggestUnstyledHeading(
        XElement paragraph,
        string text)
    {
        string candidate = text.Trim();
        if (candidate.Length is < 4 or > 100 ||
            candidate.Contains('\n') ||
            candidate.EndsWith('.') ||
            candidate.EndsWith(';'))
        {
            return (null, null);
        }

        XElement[] contentRuns = paragraph.Descendants(WordNamespace + "r")
            .Where(run => !string.IsNullOrWhiteSpace(ExtractText(run)))
            .ToArray();

        if (contentRuns.Length == 0)
        {
            return (null, null);
        }

        int boldRuns = contentRuns.Count(run =>
        {
            XElement? bold = run.Element(WordNamespace + "rPr")?.Element(WordNamespace + "b");
            string? flag = bold?.Attribute(WordNamespace + "val")?.Value;
            return bold is not null && flag is not ("false" or "0" or "off");
        });

        int size = contentRuns
            .Select(run => ParseIntegerAttribute(
                run.Element(WordNamespace + "rPr")?.Element(WordNamespace + "sz"), "val") ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        bool mostlyBold = boldRuns * 4 >= contentRuns.Length * 3;
        if (size >= 32 && mostlyBold)
        {
            return (1, "Titre probable : texte court, taille au moins 16 pt et majoritairement gras ; confirmation nécessaire.");
        }

        int beforeSpacing = ParseIntegerAttribute(
            paragraph.Element(WordNamespace + "pPr")?
                .Element(WordNamespace + "spacing"), "before") ?? 0;

        if (mostlyBold && beforeSpacing >= 120 && candidate.Length <= 70)
        {
            return (2, "Sous-titre probable : texte court, majoritairement gras et espacement avant ; confirmation nécessaire.");
        }

        return (null, null);
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

    private sealed record UnresolvedStyle(
        string? Name,
        string? BasedOn,
        int? OutlineLevel);

    private sealed record StyleInfo(
        string? Name,
        int? HeadingLevel);
}
