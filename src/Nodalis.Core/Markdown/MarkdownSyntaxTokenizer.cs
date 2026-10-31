namespace Nodalis.Core.Markdown;

/// <summary>Identifies a color role in the Markdown source editor.</summary>
public enum MarkdownSyntaxKind
{
    Text,
    Heading,
    Quote,
    Code,
    Link,
    Strong,
    Emphasis,
    TaskOpen,
    TaskDone,
    Priority,
    Muted,
    Metadata
}

/// <summary>
/// Describes one uninterrupted syntax-colored section of a visual source line.
/// </summary>
/// <param name="Start">Zero-based character offset within the supplied line.</param>
/// <param name="Length">Length of the range in UTF-16 code units.</param>
/// <param name="Kind">Visual role of that range.</param>
public readonly record struct MarkdownSyntaxSpan(
    int Start,
    int Length,
    MarkdownSyntaxKind Kind);

/// <summary>
/// Performs bounded, allocation-light Markdown tokenization without changing source text.
/// This is a visual lexer, not a Markdown document rewriter.
/// </summary>
public static class MarkdownSyntaxTokenizer
{
    private static readonly string[] MetadataLabels =
    [
        "Responsable :", "Responsable:",
        "Priorité :", "Priorité:",
        "Statut :", "Statut:",
        "Tags :", "Tags:",
        "Résultat attendu :", "Résultat attendu:",
        "Manipulation :", "Manipulation:",
        "Issues :", "Issues:"
    ];

    /// <summary>
    /// Classifies one source line into ordered, nonoverlapping color spans.
    /// </summary>
    /// <param name="line">Visible source without the newline terminator.</param>
    /// <returns>Minimal color spans preserving every input character.</returns>
    public static IReadOnlyList<MarkdownSyntaxSpan> Tokenize(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Length == 0)
        {
            return [];
        }

        MarkdownSyntaxKind[] styles = new MarkdownSyntaxKind[line.Length];
        int indentation = 0;
        while (indentation < line.Length &&
               indentation < 3 &&
               line[indentation] == ' ')
        {
            indentation++;
        }

        string source = line[indentation..];
        if (source.StartsWith(new string((char)96, 3), StringComparison.Ordinal) ||
            source.StartsWith("~~~", StringComparison.Ordinal))
        {
            Paint(styles, indentation, source.Length, MarkdownSyntaxKind.Code);
            return GroupStyles(styles);
        }

        int headingLevel = 0;
        while (headingLevel < source.Length &&
               headingLevel < 6 &&
               source[headingLevel] == '#')
        {
            headingLevel++;
        }

        if (headingLevel > 0 &&
            headingLevel < source.Length &&
            char.IsWhiteSpace(source[headingLevel]))
        {
            Paint(styles, indentation, source.Length, MarkdownSyntaxKind.Heading);
            Paint(styles, indentation, headingLevel, MarkdownSyntaxKind.Muted);
        }
        else if (source.StartsWith(">", StringComparison.Ordinal))
        {
            Paint(styles, indentation, source.Length, MarkdownSyntaxKind.Quote);
            Paint(styles, indentation, 1, MarkdownSyntaxKind.Muted);
        }
        else if (source == "---" || source == "***" || source == "___")
        {
            Paint(styles, indentation, source.Length, MarkdownSyntaxKind.Muted);
        }
        else
        {
            ColorizeListMarker(source, indentation, styles);
        }

