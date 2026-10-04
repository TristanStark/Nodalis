using System.Text.RegularExpressions;

namespace Nodalis.Core.Markdown;

public static partial class MarkdownInlineParser
{
    /// <summary>
    /// Performs the <c>Parse</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static IReadOnlyList<MarkdownInline> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        global::System.Collections.Generic.List<global::Nodalis.Core.Markdown.MarkdownInline> result = new List<MarkdownInline>();
        int position = 0;

        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
            {
                result.Add(new MarkdownInline
                {
                    Kind = MarkdownInlineKind.Text,
                    Text = text[position..match.Index]
                });
            }

            result.Add(CreateInline(match));
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            result.Add(new MarkdownInline
            {
                Kind = MarkdownInlineKind.Text,
                Text = text[position..]
            });
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>CreateInline</c> operation.
    /// </summary>
    /// <param name="match">The <c>match</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static MarkdownInline CreateInline(Match match)
    {
        if (match.Groups["internal"].Success)
        {
            string target = match.Groups["internalTarget"].Value;
            string alias = match.Groups["internalAlias"].Success
                ? match.Groups["internalAlias"].Value
                : target;

            return new MarkdownInline
            {
                Kind = MarkdownInlineKind.InternalLink,
                Text = alias,
                Target = target
            };
        }

        if (match.Groups["image"].Success)
        {
            return new MarkdownInline
            {
                Kind = MarkdownInlineKind.Image,
                Text = match.Groups["imageAlt"].Value,
                Target = match.Groups["imageTarget"].Value
            };
        }

        if (match.Groups["link"].Success)
        {
            return new MarkdownInline
            {
                Kind = MarkdownInlineKind.Link,
                Text = match.Groups["linkText"].Value,
                Target = match.Groups["linkTarget"].Value
            };
        }

        if (match.Groups["bold"].Success)
        {
            return new MarkdownInline
            {
                Kind = MarkdownInlineKind.Bold,
                Text = match.Groups["boldText"].Value
            };
        }

        if (match.Groups["code"].Success)
        {
            return new MarkdownInline
            {
                Kind = MarkdownInlineKind.Code,
                Text = match.Groups["codeText"].Value
            };
        }

        if (match.Groups["italic"].Success)
        {
            return new MarkdownInline
            {
                Kind = MarkdownInlineKind.Italic,
                Text = match.Groups["italicText"].Value
            };
        }

        return new MarkdownInline
        {
            Kind = MarkdownInlineKind.Text,
            Text = match.Value
        };
    }

    /// <summary>
    /// Performs the <c>InlinePattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
[GeneratedRegex(
        @"(?<internal>\[\[(?<internalTarget>[^\]|\r\n]+)(?:\|(?<internalAlias>[^\]\r\n]+))?\]\])|" +
        @"(?<image>!\[(?<imageAlt>[^\]\r\n]*)\]\((?<imageTarget>[^)\r\n]+)\))|" +
        @"(?<link>\[(?<linkText>[^\]\r\n]+)\]\((?<linkTarget>[^)\r\n]+)\))|" +
        @"(?<bold>\*\*(?<boldText>.+?)\*\*)|" +
        @"(?<code>\x60(?<codeText>[^\x60\r\n]+)\x60)|" +
        @"(?<italic>\*(?<italicText>[^*\r\n]+)\*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex InlinePattern();
}
