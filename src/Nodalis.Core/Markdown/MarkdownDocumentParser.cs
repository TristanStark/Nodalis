using System.Text.RegularExpressions;

namespace Nodalis.Core.Markdown;

public static partial class MarkdownDocumentParser
{
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = markdown
            .Replace("
", "
", StringComparison.Ordinal)
            .Replace('', '
');

        var lines = normalized.Split('
');
        var blocks = new List<MarkdownBlock>();

        for (var index = 0; index < lines.Length;)
        {
            var line = lines[index];

            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            var fence = CodeFencePattern().Match(line);
            if (fence.Success)
            {
                var language = fence.Groups["language"].Value;
                var code = new List<string>();
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
                    Text = string.Join("
", code),
                    Language = string.IsNullOrWhiteSpace(language)
                        ? null
                        : language
                });

                continue;
            }

            if (TryParseTable(lines, ref index, out var table))
            {
                blocks.Add(table);
                continue;
            }

            var heading = HeadingPattern().Match(line);
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

            var checkbox = CheckboxPattern().Match(line);
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

            var unordered = UnorderedListPattern().Match(line);
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

            var ordered = OrderedListPattern().Match(line);
            if (ordered.Success)
            {
                blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.OrderedListItem,
                    Text = ordered.Groups["text"].Value
                });

                index++;
                continue;
            }

            var quote = QuotePattern().Match(line);
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

        var rows = new List<List<string>>
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

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim().Trim('|');

        return trimmed
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
    }

    [GeneratedRegex(
        @"^(?<marks>#{1,6})s+(?<text>.+?)s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(
        @"^s*[-*+]s+[(?<state>[ xX])]s+(?<text>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CheckboxPattern();

    [GeneratedRegex(
        @"^s*[-*+]s+(?<text>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnorderedListPattern();

    [GeneratedRegex(
        @"^s*d+[.)]s+(?<text>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListPattern();

    [GeneratedRegex(
        @"^s*>s?(?<text>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuotePattern();

    [GeneratedRegex(
        @"^s*`{3}(?<language>[A-Za-z0-9_.+-]*)s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CodeFencePattern();

    [GeneratedRegex(
        @"^s*|?s*:?-{3,}:?s*(?:|s*:?-{3,}:?s*)+|?s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorPattern();
}
