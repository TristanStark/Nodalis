using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Nodalis.Core.Markdown;

namespace Nodalis.App.Editor;

/// <summary>
/// Mirrors visible TextBox glyphs with syntax-colored WPF runs while leaving the
/// original source editor in charge of input, caret, selection, undo and autosave.
/// The overlay is noninteractive and falls back to native text if layout is unsafe.
/// </summary>
internal sealed class MarkdownSyntaxOverlay : IDisposable
{
    private const int MaximumVisualLines = 110;
    private const int MaximumVisualLineLength = 1200;
    private const int MaximumVisibleCharacters = 16000;

    private readonly TextBox _editor;
    private readonly Canvas _overlay;
    private readonly Brush _originalForeground;
    private readonly Brush[] _colors;
    private readonly DispatcherTimer _refreshTimer;
    private bool _enabled;
    private bool _disposed;

    /// <summary>
    /// Sets up delayed, viewport-scoped source coloring for one existing editor.
    /// </summary>
    /// <param name="editor">The unchanged editable source TextBox.</param>
    /// <param name="overlay">An input-transparent Canvas covering that TextBox.</param>
    public MarkdownSyntaxOverlay(TextBox editor, Canvas overlay)
    {
        _editor = editor;
        _overlay = overlay;
        _overlay.IsHitTestVisible = false;
        _originalForeground = editor.Foreground;
        _colors =
        [
            ResolveBrush("PrimaryTextBrush", Brushes.White),
            ResolveBrush("MarkdownHeadingBrush", Brushes.MediumPurple),
            ResolveBrush("MarkdownQuoteBrush", Brushes.CadetBlue),
            ResolveBrush("MarkdownCodeBrush", Brushes.Plum),
            ResolveBrush("MarkdownLinkBrush", Brushes.CornflowerBlue),
            ResolveBrush("MarkdownStrongBrush", Brushes.Khaki),
            ResolveBrush("MarkdownEmphasisBrush", Brushes.LightGoldenrodYellow),
            ResolveBrush("MarkdownPendingTaskBrush", Brushes.Goldenrod),
            ResolveBrush("MarkdownCompletedTaskBrush", Brushes.MediumSeaGreen),
            ResolveBrush("MarkdownPriorityBrush", Brushes.IndianRed),
            ResolveBrush("MarkdownMutedSyntaxBrush", Brushes.Gray),
            ResolveBrush("MarkdownMetadataBrush", Brushes.LightSteelBlue)
        ];

        _refreshTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            editor.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(75),
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
    }

