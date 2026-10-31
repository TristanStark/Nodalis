namespace Nodalis.Core.Markdown;

/// <summary>
/// Provides read-only discovery and explicitly requested, lossless insertion of stable
/// Markdown block IDs. No existing project or document is migrated on a read.
/// </summary>
public static class MarkdownBlockIdentityCodec
{
    private const string MarkerPrefix = "<!-- nodalis:block:";
    private const string MarkerSuffix = "-->";

    /// <summary>
    /// Inspects immediately adjacent marker lines on ATX headings outside fenced code.
    /// Titles and offsets are navigation hints only; the GUID is the persistent identity.
    /// </summary>
    /// <param name="markdown">Unmodified Markdown source.</param>
    /// <returns>Headings, parsed IDs and explicit diagnostics.</returns>
    public static MarkdownBlockIdentityScan Inspect(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        List<MarkdownBlockIdentityEntry> blocks = [];
        List<MarkdownBlockIdentityIssue> issues = [];
        HashSet<Guid> seenIds = [];

        foreach (MarkdownOutlineEntry heading in MarkdownOutlineParser.Parse(markdown))
        {
            Guid? blockId = null;
            bool hasMarker = false;
            int? markerLine = null;
            int headingLineEnd = markdown.IndexOf('\n', heading.Offset);

            if (headingLineEnd >= 0 && headingLineEnd + 1 < markdown.Length)
            {
                int markerStart = headingLineEnd + 1;
                int markerEnd = markdown.IndexOf('\n', markerStart);
                if (markerEnd < 0)
                {
                    markerEnd = markdown.Length;
                }

                string nextLine = markdown.Substring(
                    markerStart, markerEnd - markerStart).Trim();

                if (nextLine.StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    hasMarker = true;
                    markerLine = heading.LineNumber + 1;

                    if (TryReadMarker(nextLine, out Guid parsedId))
                    {
                        blockId = parsedId;
                        if (!seenIds.Add(parsedId))
                        {
                            issues.Add(new MarkdownBlockIdentityIssue(
                                MarkdownBlockIdentityIssueKind.DuplicateId,
                                heading.LineNumber, markerLine.Value, parsedId));
                        }
                    }
                    else
                    {
                        issues.Add(new MarkdownBlockIdentityIssue(
                            MarkdownBlockIdentityIssueKind.MalformedMarker,
                            heading.LineNumber, markerLine.Value, null));
                    }
                }
            }

            blocks.Add(new MarkdownBlockIdentityEntry(
                heading.LineNumber, heading.Title, heading.Level, heading.Offset,
                blockId, hasMarker, markerLine));
        }

        return new MarkdownBlockIdentityScan(blocks, issues);
    }

    /// <summary>
    /// Formats a canonical, invisible-to-renderers Markdown identity marker.
    /// </summary>
    /// <param name="id">Non-empty persistent block identity.</param>
    /// <returns>A standalone HTML comment to place immediately after its heading.</returns>
    public static string FormatMarker(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A block ID must not be empty.", nameof(id));
        }

        return $"{MarkerPrefix}{id:D} {MarkerSuffix}";
    }

    /// <summary>
    /// Builds the updated in-memory Markdown after an explicit user-approved
    /// identity assignment. Repeating the same assignment is idempotent.
    /// Existing markers, collisions and malformed documents are never overwritten.
    /// The caller remains responsible for optimistic-concurrency checks and atomic saving.
    /// </summary>
    /// <param name="markdown">Current source snapshot.</param>
    /// <param name="headingLine">One-based line of the heading to identify.</param>
    /// <param name="id">Chosen unique block identifier.</param>
    /// <returns>New Markdown, preserving every original character and newline style.</returns>
    public static string InsertMarker(string markdown, int headingLine, Guid id)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        string marker = FormatMarker(id);

        MarkdownBlockIdentityScan scan = Inspect(markdown);
        if (scan.Issues.Count != 0)
        {
            throw new InvalidOperationException(
                "Block identity diagnostics must be resolved before writing.");
        }

        MarkdownBlockIdentityEntry? heading = scan.Blocks.FirstOrDefault(
            block => block.HeadingLine == headingLine);
        if (heading is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(headingLine), "The requested heading does not exist.");
        }

        if (heading.Id == id)
        {
            return markdown;
        }

        if (heading.HasMarker || scan.Blocks.Any(block => block.Id == id))
        {
            throw new InvalidOperationException(
                "A block marker cannot replace an existing identity or reuse another block ID.");
        }

        int newlineIndex = markdown.IndexOf('\n', heading.HeadingOffset);
        if (newlineIndex < 0)
        {
            string documentNewline = DetectNewline(markdown);
            return markdown + documentNewline + marker;
        }

        string newline = newlineIndex > 0 && markdown[newlineIndex - 1] == '\r'
            ? "\r\n"
            : "\n";

        return markdown.Insert(newlineIndex + 1, marker + newline);
    }

    /// <summary>
    /// Finds the same stable ID in different documents without modifying either
    /// file. Same-document collisions are exposed by <see cref="Inspect"/>.
    /// </summary>
    /// <param name="documents">Canonical Markdown sources, already selected by the caller.</param>
    /// <returns>Cross-document collisions in source order.</returns>
    public static IReadOnlyList<MarkdownBlockIdentityCollision> FindCrossDocumentCollisions(
        IEnumerable<MarkdownBlockIdentitySource> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        Dictionary<Guid, (string Path, int Line)> firstOccurrences = [];
        List<MarkdownBlockIdentityCollision> collisions = [];

        foreach (MarkdownBlockIdentitySource document in documents)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(document.RelativePath);
            ArgumentNullException.ThrowIfNull(document.Markdown);

            foreach (MarkdownBlockIdentityEntry block in Inspect(document.Markdown).Blocks)
            {
                if (block.Id is not Guid id)
                {
                    continue;
                }

                if (!firstOccurrences.TryGetValue(id, out (string Path, int Line) first))
                {
                    firstOccurrences.Add(id, (document.RelativePath, block.HeadingLine));
                }
                else if (!string.Equals(
                    first.Path, document.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    collisions.Add(new MarkdownBlockIdentityCollision(
                        id, first.Path, first.Line, document.RelativePath, block.HeadingLine));
                }
            }
        }

        return collisions;
    }

    /// <summary>
    /// Parses a strictly delimited, non-empty GUID in the canonical comment syntax.
    /// </summary>
    /// <param name="line">Trimmed candidate marker.</param>
    /// <param name="id">Parsed GUID when valid.</param>
    /// <returns>True only for an unambiguous marker.</returns>
    private static bool TryReadMarker(string line, out Guid id)
    {
        id = Guid.Empty;

        if (!line.StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase) ||
            !line.EndsWith(MarkerSuffix, StringComparison.Ordinal) ||
            line.Length <= MarkerPrefix.Length + MarkerSuffix.Length)
        {
            return false;
        }

        string raw = line[MarkerPrefix.Length..^MarkerSuffix.Length].Trim();
        return Guid.TryParseExact(raw, "D", out id) && id != Guid.Empty;
    }

    /// <summary>
    /// Uses the first existing newline style, or LF for a one-line document.
    /// </summary>
    /// <param name="markdown">Source text.</param>
    /// <returns>CRLF or LF.</returns>
    private static string DetectNewline(string markdown)
    {
        int index = markdown.IndexOf('\n');
        return index > 0 && markdown[index - 1] == '\r' ? "\r\n" : "\n";
    }
}
