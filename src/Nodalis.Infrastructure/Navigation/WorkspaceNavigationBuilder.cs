using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Navigation;

public sealed class WorkspaceNavigationBuilder
{
    /// <summary>
    /// Performs the <c>BuildAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<WorkspaceNavigationNode> BuildAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string root = Path.GetFullPath(workspaceRoot);
        global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore workspaceStore = new FileSystemWorkspaceStore(root);
        global::Nodalis.Core.Domain.WorkspaceManifest workspace = await workspaceStore.LoadAsync(cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> children = new List<WorkspaceNavigationNode>
        {
            BuildGlobalNode(root)
        };

        string applicationsPath = Path.Combine(
            root,
            WorkspaceLayout.ApplicationsDirectoryName);

        children.Add(await BuildApplicationsRootAsync(
            root,
            applicationsPath,
            cancellationToken));

        return new WorkspaceNavigationNode
        {
            Id = workspace.Id,
            DisplayName = workspace.Name,
            Kind = WorkspaceNodeKind.Workspace,
            FullPath = root,
            Children = children
        };
    }

    /// <summary>
    /// Performs the <c>BuildGlobalNode</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static WorkspaceNavigationNode BuildGlobalNode(string workspaceRoot)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> children = EnumerateMarkdownFiles(workspaceRoot)
            .Select(file =>
                BuildDocumentNode(
                    workspaceRoot,
                    file))
            .ToList();

