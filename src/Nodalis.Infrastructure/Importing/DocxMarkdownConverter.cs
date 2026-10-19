using System.Text;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

public static class DocxMarkdownConverter
{
    /// <summary>
    /// Performs the <c>ConvertBlocks</c> operation.
    /// </summary>
    /// <param name="blocks">The <c>blocks</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static string ConvertBlocks(
            IReadOnlyList<DocxBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        global::System.Text.StringBuilder builder = new StringBuilder();

        foreach (global::Nodalis.Core.Importing.DocxBlock block in blocks)
        {
            switch (block.Kind)
            {
                case DocxBlockKind.Paragraph
                    when block.Paragraph is not null:
                    AppendParagraph(
                        builder,
                        block.Paragraph);
                    break;

                case DocxBlockKind.Table
                    when block.Table is not null:
                    AppendTable(
                        builder,
                        block.Table);
                    break;
            }
        }

        return NormalizeSpacing(
            builder.ToString());
    }

    /// <summary>
    /// Performs the <c>AppendParagraph</c> operation.
    /// </summary>
    /// <param name="builder">The <c>builder</c> value.</param>
    /// <param name="paragraph">The <c>paragraph</c> value.</param>
    private static void AppendParagraph(
            StringBuilder builder,
            DocxParagraph paragraph)
    {
        string text = ApplyHyperlinks(
            InsertInlineMedia(paragraph.Text, paragraph.Media),
            paragraph.Hyperlinks);

        if (string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine();
            return;
        }

        if (paragraph.HeadingLevel is int headingLevel)
        {
            int level = Math.Clamp(
                headingLevel,
                1,
                6);

            builder.Append(
                new string(
                    '#',
                    level));
            builder.Append(' ');
            builder.AppendLine(
                text.Trim());
            builder.AppendLine();
            return;
        }

        if (paragraph.IsListItem)
        {
            int level = Math.Max(
                0,
                paragraph.ListLevel ?? 0);

            builder.Append(
                new string(
                    ' ',
                    level * 2));
            builder.Append("- ");
            builder.AppendLine(
                text.Trim());
            return;
        }

        builder.AppendLine(
            text.Trim());
        builder.AppendLine();
    }

    /// <summary>
    /// Injects Markdown images in the source character order, retaining placeholder warnings
    /// for unsupported objects and avoiding active links or downloaded resources.
    /// </summary>
    private static string InsertInlineMedia(string text, IReadOnlyList<DocxInlineMedia> media)
    {
        if (media.Count == 0)
        {
            return text;
        }

        StringBuilder result = new StringBuilder();
        int previousOffset = 0;
        foreach (DocxInlineMedia item in media.OrderBy(item => item.TextOffset))
        {
            int position = Math.Clamp(item.TextOffset, previousOffset, text.Length);
            result.Append(text, previousOffset, position - previousOffset);
            if (result.Length > 0 && !char.IsWhiteSpace(result[^1]))
            {
                result.Append(' ');
            }

            if (item.IsSupported)
            {
                string alt = item.AlternativeText
                    .Replace("[", "\\[", StringComparison.Ordinal)
                    .Replace("]", "\\]", StringComparison.Ordinal)
                    .Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " ", StringComparison.Ordinal);
                result.Append("![").Append(alt).Append("](")
                    .Append(item.MarkdownRelativePath).Append(')');
            }
            else
            {
                result.Append("[⚠ Objet Word non converti : ")
                    .Append((item.Warning ?? "élément non pris en charge")
                        .Replace("[", "(", StringComparison.Ordinal)
                        .Replace("]", ")", StringComparison.Ordinal))
                    .Append(']');
            }

            result.Append(' ');
            previousOffset = position;
        }

        result.Append(text, previousOffset, text.Length - previousOffset);
        return result.ToString();
    }

    /// <summary>
    /// Performs the <c>AppendTable</c> operation.
    /// </summary>
    /// <param name="builder">The <c>builder</c> value.</param>
    /// <param name="table">The <c>table</c> value.</param>
    private static void AppendTable(
            StringBuilder builder,
            DocxTable table)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        bool complex = table.Warnings.Count > 0 ||
            table.Rows.Any(row => row.Cells.Any(cell =>
                cell.ColumnSpan != 1 ||
                cell.VerticalMerge != DocxVerticalMerge.None ||
                cell.NestedTables.Count > 0)) ||
            table.Rows.Select(row => row.Cells.Count).Distinct().Count() > 1;

        if (complex)
        {
            // Never pretend merged or nested Word cells can be represented faithfully by pipes.
            builder.AppendLine("> ⚠ Tableau Word complexe : HTML local, fusions verticales à vérifier ; original DOCX conservé.");
            builder.AppendLine();
            AppendHtmlTable(builder, table);
            builder.AppendLine();
            return;
        }

        int columns = table.Rows.Max(row => row.Cells.Count);
        if (columns == 0)
        {
            return;
        }

        AppendTableRow(builder, table.Rows[0], columns);
        builder.Append('|');
        for (int index = 0; index < columns; index++)
        {
            builder.Append(" --- |");
        }

        builder.AppendLine();
        foreach (DocxTableRow row in table.Rows.Skip(1))
        {
            AppendTableRow(builder, row, columns);
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Writes local-only HTML for Word tables whose layout cannot fit standard Markdown.
    /// All source text is escaped; no script, event, URL or resource is copied to markup.
    /// </summary>
    /// <param name="builder">Markdown output buffer.</param>
    /// <param name="table">Complex or nested Word table.</param>
    private static void AppendHtmlTable(StringBuilder builder, DocxTable table)
    {
        builder.AppendLine("<table>");
        foreach (DocxTableRow row in table.Rows)
        {
            builder.AppendLine("  <tr>");
            foreach (DocxTableCell cell in row.Cells)
            {
                string element = row.IsHeader ? "th" : "td";
                builder.Append("    <").Append(element);

                if (cell.ColumnSpan > 1)
                {
                    builder.Append(" colspan=\"").Append(cell.ColumnSpan).Append('"');
                }

                if (cell.VerticalMerge != DocxVerticalMerge.None)
                {
                    // Preserve the Word merge state without fabricating a rowspan.
                    builder.Append(" data-word-vmerge=\"")
                        .Append(cell.VerticalMerge == DocxVerticalMerge.Start
                            ? "restart" : "continue").Append('"');
                }

                builder.Append('>');
                if (cell.Parts.Count == 0)
                {
                    // Compatibility with fixture tables constructed without ordered parts.
                    AppendEscapedHtmlText(builder, cell.Text);
                    foreach (DocxTable nested in cell.NestedTables)
                    {
                        AppendHtmlTable(builder, nested);
                    }
                }
                else
                {
                    bool precedingParagraph = false;
                    foreach (DocxTableCellPart part in cell.Parts)
                    {
                        if (part.Text is string text)
                        {
                            if (precedingParagraph)
                            {
                                builder.Append("<br>");
                            }

                            AppendEscapedHtmlText(builder, text);
                            precedingParagraph = true;
                        }
                        else if (part.NestedTable is DocxTable childTable)
                        {
                            AppendHtmlTable(builder, childTable);
                            precedingParagraph = false;
                        }
                    }
                }

                builder.Append("</").Append(element).AppendLine(">");
            }

            builder.AppendLine("  </tr>");
        }

        builder.AppendLine("</table>");
    }

    /// <summary>
    /// Appends safe text content while retaining all paragraph line breaks.
    /// </summary>
    /// <param name="builder">Destination HTML buffer.</param>
    /// <param name="text">Source Word cell text.</param>
    private static void AppendEscapedHtmlText(StringBuilder builder, string text)
    {
        builder.Append(EscapeHtml(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br>", StringComparison.Ordinal));
    }

    /// <summary>Escapes arbitrary source Word text from HTML execution or injection.</summary>
    /// <param name="text">Raw cell text.</param>
    /// <returns>Only encoded character data, safe inside HTML cells.</returns>
    private static string EscapeHtml(string text) =>
        (text ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    /// <summary>
    /// Performs the <c>AppendTableRow</c> operation.
    /// </summary>
    /// <param name="builder">The <c>builder</c> value.</param>
    /// <param name="row">The <c>row</c> value.</param>
    /// <param name="columnCount">The <c>columnCount</c> value.</param>
    private static void AppendTableRow(
            StringBuilder builder,
            DocxTableRow row,
            int columnCount)
    {
        builder.Append('|');

        for (int index = 0;
             index < columnCount;
             index++)
        {
            string text = index < row.Cells.Count
                ? EscapeTableCell(
                    row.Cells[index].Text)
                : string.Empty;

            builder.Append(' ');
            builder.Append(text);
            builder.Append(" |");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Performs the <c>ApplyHyperlinks</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="hyperlinks">The <c>hyperlinks</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string ApplyHyperlinks(
            string text,
            IReadOnlyList<DocxHyperlink> hyperlinks)
    {
        string result = text;

        foreach (global::Nodalis.Core.Importing.DocxHyperlink hyperlink in hyperlinks)
        {
            if (string.IsNullOrWhiteSpace(hyperlink.Text) ||
                string.IsNullOrWhiteSpace(hyperlink.Target))
            {
                continue;
            }

            int index = result.IndexOf(
                hyperlink.Text,
                StringComparison.CurrentCulture);

            if (index < 0)
            {
                continue;
            }

            string replacement =
                $"[{EscapeLinkText(hyperlink.Text)}](<{hyperlink.Target.Trim()}>)";

            result =
                result[..index] +
                replacement +
                result[(index + hyperlink.Text.Length)..];
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>EscapeTableCell</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string EscapeTableCell(string value) =>
            (value ?? string.Empty)
                .Replace(
                    "\r\n",
                    "<br>",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Replace(
                    "\n",
                    "<br>",
                    StringComparison.Ordinal)
                .Replace(
                    "|",
                    "\\|",
                    StringComparison.Ordinal)
                .Trim();

    /// <summary>
    /// Performs the <c>EscapeLinkText</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string EscapeLinkText(string value) =>
            value
                .Replace(
                    "[",
                    "\\[",
                    StringComparison.Ordinal)
                .Replace(
                    "]",
                    "\\]",
                    StringComparison.Ordinal);

    /// <summary>
    /// Performs the <c>NormalizeSpacing</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeSpacing(string value)
    {
        string normalized = value
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

        while (normalized.Contains(
                   "\n\n\n",
                   StringComparison.Ordinal))
        {
            normalized = normalized.Replace(
                "\n\n\n",
                "\n\n",
                StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(
                normalized))
        {
            return string.Empty;
        }

        return normalized.Trim(
                   '\n') +
               "\n";
    }
}
