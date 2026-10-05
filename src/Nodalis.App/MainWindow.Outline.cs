using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nodalis.App.Editor;
using Nodalis.Core.Markdown;

namespace Nodalis.App;

public partial class MainWindow
{
    private readonly ObservableCollection<DocumentOutlineItemViewModel> _documentOutlineItems = [];
    private readonly global::System.Collections.Generic.List<DocumentOutlineItemViewModel> _flatDocumentOutlineItems = [];
    private DispatcherTimer? _documentOutlineTimer;
    private DocumentOutlineItemViewModel? _currentDocumentOutlineItem;

    /// <summary>
    /// Initializes the debounced Markdown outline displayed in the context panel.
    /// </summary>
    private void InitializeDocumentOutline()
    {
        DocumentOutlineTree.ItemsSource =
            _documentOutlineItems;

        _documentOutlineTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(300),
            DispatcherPriority.Background,
            DocumentOutlineTimer_Tick,
            Dispatcher)
        {
            IsEnabled = false
        };

        MarkdownEditorTextBox.TextChanged +=
            MarkdownEditorTextBox_OutlineTextChanged;

        MarkdownEditorTextBox.SelectionChanged +=
            MarkdownEditorTextBox_OutlineSelectionChanged;

        DocumentEditorHost.IsVisibleChanged +=
            DocumentEditorHost_OutlineVisibilityChanged;

        RebuildDocumentOutline();
    }

    /// <summary>
    /// Restarts the outline debounce after the Markdown source changes.
    /// </summary>
    /// <param name="sender">The editor text box.</param>
    /// <param name="e">The text change event arguments.</param>
    private void MarkdownEditorTextBox_OutlineTextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        ScheduleDocumentOutlineRefresh();
    }

    /// <summary>
    /// Refreshes the current-section highlight when the caret or selection moves.
    /// </summary>
    /// <param name="sender">The editor text box.</param>
    /// <param name="e">The selection change event arguments.</param>
    private void MarkdownEditorTextBox_OutlineSelectionChanged(
            object sender,
            RoutedEventArgs e)
    {
        UpdateCurrentDocumentOutlineSection();
    }

    /// <summary>
    /// Rebuilds or clears the outline when the document editor becomes visible or hidden.
    /// </summary>
    /// <param name="sender">The document editor host.</param>
    /// <param name="e">The visibility change event arguments.</param>
    private void DocumentEditorHost_OutlineVisibilityChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
    {
        if (DocumentEditorHost.IsVisible)
        {
            ScheduleDocumentOutlineRefresh();
            return;
        }

        ClearDocumentOutline();
    }

    /// <summary>
    /// Executes the delayed outline refresh after the user pauses typing.
    /// </summary>
    /// <param name="sender">The outline timer.</param>
    /// <param name="e">The timer event arguments.</param>
    private void DocumentOutlineTimer_Tick(
            object? sender,
            EventArgs e)
    {
        _documentOutlineTimer?.Stop();
        RebuildDocumentOutline();
    }

    /// <summary>
    /// Schedules a single outline rebuild after a short debounce interval.
    /// </summary>
    private void ScheduleDocumentOutlineRefresh()
    {
        if (_documentOutlineTimer is null)
        {
            return;
        }

        _documentOutlineTimer.Stop();
        _documentOutlineTimer.Start();
    }

    /// <summary>
    /// Parses the current Markdown text and rebuilds the hierarchical outline tree.
    /// </summary>
    private void RebuildDocumentOutline()
    {
        _documentOutlineTimer?.Stop();
        _documentOutlineItems.Clear();
        _flatDocumentOutlineItems.Clear();
        _currentDocumentOutlineItem = null;

        if (_activeDocumentTab is null ||
            !DocumentEditorHost.IsVisible)
        {
            UpdateDocumentOutlineEmptyState();
            return;
        }

        IReadOnlyList<MarkdownOutlineEntry> entries =
            MarkdownOutlineParser.Parse(
                MarkdownEditorTextBox.Text);

        global::System.Collections.Generic.Stack<DocumentOutlineItemViewModel> ancestors =
            new Stack<DocumentOutlineItemViewModel>();

        foreach (MarkdownOutlineEntry entry in entries)
        {
            DocumentOutlineItemViewModel item =
                new DocumentOutlineItemViewModel(entry);

            while (ancestors.Count > 0 &&
                   ancestors.Peek().Level >= item.Level)
            {
                ancestors.Pop();
            }

            if (ancestors.Count == 0)
            {
                _documentOutlineItems.Add(item);
            }
            else
            {
                ancestors.Peek().Children.Add(item);
            }

            _flatDocumentOutlineItems.Add(item);
            ancestors.Push(item);
        }

        UpdateDocumentOutlineEmptyState();
        UpdateCurrentDocumentOutlineSection();
    }

    /// <summary>
    /// Removes every outline item when no editable Markdown document is active.
    /// </summary>
    private void ClearDocumentOutline()
    {
        _documentOutlineTimer?.Stop();

        if (_currentDocumentOutlineItem is not null)
        {
            _currentDocumentOutlineItem.IsCurrent = false;
        }

        _currentDocumentOutlineItem = null;
        _documentOutlineItems.Clear();
        _flatDocumentOutlineItems.Clear();
        UpdateDocumentOutlineEmptyState();
    }

    /// <summary>
    /// Shows a compact empty-state message only when the outline has no headings.
    /// </summary>
    private void UpdateDocumentOutlineEmptyState()
    {
        DocumentOutlineEmptyText.Visibility =
            _flatDocumentOutlineItems.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// Highlights the deepest source heading that precedes the current editor caret.
    /// </summary>
    private void UpdateCurrentDocumentOutlineSection()
    {
        if (_flatDocumentOutlineItems.Count == 0)
        {
            if (_currentDocumentOutlineItem is not null)
            {
                _currentDocumentOutlineItem.IsCurrent = false;
                _currentDocumentOutlineItem = null;
            }

            return;
        }

        int caretIndex = MarkdownEditorTextBox.CaretIndex;
        DocumentOutlineItemViewModel? current = null;

        foreach (DocumentOutlineItemViewModel item in _flatDocumentOutlineItems)
        {
            if (item.Offset > caretIndex)
            {
                break;
            }

            current = item;
        }

        if (ReferenceEquals(
                current,
                _currentDocumentOutlineItem))
        {
            return;
        }

        if (_currentDocumentOutlineItem is not null)
        {
            _currentDocumentOutlineItem.IsCurrent = false;
        }

        _currentDocumentOutlineItem = current;

        if (_currentDocumentOutlineItem is not null)
        {
            _currentDocumentOutlineItem.IsCurrent = true;
        }
    }

    /// <summary>
    /// Moves the editor caret to the heading selected in the context-panel outline.
    /// </summary>
    /// <param name="sender">The outline tree.</param>
    /// <param name="e">The selected-item change event arguments.</param>
    private void DocumentOutlineTree_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not
            DocumentOutlineItemViewModel item)
        {
            return;
        }

        int caretIndex = Math.Clamp(
            item.Offset,
            0,
            MarkdownEditorTextBox.Text.Length);

        MarkdownEditorTextBox.Focus();
        MarkdownEditorTextBox.CaretIndex = caretIndex;
        MarkdownEditorTextBox.SelectionLength = 0;
        MarkdownEditorTextBox.ScrollToLine(
            Math.Max(
                0,
                item.LineNumber - 1));

        UpdateCurrentDocumentOutlineSection();
    }
}
