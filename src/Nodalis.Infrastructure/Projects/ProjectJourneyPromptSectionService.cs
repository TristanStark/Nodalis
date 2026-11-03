using System.Text;
using Nodalis.Core.Projects;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Lists and reads only bounded, user-linked Markdown sections. No recursive
/// project scan or document mutation; headings inside fences are ignored.
/// </summary>
public sealed class ProjectJourneyPromptSectionService
{
    private const int MaximumScanCharacters = 256000;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-contained source inspector.</summary>
    /// <param name="workspaceRoot">Single workspace root.</param>
    public ProjectJourneyPromptSectionService(string workspaceRoot)
    {
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>Lists heading anchors found in a bounded file prefix.</summary>
    /// <param name="reference">Explicitly linked source path.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Headings in source order, with physical line numbers.</returns>
    public async Task<IReadOnlyList<ProjectJourneyPromptSection>> ListAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        string path = _sources.ResolveExistingFile(reference);
        if (!string.Equals(Path.GetExtension(path), ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        (string prefix, bool truncated) = await ReadPrefixAsync(path,
            MaximumScanCharacters, cancellationToken);
        string[] lines = SplitCompleteLines(prefix, truncated);
        List<ProjectJourneyPromptSection> headings = [];
        bool fenced = false;
        for (int index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsFence(lines[index]))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced && TryHeading(lines[index], out int level,
                    out string? title))
            {
                headings.Add(new ProjectJourneyPromptSection(
                    title!, level, index + 1));
            }
        }

        return headings;
    }

    /// <summary>
    /// Reads exactly the previously selected heading section, or a bounded
    /// whole-file prefix. A moved heading fails closed instead of silently
    /// selecting another similarly named section.
    /// </summary>
    /// <param name="reference">Previously linked workspace relative file.</param>
    /// <param name="headingLine">Selected physical heading line, or null.</param>
    /// <param name="headingTitle">Exact selected heading text, or null.</param>
    /// <param name="maxCharacters">Remaining explicit extraction budget.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Exact bounded excerpt, or null if its anchor moved.</returns>
    public async Task<ProjectJourneyPromptExcerpt?> ReadAsync(
        string reference, int? headingLine, string? headingTitle,
        int maxCharacters, CancellationToken cancellationToken = default)
    {
        if (maxCharacters is < 1 or > 12000 ||
            headingLine.HasValue != (headingTitle is not null) ||
            headingLine is < 1)
        {
            throw new ArgumentException("Sélection de section invalide.");
        }

        string path = _sources.ResolveExistingFile(reference);
        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Seules les sources Markdown et texte peuvent être injectées.");
        }

