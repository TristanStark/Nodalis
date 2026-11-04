namespace Nodalis.Core.Requirements;

/// <summary>
/// Read-only projection of the canonical EXI description section. Never
/// interprets arbitrary AI text as Markdown structure or changes a source.
/// </summary>
public static class ProjectRequirementDescriptionCodec
{
    private const int MaxDocumentCharacters = 1024 * 1024;
    private const int MaxDescriptionCharacters = 8000;

    /// <summary>
    /// Extracts the unique level-two "Description vérifiable" section
    /// outside fenced blocks. An absent, duplicated, malformed or oversized
    /// section remains unresolved rather than inventing a current value.
    /// </summary>
    /// <param name="markdown">Already validated project-local EXI Markdown.</param>
    /// <returns>Normalized section body, or null if it is not safe to compare.</returns>
    public static string? Extract(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxDocumentCharacters)
        {
            return null;
        }

        string[] lines = markdown.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        int start = -1;
        int end = lines.Length;
        bool fenced = false;
        char fenceMarker = '\0';
        int fenceLength = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string noIndent = line.TrimStart(' ');
            int indent = line.Length - noIndent.Length;
            if (indent <= 3 && noIndent.Length >= 3 &&
                (noIndent[0] == (char)96 || noIndent[0] == '~'))
            {
                int count = 0;
                while (count < noIndent.Length && noIndent[count] == noIndent[0])
                {
                    count++;
                }

                if (!fenced && count >= 3)
                {
                    fenced = true;
                    fenceMarker = noIndent[0];
                    fenceLength = count;
                    continue;
                }

                if (fenced && noIndent[0] == fenceMarker &&
                    count >= fenceLength &&
                    string.IsNullOrWhiteSpace(noIndent[count..]))
                {
                    fenced = false;
                    continue;
                }
            }

            if (fenced)
            {
                continue;
            }

            if (string.Equals(line, "## Description vérifiable",
                    StringComparison.Ordinal))
            {
                if (start >= 0)
                {
                    return null;
                }

                start = index;
                continue;
            }

            if (start >= 0 && end == lines.Length &&
                (line.StartsWith("# ", StringComparison.Ordinal) ||
                 line.StartsWith("## ", StringComparison.Ordinal)))
            {
                end = index;
            }
        }

        if (start < 0 || fenced)
        {
            return null;
        }

        string description = string.Join("\n",
            lines.Skip(start + 1).Take(end - start - 1)).Trim('\n');
        return description.Length <= MaxDescriptionCharacters
            ? description : null;
    }
}
