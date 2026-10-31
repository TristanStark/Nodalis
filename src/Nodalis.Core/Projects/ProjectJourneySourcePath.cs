namespace Nodalis.Core.Projects;

/// <summary>
/// Portable, workspace-relative reference policy. A source path is an
/// identifier, not permission to access a file; consumers must independently
/// check workspace containment and symbolic links before opening it.
/// </summary>
public static class ProjectJourneySourcePath
{
    /// <summary>Validates a normalized, portable source reference.</summary>
    /// <param name="path">Workspace-relative path using forward slashes.</param>
    /// <returns>Whether the value is safe for the portable JSON format.</returns>
    public static bool IsSafe(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.Length <= 1024 &&
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') &&
        path.Split('/').All(part => part is not ("" or "." or "..")) &&
        !path.Any(char.IsControl) &&
        string.Equals(path, path.Trim(), StringComparison.Ordinal);

    /// <summary>Requires an already workspace-relative portable path.</summary>
    /// <param name="path">Path to validate.</param>
    /// <returns>The exact stable reference after validation.</returns>
    public static string RequireSafe(string path)
    {
        if (!IsSafe(path))
        {
            throw new ArgumentException(
                "La source doit être un chemin relatif au workspace, sans traversal.",
                nameof(path));
        }

        return path;
    }
}
