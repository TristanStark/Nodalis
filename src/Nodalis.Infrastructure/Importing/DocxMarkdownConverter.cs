using System.Text;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

public static class DocxMarkdownConverter
{
    public static string ConvertBlocks(
        IReadOnlyList<DocxBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var builder = new StringBuilder();

        foreach (var block in blocks)
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

    private static void AppendParagraph(
        StringBuilder builder,
        DocxParagraph paragraph)
    {
        var text = ApplyHyperlinks(
            paragraph.Text,
            paragraph.Hyperlinks);

        if (string.IsNullOrWhiteSpace(text))
        {
            builder.AppendLine();
            return;
        }

        if (paragraph.HeadingLevel is int headingLevel)
        {
            var level = Math.Clamp(
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
            var level = Math.Max(
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

    private static void AppendTable(
        StringBuilder builder,
        DocxTable table)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        var columnCount = table.Rows.Max(row =>
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

        for (var index = 0;
             index < columnCount;
             index++)
        {
            builder.Append(" --- |");
        }

        builder.AppendLine();

        foreach (var row in table.Rows.Skip(1))
        {
            AppendTableRow(
                builder,
                row,
                columnCount);
        }

        builder.AppendLine();
    }

    private static void AppendTableRow(
        StringBuilder builder,
        DocxTableRow row,
        int columnCount)
    {
        builder.Append('|');

        for (var index = 0;
             index < columnCount;
             index++)
        {
            var text = index < row.Cells.Count
                ? EscapeTableCell(
                    row.Cells[index].Text)
                : string.Empty;

            builder.Append(' ');
            builder.Append(text);
            builder.Append(" |");
        }

        builder.AppendLine();
    }

    private static string ApplyHyperlinks(
        string text,
        IReadOnlyList<DocxHyperlink> hyperlinks)
    {
        var result = text;

        foreach (var hyperlink in hyperlinks)
        {
            if (string.IsNullOrWhiteSpace(hyperlink.Text) ||
                string.IsNullOrWhiteSpace(hyperlink.Target))
            {
                continue;
            }

            var index = result.IndexOf(
                hyperlink.Text,
                StringComparison.CurrentCulture);

            if (index < 0)
            {
                continue;
            }

            var replacement =
                $"[{EscapeLinkText(hyperlink.Text)}](<{hyperlink.Target.Trim()}>)";

            result =
                result[..index] +
                replacement +
                result[(index + hyperlink.Text.Length)..];
        }

        return result;
    }

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

    private static string NormalizeSpacing(string value)
    {
        var normalized = value
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
