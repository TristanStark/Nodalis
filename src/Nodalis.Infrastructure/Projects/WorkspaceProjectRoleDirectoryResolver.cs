using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Resolves a project-only built-in collection such as Réunions or Décisions
/// using the existing canonical section manifest, without creating folders.
/// Applications continue to use their original root collections.
/// </summary>
public static class WorkspaceProjectRoleDirectoryResolver
{
    /// <summary>
    /// Returns the configured directory for one project collection when it is
    /// unambiguously declared. Falls back to the legacy root for applications,
    /// old profiles, and projects without that explicit section.
    /// </summary>
    /// <param name="scopeDirectory">Existing application or project root.</param>
    /// <param name="roleName">Built-in collection label.</param>
    /// <param name="cancellationToken">Cancellation of the manifest read.</param>
    /// <returns>Full chosen collection directory without modifying any source.</returns>
    public static async Task<string> ResolveAsync(
        string scopeDirectory,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        string root = Path.GetFullPath(scopeDirectory);
        string legacy = Path.Combine(root, roleName);
        string manifestPath = Path.Combine(root, WorkspaceLayout.ProjectManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return legacy;
        }

        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);

        SectionManifest[] matches = project.Sections
            .Where(section => string.Equals(section.Name, roleName,
                    StringComparison.OrdinalIgnoreCase) ||
                (section.RelativeDirectory is string relative &&
                 string.Equals(relative.Split('/')[^1], roleName,
                     StringComparison.OrdinalIgnoreCase)))
            .Where(section => section.TemplateKey is null &&
                !section.IsSingleton)
            .ToArray();

        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"Plusieurs sections candidates pour « {roleName} » : réglez l'ambiguïté avant d'écrire.");
        }

        return matches.Length == 1
            ? ProjectSectionDirectoryLayout.Resolve(root, matches[0])
            : legacy;
    }
}
