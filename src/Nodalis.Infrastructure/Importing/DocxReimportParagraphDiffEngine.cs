using System.Text;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Deterministic paragraph-level inspection of an imported Markdown fragment
/// against the latest Word text and the local Nodalis document. This is a
/// bounded, read-only diff, never a merge or a replacement algorithm.
/// </summary>
public static class DocxReimportParagraphDiffEngine
{
    private const int MaxParagraphs = 300;
    private const int MaxTextLength = 256_000;

    /// <summary>
    /// Compares the previous fragment to each side independently. Outside
    /// Markdown is not mistaken for a fragment edit when the exact baseline
    /// still exists exactly once inside the current Nodalis destination.
    /// Otherwise the entire destination is clearly marked as a fallback.
    /// </summary>
    public static DocxReimportParagraphDiff Compare(
        DocxReimportReviewedFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        string baseline = fragment.BaseMarkdown;

        string? word = fragment.Association.CurrentSectionIndex is not null
            ? fragment.LatestWordMarkdown : null;
        string? local = fragment.CurrentNodalisMarkdown;
        bool includesSurroundings = false;
        if (local is not null && !string.IsNullOrEmpty(baseline))
        {
            int first = local.IndexOf(baseline, StringComparison.Ordinal);
            if (first >= 0 && local.IndexOf(baseline,
                    first + baseline.Length, StringComparison.Ordinal) < 0)
            {
                local = baseline;
            }
            else
            {
                includesSurroundings = true;
            }
        }

        DocxReimportParagraphComparison wordComparison =
            ComparePair(baseline, word);
        DocxReimportParagraphComparison localComparison =
            ComparePair(baseline, local);
        return new DocxReimportParagraphDiff
        {
            Word = wordComparison,
            Nodalis = localComparison,
            LocalIncludesSurroundings = includesSurroundings
        };
    }

    /// <summary>
    /// Builds a bounded sequence diff using longest common subsequence.
    /// Groups adjacent removal/addition into a visual modification without
    /// inventing semantic equivalence between unrelated paragraphs.
    /// </summary>
    public static DocxReimportParagraphComparison ComparePair(
        string baseline, string? updated)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (updated is null)
        {
            return new DocxReimportParagraphComparison { Available = false };
        }

        if (baseline.Length > MaxTextLength || updated.Length > MaxTextLength)
        {
            return new DocxReimportParagraphComparison
            {
                Available = true,
                Truncated = true
            };
        }

        List<Paragraph> original = SplitParagraphs(baseline);
        List<Paragraph> current = SplitParagraphs(updated);
        if (original.Count > MaxParagraphs || current.Count > MaxParagraphs)
        {
            return new DocxReimportParagraphComparison
            {
                Available = true,
                Truncated = true
            };
        }

