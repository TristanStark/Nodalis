using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Nodalis.App.Editor;

/// <summary>
/// Draws unobtrusive Markdown structure markers beside visible editor lines.
/// The source stays in a normal TextBox, so selection, undo and autosave are unchanged.
/// </summary>
internal sealed class MarkdownEditorLineMarkers
{
    private const int MaximumVisibleLines = 150;

    private readonly TextBox _editor;
    private readonly Canvas _markers;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Brush _headingBrush;
    private readonly Brush _pendingTaskBrush;
    private readonly Brush _completedTaskBrush;
    private readonly Brush _priorityBrush;
    private readonly Brush _quoteBrush;
    private readonly Brush _codeBrush;
    private readonly Brush _linkBrush;

    /// <summary>
    /// Connects a non-interactive marker overlay to an existing Markdown source editor.
    /// </summary>
    /// <param name="editor">The TextBox that owns the Markdown text and scrolling.</param>
    /// <param name="markers">The Canvas positioned directly above the editor.</param>
    public MarkdownEditorLineMarkers(TextBox editor, Canvas markers)
    {
        _editor = editor;
        _markers = markers;
        _headingBrush = ResolveBrush("MarkdownHeadingBrush", Brushes.MediumPurple);
        _pendingTaskBrush = ResolveBrush("MarkdownPendingTaskBrush", Brushes.Goldenrod);
        _completedTaskBrush = ResolveBrush("MarkdownCompletedTaskBrush", Brushes.MediumSeaGreen);
        _priorityBrush = ResolveBrush("MarkdownPriorityBrush", Brushes.IndianRed);
        _quoteBrush = ResolveBrush("MarkdownQuoteBrush", Brushes.CadetBlue);
        _codeBrush = ResolveBrush("MarkdownCodeBrush", Brushes.Plum);
        _linkBrush = ResolveBrush("MarkdownLinkBrush", Brushes.CornflowerBlue);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(90),
            IsEnabled = false
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        _editor.TextChanged += Editor_TextChanged;
        _editor.Loaded += Editor_Loaded;
        _editor.SizeChanged += Editor_SizeChanged;
        _editor.IsVisibleChanged += Editor_IsVisibleChanged;
        _editor.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(Editor_ScrollChanged));
        _markers.IsHitTestVisible = false;
    }

    /// <summary>
    /// Uses the current dark theme brush or a safe fallback if the resource is missing.
    /// </summary>
    /// <param name="key">The theme resource key.</param>
    /// <param name="fallback">The brush used without that resource.</param>
    /// <returns>The brush used for the marker.</returns>
    private static Brush ResolveBrush(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;

    /// <summary>
    /// Schedules an update when a document is edited.
    /// </summary>
    /// <param name="sender">The source editor.</param>
    /// <param name="e">The text change details.</param>
    private void Editor_TextChanged(object sender, TextChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Renders markers when the editor first receives a layout.
    /// </summary>
    /// <param name="sender">The source editor.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Editor_Loaded(object sender, RoutedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Updates positions after editor resizing or text reflow.
    /// </summary>
    /// <param name="sender">The source editor.</param>
    /// <param name="e">The new editor size.</param>
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Clears hidden editor overlays and restores them when shown.
    /// </summary>
    /// <param name="sender">The source editor.</param>
    /// <param name="e">The visibility change.</param>
    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Moves structural markers together with the TextBox scroll viewport.
    /// </summary>
    /// <param name="sender">The source editor.</param>
    /// <param name="e">The scroll change information.</param>
    private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Coalesces rapid edits, scrollbar events and layout changes into a single redraw.
    /// </summary>
    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>
    /// Redraws only the visible portion of the source after the debounce interval.
    /// </summary>
    /// <param name="sender">The refresh timer.</param>
    /// <param name="e">The timer event arguments.</param>
    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RefreshVisibleMarkers();
    }

    /// <summary>
    /// Draws a subtle section band and color stripe for visible structural lines only.
    /// </summary>
    private void RefreshVisibleMarkers()
    {
        _markers.Children.Clear();

        if (!_editor.IsVisible ||
            _markers.ActualWidth <= 0 ||
            _markers.ActualHeight <= 0)
        {
            return;
        }

        int firstVisibleLine = _editor.GetFirstVisibleLineIndex();
        int lastVisibleLine = _editor.GetLastVisibleLineIndex();

        if (firstVisibleLine < 0 || lastVisibleLine < firstVisibleLine)
        {
            return;
        }

        string content = _editor.Text;
        int upperBound = Math.Min(
            lastVisibleLine,
            firstVisibleLine + MaximumVisibleLines - 1);

        for (int lineIndex = firstVisibleLine; lineIndex <= upperBound; lineIndex++)
        {
            int characterIndex = _editor.GetCharacterIndexFromLineIndex(lineIndex);
            if (characterIndex < 0 || characterIndex >= content.Length)
            {
                continue;
            }

            // Wrapped visual lines are not new Markdown blocks.
            if (characterIndex > 0 &&
                content[characterIndex - 1] != '\n' &&
                content[characterIndex - 1] != '\r')
            {
                continue;
            }

            ReadOnlySpan<char> line = content.AsSpan(
                characterIndex,
                Math.Min(200, content.Length - characterIndex));
            int endOfLine = line.IndexOfAny('\r', '\n');
            if (endOfLine >= 0)
            {
                line = line[..endOfLine];
            }

            if (!TryResolveMarker(
                    line.TrimStart(),
                    out Brush? brush,
                    out double stripeWidth,
                    out bool isHeading))
            {
                continue;
            }

            Rect bounds = _editor.GetRectFromCharacterIndex(characterIndex);
            if (bounds.IsEmpty ||
                bounds.Bottom <= 0 ||
                bounds.Top >= _markers.ActualHeight)
            {
                continue;
            }

            double markerHeight = Math.Min(20, Math.Max(7, bounds.Height - 2));
            Rectangle stripe = new Rectangle
            {
                Width = stripeWidth,
                Height = markerHeight,
                Fill = brush,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(stripe, 6);
            Canvas.SetTop(stripe, bounds.Top + 1);
            _markers.Children.Add(stripe);

            if (isHeading)
            {
                Rectangle band = new Rectangle
                {
                    Width = Math.Max(0, _markers.ActualWidth - 28),
                    Height = Math.Max(1, bounds.Height),
                    Fill = brush,
                    Opacity = 0.07
                };
                Canvas.SetLeft(band, 20);
                Canvas.SetTop(band, bounds.Top);
                _markers.Children.Add(band);
            }
        }
    }

    /// <summary>
    /// Maps the leading Markdown syntax to an accessible structural accent.
    /// </summary>
    /// <param name="line">At most the first 200 characters of the logical line.</param>
    /// <param name="brush">The color corresponding to the block kind.</param>
    /// <param name="stripeWidth">The relative prominence of this marker.</param>
    /// <param name="isHeading">Whether the line should also receive a subtle band.</param>
    /// <returns>True when the line has a recognized structure.</returns>
    private bool TryResolveMarker(
        ReadOnlySpan<char> line,
        out Brush? brush,
        out double stripeWidth,
        out bool isHeading)
    {
        brush = null;
        stripeWidth = 3;
        isHeading = false;

        if (line.IsEmpty)
        {
            return false;
        }

        int level = 0;
        while (level < line.Length && level < 6 && line[level] == '#')
        {
            level++;
        }

        if (level > 0 && level < line.Length && line[level] == ' ')
        {
            brush = _headingBrush;
            stripeWidth = Math.Max(3, 7 - level);
            isHeading = true;
            return true;
        }

        if (line.Length >= 5 &&
            (line[0] == '-' || line[0] == '*' || line[0] == '+') &&
            line[1] == ' ' &&
            line[2] == '[' &&
            line[4] == ']')
        {
            bool completed = line[3] == 'x' || line[3] == 'X';
            if (completed || line[3] == ' ')
            {
                brush = completed
                    ? _completedTaskBrush
                    : line.IndexOf("Priorité : Haute".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
                        ? _priorityBrush
                        : _pendingTaskBrush;
                stripeWidth = 4;
                return true;
            }
        }

        if (line[0] == '>')
        {
            brush = _quoteBrush;
            return true;
        }

        if (line.StartsWith("```".AsSpan(), StringComparison.Ordinal) ||
            line.StartsWith("~~~".AsSpan(), StringComparison.Ordinal))
        {
            brush = _codeBrush;
            return true;
        }

        if (line.StartsWith("[[".AsSpan(), StringComparison.Ordinal) ||
            line.IndexOf("](".AsSpan(), StringComparison.Ordinal) >= 0)
        {
            brush = _linkBrush;
            return true;
        }

        return false;
    }
}
