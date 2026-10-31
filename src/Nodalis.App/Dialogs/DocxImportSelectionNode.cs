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
    private bool _selfSelected = true;

    /// <summary>
    /// Raised once at the root whenever selection changes anywhere in the subtree.
    /// The sender identifies the changed node; whole-document operations send the document root.
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
    private string _kindLabel = string.Empty;

    public required string KindLabel
    {
        get => _kindLabel;
        set
        {
            _kindLabel = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the readable node text.
    /// </summary>
    public required string DisplayText { get; init; }

    /// <summary>
    /// Gets the Markdown rendering of this individual source block.
    /// </summary>
    private string _markdownPreview = string.Empty;

    public string MarkdownPreview
    {
        get => _markdownPreview;
        set
        {
            _markdownPreview = value;
            OnPropertyChanged();
        }
    }

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
    /// Removes descendants while retaining their source selections for structural regrouping.
    /// </summary>
    /// <returns>All detached descendants in the previous visual order.</returns>
    public IReadOnlyList<DocxImportSelectionNode> DetachDescendants()
    {
        List<DocxImportSelectionNode> descendants = [];
        CollectDescendants(this, descendants);
        foreach (DocxImportSelectionNode child in descendants)
        {
            child.Parent = null;
            child.Children.Clear();
        }

        Children.Clear();
        return descendants;

        static void CollectDescendants(
            DocxImportSelectionNode parent,
            List<DocxImportSelectionNode> target)
        {
            foreach (DocxImportSelectionNode child in parent.Children)
            {
                target.Add(child);
                CollectDescendants(child, target);
            }
        }
    }

    /// <summary>
    /// Recomputes tri-state values after a non-destructive hierarchy correction.
    /// </summary>
    public void RefreshTreeSelection()
    {
        RefreshRecursively(this);
        RefreshAncestors();
        RaiseRootSelectionChanged();

        static void RefreshRecursively(DocxImportSelectionNode node)
        {
            foreach (DocxImportSelectionNode child in node.Children)
            {
                RefreshRecursively(child);
            }

            if (node.Children.Count == 0)
            {
                return;
            }

            bool all = node._selfSelected &&
                node.Children.All(child => child._isSelected == true);
            bool none = !node._selfSelected &&
                node.Children.All(child => child._isSelected == false);
            node.SetSelectionValue(all ? true : none ? false : null);
        }
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
        _selfSelected = selected;
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
                current._selfSelected &&
                current.Children.All(child => child._isSelected == true);
            bool allDeselected =
                !current._selfSelected &&
                current.Children.All(child => child._isSelected == false);

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
            this,
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
            _selfSelected)
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
