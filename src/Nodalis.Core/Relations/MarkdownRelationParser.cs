using System.Text;

namespace Nodalis.Core.Relations;

/// <summary>
/// Parses and formats Nodalis typed relations stored as ordinary readable Markdown blockquotes.
/// </summary>
public static class MarkdownRelationParser
{
    private const string Prefix = "> Relation:";

    /// <summary>
    /// Parses every relation line outside fenced code blocks.
    /// Unknown relation types and malformed target IDs are retained instead of rejected.
    /// </summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <returns>The relations in source order.</returns>
    public static IReadOnlyList<MarkdownRelationEntry> Parse(
            string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        global::System.Collections.Generic.List<global::Nodalis.Core.Relations.MarkdownRelationEntry> result =
            new List<MarkdownRelationEntry>();

        using StringReader reader =
            new StringReader(markdown);

        bool inFence =
            false;
        int lineNumber =
            0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            string trimmed =
                line.Trim();

            if (trimmed.StartsWith(
                    "```",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "~~~",
                    StringComparison.Ordinal))
            {
                inFence =
                    !inFence;
                continue;
            }

            if (inFence ||
                !trimmed.StartsWith(
                    Prefix,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            MarkdownRelationEntry? relation =
                ParseLine(
                    line,
                    lineNumber);

            if (relation is not null)
            {
                result.Add(
                    relation);
            }
        }

        return result;
    }

    /// <summary>
    /// Formats one canonical readable relation line.
    /// </summary>
    /// <param name="relationType">The relation type.</param>
    /// <param name="targetId">The stable target identifier.</param>
    /// <param name="targetLabel">The current human-readable target label.</param>
    /// <returns>A Markdown blockquote relation line.</returns>
    public static string Format(
            string relationType,
            Guid targetId,
            string targetLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLabel);

        string normalizedType =
            NormalizeField(
                relationType);
        string normalizedLabel =
            NormalizeField(
                targetLabel);

        return
            $"{Prefix} {normalizedType} | Cible: {normalizedLabel} | ID: {targetId:D}";
    }

    /// <summary>
    /// Parses one canonical or user-authored relation line.
    /// </summary>
    /// <param name="line">The original source line.</param>
    /// <param name="lineNumber">The one-based source line.</param>
    /// <returns>The parsed relation, or <see langword="null"/> when required fields are absent.</returns>
    private static MarkdownRelationEntry? ParseLine(
            string line,
            int lineNumber)
    {
        string trimmed =
            line.Trim();
        string payload =
            trimmed[Prefix.Length..].Trim();

        string[] parts =
            payload.Split(
                '|',
                StringSplitOptions.TrimEntries);

        if (parts.Length == 0 ||
            string.IsNullOrWhiteSpace(
                parts[0]))
        {
            return null;
        }

        string relationType =
            parts[0].Trim();
        string? label =
            null;
        string? rawId =
            null;

        for (int index = 1;
             index < parts.Length;
             index++)
        {
            string part =
                parts[index];

            int separator =
                part.IndexOf(':');

            if (separator <= 0)
            {
                continue;
            }

            string key =
                part[..separator].Trim();
            string value =
                part[(separator + 1)..].Trim();

            if (key.Equals(
                    "Cible",
                    StringComparison.CurrentCultureIgnoreCase) ||
                key.Equals(
                    "Target",
                    StringComparison.CurrentCultureIgnoreCase))
            {
                label =
                    value;
            }
            else if (key.Equals(
                         "ID",
                         StringComparison.OrdinalIgnoreCase))
            {
                rawId =
                    value;
            }
        }

        if (string.IsNullOrWhiteSpace(
                rawId))
        {
            return null;
        }

        Guid? targetId =
            Guid.TryParse(
                rawId,
                out Guid parsedId)
                ? parsedId
                : null;

        return new MarkdownRelationEntry
        {
            RelationType =
                relationType,
            TargetId =
                targetId,
            RawTargetId =
                rawId,
            TargetLabel =
                string.IsNullOrWhiteSpace(
                    label)
                    ? null
                    : label,
            LineNumber =
                lineNumber,
            RawLine =
                line
        };
    }

    /// <summary>
    /// Normalizes a user-visible field so the canonical pipe-delimited syntax remains parseable.
    /// </summary>
    /// <param name="value">The field to normalize.</param>
    /// <returns>A single-line field with pipe separators replaced by slashes.</returns>
    private static string NormalizeField(
            string value)
    {
        StringBuilder builder =
            new StringBuilder(
                value.Trim().Length);

        foreach (char character in value.Trim())
        {
            builder.Append(
                character switch
                {
                    '\r' or '\n' => ' ',
                    '|' => '/',
                    _ => character
                });
        }

        return builder
            .ToString()
            .Trim();
    }
}
