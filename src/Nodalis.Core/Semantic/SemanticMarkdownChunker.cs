using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Composites;

namespace Nodalis.Core.Semantic;

/// <summary>Opt-in index limits, independent of workspace business metadata.</summary>
public sealed record SemanticChunkingOptions
{
    /// <summary>Maximum source text in one embedded passage.</summary>
    public int MaxCharacters { get; init; } = 1200;

    /// <summary>Repeated characters at a split boundary to maintain context.</summary>
    public int OverlapCharacters { get; init; } = 120;

    /// <summary>Whether fenced code is explicitly included (disabled by default).</summary>
    public bool IncludeCode { get; init; }

    /// <summary>Additional blocked workspace-relative path patterns, case-insensitive.</summary>
    public IReadOnlyList<string> ExcludedPathPatterns { get; init; } = [];
}

/// <summary>A canonical source passage with a stable hash and exact line provenance.</summary>
public sealed record SemanticChunk(
    string ChunkId,
    Guid DocumentId,
    string RelativePath,
    string? Heading,
    int HeadingLine,
    int SourceLine,
    string Content,
    string ContentHash);

/// <summary>
/// Deterministic Markdown passage chunker; never expands transclusions or indexes
/// front matter, fenced code or technical files by default.
/// </summary>
public static class SemanticMarkdownChunker
{
    /// <summary>Returns whether a canonical Markdown path is safe to index.</summary>
    /// <param name="relativePath">Workspace-relative document path.</param>
    /// <param name="options">Explicit exclusions.</param>
    /// <returns>True only for ordinary Markdown sources not matching exclusions.</returns>
    public static bool IsEligiblePath(string relativePath, SemanticChunkingOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 2048 ||
            Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
            relativePath.Contains('\0') || relativePath.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            segments.Any(segment => string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." || segment.StartsWith(".", StringComparison.Ordinal) ||
                segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("secrets", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("credentials", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("private", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("private.md", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("secret.md", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("credentials.md", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (string pattern in options?.ExcludedPathPatterns ?? [])
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            string token = pattern.Trim().Replace('\\', '/');
            if (token.Length > 200) continue;
            if (token.Contains('*'))
            {
                string regex = "^" + System.Text.RegularExpressions.Regex.Escape(token)
                    .Replace("\\*", ".*", StringComparison.Ordinal) + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(normalized, regex,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50))) return false;
            }
            else if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds bounded passages from one unexpanded canonical Markdown source.</summary>
    /// <param name="documentId">Stable existing document GUID.</param>
    /// <param name="relativePath">Workspace-relative document location.</param>
    /// <param name="markdown">Original source Markdown, never a virtual composite.</param>
    /// <param name="options">Segmentation and exclusion policy.</param>
    /// <returns>Deterministic passages with source provenance; empty for blocked sources.</returns>
    public static IReadOnlyList<SemanticChunk> Split(Guid documentId,
        string relativePath, string markdown, SemanticChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        SemanticChunkingOptions settings = options ?? new();
        if (documentId == Guid.Empty || !IsEligiblePath(relativePath, settings))
            return [];

        int max = Math.Clamp(settings.MaxCharacters, 160, 8192);
        int overlap = Math.Clamp(settings.OverlapCharacters, 0, Math.Min(512, max / 4));
        string[] lines = CompositeMarkdownSections.Normalize(markdown).Split('\n');
        int first = FrontMatterEnd(lines);
        List<SemanticChunk> result = [];
        List<(int Line, string Text)> paragraph = [];
        string? heading = null;
        int headingLine = 0;
        bool fenced = false;
        char marker = '\0';
        int markerLength = 0;

        for (int index = first; index <= lines.Length; index++)
        {
            string line = index < lines.Length ? lines[index] : string.Empty;
            if (index < lines.Length &&
                CompositeMarkdownSections.ToggleFence(line, ref fenced,
                    ref marker, ref markerLength))
            {
                Flush();
                continue;
            }

            if (fenced && !settings.IncludeCode) continue;

            if (index < lines.Length &&
                CompositeMarkdownSections.TryHeading(line, out _, out string title))
            {
                Flush();
                heading = title;
                headingLine = index + 1;
                continue;
            }

            // Do not index virtual copies of other canonical documents.
            if (index == lines.Length || string.IsNullOrWhiteSpace(line) ||
                CompositeMarkdownSyntax.Parse(line, index + 1) is not null)
            {
                Flush();
                continue;
            }

            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }

            paragraph.Add((index + 1, line));
        }

        return result;

        // Preserve accurate line positions even when a long paragraph is split.
        void Flush()
        {
            if (paragraph.Count == 0) return;
            string text = string.Join('\n', paragraph.Select(entry => entry.Text)).Trim();
            int firstLine = paragraph[0].Line;
            paragraph.Clear();
            if (text.Length == 0) return;
            int cursor = 0;
            int ordinal = 0;
            int scannedUntil = 0;
            int currentLine = firstLine;
            while (cursor < text.Length)
            {
                int amount = Math.Min(max, text.Length - cursor);
                string slice = text.Substring(cursor, amount).Trim();
                if (slice.Length > 0)
                {
                    while (scannedUntil < cursor)
                    {
                        if (text[scannedUntil] == '\n') currentLine++;
                        scannedUntil++;
                    }

                    int line = currentLine;

                    string hash = Hex(slice);
                    string identity = documentId.ToString("D") + "|" +
                        (heading ?? "") + "|" + headingLine + "|" + firstLine +
                        "|" + ordinal + "|" + hash;
                    result.Add(new SemanticChunk(Hex(identity), documentId,
                        relativePath.Replace('\\', '/'), heading, headingLine,
                        line, slice, hash));
                }

                if (cursor + amount == text.Length) break;
                cursor += amount - overlap;
                ordinal++;
            }
        }
    }

    /// <summary>Skips a fully closed YAML front matter block.</summary>
    /// <param name="lines">Source Markdown lines.</param>
    /// <returns>First body line index, or zero for malformed front matter.</returns>
    private static int FrontMatterEnd(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].Trim() != "---") return 0;
        for (int i = 1; i < Math.Min(lines.Count, 1024); i++)
        {
            if (lines[i].Trim() == "---") return i + 1;
        }

        return 0;
    }

    /// <summary>Hashes source passages using deterministic SHA256 identifiers.</summary>
    /// <param name="content">Text to hash.</param>
    /// <returns>Lowercase SHA256 hex.</returns>
    private static string Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
}
