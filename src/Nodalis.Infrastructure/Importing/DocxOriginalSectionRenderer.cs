using System.Text;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Produces a safe, read-only text interpretation of the original DOCX section.
/// This is not intended to reproduce the Office pixel layout.
/// </summary>
public static class DocxOriginalSectionRenderer
{
    /// <summary>
    /// Renders every original Word source block, including blocks excluded from the output.
    /// </summary>
    public static string Render(DocxImportSectionPreview section)
    {
        ArgumentNullException.ThrowIfNull(section);
        StringBuilder buffer = new StringBuilder();
        buffer.AppendLine("SECTION WORD : " + section.SourceHeading);
        buffer.AppendLine();
        foreach (DocxImportBlockPreview original in section.Blocks.OrderBy(x => x.BlockIndex))
        {
            DocxBlock block = original.Block;
            buffer.Append('#').Append(original.BlockIndex).Append(" · ")
                .Append(original.KindLabel).AppendLine();
            if (block.Paragraph is DocxParagraph paragraph)
            {
                buffer.AppendLine(paragraph.Text);
                foreach (DocxInlineMedia media in paragraph.Media)
                {
                    buffer.Append("  [")
                        .Append(media.IsSupported ? "Image intégrée" : "Objet non converti")
                        .Append("] ")
                        .Append(media.AlternativeText);
                    if (!string.IsNullOrWhiteSpace(media.Warning))
                    {
                        buffer.Append(" — ").Append(media.Warning);
                    }

                    buffer.AppendLine();
                }
            }
            else if (block.Table is DocxTable table)
            {
                AppendTable(buffer, table, 0);
            }

            buffer.AppendLine();
        }

        return buffer.ToString();
    }

    /// <summary>Appends rows, cells and embedded tables without interpreting markup.</summary>
    private static void AppendTable(StringBuilder output, DocxTable table, int depth)
    {
        foreach (DocxTableRow row in table.Rows)
        {
            output.Append(new string(' ', Math.Min(depth * 2, 10)));
            output.AppendLine(string.Join(" | ", row.Cells.Select(cell => cell.Text)));
            foreach (DocxTableCell cell in row.Cells)
            {
                foreach (DocxTable nested in cell.NestedTables)
                {
                    AppendTable(output, nested, depth + 1);
                }
            }
        }

        foreach (string warning in table.Warnings)
        {
            output.AppendLine("⚠ " + warning);
        }
    }
}
