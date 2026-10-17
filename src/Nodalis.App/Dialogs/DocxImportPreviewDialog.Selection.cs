using System.Windows;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

namespace Nodalis.App.Dialogs;

public partial class DocxImportPreviewDialog
{
    /// <summary>
    /// Creates one UI row and its hierarchical selection tree from a DOCX section preview.
    /// </summary>
    /// <param name="section">The preview section.</param>
    /// <returns>The initialized section row.</returns>
    private static SectionRow CreateSectionRow(
            DocxImportSectionPreview section)
    {
        DocxImportSelectionNode root =
            new DocxImportSelectionNode
            {
                KindLabel =
                    "Section",
                DisplayText =
                    section.SourceHeading
            };

        Dictionary<int, DocxImportSelectionNode> nodes =
            new Dictionary<int, DocxImportSelectionNode>();

        foreach (global::Nodalis.Core.Importing.DocxImportBlockPreview block in section.Blocks.OrderBy(block =>
                     block.BlockIndex))
        {
            nodes[block.BlockIndex] =
                new DocxImportSelectionNode
                {
                    BlockIndex =
                        block.BlockIndex,
                    KindLabel =
                        block.KindLabel,
                    DisplayText =
                        block.DisplayText,
                    MarkdownPreview =
                        block.MarkdownPreview
                };
        }

        foreach (global::Nodalis.Core.Importing.DocxImportBlockPreview block in section.Blocks.OrderBy(block =>
                     block.BlockIndex))
        {
            DocxImportSelectionNode node =
                nodes[block.BlockIndex];

            if (block.ParentBlockIndex is int parentBlockIndex &&
                nodes.TryGetValue(
                    parentBlockIndex,
                    out DocxImportSelectionNode? parent))
            {
                parent.AddChild(
                    node);
            }
            else
            {
                root.AddChild(
                    node);
            }
        }

        SectionRow row =
            new SectionRow
            {
                Index =
                    section.Index,
                SourceHeading =
                    section.SourceHeading,
                TargetSection =
                    section.SuggestedTargetSection,
                BlockCount =
                    section.BlockCount,
                Preview =
                    section,
                SelectionRoot =
                    root
            };

        row.InitializeMarkdownPreview(
            section.MarkdownPreview);

        return row;
    }

    /// <summary>
    /// Updates the live section Markdown and invalidates the write plan after any hierarchical selection change.
    /// </summary>
    /// <param name="sender">The changed section root.</param>
    /// <param name="e">The event arguments.</param>
    private void DocumentSelectionRoot_SelectionChanged(
            object? sender,
            EventArgs e)
    {
        foreach (global::Nodalis.App.Dialogs.DocxImportPreviewDialog.SectionRow section in _sections)
        {
            IReadOnlyList<int> selectedBlockIndexes =
                section.SelectionRoot.GetSelectedBlockIndexes();

            string markdown =
                section.Include
                    ? DocxImportSelectionRenderer.Render(
                        section.Preview,
                        selectedBlockIndexes)
                    : string.Empty;

            section.RefreshSelection(
                markdown);
        }

        UpdateSelectionSummary();
        InvalidatePlan();
    }

    /// <summary>
    /// Selects every section and every selectable DOCX block.
    /// </summary>
    /// <param name="sender">The button that requested the operation.</param>
    /// <param name="e">The routed event arguments.</param>
    private void SelectAllBlocks_Click(
            object sender,
            RoutedEventArgs e)
    {
        _documentSelectionRoot.IsSelected =
            true;
    }

    /// <summary>
    /// Deselects every section and every selectable DOCX block without changing the staged source document.
    /// </summary>
    /// <param name="sender">The button that requested the operation.</param>
    /// <param name="e">The routed event arguments.</param>
    private void DeselectAllBlocks_Click(
            object sender,
            RoutedEventArgs e)
    {
        _documentSelectionRoot.IsSelected =
            false;
    }

    /// <summary>
    /// Displays the current fine-grained selection size in the dialog footer.
    /// </summary>
    private void UpdateSelectionSummary()
    {
        if (SummaryText is null)
        {
            return;
        }

        int selectedSections =
            _sections.Count(section =>
                section.Include);
        int selectedBlocks =
            _sections.Sum(section =>
                section.SelectionRoot
                    .GetSelectedBlockIndexes()
                    .Count);
        int totalBlocks =
            _sections.Sum(section =>
                section.BlockCount);

        SummaryText.Text =
            selectedSections +
            " section(s) · " +
            selectedBlocks +
            "/" +
            totalBlocks +
            " bloc(s) sélectionné(s)";
    }
}