        ColorizeEmphasisAndLinks(line, styles);
        ColorizeMetadata(line, styles);
        return GroupStyles(styles);
    }

    /// <summary>
    /// Highlights checkboxes and list prefixes without changing the list body.
    /// </summary>
    /// <param name="source">The line without its leading indentation.</param>
    /// <param name="start">Original offset of the stripped indentation.</param>
    /// <param name="styles">Mutable roles for every character.</param>
    private static void ColorizeListMarker(
        string source,
        int start,
        MarkdownSyntaxKind[] styles)
    {
        if (source.Length >= 6 &&
            (source[0] == '-' || source[0] == '*' || source[0] == '+') &&
            source[1] == ' ' &&
            source[2] == '[' &&
            source[4] == ']' &&
            source[5] == ' ')
        {
            if (source[3] == 'x' || source[3] == 'X')
            {
                Paint(styles, start, 5, MarkdownSyntaxKind.TaskDone);
                return;
            }

            if (source[3] == ' ')
            {
                Paint(styles, start, 5, MarkdownSyntaxKind.TaskOpen);
                return;
            }
        }

        if (source.Length > 1 &&
            (source[0] == '-' || source[0] == '+' || source[0] == '*') &&
            source[1] == ' ')
        {
            Paint(styles, start, 1, MarkdownSyntaxKind.Muted);
            return;
        }

        int digits = 0;
        while (digits < source.Length &&
               digits < 5 &&
               char.IsDigit(source[digits]))
        {
            digits++;
        }

        if (digits > 0 &&
            digits + 1 < source.Length &&
            (source[digits] == '.' || source[digits] == ')') &&
            source[digits + 1] == ' ')
        {
            Paint(styles, start, digits + 1, MarkdownSyntaxKind.Muted);
        }
    }

    /// <summary>
    /// Identifies inline links, code and emphasis without whole-document parsing.
    /// </summary>
    /// <param name="line">The visible source line.</param>
    /// <param name="styles">Mutable roles for every character.</param>
    private static void ColorizeEmphasisAndLinks(
        string line,
        MarkdownSyntaxKind[] styles)
    {
        int cursor = 0;
        while (cursor < line.Length)
        {
            if (line[cursor] == (char)96)
            {
                int close = line.IndexOf((char)96, cursor + 1);
                if (close > cursor + 1)
                {
                    Paint(styles, cursor, close - cursor + 1, MarkdownSyntaxKind.Code);
                    cursor = close + 1;
                    continue;
                }
            }

            if (cursor + 1 < line.Length &&
                line[cursor] == '[' &&
                line[cursor + 1] == '[')
            {
                int close = line.IndexOf("]]", cursor + 2, StringComparison.Ordinal);
                if (close >= 0)
                {
                    Paint(styles, cursor, close + 2 - cursor, MarkdownSyntaxKind.Link);
                    cursor = close + 2;
                    continue;
                }
            }

            if (line[cursor] == '[')
            {
                int divider = line.IndexOf("](", cursor + 1, StringComparison.Ordinal);
                if (divider > cursor + 1)
                {
                    int close = line.IndexOf(')', divider + 2);
                    if (close > divider + 2)
                    {
                        Paint(styles, cursor, close + 1 - cursor, MarkdownSyntaxKind.Link);
                        cursor = close + 1;
                        continue;
                    }
                }
            }

            char character = line[cursor];
            if (character == '*' || character == '_' || character == '~')
            {
                int markerLength =
                    cursor + 1 < line.Length && line[cursor + 1] == character
                        ? 2
                        : 1;

                if (character == '~' && markerLength != 2)
                {
                    cursor++;
                    continue;
                }

                if (markerLength == 1 &&
                    character == '_' &&
                    cursor > 0 &&
                    char.IsLetterOrDigit(line[cursor - 1]))
                {
                    cursor++;
                    continue;
                }

                string marker = new string(character, markerLength);
                int close = line.IndexOf(
                    marker,
                    cursor + markerLength,
                    StringComparison.Ordinal);

                if (close > cursor + markerLength)
                {
                    MarkdownSyntaxKind role = markerLength == 2 && character != '~'
                        ? MarkdownSyntaxKind.Strong
                        : MarkdownSyntaxKind.Emphasis;
                    Paint(styles, cursor, close + markerLength - cursor, role);
                    Paint(styles, cursor, markerLength, MarkdownSyntaxKind.Muted);
                    Paint(styles, close, markerLength, MarkdownSyntaxKind.Muted);
                    cursor = close + markerLength;
                    continue;
                }
            }

            cursor++;
        }
    }

    /// <summary>
    /// Distinguishes task metadata labels and critical high-priority values.
    /// </summary>
    /// <param name="line">The visible source line.</param>
    /// <param name="styles">Mutable roles for every character.</param>
    private static void ColorizeMetadata(
        string line,
        MarkdownSyntaxKind[] styles)
    {
        foreach (string label in MetadataLabels)
        {
            int offset = 0;
            while (offset < line.Length)
            {
                int found = line.IndexOf(
                    label,
                    offset,
                    StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                bool boundary = found == 0 ||
                    char.IsWhiteSpace(line[found - 1]) ||
                    line[found - 1] == '|';
                if (boundary && styles[found] == MarkdownSyntaxKind.Text)
                {
                    Paint(styles, found, label.Length, MarkdownSyntaxKind.Metadata);
                }

                offset = found + label.Length;
            }
        }

        string highPriority = "Priorité : Haute";
        int priorityStart = line.IndexOf(
            highPriority,
            StringComparison.OrdinalIgnoreCase);
        if (priorityStart >= 0 &&
            styles[priorityStart] != MarkdownSyntaxKind.Code)
        {
            Paint(styles, priorityStart, highPriority.Length, MarkdownSyntaxKind.Priority);
        }
    }

    /// <summary>
    /// Applies one role to a clamped range in the line.
    /// </summary>
    /// <param name="styles">The mutable character roles.</param>
    /// <param name="start">The first character in the range.</param>
    /// <param name="length">The requested number of characters.</param>
    /// <param name="role">The role applied to that range.</param>
    private static void Paint(
        MarkdownSyntaxKind[] styles,
        int start,
        int length,
        MarkdownSyntaxKind role)
    {
        int end = Math.Min(styles.Length, start + length);
        for (int index = Math.Max(0, start); index < end; index++)
        {
            styles[index] = role;
        }
    }

    /// <summary>
    /// Combines adjacent equal roles into render-ready spans.
    /// </summary>
    /// <param name="styles">One color role per character.</param>
    /// <returns>Ordered, gapless source spans.</returns>
    private static IReadOnlyList<MarkdownSyntaxSpan> GroupStyles(
        MarkdownSyntaxKind[] styles)
    {
        List<MarkdownSyntaxSpan> spans = [];
        int start = 0;
        MarkdownSyntaxKind previous = styles[0];

        for (int index = 1; index <= styles.Length; index++)
        {
            if (index < styles.Length && styles[index] == previous)
            {
                continue;
            }

            spans.Add(new MarkdownSyntaxSpan(
                start, index - start, previous));
            if (index < styles.Length)
            {
                start = index;
                previous = styles[index];
            }
        }

        return spans;
    }
}
