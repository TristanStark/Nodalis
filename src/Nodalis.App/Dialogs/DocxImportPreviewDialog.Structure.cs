using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

namespace Nodalis.App.Dialogs;

public partial class DocxImportPreviewDialog
{
    private readonly Stack<(int SectionIndex, int BlockIndex, int? PreviousLevel)> _headingEdits = new Stack<(int SectionIndex, int BlockIndex, int? PreviousLevel)>();

    public sealed record HeadingChoice(int? Level, string Label);

    /// <summary>
    /// Filters only the displayed Word section list, not the import selection itself.
    /// </summary>
    private void SectionSearchText_Changed(object sender, TextChangedEventArgs e)
    {
        if (SectionsListBox?.ItemsSource is null)
        {
            return;
        }

        ICollectionView view = CollectionViewSource.GetDefaultView(SectionsListBox.ItemsSource);
        string query = SectionSearchTextBox.Text.Trim();
        view.Filter = item =>
            item is SectionRow row &&
            (query.Length == 0 ||
             row.SourceHeading.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        view.Refresh();
    }

    /// <summary>
    /// Displays current classification and transparent suggestions for the selected Word block.
    /// </summary>
    private void ContentTreeView_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (HeadingLevelComboBox is null || HeadingSuggestionText is null ||
            e.NewValue is not DocxImportSelectionNode node ||
            node.BlockIndex is not int index ||
            SectionsListBox.SelectedItem is not SectionRow section)
        {
            return;
        }

        DocxImportBlockPreview? block = section.Preview.Blocks
            .FirstOrDefault(candidate => candidate.BlockIndex == index);
        HeadingLevelComboBox.IsEnabled = block?.Block.Paragraph is not null;
        HeadingLevelComboBox.SelectedItem = HeadingLevelComboBox.Items
            .Cast<HeadingChoice>()
            .FirstOrDefault(candidate => candidate.Level == block?.Block.Paragraph?.HeadingLevel);
        HeadingSuggestionText.Text = block?.Block.Paragraph?.HeadingSuggestionReason ??
            "Correction manuelle : le DOCX source reste intact.";
    }

    /// <summary>
    /// Promotes or demotes a paragraph within the current mapped section.
    /// </summary>
    private void ApplyHeading_Click(object sender, RoutedEventArgs e)
    {
        if (ContentTreeView.SelectedItem is not DocxImportSelectionNode node ||
            node.BlockIndex is not int index ||
            SectionsListBox.SelectedItem is not SectionRow section ||
            HeadingLevelComboBox.SelectedItem is not HeadingChoice choice)
        {
            return;
        }

        DocxImportBlockPreview block = section.Preview.Blocks
            .Single(candidate => candidate.BlockIndex == index);
        if (block.Block.Paragraph is null ||
            block.Block.Paragraph.HeadingLevel == choice.Level)
        {
            return;
        }

        int? previous = DocxImportStructureEditor.ChangeHeading(
            section.Preview, index, choice.Level);
        _headingEdits.Push((section.Index, index, previous));
        UndoHeadingButton.IsEnabled = true;
        RefreshStructure(section);
    }

    /// <summary>
    /// Reverses the most recent title correction without reparsing or modifying Word.
    /// </summary>
    private void UndoHeading_Click(object sender, RoutedEventArgs e)
    {
        if (_headingEdits.Count == 0)
        {
            return;
        }

        (int sectionIndex, int blockIndex, int? previous) = _headingEdits.Pop();
        SectionRow section = _sections.Single(row => row.Index == sectionIndex);
        DocxImportStructureEditor.ChangeHeading(section.Preview, blockIndex, previous);
        UndoHeadingButton.IsEnabled = _headingEdits.Count > 0;
        RefreshStructure(section);
    }

    /// <summary>
    /// Rebuilds the visual hierarchy while preserving the explicit original block selection.
    /// </summary>
    private void RefreshStructure(SectionRow section)
    {
        DocxImportSelectionNode root = section.SelectionRoot;
        HashSet<int> selected = root.GetSelectedBlockIndexes().ToHashSet();
        Dictionary<int, DocxImportSelectionNode> nodes = root.DetachDescendants()
            .Where(node => node.BlockIndex is not null)
            .ToDictionary(node => node.BlockIndex!.Value);

        foreach (DocxImportBlockPreview block in section.Preview.Blocks)
        {
            DocxImportSelectionNode node = nodes[block.BlockIndex];
            node.KindLabel = block.KindLabel;
            node.MarkdownPreview = block.MarkdownPreview;
            node.IsSelected = selected.Contains(block.BlockIndex);
        }

        foreach (DocxImportBlockPreview block in section.Preview.Blocks)
        {
            DocxImportSelectionNode node = nodes[block.BlockIndex];
            DocxImportSelectionNode parent =
                block.ParentBlockIndex is int parentIndex &&
                nodes.TryGetValue(parentIndex, out DocxImportSelectionNode? ancestor)
                    ? ancestor
                    : root;
            parent.AddChild(node);
        }

        root.RefreshTreeSelection();
        DocumentSelectionRoot_SelectionChanged(root, EventArgs.Empty);
        ContentTreeView.Items.Refresh();
    }
}
