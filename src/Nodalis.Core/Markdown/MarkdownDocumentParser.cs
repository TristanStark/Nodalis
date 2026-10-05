using System.Text.RegularExpressions;

namespace Nodalis.Core.Markdown;

public static partial class MarkdownDocumentParser
{
    /// <summary>
    /// Performs the <c>Parse</c> operation.
    /// </summary>
    /// <param name="markdown">The <c>markdown</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string lineFeed = ((char)10).ToString();
        string carriageReturnLineFeed = string.Concat((char)13, (char)10);

        string content = MarkdownFrontMatterParser.Parse(
                markdown)
            .Body;

        string normalized = content
            .Replace(
                carriageReturnLineFeed,
                lineFeed,
                StringComparison.Ordinal)
            .Replace((char)13, (char)10);

        string[] lines = normalized.Split((char)10);
        global::System.Collections.Generic.List<global::Nodalis.Core.Markdown.MarkdownBlock> blocks = new List<MarkdownBlock>();

        for (int index = 0; index < lines.Length;)
        {
            string line = lines[index];

            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            global::System.Text.RegularExpressions.Match fence = CodeFencePattern().Match(line);
            if (fence.Success)
            {
                string language = fence.Groups["language"].Value;
                global::System.Collections.Generic.List<string> code = new List<string>();
                index++;

                while (index < lines.Length &&
                       !CodeFencePattern().IsMatch(lines[index]))
                {
                    code.Add(lines[index]);
                    index++;
                }

                if (index < lines.Length)
                {
                    index++;
                }

                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.CodeBlock,
                    Text = string.Join(lineFeed, code),
                    Language = string.IsNullOrWhiteSpace(language)
                        ? null
                        : language
                });

                continue;
            }

            if (TryParseTable(lines, ref index, out global::Nodalis.Core.Markdown.MarkdownBlock? table))
            {
                blocks.Add(table);
                continue;
            }

            global::System.Text.RegularExpressions.Match heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Heading,
                    Level = heading.Groups["marks"].Value.Length,
                    Text = heading.Groups["text"].Value.Trim()
                });

                index++;
                continue;
            }

            global::System.Text.RegularExpressions.Match checkbox = CheckboxPattern().Match(line);
            if (checkbox.Success)
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.ChecklistItem,
                    Text = checkbox.Groups["text"].Value,
                    IsChecked = checkbox.Groups["state"].Value
                        .Equals("x", StringComparison.OrdinalIgnoreCase)
                });

                index++;
                continue;
            }

            global::System.Text.RegularExpressions.Match unordered = UnorderedListPattern().Match(line);
            if (unordered.Success)
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.UnorderedListItem,
                    Text = unordered.Groups["text"].Value
                });

                index++;
                continue;
            }

            global::System.Text.RegularExpressions.Match ordered = OrderedListPattern().Match(line);
            if (ordered.Success)
            {
                int orderedListNumber = int.TryParse(
                        ordered.Groups["number"].Value,
                        out int parsedOrderedListNumber)
                    ? Math.Max(
                        1,
                        parsedOrderedListNumber)
                    : 1;

                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.OrderedListItem,
                    Text = ordered.Groups["text"].Value,
                    OrderedListNumber = orderedListNumber
                });

                index++;
                continue;
            }

            global::System.Text.RegularExpressions.Match quote = QuotePattern().Match(line);
            if (quote.Success)
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Quote,
                    Text = quote.Groups["text"].Value
                });

                index++;
                continue;
            }

            blocks.Add(new MarkdownBlock
            {
                Kind = MarkdownBlockKind.Paragraph,
                Text = line
            });

            index++;
        }

        return blocks;
    }

    /// <summary>
    /// Performs the <c>TryParseTable</c> operation.
    /// </summary>
    /// <param name="lines">The <c>lines</c> value.</param>
    /// <param name="index">The <c>index</c> value.</param>
    /// <param name="block">The <c>block</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool TryParseTable(
            IReadOnlyList<string> lines,
            ref int index,
            out MarkdownBlock block)
    {
        block = null!;

        if (index + 1 >= lines.Count ||
            !lines[index].Contains('|', StringComparison.Ordinal) ||
            !TableSeparatorPattern().IsMatch(lines[index + 1]))
        {
            return false;
        }

        global::System.Collections.Generic.List<global::System.Collections.Generic.List<string>> rows = new List<List<string>>
        {
            SplitTableRow(lines[index])
        };

        index += 2;

        while (index < lines.Count &&
               !string.IsNullOrWhiteSpace(lines[index]) &&
               lines[index].Contains('|', StringComparison.Ordinal))
        {
            rows.Add(SplitTableRow(lines[index]));
            index++;
        }

        block = new MarkdownBlock
        {
            Kind = MarkdownBlockKind.Table,
            TableRows = rows
        };

        return true;
    }

    /// <summary>
    /// Performs the <c>SplitTableRow</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> SplitTableRow(string line)
    {
        string trimmed = line.Trim().Trim('|');

        return trimmed
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
    }

    /// <summary>
    /// Performs the <c>HeadingPattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^(?<marks>#{1,6})\s+(?<text>.+?)\s*$",
            RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    /// <summary>
    /// Performs the <c>CheckboxPattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^\s*[-*+]\s+\[(?<state>[ xX])\]\s+(?<text>.*)$",
            RegexOptions.CultureInvariant)]
    private static partial Regex CheckboxPattern();

    /// <summary>
    /// Performs the <c>UnorderedListPattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^\s*[-*+]\s+(?<text>.+)$",
            RegexOptions.CultureInvariant)]
    private static partial Regex UnorderedListPattern();

    /// <summary>
    /// Performs the <c>OrderedListPattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^\s*(?<number>\d+)[.)]\s+(?<text>.+)$",
            RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListPattern();

    /// <summary>
    /// Performs the <c>QuotePattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^\s*>\s?(?<text>.*)$",
            RegexOptions.CultureInvariant)]
    private static partial Regex QuotePattern();

    /// <summary>
    /// Performs the <c>CodeFencePattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^\s*\x60{3}(?<language>[A-Za-z0-9_.+-]*)\s*$",
            RegexOptions.CultureInvariant)]
    private static partial Regex CodeFencePattern();

    /// <summary>
    /// Performs the <c>TableSeparatorPattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$",
            RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorPattern();
}
