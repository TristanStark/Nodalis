using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

public sealed class ProjectCreationTargetDiscovery
{
    /// <summary>
    /// Performs the <c>DiscoverAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<ProjectCreationTarget>> DiscoverAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string root = Path.GetFullPath(workspaceRoot);
        string applicationsRoot = Path.Combine(
            root,
            WorkspaceLayout.ApplicationsDirectoryName);

        if (!Directory.Exists(applicationsRoot))
        {
            return [];
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Projects.ProjectCreationTarget> targets = new List<ProjectCreationTarget>();

        foreach (string applicationDirectory in Directory
                     .EnumerateDirectories(applicationsRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string manifestPath = Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            global::Nodalis.Core.Domain.ApplicationManifest application = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                manifestPath,
                cancellationToken);

            targets.Add(new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationDirectory,
                DisplayName = application.Name
            });

            string modulesRoot = Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ModulesDirectoryName);

            if (Directory.Exists(modulesRoot))
            {
                await DiscoverModulesAsync(
                    modulesRoot,
                    application,
                    targets,
                    cancellationToken);
            }

            string projectsRoot = Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName);

            if (Directory.Exists(projectsRoot))
            {
                await DiscoverProjectsAsync(
                    projectsRoot,
                    application,
                    module: null,
                    targets,
                    cancellationToken);
            }
        }

        return targets
            .OrderBy(target => target.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(target => target.ModuleName is null ? 0 : 1)
            .ThenBy(target => target.ParentProjectName is null ? 0 : 1)
            .ThenBy(target => target.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>DiscoverModulesAsync</c> operation.
    /// </summary>
    /// <param name="modulesRoot">The <c>modulesRoot</c> value.</param>
    /// <param name="application">The <c>application</c> value.</param>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static async Task DiscoverModulesAsync(
        string modulesRoot,
        ApplicationManifest application,
        ICollection<ProjectCreationTarget> targets,
        CancellationToken cancellationToken)
    {
        foreach (string moduleDirectory in Directory
                     .EnumerateDirectories(modulesRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string manifestPath = Path.Combine(
                moduleDirectory,
                WorkspaceLayout.ModuleManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            global::Nodalis.Core.Domain.ModuleManifest module = await AtomicJsonFile.ReadAsync<ModuleManifest>(
                manifestPath,
                cancellationToken);

            targets.Add(new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ModuleId = module.Id,
                ModuleName = module.Name,
                ParentDirectory = moduleDirectory,
                DisplayName = $"{application.Name} / {module.Name}"
            });

            string nestedModules = Path.Combine(
                moduleDirectory,
                WorkspaceLayout.ModulesDirectoryName);

            if (Directory.Exists(nestedModules))
            {
                await DiscoverModulesAsync(
                    nestedModules,
                    application,
                    targets,
                    cancellationToken);
            }

            string projectsRoot = Path.Combine(
                moduleDirectory,
                WorkspaceLayout.ProjectsDirectoryName);

            if (Directory.Exists(projectsRoot))
            {
                await DiscoverProjectsAsync(
                    projectsRoot,
                    application,
                    module,
                    targets,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Performs the <c>DiscoverProjectsAsync</c> operation.
    /// </summary>
    /// <param name="projectsRoot">The <c>projectsRoot</c> value.</param>
    /// <param name="application">The <c>application</c> value.</param>
    /// <param name="module">The <c>module</c> value.</param>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static async Task DiscoverProjectsAsync(
        string projectsRoot,
        ApplicationManifest application,
        ModuleManifest? module,
        ICollection<ProjectCreationTarget> targets,
        CancellationToken cancellationToken)
    {
        foreach (string projectDirectory in Directory
                     .EnumerateDirectories(projectsRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string manifestPath = Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            global::Nodalis.Core.Domain.ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                manifestPath,
                cancellationToken);

            targets.Add(new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ModuleId = project.ModuleId,
                ModuleName = module?.Name,
                ParentProjectId = project.Id,
                ParentProjectName = project.Name,
                ParentDirectory = projectDirectory,
                DisplayName = module is null
                    ? $"{application.Name} / {project.Name} (sous-projet)"
                    : $"{application.Name} / {module.Name} / {project.Name} (sous-projet)"
            });

            string subProjectsRoot = Path.Combine(
                projectDirectory,
                WorkspaceLayout.SubProjectsDirectoryName);

            if (Directory.Exists(subProjectsRoot))
            {
                await DiscoverProjectsAsync(
                    subProjectsRoot,
                    application,
                    module,
                    targets,
                    cancellationToken);
            }
        }
    }
}
