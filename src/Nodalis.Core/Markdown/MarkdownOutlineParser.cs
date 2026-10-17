namespace Nodalis.Core.Markdown;

/// <summary>
/// Extracts navigable ATX headings from Markdown without modifying the source text.
/// </summary>
public static class MarkdownOutlineParser
{
    /// <summary>
    /// Extracts every level 1 through 6 ATX heading and preserves its source position.
    /// Headings inside fenced code blocks are ignored.
    /// </summary>
    /// <param name="markdown">The Markdown source to inspect.</param>
    /// <returns>Headings in source order with stable offsets, including duplicate titles.</returns>
    public static IReadOnlyList<MarkdownOutlineEntry> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        global::System.Collections.Generic.List<global::Nodalis.Core.Markdown.MarkdownOutlineEntry> entries =
            new List<MarkdownOutlineEntry>();

        bool insideFence = false;
        char fenceMarker = char.MinValue;
        int fenceLength = 0;
        int lineNumber = 1;
        int lineStart = 0;

        while (lineStart < markdown.Length)
        {
            int newlineIndex = markdown.IndexOf('\n', lineStart);
            int lineEnd = newlineIndex >= 0
                ? newlineIndex
                : markdown.Length;

            int contentEnd =
                lineEnd > lineStart &&
                markdown[lineEnd - 1] == '\r'
                    ? lineEnd - 1
                    : lineEnd;

            string line = markdown.Substring(
                lineStart,
                contentEnd - lineStart);

            if (insideFence)
            {
                if (IsClosingFence(
                        line,
                        fenceMarker,
                        fenceLength))
                {
                    insideFence = false;
                    fenceMarker = char.MinValue;
                    fenceLength = 0;
                }
            }
            else if (TryReadOpeningFence(
                         line,
                         out char openingMarker,
                         out int openingLength))
            {
                insideFence = true;
                fenceMarker = openingMarker;
                fenceLength = openingLength;
            }
            else if (TryReadHeading(
                         line,
                         lineStart,
                         lineNumber,
                         out MarkdownOutlineEntry entry))
            {
                entries.Add(entry);
            }

            if (newlineIndex < 0)
            {
                break;
            }

            lineStart = newlineIndex + 1;
            lineNumber++;
        }

        return entries;
    }

    /// <summary>
    /// Detects an opening fenced-code marker with up to three leading spaces.
    /// </summary>
    /// <param name="line">Source line to inspect.</param>
    /// <param name="marker">Detected fence marker character.</param>
    /// <param name="length">Detected marker run length.</param>
    /// <returns><see langword="true"/> when the line opens a Markdown code fence.</returns>
    private static bool TryReadOpeningFence(
            string line,
            out char marker,
            out int length)
    {
        marker = char.MinValue;
        length = 0;

        int contentStart = CountAllowedIndentation(line);
        if (contentStart < 0 ||
            contentStart >= line.Length)
        {
            return false;
        }

        char candidate = line[contentStart];
        if (candidate != (char)96 &&
            candidate != '~')
        {
            return false;
        }

        int index = contentStart;
        while (index < line.Length &&
               line[index] == candidate)
        {
            index++;
        }

        int runLength = index - contentStart;
        if (runLength < 3)
        {
            return false;
        }

        marker = candidate;
        length = runLength;
        return true;
    }

    /// <summary>
    /// Detects the closing marker for the active fenced code block.
    /// </summary>
    /// <param name="line">Source line to inspect.</param>
    /// <param name="marker">Fence marker character that opened the block.</param>
    /// <param name="minimumLength">Minimum closing marker length.</param>
    /// <returns><see langword="true"/> when the line closes the active fence.</returns>
    private static bool IsClosingFence(
            string line,
            char marker,
            int minimumLength)
    {
        int contentStart = CountAllowedIndentation(line);
        if (contentStart < 0 ||
            contentStart >= line.Length ||
            line[contentStart] != marker)
        {
            return false;
        }

        int index = contentStart;
        while (index < line.Length &&
               line[index] == marker)
        {
            index++;
        }

        if (index - contentStart < minimumLength)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(
            line[index..]);
    }

    /// <summary>
    /// Parses one ATX heading while preserving the source line position.
    /// </summary>
    /// <param name="line">Source line to inspect.</param>
    /// <param name="offset">Character offset of the line in the original source.</param>
    /// <param name="lineNumber">One-based source line number.</param>
    /// <param name="entry">Parsed heading when successful.</param>
    /// <returns><see langword="true"/> when the line contains a level 1 through 6 heading.</returns>
    private static bool TryReadHeading(
            string line,
            int offset,
            int lineNumber,
            out MarkdownOutlineEntry entry)
    {
        entry = null!;

        int contentStart = CountAllowedIndentation(line);
        if (contentStart < 0 ||
            contentStart >= line.Length ||
            line[contentStart] != '#')
        {
            return false;
        }

        int markerEnd = contentStart;
        while (markerEnd < line.Length &&
               line[markerEnd] == '#')
        {
            markerEnd++;
        }

        int level = markerEnd - contentStart;
        if (level is < 1 or > 6)
        {
            return false;
        }

        if (markerEnd < line.Length &&
            !char.IsWhiteSpace(line[markerEnd]))
        {
            return false;
        }

        string rawTitle = markerEnd < line.Length
            ? line[markerEnd..].Trim()
            : string.Empty;

        string title = TrimClosingMarkers(rawTitle);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "(titre vide)";
        }

        entry = new MarkdownOutlineEntry(
            level,
            title,
            offset,
            lineNumber);

        return true;
    }

    /// <summary>
    /// Counts Markdown's allowed indentation before block markers.
    /// </summary>
    /// <param name="line">Source line to inspect.</param>
    /// <returns>The marker start offset, or -1 when indentation exceeds three spaces.</returns>
    private static int CountAllowedIndentation(
            string line)
    {
        int index = 0;
        while (index < line.Length &&
               line[index] == ' ')
        {
            index++;
        }

        return index <= 3
            ? index
            : -1;
    }

    /// <summary>
    /// Removes optional closing hash markers when they are separated from the heading text by whitespace.
    /// </summary>
    /// <param name="title">Raw heading text after the opening marker.</param>
    /// <returns>The visible heading title.</returns>
    private static string TrimClosingMarkers(
            string title)
    {
        string trimmed = title.TrimEnd();
        int markerStart = trimmed.Length;

        while (markerStart > 0 &&
               trimmed[markerStart - 1] == '#')
        {
            markerStart--;
        }

        if (markerStart == trimmed.Length ||
            markerStart == 0 ||
            !char.IsWhiteSpace(
                trimmed[markerStart - 1]))
        {
            return trimmed;
        }

        return trimmed[..markerStart]
            .TrimEnd();
    }
}
