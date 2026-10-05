namespace Nodalis.Core.Projects;

/// <summary>
/// Defines the four project roles that must remain present after structural edits.
/// </summary>
public static class ProjectRequiredSectionPolicy
{
    /// <summary>
    /// Gets the required logical role labels shown to users.
    /// </summary>
    public static IReadOnlyList<string> RequiredRoles { get; } =
    [
        "Jalons",
        "Technique",
        "Glossaire",
        "Tests"
    ];

    /// <summary>
    /// Determines whether a template key fulfills one of the required project roles.
    /// </summary>
    /// <param name="templateKey">The section template key.</param>
    /// <returns><see langword="true"/> when the section is structurally required.</returns>
    public static bool IsRequiredTemplateKey(
            string? templateKey)
    {
        if (string.IsNullOrWhiteSpace(
                templateKey))
        {
            return false;
        }

        return string.Equals(
                   templateKey,
                   "milestones",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   templateKey,
                   "glossary",
                   StringComparison.OrdinalIgnoreCase) ||
               templateKey.StartsWith(
                   "technical",
                   StringComparison.OrdinalIgnoreCase) ||
               templateKey.StartsWith(
                   "tests",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a stable logical role for a required template key.
    /// </summary>
    /// <param name="templateKey">The section template key.</param>
    /// <returns>The role label, or <see langword="null"/> for an optional role.</returns>
    public static string? GetRequiredRole(
            string? templateKey)
    {
        if (string.IsNullOrWhiteSpace(
                templateKey))
        {
            return null;
        }

        if (string.Equals(
                templateKey,
                "milestones",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Jalons";
        }

        if (string.Equals(
                templateKey,
                "glossary",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Glossaire";
        }

        if (templateKey.StartsWith(
                "technical",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Technique";
        }

        if (templateKey.StartsWith(
                "tests",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Tests";
        }

        return null;
    }
}
