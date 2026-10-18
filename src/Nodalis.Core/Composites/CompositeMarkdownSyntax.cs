namespace Nodalis.Core.Composites;

/// <summary>One full-line Markdown transclusion reference with optional section.</summary>
public sealed record CompositeInclude(string Target, string? Section, string? Alias, int Line);

/// <summary>Source excerpt and exact original heading position.</summary>
public sealed record CompositeExcerpt(string Markdown, int Line, string? Diagnostic);

/// <summary>One source diagnostic, never persisted to canonical notes.</summary>
public sealed record CompositeDiagnostic(string Source, int Line, string Message);

/// <summary>A derived inclusion edge, separate from causal impact relations.</summary>
public sealed record CompositeEdge(Guid SourceId, Guid TargetId);

/// <summary>An in-memory preview plus its source references and diagnostics.</summary>
public sealed record CompositeRenderResult(string Markdown,
    IReadOnlyList<CompositeEdge> Edges, IReadOnlyList<CompositeDiagnostic> Diagnostics);

/// <summary>Handles readable ![[document#section|alias]] directives without changing links.</summary>
public static class CompositeMarkdownSyntax
{
    /// <summary>Recognizes a standalone transclusion but never an ordinary internal link.</summary>
    /// <param name="line">Original Markdown line.</param>
    /// <param name="lineNumber">One-based source position.</param>
    /// <returns>Inclusion or null when the line is normal Markdown.</returns>
    public static CompositeInclude? Parse(string line, int lineNumber)
    {
        string text = line.Trim();
        if (!text.StartsWith("![[", StringComparison.Ordinal) ||
            !text.EndsWith("]]", StringComparison.Ordinal) || text.Length < 6)
        {
            return null;
        }

        string inside = text[3..^2];
        int bar = inside.IndexOf('|');
        string raw = bar < 0 ? inside : inside[..bar];
        string? alias = bar < 0 ? null : inside[(bar + 1)..].Trim();
        int hash = raw.IndexOf('#');
        string name = (hash < 0 ? raw : raw[..hash]).Trim();
        string? section = hash < 0 ? null : raw[(hash + 1)..].Trim();
        return new CompositeInclude(name, section, alias, lineNumber);
    }

