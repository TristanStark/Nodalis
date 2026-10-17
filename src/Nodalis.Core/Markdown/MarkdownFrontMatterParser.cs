using System.Text;

namespace Nodalis.Core.Markdown;

/// <summary>
/// Parses and rewrites a deliberately small, portable YAML-style front matter subset.
/// </summary>
public static class MarkdownFrontMatterParser
{
    private static readonly string[] PreferredPropertyOrder =
    [
        "status",
        "owner",
        "version",
        "environment",
        "type",
        "tags"
    ];

    /// <summary>
    /// Parses optional front matter at the beginning of a Markdown document.
    /// Unknown keys and malformed non-property lines are tolerated.
    /// </summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <returns>The parsed properties and unchanged Markdown body.</returns>
    public static MarkdownFrontMatterDocument Parse(
            string markdown)
    {
        ArgumentNullException.ThrowIfNull(
            markdown);

        string newLine =
            DetectNewLine(
                markdown);

        if (!TryReadFirstLine(
                markdown,
                0,
                out string firstLine,
                out int nextLineStart) ||
            !string.Equals(
                firstLine.Trim(),
                "---",
                StringComparison.Ordinal))
        {
            return new MarkdownFrontMatterDocument
            {
                Body =
                    markdown,
                HasFrontMatter =
                    false,
                NewLine =
                    newLine
            };
        }

        Dictionary<string, string> properties =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        int currentLineStart =
            nextLineStart;

        while (currentLineStart <=
               markdown.Length)
        {
            if (!TryReadFirstLine(
                    markdown,
                    currentLineStart,
                    out string line,
                    out int nextStart))
            {
                return new MarkdownFrontMatterDocument
                {
                    Body =
                        markdown,
                    HasFrontMatter =
                        false,
                    NewLine =
                        newLine
                };
            }

            if (string.Equals(
                    line.Trim(),
                    "---",
                    StringComparison.Ordinal))
            {
                return new MarkdownFrontMatterDocument
                {
                    Properties =
                        properties,
                    Body =
                        nextStart <= markdown.Length
                            ? markdown[nextStart..]
                            : string.Empty,
                    HasFrontMatter =
                        true,
                    NewLine =
                        newLine
                };
            }

            ParsePropertyLine(
                line,
                properties);

            if (nextStart <=
                currentLineStart)
            {
                break;
            }

            currentLineStart =
                nextStart;
        }

        return new MarkdownFrontMatterDocument
        {
            Body =
                markdown,
            HasFrontMatter =
                false,
            NewLine =
                newLine
        };
    }

