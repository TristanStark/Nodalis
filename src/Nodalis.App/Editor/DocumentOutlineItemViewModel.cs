using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nodalis.Core.Markdown;

namespace Nodalis.App.Editor;

/// <summary>
/// Represents one heading in the document outline tree.
/// </summary>
public sealed class DocumentOutlineItemViewModel : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private bool _isCurrent;

    /// <summary>
    /// Initializes a new outline item from a parsed Markdown heading.
    /// </summary>
    /// <param name="entry">The source heading represented by this item.</param>
    public DocumentOutlineItemViewModel(
            MarkdownOutlineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    /// <summary>
    /// Raised when a bindable outline property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the parsed Markdown heading.
    /// </summary>
    public MarkdownOutlineEntry Entry { get; }

    /// <summary>
    /// Gets the visible heading title.
    /// </summary>
    public string Title => Entry.Title;

    /// <summary>
    /// Gets the Markdown heading level.
    /// </summary>
    public int Level => Entry.Level;

    /// <summary>
    /// Gets the heading character offset in the original Markdown source.
    /// </summary>
    public int Offset => Entry.Offset;

    /// <summary>
    /// Gets the one-based source line number.
    /// </summary>
    public int LineNumber => Entry.LineNumber;

    /// <summary>
    /// Gets the child headings nested below this item.
    /// </summary>
    public ObservableCollection<DocumentOutlineItemViewModel> Children { get; } = [];

    /// <summary>
    /// Gets or sets whether this outline branch is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets whether this item is selected in the outline tree.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets whether the editor caret currently belongs to this section.
    /// </summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Raises a property-changed notification for a bindable property.
    /// </summary>
    /// <param name="propertyName">The changed property name.</param>
    private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
