using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Markdown;

namespace Nodalis.Core.Tests;

/// <summary>
/// Read-only test block extracted from one existing Markdown document.
/// Heading labels are mutable; only a unique GUID is a durable identity.
/// </summary>
/// <param name="Id">Stable block marker, or null for a legacy heading.</param>
/// <param name="Reference">Readable TEST-/TST- code.</param>
/// <param name="Title">Current heading title.</param>
/// <param name="HeadingLine">One-based Markdown source line.</param>
/// <param name="HeadingLevel">Heading nesting level.</param>
/// <param name="Markdown">Exact source section, including its heading and marker.</param>
/// <param name="Sha256">SHA-256 of the exact section, not the whole large cahier.</param>
/// <param name="CanFreeze">Whether a passage may safely capture this block.</param>
public sealed record ProjectTestDefinition(
    Guid? Id, string Reference, string Title, int HeadingLine,
    int HeadingLevel, string Markdown, string Sha256, bool CanFreeze);

/// <summary>Pure result of test definition discovery without modifying source.</summary>
/// <param name="Tests">Recognized TEST-/TST- headings in source order.</param>
/// <param name="Diagnostics">Missing, invalid, or ambiguous block IDs.</param>
public sealed record ProjectTestDefinitionScan(
    IReadOnlyList<ProjectTestDefinition> Tests,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Extracts stable, specifically named test definitions from a large Markdown
/// cahier or an individual test file; nested headings belong to their parent
/// test until the next heading at the same or a shallower level.
/// </summary>
public static class ProjectTestDefinitionCodec
{
    /// <summary>Recognizes a canonical TEST-xxxx or TST-xxxx code.</summary>
    /// <param name="reference">Human-readable heading code.</param>
    /// <returns>True for a positive three- or four-digit reference.</returns>
    public static bool IsReference(string? reference)
    {
        if (reference is null)
        {
            return false;
        }

        string digits;
        if (reference.StartsWith("TEST-", StringComparison.Ordinal))
        {
            digits = reference[5..];
        }
        else if (reference.StartsWith("TST-", StringComparison.Ordinal))
        {
            digits = reference[4..];
        }
        else
        {
            return false;
        }

        return (digits.Length is 3 or 4) &&
            int.TryParse(digits, NumberStyles.None,
                CultureInfo.InvariantCulture, out int value) &&
            value > 0 &&
            string.Equals(value.ToString("D" + digits.Length,
                CultureInfo.InvariantCulture), digits, StringComparison.Ordinal);
    }

    /// <summary>
    /// Derives exact test-section snapshots while preserving every source
    /// character. No inferred outcome or campaign mutation occurs here.
    /// </summary>
    /// <param name="markdown">Original source including its newline style.</param>
    /// <returns>Recognized test headings, source fingerprints and diagnostics.</returns>
    public static ProjectTestDefinitionScan Inspect(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        MarkdownBlockIdentityScan scan = MarkdownBlockIdentityCodec.Inspect(
            markdown);
        List<string> diagnostics = [];
        List<ProjectTestDefinition> tests = [];
        List<MarkdownBlockIdentityEntry> headings = [.. scan.Blocks];
        int[] ends = new int[headings.Count];
        Stack<int> open = new();

        // Linear-time section boundaries, including nested headings and
        // fenced-code exclusions provided by the shared outline parser.
        for (int index = 0; index < headings.Count; index++)
        {
            while (open.Count > 0 &&
                   headings[open.Peek()].Level >= headings[index].Level)
            {
                ends[open.Pop()] = headings[index].HeadingOffset;
            }

            open.Push(index);
        }

        while (open.Count > 0)
        {
            ends[open.Pop()] = markdown.Length;
        }

        HashSet<Guid> repeatedIds = scan.Issues.Where(issue =>
            issue.Kind == MarkdownBlockIdentityIssueKind.DuplicateId &&
            issue.Id.HasValue).Select(issue => issue.Id!.Value).ToHashSet();
        List<(int Index, string Reference, string Title)> recognized = [];
        for (int index = 0; index < headings.Count; index++)
        {
            string label = headings[index].Title.Trim();
            int separator = label.IndexOfAny([' ', '\t', '—', '–', ':']);
            string code = separator < 0 ? label : label[..separator];
            if (!code.StartsWith("TEST-", StringComparison.Ordinal) &&
                !code.StartsWith("TST-", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsReference(code) || separator < 0 ||
                string.IsNullOrWhiteSpace(
                    label[(separator + 1)..].TrimStart(' ', '—', '–', ':')))
            {
                diagnostics.Add("Titre de test sans code ou description valide, ligne " +
                    headings[index].HeadingLine + ".");
                continue;
            }

            recognized.Add((index, code,
                label[(separator + 1)..].TrimStart(' ', '—', '–', ':')));
        }

        HashSet<string> repeatedCodes = recognized.GroupBy(
            item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(
                StringComparer.OrdinalIgnoreCase);
        foreach ((int index, string code, string title) in recognized)
        {
            MarkdownBlockIdentityEntry heading = headings[index];
            bool invalid = heading.Id is null ||
                heading.Id is Guid id && repeatedIds.Contains(id) ||
                repeatedCodes.Contains(code) ||
                scan.Issues.Any(issue =>
                    issue.HeadingLine == heading.HeadingLine);

            if (heading.Id is null)
            {
                diagnostics.Add(code + " : GUID de bloc absent ou malformé.");
            }

            if (repeatedCodes.Contains(code))
            {
                diagnostics.Add(code + " : référence répétée dans le cahier.");
            }

            if (heading.Id is Guid repeated && repeatedIds.Contains(repeated))
            {
                diagnostics.Add(code + " : GUID répété dans le cahier.");
            }

            int length = ends[index] - heading.HeadingOffset;
            string section = markdown.Substring(heading.HeadingOffset, length);
            string sha = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(section)));
            tests.Add(new ProjectTestDefinition(
                heading.Id, code, title, heading.HeadingLine,
                heading.Level, section, sha, !invalid));
        }

        return new ProjectTestDefinitionScan(tests, diagnostics);
    }
}