    /// <summary>
    /// Replaces or creates front matter while preserving the document body.
    /// </summary>
    /// <param name="markdown">The original Markdown source.</param>
    /// <param name="properties">The complete property set to write.</param>
    /// <returns>The Markdown source with normalized front matter.</returns>
    public static string Apply(
            string markdown,
            IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(
            markdown);
        ArgumentNullException.ThrowIfNull(
            properties);

        MarkdownFrontMatterDocument parsed =
            Parse(
                markdown);

        List<KeyValuePair<string, string>> normalized =
            NormalizeProperties(
                properties);

        if (normalized.Count == 0)
        {
            return parsed.HasFrontMatter
                ? parsed.Body
                : markdown;
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "---");
        builder.Append(
            parsed.NewLine);

        foreach (KeyValuePair<string, string> property in normalized)
        {
            builder.Append(
                property.Key);
            builder.Append(
                ": ");
            builder.Append(
                property.Value);
            builder.Append(
                parsed.NewLine);
        }

        builder.Append(
            "---");

        if (parsed.Body.Length > 0)
        {
            builder.Append(
                parsed.NewLine);
            builder.Append(
                parsed.Body);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses the portable comma-separated or bracketed tag syntax used by Nodalis.
    /// </summary>
    /// <param name="value">The raw tags property.</param>
    /// <returns>Normalized, unique tags.</returns>
    public static IReadOnlyList<string> ParseTags(
            string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return [];
        }

        string normalized =
            value.Trim();

        if (normalized.StartsWith(
                '[') &&
            normalized.EndsWith(
                ']') &&
            normalized.Length >= 2)
        {
            normalized =
                normalized[1..^1];
        }

        return normalized
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(tag =>
                tag.Trim()
                    .Trim(
                        '"',
                        '\''))
            .Where(tag =>
                !string.IsNullOrWhiteSpace(
                    tag))
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Parses one key/value line when it belongs to the supported front matter subset.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <param name="properties">The destination property dictionary.</param>
    private static void ParsePropertyLine(
            string line,
            IDictionary<string, string> properties)
    {
        string trimmed =
            line.Trim();

        if (trimmed.Length == 0 ||
            trimmed.StartsWith(
                '#'))
        {
            return;
        }

        int separator =
            trimmed.IndexOf(
                ':');

        if (separator <= 0)
        {
            return;
        }

        string key =
            trimmed[..separator]
                .Trim();
        string value =
            trimmed[(separator + 1)..]
                .Trim();

        if (key.Length == 0)
        {
            return;
        }

        properties[key] =
            value;
    }

    /// <summary>
    /// Normalizes property keys and places standard Nodalis properties first.
    /// </summary>
    /// <param name="properties">The source properties.</param>
    /// <returns>The normalized ordered properties.</returns>
    private static List<KeyValuePair<string, string>> NormalizeProperties(
            IReadOnlyDictionary<string, string> properties)
    {
        Dictionary<string, string> normalized =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> property in properties)
        {
            string key =
                property.Key.Trim();

            if (key.Length == 0)
            {
                continue;
            }

            normalized[key] =
                property.Value?.Trim() ??
                string.Empty;
        }

        List<KeyValuePair<string, string>> ordered =
            new List<KeyValuePair<string, string>>();

        foreach (string preferredKey in PreferredPropertyOrder)
        {
            if (normalized.Remove(
                    preferredKey,
                    out string? value))
            {
                ordered.Add(
                    new KeyValuePair<string, string>(
                        preferredKey,
                        value));
            }
        }

        ordered.AddRange(
            normalized
                .OrderBy(
                    property => property.Key,
                    StringComparer.CurrentCultureIgnoreCase));

        return ordered;
    }

    /// <summary>
    /// Reads one line and reports the character index of the following line.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="start">The line start index.</param>
    /// <param name="line">The line content without newline characters.</param>
    /// <param name="nextLineStart">The next line start or the end of the string.</param>
    /// <returns><see langword="true"/> when a line could be read.</returns>
    private static bool TryReadFirstLine(
            string text,
            int start,
            out string line,
            out int nextLineStart)
    {
        line =
            string.Empty;
        nextLineStart =
            text.Length;

        if (start < 0 ||
            start > text.Length)
        {
            return false;
        }

        if (start ==
            text.Length)
        {
            line =
                string.Empty;
            nextLineStart =
                text.Length;
            return true;
        }

        int lineFeedIndex =
            text.IndexOf(
                '\n',
                start);

        if (lineFeedIndex < 0)
        {
            int length =
                text.Length -
                start;

            if (length > 0 &&
                text[^1] == '\r')
            {
                length--;
            }

            line =
                text.Substring(
                    start,
                    Math.Max(
                        0,
                        length));
            nextLineStart =
                text.Length;

            return true;
        }

        int contentLength =
            lineFeedIndex -
            start;

        if (contentLength > 0 &&
            text[lineFeedIndex - 1] ==
            '\r')
        {
            contentLength--;
        }

        line =
            text.Substring(
                start,
                contentLength);
        nextLineStart =
            lineFeedIndex +
            1;

        return true;
    }

    /// <summary>
    /// Detects the document newline style for metadata rewrites.
    /// </summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <returns>The preferred newline sequence.</returns>
    private static string DetectNewLine(
            string markdown)
    {
        int lineFeedIndex =
            markdown.IndexOf(
                '\n');

        return lineFeedIndex > 0 &&
               markdown[lineFeedIndex - 1] ==
               '\r'
            ? "\r\n"
            : "\n";
    }
}
