namespace Nodalis.Core.Requirements;

/// <summary>
/// Read-only projection and pure rewriting of the one canonical EXI
/// description section. No file system, model execution or approval occurs.
/// </summary>
public static class ProjectRequirementDescriptionCodec
{
    private const int MaxDocumentCharacters = 1024 * 1024;
    private const int MaxDescriptionCharacters = 8000;

    /// <summary>
    /// Extracts the unique level-two "Description vérifiable" section
    /// outside fenced blocks. Ambiguous or oversized sources fail closed.
    /// </summary>
    /// <param name="markdown">Validated project-local EXI Markdown.</param>
    /// <returns>Normalized body, or null if safe matching is impossible.</returns>
    public static string? Extract(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        (int Start, int End)? bounds = FindBounds(markdown);
        if (bounds is null)
        {
            return null;
        }

        string body = NormalizeNewlines(markdown[
            bounds.Value.Start..bounds.Value.End]).Trim('\n');
        return body.Length <= MaxDescriptionCharacters ? body : null;
    }

    /// <summary>
    /// Creates an in-memory replacement of exactly one canonical EXI
    /// description section, preserving every byte of the surrounding text
    /// when encoded with the source's UTF-8 encoding. Rejects inserted
    /// top-level headings, ambiguous fences, and stale reviewed values.
    /// </summary>
    /// <param name="markdown">Current SHA-pinned Markdown source.</param>
    /// <param name="card">Canonical identity parsed from that source.</param>
    /// <param name="expectedCurrent">Description actually reviewed by a human.</param>
    /// <param name="approved">New human-approved body (no outer newlines).</param>
    /// <returns>Proposed Markdown snapshot; this does not save anything.</returns>
    public static string Rewrite(string markdown, ProjectRequirementCard card,
        string expectedCurrent, string approved)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(approved);
        if (approved.Length > MaxDescriptionCharacters ||
            approved.Contains('\r') || approved.Contains('\0') ||
            approved != approved.Trim('\n') ||
            approved.Contains('\uFEFF'))
        {
            throw new InvalidDataException(
                "Description approuvée invalide : 8000 caractères, sauts LF uniquement.");
        }

        (int Start, int End)? bounds = FindBounds(markdown);
        if (bounds is null ||
            !string.Equals(Extract(markdown), expectedCurrent,
                StringComparison.Ordinal) ||
            ProjectRequirementMarkdownCodec.Parse(markdown) != card)
        {
            throw new InvalidDataException(
                "Section ou identité EXI différente de l'instantané approuvé.");
        }

        // The original text surrounding the section is retained verbatim,
        // including metadata, criteria, history and other Markdown sections.
        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string replacement = newline +
            approved.Replace("\n", newline, StringComparison.Ordinal) +
            newline + newline;
        string rewritten = markdown[..bounds.Value.Start] + replacement +
            markdown[bounds.Value.End..];
        if (rewritten.Length > MaxDocumentCharacters ||
            ProjectRequirementMarkdownCodec.Parse(rewritten) != card ||
            !string.Equals(Extract(rewritten), approved, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "La proposition IA altérerait la structure ou les métadonnées EXI.");
        }

        return rewritten;
    }

    /// <summary>
    /// Finds exact source character offsets to preserve unrelated original
    /// Markdown, honoring fenced code blocks and rejecting duplicate headings.
    /// </summary>
    /// <param name="markdown">Raw source, not normalized.</param>
    /// <returns>Body offsets, or null for an unsafe or missing section.</returns>
    private static (int Start, int End)? FindBounds(string markdown)
    {
        if (markdown.Length > MaxDocumentCharacters)
        {
            return null;
        }

        string[] lines = markdown.Split('\n');
        int offset = 0;
        int start = -1;
        int end = -1;
        bool fenced = false;
        char fenceMarker = '\0';
        int fenceLength = 0;
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string noIndent = line.TrimStart(' ');
            int indent = line.Length - noIndent.Length;
            bool delimiter = indent <= 3 && noIndent.Length >= 3 &&
                (noIndent[0] == (char)96 || noIndent[0] == '~');
            if (delimiter)
            {
                int length = 0;
                while (length < noIndent.Length &&
                    noIndent[length] == noIndent[0])
                {
                    length++;
                }

                if (!fenced && length >= 3)
                {
                    fenced = true;
                    fenceMarker = noIndent[0];
                    fenceLength = length;
                }
                else if (fenced && noIndent[0] == fenceMarker &&
                    length >= fenceLength &&
                    string.IsNullOrWhiteSpace(noIndent[length..]))
                {
                    fenced = false;
                }
            }
            else if (!fenced)
            {
                if (string.Equals(line, "## Description vérifiable",
                    StringComparison.Ordinal))
                {
                    if (start >= 0)
                    {
                        return null;
                    }

                    start = offset + rawLine.Length +
                        (offset + rawLine.Length < markdown.Length ? 1 : 0);
                }
                else if (start >= 0 && end < 0 &&
                    (line.StartsWith("# ", StringComparison.Ordinal) ||
                     line.StartsWith("## ", StringComparison.Ordinal)))
                {
                    end = offset;
                }
            }

            offset += rawLine.Length + 1;
        }

        if (fenced || start < 0)
        {
            return null;
        }

        return (start, end < 0 ? markdown.Length : end);
    }

    /// <summary>Normalizes line endings for three-way field comparisons.</summary>
    /// <param name="text">One bounded section of source Markdown.</param>
    /// <returns>LF-only content.</returns>
    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
