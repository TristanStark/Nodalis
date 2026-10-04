using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

public sealed class ProjectCreationTargetDiscovery
{
    public async Task<IReadOnlyList<ProjectCreationTarget>> DiscoverAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var root = Path.GetFullPath(workspaceRoot);
        var applicationsRoot = Path.Combine(
            root,
            WorkspaceLayout.ApplicationsDirectoryName);

        if (!Directory.Exists(applicationsRoot))
        {
            return [];
        }

        var targets = new List<ProjectCreationTarget>();

        foreach (var applicationDirectory in Directory
                     .EnumerateDirectories(applicationsRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var application = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                manifestPath,
                cancellationToken);

            targets.Add(new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationDirectory,
                DisplayName = application.Name
            });

            var modulesRoot = Path.Combine(
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

            var projectsRoot = Path.Combine(
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

    private static async Task DiscoverModulesAsync(
        string modulesRoot,
        ApplicationManifest application,
        ICollection<ProjectCreationTarget> targets,
        CancellationToken cancellationToken)
    {
        foreach (var moduleDirectory in Directory
                     .EnumerateDirectories(modulesRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(
                moduleDirectory,
                WorkspaceLayout.ModuleManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var module = await AtomicJsonFile.ReadAsync<ModuleManifest>(
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

            var nestedModules = Path.Combine(
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

            var projectsRoot = Path.Combine(
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

    private static async Task DiscoverProjectsAsync(
        string projectsRoot,
        ApplicationManifest application,
        ModuleManifest? module,
        ICollection<ProjectCreationTarget> targets,
        CancellationToken cancellationToken)
    {
        foreach (var projectDirectory in Directory
                     .EnumerateDirectories(projectsRoot)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
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

            var subProjectsRoot = Path.Combine(
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
