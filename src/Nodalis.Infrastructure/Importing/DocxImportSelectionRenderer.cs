using System.Text;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Renders the Markdown represented by an explicit DOCX block selection.
/// </summary>
public static class DocxImportSelectionRenderer
{
    /// <summary>
    /// Renders one preview section using only the selected block indexes, in original document order.
    /// </summary>
    /// <param name="section">The preview section.</param>
    /// <param name="selectedBlockIndexes">The explicitly selected source block indexes.</param>
    /// <returns>The selected section Markdown.</returns>
    public static string Render(
            DocxImportSectionPreview section,
            IReadOnlyCollection<int> selectedBlockIndexes)
    {
        ArgumentNullException.ThrowIfNull(
            section);
        ArgumentNullException.ThrowIfNull(
            selectedBlockIndexes);

        HashSet<int> selected =
            selectedBlockIndexes.ToHashSet();

        DocxBlock[] blocks =
            section.Blocks
                .Where(block =>
                    selected.Contains(
                        block.BlockIndex))
                .OrderBy(block =>
                    block.BlockIndex)
                .Select(block =>
                    block.Block)
                .ToArray();

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "## " +
            section.SourceHeading.Trim());
        builder.AppendLine();

        string markdown =
            DocxMarkdownConverter.ConvertBlocks(
                    blocks)
                .TrimEnd();

        if (!string.IsNullOrWhiteSpace(
                markdown))
        {
            builder.AppendLine(
                markdown);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders one preview section with every source block selected.
    /// </summary>
    /// <param name="section">The preview section.</param>
    /// <returns>The complete section Markdown.</returns>
    public static string RenderAll(
            DocxImportSectionPreview section)
    {
        ArgumentNullException.ThrowIfNull(
            section);

        int[] indexes =
            section.Blocks
                .OrderBy(block =>
                    block.BlockIndex)
                .Select(block =>
                    block.BlockIndex)
                .ToArray();

        return Render(
            section,
            indexes);
    }
}