        int[,] lcs = new int[original.Count + 1, current.Count + 1];
        for (int i = original.Count - 1; i >= 0; i--)
        {
            for (int j = current.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(original[i].Text, current[j].Text,
                    StringComparison.Ordinal)
                    ? 1 + lcs[i + 1, j + 1]
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        List<DocxReimportParagraphRow> rows = [];
        int before = 0;
        int after = 0;
        while (before < original.Count || after < current.Count)
        {
            if (before < original.Count && after < current.Count &&
                string.Equals(original[before].Text, current[after].Text,
                    StringComparison.Ordinal))
            {
                rows.Add(Row(DocxReimportParagraphChangeKind.Unchanged,
                    original[before++], current[after++]));
                continue;
            }

            List<Paragraph> removed = [];
            List<Paragraph> added = [];
            while (before < original.Count || after < current.Count)
            {
                if (before < original.Count && after < current.Count &&
                    string.Equals(original[before].Text, current[after].Text,
                        StringComparison.Ordinal))
                {
                    break;
                }

                if (after < current.Count && (before == original.Count ||
                    lcs[before, after + 1] >= lcs[before + 1, after]))
                {
                    added.Add(current[after++]);
                }
                else
                {
                    removed.Add(original[before++]);
                }
            }

            int paired = Math.Min(removed.Count, added.Count);
            for (int i = 0; i < paired; i++)
            {
                rows.Add(Row(DocxReimportParagraphChangeKind.Modified,
                    removed[i], added[i]));
            }

            foreach (Paragraph paragraph in removed.Skip(paired))
            {
                rows.Add(Row(DocxReimportParagraphChangeKind.Removed,
                    paragraph, null));
            }

            foreach (Paragraph paragraph in added.Skip(paired))
            {
                rows.Add(Row(DocxReimportParagraphChangeKind.Added,
                    null, paragraph));
            }
        }

        return new DocxReimportParagraphComparison
        {
            Available = true,
            Rows = rows
        };
    }

    /// <summary>Builds a labeled comparison row with source line locations.</summary>
    private static DocxReimportParagraphRow Row(
        DocxReimportParagraphChangeKind kind, Paragraph? before, Paragraph? after) =>
        new DocxReimportParagraphRow
        {
            Kind = kind,
            PreviousLine = before?.Line,
            CurrentLine = after?.Line,
            PreviousText = before?.Text,
            CurrentText = after?.Text
        };

    /// <summary>
    /// Splits markdown on blank lines except inside fenced code blocks.
    /// Blocks retain their first source line and original nonblank lines.
    /// A newline-style change alone does not become a paragraph edit.
    /// </summary>
    private static List<Paragraph> SplitParagraphs(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        List<Paragraph> result = [];
        StringBuilder buffer = new StringBuilder();
        int firstLine = 1;
        char fenceCharacter = '\0';
        int fenceLength = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string text = lines[index];
            string trimmed = text.TrimStart();
            if (fenceLength == 0 && IsFence(trimmed, out char start, out int count))
            {
                fenceCharacter = start;
                fenceLength = count;
            }
            else if (fenceLength > 0 &&
                trimmed.Length >= fenceLength &&
                trimmed[0] == fenceCharacter &&
                trimmed.TakeWhile(character => character == fenceCharacter).Count() >=
                    fenceLength &&
                trimmed.Trim(fenceCharacter).Trim().Length == 0)
            {
                fenceLength = 0;
            }

            if (fenceLength == 0 && string.IsNullOrWhiteSpace(text))
            {
                Flush();
                firstLine = index + 2;
                continue;
            }

            if (buffer.Length == 0)
            {
                firstLine = index + 1;
            }
            else
            {
                buffer.Append('\n');
            }

            buffer.Append(text);
        }

        Flush();
        return result;

        /// <summary>Appends the accumulated Markdown paragraph if not blank.</summary>
        void Flush()
        {
            string text = buffer.ToString().TrimEnd('\n');
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(new Paragraph(text, firstLine));
            }

            buffer.Clear();
        }
    }

    /// <summary>Detects Markdown backtick and tilde fences of length three or more.</summary>
    private static bool IsFence(string text, out char marker, out int count)
    {
        marker = '\0';
        count = 0;
        if (text.Length < 3 || text[0] is not ('`' or '~'))
        {
            return false;
        }

        marker = text[0];
        char expectedMarker = marker;
        count = text.TakeWhile(character => character == expectedMarker).Count();
        return count >= 3;
    }

    /// <summary>One Markdown paragraph with its starting source line.</summary>
    private sealed record Paragraph(string Text, int Line);
}

/// <summary>Display-only paragraph change classification.</summary>
public enum DocxReimportParagraphChangeKind
{
    Unchanged,
    Added,
    Removed,
    Modified
}

/// <summary>An aligned paragraph for a read-only Word or Nodalis comparison.</summary>
public sealed record DocxReimportParagraphRow
{
    public required DocxReimportParagraphChangeKind Kind { get; init; }

    public int? PreviousLine { get; init; }

    public int? CurrentLine { get; init; }

    public string? PreviousText { get; init; }

    public string? CurrentText { get; init; }
}

/// <summary>A bounded diff to one side of the original imported fragment.</summary>
public sealed record DocxReimportParagraphComparison
{
    public bool Available { get; init; }

    public bool Truncated { get; init; }

    public List<DocxReimportParagraphRow> Rows { get; init; } = [];

    /// <summary>Number of paragraphs added, removed or changed.</summary>
    public int ChangedCount =>
        Rows.Count(row => row.Kind != DocxReimportParagraphChangeKind.Unchanged);
}

/// <summary>Independent changes from the baseline towards Word and Nodalis.</summary>
public sealed record DocxReimportParagraphDiff
{
    public required DocxReimportParagraphComparison Word { get; init; }

    public required DocxReimportParagraphComparison Nodalis { get; init; }

    /// <summary>
    /// The imported fragment was not uniquely identifiable in the current
    /// local document. Nodalis changes show the whole file, not a safe merge.
    /// </summary>
    public bool LocalIncludesSurroundings { get; init; }
}
