using System.Text.RegularExpressions;

namespace Nodalis.Core.Templates;

public static partial class MarkdownTemplateRenderer
{
    /// <summary>
    /// Performs the <c>Render</c> operation.
    /// </summary>
    /// <param name="template">The <c>template</c> value.</param>
    /// <param name="variables">The <c>variables</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static string Render(
            string template,
            IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        global::System.Collections.Generic.Dictionary<string, string> lookup = new Dictionary<string, string>(
            variables,
            StringComparer.OrdinalIgnoreCase);

        string[] unknownVariables = VariablePattern()
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

    /// <summary>
    /// Performs the <c>CreateStandardVariables</c> operation.
    /// </summary>
    /// <param name="title">The <c>title</c> value.</param>
    /// <param name="documentId">The <c>documentId</c> value.</param>
    /// <param name="now">The <c>now</c> value.</param>
    /// <param name="additionalVariables">The <c>additionalVariables</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static Dictionary<string, string> CreateStandardVariables(
            string title,
            Guid documentId,
            DateTimeOffset now,
            IReadOnlyDictionary<string, string>? additionalVariables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        global::System.Collections.Generic.Dictionary<string, string> variables = new Dictionary<string, string>(
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
            foreach (global::System.Collections.Generic.KeyValuePair<string, string> pair in additionalVariables)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        return variables;
    }

    /// <summary>
    /// Performs the <c>VariablePattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"{{s*(?<name>[A-Za-z0-9_.-]+)s*}}",
            RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