    /// <summary>Accepts GUIDs and safe local index names, rejecting external paths/URLs.</summary>
    /// <param name="value">Raw reference.</param>
    /// <returns>True if it is safe to attempt index-only resolution.</returns>
    public static bool IsSafeTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 300 ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.IndexOfAny(['\0', '\r', '\n', '[', ']', ':', '*', '?']) >= 0 ||
            Path.IsPathRooted(value) || value.StartsWith('/'))
        {
            return false;
        }

        return !value.Replace('\\', '/').Split('/').Any(part =>
            part is "" or "." or "..");
    }

    /// <summary>Produces a stable GUID-backed include for newly inserted references.</summary>
    /// <param name="id">Canonical document GUID.</param>
    /// <param name="section">Optional heading.</param>
    /// <returns>Portable Markdown directive.</returns>
    public static string Format(Guid id, string? section = null)
    {
        if (id == Guid.Empty || section is { Length: > 200 } ||
            section?.IndexOfAny(['[', ']', '|', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Identifiant ou section invalide.");
        }

        return "![[" + id.ToString("D") +
            (string.IsNullOrWhiteSpace(section) ? "" : "#" + section.Trim()) + "]]";
    }
}

/// <summary>Extracts ATX-heading sections, excluding headings inside fenced code.</summary>
public static class CompositeMarkdownSections
{
    /// <summary>Extracts a heading, its content and nested subsections.</summary>
    /// <param name="markdown">Canonical Markdown input.</param>
    /// <param name="heading">Exact visible section title.</param>
    /// <returns>Excerpt or explicit missing/duplicate-heading diagnostic.</returns>
    public static CompositeExcerpt Extract(string markdown, string heading)
    {
        string[] lines = Normalize(markdown).Split('\n');
        List<(int Index, int Level)> matches = [];
        bool fenced = false;
        char marker = '\0';
        int count = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (ToggleFence(lines[i], ref fenced, ref marker, ref count))
            {
                continue;
            }

            if (!fenced && TryHeading(lines[i], out int depth, out string title) &&
                string.Equals(title, heading.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((i, depth));
            }
        }

        if (matches.Count != 1)
        {
            return new CompositeExcerpt("", 0, matches.Count == 0
                ? "Section introuvable : " + heading
                : "Section ambiguë (titres dupliqués) : " + heading);
        }

        (int start, int level) = matches[0];
        fenced = false;
        marker = '\0';
        count = 0;
        int end = lines.Length;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (ToggleFence(lines[i], ref fenced, ref marker, ref count))
            {
                continue;
            }

            if (!fenced && TryHeading(lines[i], out int nextLevel, out _) &&
                nextLevel <= level)
            {
                end = i;
                break;
            }
        }

        return new CompositeExcerpt(string.Join('\n', lines[start..end]), start + 1, null);
    }

    /// <summary>Returns only uniquely named section headings for insertion UX.</summary>
    /// <param name="markdown">Existing document text.</param>
    /// <returns>Unique selectable headings in appearance order.</returns>
    public static IReadOnlyList<string> Headings(string markdown)
    {
        string[] lines = Normalize(markdown).Split('\n');
        List<string> all = [];
        bool fenced = false;
        char marker = '\0';
        int count = 0;
        foreach (string line in lines)
        {
            if (ToggleFence(line, ref fenced, ref marker, ref count))
            {
                continue;
            }

            if (!fenced && TryHeading(line, out _, out string title))
            {
                all.Add(title);
            }
        }

        HashSet<string> unique = all.GroupBy(text => text,
            StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return all.Where(unique.Contains).ToArray();
    }

    /// <summary>Tracks tilde or backtick code fences of length at least three.</summary>
    /// <param name="line">Current Markdown line.</param>
    /// <param name="inside">Whether parsing is inside a fenced block.</param>
    /// <param name="marker">Current opening marker.</param>
    /// <param name="length">Current opening marker length.</param>
    /// <returns>True when a delimiter line was consumed.</returns>
    public static bool ToggleFence(string line, ref bool inside,
        ref char marker, ref int length)
    {
        string text = line.TrimStart();
        if (line.Length - text.Length > 3 || text.Length < 3 ||
            text[0] is not ('\u0060' or '~'))
        {
            return false;
        }

        int run = 0;
        while (run < text.Length && text[run] == text[0])
        {
            run++;
        }

        if (run < 3 || inside && (text[0] != marker || run < length))
        {
            return false;
        }

        if (inside)
        {
            inside = false;
            marker = '\0';
            length = 0;
        }
        else
        {
            inside = true;
            marker = text[0];
            length = run;
        }

        return true;
    }

    /// <summary>Recognizes an ATX Markdown heading (not arbitrary text starting with #).</summary>
    /// <param name="line">Source line.</param>
    /// <param name="level">Heading depth.</param>
    /// <param name="title">Visible title.</param>
    /// <returns>Whether an ATX heading was found.</returns>
    public static bool TryHeading(string line, out int level, out string title)
    {
        string text = line.TrimStart();
        level = 0;
        title = "";
        if (line.Length - text.Length > 3)
        {
            return false;
        }

        while (level < text.Length && text[level] == '#') level++;
        if (level is < 1 or > 6 || level == text.Length ||
            !char.IsWhiteSpace(text[level])) return false;
        title = text[(level + 1)..].Trim().TrimEnd('#').Trim();
        return title.Length > 0;
    }

    /// <summary>Normalizes line endings for reading while preserving disk bytes.</summary>
    /// <param name="markdown">Original input.</param>
    /// <returns>LF-separated text.</returns>
    public static string Normalize(string markdown) =>
        markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
