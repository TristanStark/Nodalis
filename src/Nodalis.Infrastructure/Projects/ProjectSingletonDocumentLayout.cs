using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Describes the active storage location for a project role represented by one root Markdown document.
/// </summary>
public sealed record ProjectSingletonDocumentResolution
{
    /// <summary>Gets the canonical root-file path for the role.</summary>
    public required string PreferredPath { get; init; }

    /// <summary>Gets the path that callers must currently use.</summary>
    public required string ActivePath { get; init; }

    /// <summary>Gets the legacy section directory when one is relevant.</summary>
    public required string LegacyDirectoryPath { get; init; }

    /// <summary>Gets whether a legacy document was moved to the canonical root location.</summary>
    public bool Migrated { get; init; }

    /// <summary>Gets whether extra legacy content prevents automatic migration.</summary>
    public bool RequiresManualMigration { get; init; }

    /// <summary>Gets legacy entries that must be handled before the directory can be removed safely.</summary>
    public IReadOnlyList<string> BlockingEntries { get; init; } = [];
}

/// <summary>
/// Centralizes the 1.0 storage contract for the Jalons and Glossaire project roles.
/// </summary>
public static class ProjectSingletonDocumentLayout
{
    /// <summary>
    /// Gets the canonical project-root Markdown file name for a singleton root-document role.
    /// </summary>
    /// <param name="templateKey">The stable project-section template key.</param>
    /// <returns>The canonical file name, or <see langword="null"/> for directory-backed sections.</returns>
    public static string? GetRootDocumentFileName(
            string? templateKey)
    {
        if (string.Equals(
                templateKey,
                "milestones",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Jalons.md";
        }

        if (string.Equals(
                templateKey,
                "glossary",
                StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceLayout.GlobalGlossaryFileName;
        }

        return null;
    }

    /// <summary>
    /// Resolves one singleton project document and optionally performs the lossless legacy-folder migration.
    /// </summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="section">The logical section owning the document.</param>
    /// <param name="migrateIfSafe">Whether an unambiguous legacy layout may be migrated immediately.</param>
    /// <returns>The active and preferred storage paths.</returns>
    public static ProjectSingletonDocumentResolution Resolve(
            string projectDirectory,
            SectionManifest section,
            bool migrateIfSafe = true)
    {
        ArgumentNullException.ThrowIfNull(
            section);

        return Resolve(
            projectDirectory,
            section.Name,
            section.TemplateKey,
            migrateIfSafe);
    }

    /// <summary>
    /// Resolves one singleton project document from its logical role data.
    /// </summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="sectionName">The logical section display name.</param>
    /// <param name="templateKey">The stable role template key.</param>
    /// <param name="migrateIfSafe">Whether an unambiguous legacy layout may be migrated immediately.</param>
    /// <returns>The active and preferred storage paths.</returns>
    public static ProjectSingletonDocumentResolution Resolve(
            string projectDirectory,
            string sectionName,
            string? templateKey,
            bool migrateIfSafe = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sectionName);

        string rootFileName =
            GetRootDocumentFileName(
                templateKey)
            ?? throw new InvalidOperationException(
                "The requested project section is not a root-document role.");

        string fullProjectDirectory =
            Path.GetFullPath(
                projectDirectory);
        string preferredPath =
            Path.Combine(
                fullProjectDirectory,
                rootFileName);
        string safeSectionName =
            WindowsPathRules.SanitizeSegment(
                sectionName);
        string legacyDirectory =
            Path.Combine(
                fullProjectDirectory,
                safeSectionName);
        string legacyPath =
            Path.Combine(
                legacyDirectory,
                safeSectionName + ".md");

        if (File.Exists(
                preferredPath))
        {
            return new ProjectSingletonDocumentResolution
            {
                PreferredPath =
                    preferredPath,
                ActivePath =
                    preferredPath,
                LegacyDirectoryPath =
                    legacyDirectory
            };
        }

        if (!File.Exists(
                legacyPath))
        {
            return new ProjectSingletonDocumentResolution
            {
                PreferredPath =
                    preferredPath,
                ActivePath =
                    preferredPath,
                LegacyDirectoryPath =
                    legacyDirectory
            };
        }

        string[] blockingEntries =
            Directory
                .EnumerateFileSystemEntries(
                    legacyDirectory)
                .Where(path =>
                    !string.Equals(
                        Path.GetFullPath(
                            path),
                        Path.GetFullPath(
                            legacyPath),
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    path => path,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        if (blockingEntries.Length >
                0 ||
            !migrateIfSafe)
        {
            return new ProjectSingletonDocumentResolution
            {
                PreferredPath =
                    preferredPath,
                ActivePath =
                    legacyPath,
                LegacyDirectoryPath =
                    legacyDirectory,
                RequiresManualMigration =
                    blockingEntries.Length >
                    0,
                BlockingEntries =
                    blockingEntries
            };
        }

        File.Move(
            legacyPath,
            preferredPath);

        if (!Directory.EnumerateFileSystemEntries(
                legacyDirectory)
            .Any())
        {
            Directory.Delete(
                legacyDirectory,
                recursive: false);
        }

        return new ProjectSingletonDocumentResolution
        {
            PreferredPath =
                preferredPath,
            ActivePath =
                preferredPath,
            LegacyDirectoryPath =
                legacyDirectory,
            Migrated =
                true
        };
    }
}
