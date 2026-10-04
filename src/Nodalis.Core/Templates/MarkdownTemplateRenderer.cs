using System.Text.RegularExpressions;

namespace Nodalis.Core.Templates;

public static partial class MarkdownTemplateRenderer
{
    public static string Render(
        string template,
        IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        var lookup = new Dictionary<string, string>(
            variables,
            StringComparer.OrdinalIgnoreCase);

        var unknownVariables = VariablePattern()
            .Matches(template)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !lookup.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unknownVariables.Length > 0)
        {
            throw new TemplateRenderException(
                "Unknown template variable(s): " +
                string.Join(", ", unknownVariables));
        }

        return VariablePattern().Replace(
            template,
            match => lookup[match.Groups["name"].Value]);
    }

    public static Dictionary<string, string> CreateStandardVariables(
        string title,
        Guid documentId,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string>? additionalVariables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var variables = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = title.Trim(),
            ["id"] = documentId.ToString("D"),
            ["date"] = now.ToString("yyyy-MM-dd"),
            ["datetime"] = now.ToString("yyyy-MM-dd HH:mm"),
            ["year"] = now.ToString("yyyy")
        };

        if (additionalVariables is not null)
        {
            foreach (var pair in additionalVariables)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        return variables;
    }

    [GeneratedRegex(
        @"{{s*(?<name>[A-Za-z0-9_.-]+)s*}}",
        RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
