using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Represents one tri-state node in the hierarchical DOCX import selection tree.
/// </summary>
public sealed class DocxImportSelectionNode : INotifyPropertyChanged
{
    private bool? _isSelected = true;

    /// <summary>
    /// Raised once at the root whenever selection changes anywhere in the subtree.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised when a bindable property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the original DOCX block index, or null for a synthetic section root.
    /// </summary>
    public int? BlockIndex { get; init; }

    /// <summary>
    /// Gets the compact block kind label.
    /// </summary>
    public required string KindLabel { get; init; }

    /// <summary>
    /// Gets the readable node text.
    /// </summary>
    public required string DisplayText { get; init; }

    /// <summary>
    /// Gets the Markdown rendering of this individual source block.
    /// </summary>
    public string MarkdownPreview { get; init; } = string.Empty;

    /// <summary>
    /// Gets this node's children.
    /// </summary>
    public ObservableCollection<DocxImportSelectionNode> Children { get; } = [];

    /// <summary>
    /// Gets the parent selection node.
    /// </summary>
    public DocxImportSelectionNode? Parent { get; private set; }

    /// <summary>
    /// Gets or sets the tri-state subtree selection. Null means partially selected.
    /// </summary>
    public bool? IsSelected
    {
        get => _isSelected;
        set
        {
            if (value is null)
            {
                return;
            }

            bool selected =
                value.Value;

            SetSubtreeSelection(
                selected);
            RefreshAncestors();
            RaiseRootSelectionChanged();
        }
    }

    /// <summary>
    /// Adds one child node and connects it to this selection subtree.
    /// </summary>
    /// <param name="child">The child to add.</param>
    public void AddChild(
            DocxImportSelectionNode child)
    {
        ArgumentNullException.ThrowIfNull(
            child);

        if (child.Parent is not null)
        {
            throw new InvalidOperationException(
                "Le nœud de sélection DOCX possède déjà un parent.");
        }

        child.Parent =
            this;
        Children.Add(
            child);
    }

    /// <summary>
    /// Returns selected source block indexes in original tree order.
    /// </summary>
    /// <returns>The selected source block indexes.</returns>
    public IReadOnlyList<int> GetSelectedBlockIndexes()
    {
        List<int> result =
            new List<int>();

        CollectSelectedBlockIndexes(
            result);

        return result;
    }

    /// <summary>
    /// Applies one concrete selection state to this node and every descendant.
    /// </summary>
    /// <param name="selected">The state to apply.</param>
    private void SetSubtreeSelection(
            bool selected)
    {
        SetSelectionValue(
            selected);

        foreach (global::Nodalis.App.Dialogs.DocxImportSelectionNode child in Children)
        {
            child.SetSubtreeSelection(
                selected);
        }
    }

    /// <summary>
    /// Recomputes all ancestor tri-state values after one subtree changes.
    /// </summary>
    private void RefreshAncestors()
    {
        DocxImportSelectionNode? current =
            Parent;

        while (current is not null)
        {
            bool allSelected =
                current.Children.Count == 0 ||
                current.Children.All(child =>
                    child._isSelected == true);
            bool allDeselected =
                current.Children.Count > 0 &&
                current.Children.All(child =>
                    child._isSelected == false);

            current.SetSelectionValue(
                allSelected
                    ? true
                    : allDeselected
                        ? false
                        : null);

            current =
                current.Parent;
        }
    }

    /// <summary>
    /// Updates the local tri-state value and notifies WPF bindings.
    /// </summary>
    /// <param name="value">The new local state.</param>
    private void SetSelectionValue(
            bool? value)
    {
        if (_isSelected == value)
        {
            return;
        }

        _isSelected =
            value;
        OnPropertyChanged(
            nameof(IsSelected));
    }

    /// <summary>
    /// Raises the selection event only on the root node.
    /// </summary>
    private void RaiseRootSelectionChanged()
    {
        DocxImportSelectionNode root =
            this;

        while (root.Parent is not null)
        {
            root =
                root.Parent;
        }

        root.SelectionChanged?.Invoke(
            root,
            EventArgs.Empty);
    }

    /// <summary>
    /// Collects selected source block indexes while preserving the hierarchy's original order.
    /// </summary>
    /// <param name="destination">The destination index list.</param>
    private void CollectSelectedBlockIndexes(
            ICollection<int> destination)
    {
        if (BlockIndex is int blockIndex &&
            _isSelected != false)
        {
            destination.Add(
                blockIndex);
        }

        foreach (global::Nodalis.App.Dialogs.DocxImportSelectionNode child in Children)
        {
            child.CollectSelectedBlockIndexes(
                destination);
        }
    }

    /// <summary>
    /// Raises a property change notification.
    /// </summary>
    /// <param name="propertyName">The changed property name.</param>
    private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}