        if (headingLine is null)
        {
            (string text, bool truncated) = await ReadPrefixAsync(
                path, maxCharacters, cancellationToken);
            return new ProjectJourneyPromptExcerpt(text, 1,
                1 + text.Count(character => character == '\n'),
                truncated);
        }

        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Une section ne peut être choisie que dans un Markdown.");
        }

        (string prefix, bool prefixTruncated) = await ReadPrefixAsync(
            path, MaximumScanCharacters, cancellationToken);
        string[] lines = SplitCompleteLines(prefix, prefixTruncated);
        int startIndex = headingLine.Value - 1;
        if (startIndex >= lines.Length)
        {
            return null;
        }

        bool fenced = false;
        int selectedLevel = 0;
        for (int index = 0; index <= startIndex; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsFence(lines[index]))
            {
                fenced = !fenced;
                continue;
            }

            if (index == startIndex && !fenced &&
                TryHeading(lines[index], out int level, out string? title) &&
                string.Equals(title, headingTitle, StringComparison.Ordinal))
            {
                selectedLevel = level;
            }
        }

        if (selectedLevel == 0)
        {
            return null;
        }

        StringBuilder content = new();
        bool truncatedSection = false;
        int lastLine = headingLine.Value - 1;
        for (int index = startIndex; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index > startIndex && !fenced &&
                TryHeading(lines[index], out int level, out _) &&
                level <= selectedLevel)
            {
                break;
            }

            string line = lines[index] + "\n";
            int remaining = maxCharacters - content.Length;
            if (line.Length > remaining)
            {
                content.Append(line.AsSpan(0, Math.Max(remaining, 0)));
                truncatedSection = true;
                lastLine = index + 1;
                break;
            }

            content.Append(line);
            lastLine = index + 1;
            if (IsFence(lines[index]))
            {
                fenced = !fenced;
            }
        }

        if (lastLine == lines.Length && prefixTruncated)
        {
            truncatedSection = true;
        }

        return new ProjectJourneyPromptExcerpt(content.ToString(),
            headingLine.Value, lastLine, truncatedSection);
    }

    /// <summary>Reads at most limit+1 UTF-8 characters without whole-file I/O.</summary>
    /// <param name="path">Already validated local file.</param>
    /// <param name="limit">Maximum content size.</param>
    /// <param name="ct">Cooperative cancellation.</param>
    /// <returns>Prefix and whether more text exists.</returns>
    private static async Task<(string Text, bool Truncated)> ReadPrefixAsync(
        string path, int limit, CancellationToken ct)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 8192, FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        char[] chars = new char[limit + 1];
        int used = 0;
        while (used < chars.Length)
        {
            int read = await reader.ReadAsync(chars.AsMemory(used,
                chars.Length - used), ct);
            if (read == 0)
            {
                break;
            }

            used += read;
        }

        return (new string(chars, 0, Math.Min(used, limit)), used > limit);
    }

    /// <summary>Excludes the possibly incomplete last line of a capped scan.</summary>
    /// <param name="prefix">Bounded text.</param>
    /// <param name="truncated">Whether more text exists.</param>
    /// <returns>Complete logical source lines.</returns>
    private static string[] SplitCompleteLines(string prefix, bool truncated)
    {
        string[] lines = prefix.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n');
        if (truncated && !prefix.EndsWith('\n'))
        {
            return lines[..^1];
        }

        return lines;
    }

    /// <summary>Detects a fence marker without treating headings as active.</summary>
    /// <param name="line">Source line.</param>
    /// <returns>True for a Markdown backtick or tilde fence.</returns>
    private static bool IsFence(string line)
    {
        string trimmed = line.TrimStart(' ', '\t');
        return trimmed.StartsWith("```", StringComparison.Ordinal) ||
            trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    /// <summary>Recognizes one ATX heading, not headings inside code fences.</summary>
    /// <param name="line">Markdown line.</param>
    /// <param name="level">Markdown level.</param>
    /// <param name="title">Exact readable title without trailing hashes.</param>
    /// <returns>True for a nonempty H1-H6 heading.</returns>
    private static bool TryHeading(
        string line, out int level, out string? title)
    {
        level = 0;
        title = null;
        while (level < line.Length && level < 6 && line[level] == '#')
        {
            level++;
        }

        if (level == 0 || level >= line.Length ||
            line[level] is not (' ' or '\t'))
        {
            return false;
        }

        title = line[(level + 1)..].Trim();
        while (title.EndsWith('#'))
        {
            title = title[..^1].TrimEnd();
        }

        return title.Length != 0;
    }
}

/// <summary>One explicit section choice with a physical source anchor.</summary>
/// <param name="Title">Heading title.</param>
/// <param name="Level">Markdown heading level 1-6.</param>
/// <param name="StartLine">One-indexed physical source line.</param>
public sealed record ProjectJourneyPromptSection(
    string Title, int Level, int StartLine)
{
    /// <summary>UI label disambiguating same-title headings by line.</summary>
    public string Label => "L" + StartLine + " · " +
        new string('#', Level) + " " + Title;
}

/// <summary>Read-only source excerpt bound to a selected section or document.</summary>
/// <param name="Text">Bounded UTF-8 content.</param>
/// <param name="StartLine">One-indexed source start line.</param>
/// <param name="EndLine">One-indexed source last included line.</param>
/// <param name="Truncated">Whether the selected content was truncated.</param>
public sealed record ProjectJourneyPromptExcerpt(
    string Text, int StartLine, int EndLine, bool Truncated);
