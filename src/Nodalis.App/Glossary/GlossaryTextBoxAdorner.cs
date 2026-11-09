using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nodalis.Core.Glossary;

namespace Nodalis.App.Glossary;

public sealed class GlossaryTextBoxAdorner : Adorner
{
    private readonly TextBox _textBox;
    private IReadOnlyList<GlossaryTextMatch> _matches = [];

    /// <summary>
    /// Initializes a new instance of <see cref="GlossaryTextBoxAdorner"/>.
    /// </summary>
    /// <param name="textBox">The <c>textBox</c> value.</param>
    public GlossaryTextBoxAdorner(TextBox textBox)
            : base(textBox)
    {
        _textBox = textBox;
        IsHitTestVisible = false;

        _textBox.SizeChanged += (_, _) =>
            InvalidateVisual();

        _textBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(
                (_, _) => InvalidateVisual()));
    }

    /// <summary>
    /// Performs the <c>SetMatches</c> operation.
    /// </summary>
    /// <param name="matches">The <c>matches</c> value.</param>
    public void SetMatches(
            IReadOnlyList<GlossaryTextMatch> matches)
    {
        _matches = matches ?? [];
        InvalidateVisual();
    }

    /// <summary>
    /// Performs the <c>OnRender</c> operation.
    /// </summary>
    /// <param name="drawingContext">The <c>drawingContext</c> value.</param>
    protected override void OnRender(
            DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_matches.Count == 0 ||
            _textBox.Text.Length == 0 ||
            !_textBox.IsVisible)
        {
            return;
        }

        try
        {
            RenderVisibleMatches(drawingContext);
        }
        catch (ArgumentException)
        {
            // A TextBox can report visible lines from an old layout while
            // rebuilding its line map during resize or visibility changes.
            // Glossary underlines are decorative and must never block input.
        }
        catch (InvalidOperationException)
        {
            // The visual line map may be temporarily unavailable during reflow.
        }
    }

    /// <summary>
    /// Draws visible glossary matches only while the TextBox has valid lines.
    /// </summary>
    /// <param name="drawingContext">The drawing context.</param>
    private void RenderVisibleMatches(
            DrawingContext drawingContext)
    {
        int lineCount = _textBox.LineCount;
        if (lineCount <= 0)
        {
            return;
        }

        int firstVisibleLine = _textBox.GetFirstVisibleLineIndex();
        int lastVisibleLine = _textBox.GetLastVisibleLineIndex();
        if (firstVisibleLine < 0 ||
            lastVisibleLine < firstVisibleLine ||
            firstVisibleLine >= lineCount ||
            lastVisibleLine >= lineCount)
        {
            return;
        }

        int firstVisibleCharacter = _textBox.GetCharacterIndexFromLineIndex(
            firstVisibleLine);
        int lastLineStart = _textBox.GetCharacterIndexFromLineIndex(
            lastVisibleLine);
        if (firstVisibleCharacter < 0 || lastLineStart < 0)
        {
            return;
        }

        int textLength = _textBox.Text.Length;
        int endVisibleCharacter = (int)Math.Min(
            textLength,
            (long)lastLineStart + _textBox.GetLineLength(lastVisibleLine) + 1);
        (int firstMatch, int endMatch) = GlossaryMatchIndex.GetVisibleRange(
            _matches,
            firstVisibleCharacter,
            endVisibleCharacter);
        if (firstMatch == endMatch)
        {
            return;
        }

        global::System.Windows.Media.Brush brush =
            Application.Current.TryFindResource("AccentBrush") as Brush ??
            Brushes.CornflowerBlue;

        global::System.Windows.Media.Pen pen = new Pen(
            brush,
            1)
        {
            DashStyle = DashStyles.Dot
        };

        // GetRectFromCharacterIndex performs WPF layout work. Never invoke
        // it for offscreen matches, even if a document has thousands of terms.
        for (int index = firstMatch; index < endMatch; index++)
        {
            DrawMatch(
                drawingContext,
                pen,
                _matches[index],
                firstVisibleLine,
                lastVisibleLine,
                textLength);
        }
    }

    /// <summary>
    /// Performs the <c>DrawMatch</c> operation.
    /// </summary>
    /// <param name="drawingContext">The <c>drawingContext</c> value.</param>
    /// <param name="pen">The <c>pen</c> value.</param>
    /// <param name="match">The <c>match</c> value.</param>
    /// <param name="firstVisibleLine">First visible visual line.</param>
    /// <param name="lastVisibleLine">Last visible visual line.</param>
    /// <param name="textLength">Current editor text length.</param>
    private void DrawMatch(
            DrawingContext drawingContext,
            Pen pen,
            GlossaryTextMatch match,
            int firstVisibleLine,
            int lastVisibleLine,
            int textLength)
    {
        if (match.Start < 0 ||
            match.Length <= 0 ||
            (long)match.Start + match.Length > textLength)
        {
            return;
        }

        int firstLine = _textBox.GetLineIndexFromCharacterIndex(
            match.Start);

        int lastCharacter = Math.Max(
            match.Start,
            match.Start + match.Length - 1);

        int lastLine = _textBox.GetLineIndexFromCharacterIndex(
            lastCharacter);

        int lineCount = _textBox.LineCount;

        if (lineCount <= 0 ||
            firstLine < 0 ||
            lastLine < 0 ||
            firstLine >= lineCount ||
            lastLine >= lineCount)
        {
            return;
        }

        // A very long glossary synonym can span multiple visual lines.
        // Only query geometry for the lines actually on screen.
        for (int line = Math.Max(firstLine, firstVisibleLine);
             line <= Math.Min(lastLine, lastVisibleLine);
             line++)
        {
            if (line < 0 ||
                line >= _textBox.LineCount)
            {
                continue;
            }

            int lineStart = _textBox.GetCharacterIndexFromLineIndex(
                line);

            if (lineStart < 0)
            {
                continue;
            }

            int lineLength = _textBox.GetLineLength(
                line);

            int segmentStart = Math.Max(
                match.Start,
                lineStart);

            int segmentEnd = Math.Min(
                match.Start + match.Length,
                lineStart + lineLength);

            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            global::System.Windows.Rect startRect = _textBox.GetRectFromCharacterIndex(
                segmentStart,
                trailingEdge: false);

            global::System.Windows.Rect endRect = _textBox.GetRectFromCharacterIndex(
                segmentEnd - 1,
                trailingEdge: true);

            if (startRect.IsEmpty ||
                endRect.IsEmpty)
            {
                continue;
            }

            double y = Math.Max(
                startRect.Bottom - 1,
                startRect.Top + 1);

            if (y < 0 ||
                y > ActualHeight)
            {
                continue;
            }

            drawingContext.DrawLine(
                pen,
                new Point(
                    startRect.Left,
                    y),
                new Point(
                    Math.Max(
                        startRect.Left + 2,
                        endRect.Right),
                    y));
        }
    }
}
