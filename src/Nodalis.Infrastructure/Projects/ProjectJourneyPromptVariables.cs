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
}
