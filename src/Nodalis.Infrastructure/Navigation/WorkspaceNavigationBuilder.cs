using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Navigation;

public sealed class WorkspaceNavigationBuilder
{
    public async Task<WorkspaceNavigationNode> BuildAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var root = Path.GetFullPath(workspaceRoot);
        var workspaceStore = new FileSystemWorkspaceStore(root);
        var workspace = await workspaceStore.LoadAsync(cancellationToken);

        var children = new List<WorkspaceNavigationNode>
        {
            BuildGlobalNode(root)
        };

        var applicationsPath = Path.Combine(
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

    private static WorkspaceNavigationNode BuildGlobalNode(string workspaceRoot)
    {
        var children = EnumerateMarkdownFiles(workspaceRoot)
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

    private static async Task<WorkspaceNavigationNode> BuildApplicationsRootAsync(
        string workspaceRoot,
        string applicationsPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(applicationsPath);

        var applications = new List<WorkspaceNavigationNode>();

        foreach (var directory in EnumerateDirectories(applicationsPath))
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

    private static async Task<WorkspaceNavigationNode> BuildApplicationAsync(
        string workspaceRoot,
        string directory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ApplicationManifestFileName);

        var manifest = await TryReadManifestAsync<ApplicationManifest>(
            manifestPath,
            cancellationToken);

        var children = new List<WorkspaceNavigationNode>();

        foreach (var file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (var childDirectory in EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(childDirectory);

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

    private static async Task<WorkspaceNavigationNode> BuildModulesRootAsync(
        string workspaceRoot,
        string modulesPath,
        CancellationToken cancellationToken)
    {
        var modules = new List<WorkspaceNavigationNode>();

        foreach (var directory in EnumerateDirectories(modulesPath))
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

    private static async Task<WorkspaceNavigationNode> BuildModuleAsync(
        string workspaceRoot,
        string directory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ModuleManifestFileName);

        var manifest = await TryReadManifestAsync<ModuleManifest>(
            manifestPath,
            cancellationToken);

        var children = new List<WorkspaceNavigationNode>();

        foreach (var file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (var childDirectory in EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(childDirectory);

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

    private static async Task<WorkspaceNavigationNode> BuildProjectsRootAsync(
        string workspaceRoot,
        string projectsPath,
        CancellationToken cancellationToken)
    {
        var projects = new List<WorkspaceNavigationNode>();

        foreach (var directory in EnumerateDirectories(projectsPath))
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

    private static async Task<WorkspaceNavigationNode> BuildProjectAsync(
        string workspaceRoot,
        string directory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ProjectManifestFileName);

        var manifest = await TryReadManifestAsync<ProjectManifest>(
            manifestPath,
            cancellationToken);

        var children = new List<WorkspaceNavigationNode>();

        foreach (var file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (var childDirectory in EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(childDirectory);

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

    private static async Task<WorkspaceNavigationNode> BuildSubProjectsRootAsync(
        string workspaceRoot,
        string path,
        CancellationToken cancellationToken)
    {
        var projects = new List<WorkspaceNavigationNode>();

        foreach (var directory in EnumerateDirectories(path))
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

    private static async Task<WorkspaceNavigationNode> BuildGenericFolderAsync(
        string workspaceRoot,
        string directory,
        WorkspaceNodeKind kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var children = new List<WorkspaceNavigationNode>();

        foreach (var file in EnumerateMarkdownFiles(directory))
        {
            children.Add(BuildDocumentNode(workspaceRoot, file));
        }

        foreach (var childDirectory in EnumerateDirectories(directory))
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

    private static IEnumerable<string> EnumerateDirectories(string path) =>
        Directory
            .EnumerateDirectories(path)
            .Where(directory =>
                !Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            .OrderBy(directory => Path.GetFileName(directory), StringComparer.CurrentCultureIgnoreCase);

    private static IEnumerable<string> EnumerateMarkdownFiles(string path) =>
        Directory
            .EnumerateFiles(path, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(file => Path.GetFileName(file), StringComparer.CurrentCultureIgnoreCase);

    private static List<WorkspaceNavigationNode> Sort(
        IEnumerable<WorkspaceNavigationNode> nodes) =>
        nodes
            .OrderBy(node => GetKindOrder(node.Kind))
            .ThenBy(node => node.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static int GetKindOrder(WorkspaceNodeKind kind) =>
        kind switch
        {
            WorkspaceNodeKind.Document => 20,
            WorkspaceNodeKind.Project => 30,
            _ => 10
        };

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

    private static Guid CreatePathId(
        string workspaceRoot,
        string path)
    {
        var relativePath = Path.GetRelativePath(
            workspaceRoot,
            Path.GetFullPath(path));

        return CreateDeterministicId(
            relativePath.Replace('\\', '/').ToUpperInvariant());
    }

    private static Guid CreateDeterministicId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
