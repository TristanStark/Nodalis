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

    public void SetMatches(
        IReadOnlyList<GlossaryTextMatch> matches)
    {
        _matches = matches ?? [];
        InvalidateVisual();
    }

    protected override void OnRender(
        DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_matches.Count == 0 ||
            _textBox.Text.Length == 0)
        {
            return;
        }

        var brush =
            Application.Current.TryFindResource("AccentBrush") as Brush ??
            Brushes.CornflowerBlue;

        var pen = new Pen(
            brush,
            1)
        {
            DashStyle = DashStyles.Dot
        };

        foreach (var match in _matches)
        {
            DrawMatch(
                drawingContext,
                pen,
                match);
        }
    }

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

        var firstLine = _textBox.GetLineIndexFromCharacterIndex(
            match.Start);

        var lastCharacter = Math.Max(
            match.Start,
            match.Start + match.Length - 1);

        var lastLine = _textBox.GetLineIndexFromCharacterIndex(
            lastCharacter);

        for (var line = firstLine;
             line <= lastLine;
             line++)
        {
            var lineStart = _textBox.GetCharacterIndexFromLineIndex(
                line);

            if (lineStart < 0)
            {
                continue;
            }

            var lineLength = _textBox.GetLineLength(
                line);

            var segmentStart = Math.Max(
                match.Start,
                lineStart);

            var segmentEnd = Math.Min(
                match.Start + match.Length,
                lineStart + lineLength);

            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            var startRect = _textBox.GetRectFromCharacterIndex(
                segmentStart,
                trailingEdge: false);

            var endRect = _textBox.GetRectFromCharacterIndex(
                segmentEnd - 1,
                trailingEdge: true);

            if (startRect.IsEmpty ||
                endRect.IsEmpty)
            {
                continue;
            }

            var y = Math.Max(
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
