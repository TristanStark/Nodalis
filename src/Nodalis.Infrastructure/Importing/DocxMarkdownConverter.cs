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
    /// <returns>The result of the operation.</returns>
private static void AppendParagraph(
        StringBuilder builder,
        DocxParagraph paragraph)
    {
        string text = ApplyHyperlinks(
            paragraph.Text,
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
    /// Performs the <c>AppendTable</c> operation.
    /// </summary>
    /// <param name="builder">The <c>builder</c> value.</param>
    /// <param name="table">The <c>table</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static void AppendTable(
        StringBuilder builder,
        DocxTable table)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        int columnCount = table.Rows.Max(row =>
            row.Cells.Count);

        if (columnCount == 0)
        {
            return;
        }

        AppendTableRow(
            builder,
            table.Rows[0],
            columnCount);

        builder.Append('|');

        for (int index = 0;
             index < columnCount;
             index++)
        {
            builder.Append(" --- |");
        }

        builder.AppendLine();

        foreach (global::Nodalis.Core.Importing.DocxTableRow row in table.Rows.Skip(1))
        {
            AppendTableRow(
                builder,
                row,
                columnCount);
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Performs the <c>AppendTableRow</c> operation.
    /// </summary>
    /// <param name="builder">The <c>builder</c> value.</param>
    /// <param name="row">The <c>row</c> value.</param>
    /// <param name="columnCount">The <c>columnCount</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

        return normalized.Trim() +
               (string.IsNullOrWhiteSpace(normalized)
                   ? string.Empty
                   : "\n");
    }
}