        return new WorkspaceNavigationNode
        {
            Id = CreateDeterministicId("global"),
            DisplayName = "Global",
            Kind = WorkspaceNodeKind.Global,
            FullPath = workspaceRoot,
            Children = Sort(children)
        };
    }

    /// <summary>
    /// Performs the <c>BuildApplicationsRootAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="applicationsPath">The <c>applicationsPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildApplicationsRootAsync(
            string workspaceRoot,
            string applicationsPath,
            CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(applicationsPath);

        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> applications = new List<WorkspaceNavigationNode>();

        foreach (string directory in EnumerateDirectories(applicationsPath))
        {
            applications.Add(await BuildApplicationAsync(
                workspaceRoot,
                directory,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = CreateDeterministicId("applications"),
            DisplayName = "Applications",
            Kind = WorkspaceNodeKind.ApplicationsRoot,
            FullPath = applicationsPath,
            Children = applications
        };
    }

    /// <summary>
    /// Performs the <c>BuildApplicationAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="directory">The <c>directory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildApplicationAsync(
            string workspaceRoot,
            string directory,
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ApplicationManifestFileName);

        global::Nodalis.Core.Domain.ApplicationManifest? manifest = await TryReadManifestAsync<ApplicationManifest>(
            manifestPath,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> children = new List<WorkspaceNavigationNode>();

        foreach (string file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (string childDirectory in EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(childDirectory);

            if (name.Equals(
                    WorkspaceLayout.ModulesDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                children.Add(await BuildModulesRootAsync(
                    workspaceRoot,
                    childDirectory,
                    cancellationToken));
                continue;
            }

            if (name.Equals(
                    WorkspaceLayout.ProjectsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                children.Add(await BuildProjectsRootAsync(
                    workspaceRoot,
                    childDirectory,
                    cancellationToken));
                continue;
            }

            children.Add(await BuildGenericFolderAsync(
                workspaceRoot,
                childDirectory,
                WorkspaceNodeKind.Section,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = manifest?.Id ?? CreatePathId(workspaceRoot, directory),
            DisplayName = manifest?.Name ?? Path.GetFileName(directory),
            Kind = WorkspaceNodeKind.Application,
            FullPath = directory,
            Children = Sort(children)
        };
    }

    /// <summary>
    /// Performs the <c>BuildModulesRootAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="modulesPath">The <c>modulesPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildModulesRootAsync(
            string workspaceRoot,
            string modulesPath,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> modules = new List<WorkspaceNavigationNode>();

        foreach (string directory in EnumerateDirectories(modulesPath))
        {
            modules.Add(await BuildModuleAsync(
                workspaceRoot,
                directory,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = CreatePathId(workspaceRoot, modulesPath),
            DisplayName = "Modules",
            Kind = WorkspaceNodeKind.Folder,
            FullPath = modulesPath,
            Children = modules
        };
    }

    /// <summary>
    /// Performs the <c>BuildModuleAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="directory">The <c>directory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildModuleAsync(
            string workspaceRoot,
            string directory,
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ModuleManifestFileName);

        global::Nodalis.Core.Domain.ModuleManifest? manifest = await TryReadManifestAsync<ModuleManifest>(
            manifestPath,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> children = new List<WorkspaceNavigationNode>();

        foreach (string file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (string childDirectory in EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(childDirectory);

            if (name.Equals(
                    WorkspaceLayout.ModulesDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                children.Add(await BuildModulesRootAsync(
                    workspaceRoot,
                    childDirectory,
                    cancellationToken));
                continue;
            }

            if (name.Equals(
                    WorkspaceLayout.ProjectsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                children.Add(await BuildProjectsRootAsync(
                    workspaceRoot,
                    childDirectory,
                    cancellationToken));
                continue;
            }

            children.Add(await BuildGenericFolderAsync(
                workspaceRoot,
                childDirectory,
                WorkspaceNodeKind.Section,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = manifest?.Id ?? CreatePathId(workspaceRoot, directory),
            DisplayName = manifest?.Name ?? Path.GetFileName(directory),
            Kind = WorkspaceNodeKind.Module,
            FullPath = directory,
            Children = Sort(children)
        };
    }

    /// <summary>
    /// Performs the <c>BuildProjectsRootAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="projectsPath">The <c>projectsPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildProjectsRootAsync(
            string workspaceRoot,
            string projectsPath,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> projects = new List<WorkspaceNavigationNode>();

        foreach (string directory in EnumerateDirectories(projectsPath))
        {
            projects.Add(await BuildProjectAsync(
                workspaceRoot,
                directory,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = CreatePathId(workspaceRoot, projectsPath),
            DisplayName = "Projets",
            Kind = WorkspaceNodeKind.ProjectsRoot,
            FullPath = projectsPath,
            Children = projects
        };
    }

    /// <summary>
    /// Performs the <c>BuildProjectAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="directory">The <c>directory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildProjectAsync(
            string workspaceRoot,
            string directory,
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ProjectManifestFileName);

        global::Nodalis.Core.Domain.ProjectManifest? manifest = await TryReadManifestAsync<ProjectManifest>(
            manifestPath,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> children = new List<WorkspaceNavigationNode>();

        foreach (string file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (string childDirectory in EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(childDirectory);

            if (name.Equals(
                    WorkspaceLayout.SubProjectsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                children.Add(await BuildSubProjectsRootAsync(
                    workspaceRoot,
                    childDirectory,
                    cancellationToken));
                continue;
            }

            children.Add(await BuildGenericFolderAsync(
                workspaceRoot,
                childDirectory,
                WorkspaceNodeKind.Section,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = manifest?.Id ?? CreatePathId(workspaceRoot, directory),
            DisplayName = manifest?.Name ?? Path.GetFileName(directory),
            Kind = WorkspaceNodeKind.Project,
            FullPath = directory,
            Children = Sort(children)
        };
    }

    /// <summary>
    /// Performs the <c>BuildSubProjectsRootAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildSubProjectsRootAsync(
            string workspaceRoot,
            string path,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> projects = new List<WorkspaceNavigationNode>();

        foreach (string directory in EnumerateDirectories(path))
        {
            projects.Add(await BuildProjectAsync(
                workspaceRoot,
                directory,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = CreatePathId(workspaceRoot, path),
            DisplayName = "Sous-projets",
            Kind = WorkspaceNodeKind.ProjectsRoot,
            FullPath = path,
            Children = projects
        };
    }

    /// <summary>
    /// Performs the <c>BuildGenericFolderAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="directory">The <c>directory</c> value.</param>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<WorkspaceNavigationNode> BuildGenericFolderAsync(
            string workspaceRoot,
            string directory,
            WorkspaceNodeKind kind,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        global::System.Collections.Generic.List<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> children = new List<WorkspaceNavigationNode>();

        foreach (string file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (string childDirectory in EnumerateDirectories(directory))
        {
            children.Add(await BuildGenericFolderAsync(
                workspaceRoot,
                childDirectory,
                WorkspaceNodeKind.Section,
                cancellationToken));
        }

        return new WorkspaceNavigationNode
        {
            Id = CreatePathId(workspaceRoot, directory),
            DisplayName = Path.GetFileName(directory),
            Kind = kind,
            FullPath = directory,
            Children = Sort(children)
        };
    }

    /// <summary>
    /// Performs the <c>BuildDocumentNode</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="file">The <c>file</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static WorkspaceNavigationNode BuildDocumentNode(
            string workspaceRoot,
            string file)
    {
        return new WorkspaceNavigationNode
        {
            Id = CreatePathId(workspaceRoot, file),
            DisplayName = Path.GetFileNameWithoutExtension(file),
            Kind = WorkspaceNodeKind.Document,
            FullPath = file
        };
    }

    /// <summary>
    /// Performs the <c>AddDocumentIfExists</c> operation.
    /// </summary>
    /// <param name="children">The <c>children</c> value.</param>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="file">The <c>file</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static void AddDocumentIfExists(
            ICollection<WorkspaceNavigationNode> children,
            string workspaceRoot,
            string file)
    {
        if (File.Exists(file))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }
    }

    /// <summary>
    /// Performs the <c>EnumerateDirectories</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IEnumerable<string> EnumerateDirectories(string path) =>
            Directory
                .EnumerateDirectories(path)
                .Where(directory =>
                    !Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
                .OrderBy(directory => Path.GetFileName(directory), StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// Performs the <c>EnumerateMarkdownFiles</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IEnumerable<string> EnumerateMarkdownFiles(string path) =>
            Directory
                .EnumerateFiles(path, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(file => Path.GetFileName(file), StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// Performs the <c>Sort</c> operation.
    /// </summary>
    /// <param name="nodes">The <c>nodes</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<WorkspaceNavigationNode> Sort(
            IEnumerable<WorkspaceNavigationNode> nodes) =>
            nodes
                .OrderBy(node => GetKindOrder(node.Kind))
                .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    /// <summary>
    /// Performs the <c>GetKindOrder</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static int GetKindOrder(WorkspaceNodeKind kind) =>
            kind switch
            {
                WorkspaceNodeKind.Document => 20,
                WorkspaceNodeKind.Project => 30,
                _ => 10
            };

    /// <summary>
    /// Performs the <c>TryReadManifestAsync</c> operation.
    /// </summary>
    /// <typeparam name="T">The <c>T</c> type.</typeparam>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<T?> TryReadManifestAsync<T>(
            string path,
            CancellationToken cancellationToken)
            where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await AtomicJsonFile.ReadAsync<T>(
                path,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Performs the <c>CreatePathId</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Guid CreatePathId(
            string workspaceRoot,
            string path)
    {
        string relativePath = Path.GetRelativePath(
            workspaceRoot,
            Path.GetFullPath(path));

        return CreateDeterministicId(
            relativePath.Replace('\\', '/').ToUpperInvariant());
    }

    /// <summary>
    /// Performs the <c>CreateDeterministicId</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Guid CreateDeterministicId(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
