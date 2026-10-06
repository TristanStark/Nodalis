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
            _textBox.Text.Length == 0)
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

        foreach (global::Nodalis.Core.Glossary.GlossaryTextMatch match in _matches)
        {
            DrawMatch(
                drawingContext,
                pen,
                match);
        }
    }

    /// <summary>
    /// Performs the <c>DrawMatch</c> operation.
    /// </summary>
    /// <param name="drawingContext">The <c>drawingContext</c> value.</param>
    /// <param name="pen">The <c>pen</c> value.</param>
    /// <param name="match">The <c>match</c> value.</param>
    private void DrawMatch(
            DrawingContext drawingContext,
            Pen pen,
            GlossaryTextMatch match)
    {
        if (match.Start < 0 ||
            match.Length <= 0 ||
            match.Start + match.Length > _textBox.Text.Length)
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

        for (int line = firstLine;
             line <= lastLine;
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
