using System.Text.RegularExpressions;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Identifies unresolved placeholder tokens in user prompts without
/// replacing them, assigning hidden sources or mutating original templates.
/// </summary>
public static class ProjectJourneyPromptVariables
{
    private static readonly Regex Placeholders = new(
        @"<([A-Z][A-Z0-9_]{1,39})>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    /// <summary>Returns unique placeholders in their original order.</summary>
    /// <param name="text">Human-editable draft or original user prompt.</param>
    /// <returns>Tokens that require explicit human replacement or removal.</returns>
    public static IReadOnlyList<string> FindPlaceholders(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Placeholders.Matches(text).Select(match => match.Value)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Replaces ONLY tokens present in the original prompt in a single pass.
    /// Substituted source text is never inspected for further variables.
    /// </summary>
    /// <param name="template">Unmodified original user prompt.</param>
    /// <param name="bindings">Validated resolved token texts.</param>
    /// <returns>Explicitly expanded prompt preserving unknown tokens.</returns>
    public static string ReplaceBoundVariables(
        string template, IReadOnlyDictionary<string, string> bindings)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(bindings);
        return Placeholders.Replace(template, match =>
            bindings.TryGetValue(match.Value, out string? value)
                ? value : match.Value);
    }
}