    /// <summary>
    /// Enables or disables colored source text for the associated editor.
    /// Disabling always restores WPF's native readable text rendering.
    /// </summary>
    /// <param name="enabled">Whether syntax coloring should be active.</param>
    public void SetEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            ScheduleRefresh();
        }
        else
        {
            _refreshTimer.Stop();
            RestoreNativeText();
        }
    }

    /// <summary>
    /// Removes event subscriptions and restores native text drawing on shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        _editor.TextChanged -= Editor_TextChanged;
        _editor.Loaded -= Editor_Loaded;
        _editor.SizeChanged -= Editor_SizeChanged;
        _editor.IsVisibleChanged -= Editor_IsVisibleChanged;
        _editor.RemoveHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(Editor_ScrollChanged));
        RestoreNativeText();
    }

    /// <summary>
    /// Looks up a semantic color without requiring an installed UI theme.
    /// </summary>
    /// <param name="key">Theme resource name.</param>
    /// <param name="fallback">Fallback foreground brush.</param>
    /// <returns>The resulting foreground brush.</returns>
    private static Brush ResolveBrush(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;

    /// <summary>
    /// Coalesces new keystrokes into a single visible-region repaint.
    /// </summary>
    /// <param name="sender">Source editor.</param>
    /// <param name="e">Text change information.</param>
    private void Editor_TextChanged(object sender, TextChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Renders the first viewport after the editor has loaded.
    /// </summary>
    /// <param name="sender">Source editor.</param>
    /// <param name="e">Event details.</param>
    private void Editor_Loaded(object sender, RoutedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Repositions visual lines when the editor width or height changes.
    /// </summary>
    /// <param name="sender">Source editor.</param>
    /// <param name="e">Size change details.</param>
    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Switches back to native rendering while the editor is hidden.
    /// </summary>
    /// <param name="sender">Source editor.</param>
    /// <param name="e">Visibility change details.</param>
    private void Editor_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!_editor.IsVisible)
        {
            _refreshTimer.Stop();
            RestoreNativeText();
            return;
        }

        ScheduleRefresh();
    }

    /// <summary>
    /// Keeps the source mirror aligned with vertical and horizontal scrolling.
    /// </summary>
    /// <param name="sender">Source editor.</param>
    /// <param name="e">Scroll offset changes.</param>
    private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        ScheduleRefresh();

    /// <summary>
    /// Enqueues a redraw without performing parsing in the keystroke handler.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>
    /// Invokes the viewport repaint after rapid UI events have settled.
    /// </summary>
    /// <param name="sender">Refresh timer.</param>
    /// <param name="e">Timer arguments.</param>
    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();

        try
        {
            if (!TryRenderVisibleLines())
            {
                RestoreNativeText();
            }
        }
        catch (ArgumentException)
        {
            RestoreNativeText();
        }
        catch (InvalidOperationException)
        {
            RestoreNativeText();
        }
    }

    /// <summary>
    /// Rebuilds only visible visual lines with bounded tokenization and WPF runs.
    /// </summary>
    /// <returns>True when the overlay safely represents the visible source.</returns>
    private bool TryRenderVisibleLines()
    {
        if (!_enabled ||
            !_editor.IsVisible ||
            _editor.ActualWidth <= 0 ||
            _overlay.ActualWidth <= 0)
        {
            return false;
        }

        string text = _editor.Text;
        if (text.Length == 0)
        {
            return false;
        }

        int firstLine = _editor.GetFirstVisibleLineIndex();
        int lastLine = _editor.GetLastVisibleLineIndex();
        if (firstLine < 0 ||
            lastLine < firstLine ||
            lastLine - firstLine >= MaximumVisualLines)
        {
            return false;
        }

        List<TextBlock> newLines = [];
        List<Point> positions = [];
        int processedCharacters = 0;

        for (int lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
        {
            int start = _editor.GetCharacterIndexFromLineIndex(lineIndex);
            int length = _editor.GetLineLength(lineIndex);
            if (start < 0 ||
                length < 0 ||
                start > text.Length ||
                length > MaximumVisualLineLength ||
                start + length > text.Length)
            {
                return false;
            }

            processedCharacters += length;
            if (processedCharacters > MaximumVisibleCharacters)
            {
                return false;
            }

            if (length == 0)
            {
                continue;
            }

            while (length > 0 &&
                   (text[start + length - 1] == '\r' ||
                    text[start + length - 1] == '\n'))
            {
                length--;
            }

            if (length == 0)
            {
                continue;
            }

            // Tabs use editor-specific tab stops. Avoid mirrored glyph drift.
            string visualText = text.Substring(start, length);
            if (visualText.IndexOf('\t') >= 0)
            {
                return false;
            }

            Rect bounds = _editor.GetRectFromCharacterIndex(start);
            if (bounds.IsEmpty)
            {
                return false;
            }

            if (bounds.Bottom < 0 ||
                bounds.Top > _overlay.ActualHeight)
            {
                continue;
            }

            TextBlock visualLine = new TextBlock
            {
                FontFamily = _editor.FontFamily,
                FontSize = _editor.FontSize,
                FontWeight = _editor.FontWeight,
                FontStyle = _editor.FontStyle,
                Foreground = _colors[(int)MarkdownSyntaxKind.Text],
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false
            };

            IReadOnlyList<MarkdownSyntaxSpan> tokens =
                MarkdownSyntaxTokenizer.Tokenize(visualText);
            foreach (MarkdownSyntaxSpan token in tokens)
            {
                Run run = new Run(visualText.Substring(
                    token.Start,
                    token.Length))
                {
                    Foreground = _colors[(int)token.Kind]
                };
                visualLine.Inlines.Add(run);
            }

            newLines.Add(visualLine);
            positions.Add(new Point(bounds.Left, bounds.Top));
        }

        // Keep the TextBox foreground intact until the entire overlay is ready.
        _overlay.Children.Clear();
        for (int index = 0; index < newLines.Count; index++)
        {
            Canvas.SetLeft(newLines[index], positions[index].X);
            Canvas.SetTop(newLines[index], positions[index].Y);
            _overlay.Children.Add(newLines[index]);
        }

        _editor.Foreground = _colors[(int)MarkdownSyntaxKind.Text] == Brushes.Transparent
            ? _originalForeground
            : Brushes.Transparent;
        return true;
    }

    /// <summary>
    /// Restores standard WPF source rendering when colorization cannot be aligned.
    /// </summary>
    private void RestoreNativeText()
    {
        _overlay.Children.Clear();
        _editor.Foreground = _originalForeground;
    }
}
