using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Resolves project section directories with a backward-compatible optional
/// relative path. Legacy sections continue to use sanitized section names.
/// </summary>
public static class ProjectSectionDirectoryLayout
{
    /// <summary>Resolves an existing section without altering the filesystem.</summary>
    /// <param name="projectDirectory">Canonical project root.</param>
    /// <param name="section">Persisted section manifest.</param>
    /// <returns>Validated absolute section directory.</returns>
    public static string Resolve(string projectDirectory, SectionManifest section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return Resolve(projectDirectory, section.Name, section.RelativeDirectory);
    }

    /// <summary>
    /// Resolves an optional explicit nested folder, rejecting Windows-invalid
    /// components and traversal instead of silently sanitizing configured paths.
    /// </summary>
    /// <param name="projectDirectory">Project root.</param>
    /// <param name="name">Legacy fallback section name.</param>
    /// <param name="relativeDirectory">Optional explicit slash-separated folder.</param>
    /// <returns>Absolute directory staying inside the project.</returns>
    public static string Resolve(
        string projectDirectory, string name, string? relativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalized = relativeDirectory is null
            ? WindowsPathRules.SanitizeSegment(name)
            : ValidateRelativeDirectory(relativeDirectory);

        return Path.Combine(Path.GetFullPath(projectDirectory),
            normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Validates a portable, non-rooted, slash-separated section path.</summary>
    /// <param name="relativeDirectory">Configured explicit section directory.</param>
    /// <returns>The unchanged portable path when it is safe.</returns>
    public static string ValidateRelativeDirectory(string relativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeDirectory);

        if (Path.IsPathRooted(relativeDirectory) ||
            relativeDirectory.Contains('\\') ||
            relativeDirectory.Contains(':') ||
            relativeDirectory.Contains('\0'))
        {
            throw new InvalidDataException("Chemin de section non relatif ou non portable.");
        }

        string[] segments = relativeDirectory.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.Any(character => character is '<' or '>' or '"' or '|'
                    or '?' or '*' or '/' || char.IsControl(character)) ||
                !string.Equals(WindowsPathRules.SanitizeSegment(segment), segment,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Segment de section interdit : {segment}.");
            }
        }

        return relativeDirectory;
    }
}
