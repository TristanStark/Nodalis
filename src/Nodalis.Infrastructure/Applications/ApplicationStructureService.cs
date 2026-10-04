using Nodalis.Core.Domain;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Applications;

public sealed class ApplicationStructureService
{
    private readonly string _workspaceRoot;

    public ApplicationStructureService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public Task<ModuleManifest> LoadModuleAsync(
        string moduleDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleDirectory);

        return AtomicJsonFile.ReadAsync<ModuleManifest>(
            Path.Combine(
                Path.GetFullPath(moduleDirectory),
                WorkspaceLayout.ModuleManifestFileName),
            cancellationToken);
    }

    public async Task<string> CreateApplicationAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        var applicationsRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ApplicationsDirectoryName);

        Directory.CreateDirectory(applicationsRoot);

        var finalDirectory = WindowsPathRules.GetUniqueDirectoryPath(
            applicationsRoot,
            normalizedName);

        var stagingDirectory = Path.Combine(
            applicationsRoot,
            $".nodalis-application-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            var manifest = new ApplicationManifest
            {
                Id = Guid.NewGuid(),
                Name = normalizedName
            };

            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    stagingDirectory,
                    WorkspaceLayout.ApplicationManifestFileName),
                manifest,
                cancellationToken);

            await InitializeContainerAsync(
                stagingDirectory,
                includeDocumentation: true,
                includeScopeFiles: true,
                cancellationToken);

            Directory.Move(stagingDirectory, finalDirectory);
            return finalDirectory;
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            throw;
        }
    }

    public async Task<string> CreateModuleAsync(
        string parentDirectory,
        Guid applicationId,
        Guid? parentModuleId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        var modulesRoot = Path.Combine(
            Path.GetFullPath(parentDirectory),
            WorkspaceLayout.ModulesDirectoryName);

        Directory.CreateDirectory(modulesRoot);

        var finalDirectory = WindowsPathRules.GetUniqueDirectoryPath(
            modulesRoot,
            normalizedName);

        var stagingDirectory = Path.Combine(
            modulesRoot,
            $".nodalis-module-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            var manifest = new ModuleManifest
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                ParentModuleId = parentModuleId,
                Name = normalizedName
            };

            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    stagingDirectory,
                    WorkspaceLayout.ModuleManifestFileName),
                manifest,
                cancellationToken);

            await InitializeContainerAsync(
                stagingDirectory,
                includeDocumentation: false,
                includeScopeFiles: false,
                cancellationToken);

            Directory.Move(stagingDirectory, finalDirectory);
            return finalDirectory;
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            throw;
        }
    }

    public async Task<string> RenameApplicationAsync(
        string applicationDirectory,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(applicationDirectory);
        var manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ApplicationManifestFileName);

        var manifest = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
            manifestPath,
            cancellationToken);

        var destination = GetRenameDestination(
            directory,
            NormalizeName(newName));

        if (!string.Equals(
                directory,
                destination,
                StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(directory, destination);
        }

        try
        {
            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    destination,
                    WorkspaceLayout.ApplicationManifestFileName),
                manifest with { Name = NormalizeName(newName) },
                cancellationToken);
        }
        catch
        {
            if (!string.Equals(
                    directory,
                    destination,
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(destination) &&
                !Directory.Exists(directory))
            {
                Directory.Move(destination, directory);
            }

            throw;
        }

        return destination;
    }

    public async Task<string> RenameModuleAsync(
        string moduleDirectory,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(moduleDirectory);
        var manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ModuleManifestFileName);

        var manifest = await AtomicJsonFile.ReadAsync<ModuleManifest>(
            manifestPath,
            cancellationToken);

        var destination = GetRenameDestination(
            directory,
            NormalizeName(newName));

        if (!string.Equals(
                directory,
                destination,
                StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(directory, destination);
        }

        try
        {
            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    destination,
                    WorkspaceLayout.ModuleManifestFileName),
                manifest with { Name = NormalizeName(newName) },
                cancellationToken);
        }
        catch
        {
            if (!string.Equals(
                    directory,
                    destination,
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(destination) &&
                !Directory.Exists(directory))
            {
                Directory.Move(destination, directory);
            }

            throw;
        }

        return destination;
    }

    public async Task<string> MoveModuleAsync(
        string moduleDirectory,
        string newParentDirectory,
        Guid? newParentModuleId,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(moduleDirectory);
        var parent = Path.GetFullPath(newParentDirectory);

        if (IsSameOrDescendant(parent, source))
        {
            throw new DomainValidationException(
                "A module cannot be moved inside itself or one of its descendants.");
        }

        var manifest = await AtomicJsonFile.ReadAsync<ModuleManifest>(
            Path.Combine(
                source,
                WorkspaceLayout.ModuleManifestFileName),
            cancellationToken);

        if (newParentModuleId is Guid parentModuleId)
        {
            var parentManifest = await AtomicJsonFile.ReadAsync<ModuleManifest>(
                Path.Combine(
                    parent,
                    WorkspaceLayout.ModuleManifestFileName),
                cancellationToken);

            if (parentManifest.Id != parentModuleId ||
                parentManifest.ApplicationId != manifest.ApplicationId)
            {
                throw new DomainValidationException(
                    "A module can only move inside the same application.");
            }
        }
        else
        {
            var applicationManifest = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                Path.Combine(
                    parent,
                    WorkspaceLayout.ApplicationManifestFileName),
                cancellationToken);

            if (applicationManifest.Id != manifest.ApplicationId)
            {
                throw new DomainValidationException(
                    "A module can only move inside the same application.");
            }
        }

        var destinationRoot = Path.Combine(
            parent,
            WorkspaceLayout.ModulesDirectoryName);

        Directory.CreateDirectory(destinationRoot);

        var destination = WindowsPathRules.GetUniqueDirectoryPath(
            destinationRoot,
            Path.GetFileName(source));

        Directory.Move(source, destination);

        try
        {
            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    destination,
                    WorkspaceLayout.ModuleManifestFileName),
                manifest with { ParentModuleId = newParentModuleId },
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(destination) &&
                !Directory.Exists(source))
            {
                Directory.Move(destination, source);
            }

            throw;
        }

        return destination;
    }

    public Task DeleteApplicationAsync(
        string applicationDirectory,
        CancellationToken cancellationToken = default) =>
        DeleteContainerAsync(
            applicationDirectory,
            WorkspaceLayout.ApplicationManifestFileName,
            cancellationToken);

    public Task DeleteModuleAsync(
        string moduleDirectory,
        CancellationToken cancellationToken = default) =>
        DeleteContainerAsync(
            moduleDirectory,
            WorkspaceLayout.ModuleManifestFileName,
            cancellationToken);

    private static async Task InitializeContainerAsync(
        string directory,
        bool includeDocumentation,
        bool includeScopeFiles,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(
            directory,
            WorkspaceLayout.ModulesDirectoryName));
        Directory.CreateDirectory(Path.Combine(
            directory,
            WorkspaceLayout.ProjectsDirectoryName));

        if (includeDocumentation)
        {
            Directory.CreateDirectory(Path.Combine(
                directory,
                "Documentation",
                "Technique"));
            Directory.CreateDirectory(Path.Combine(
                directory,
                "Documentation",
                "Fonctionnelle"));
        }

        if (!includeScopeFiles)
        {
            return;
        }

        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(
                directory,
                WorkspaceLayout.GlobalQuickNotesFileName),
            "# Notes rapides\n\n",
            cancellationToken);

        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(
                directory,
                WorkspaceLayout.GlobalGlossaryFileName),
            "# Glossaire\n\n",
            cancellationToken);
    }

    private static Task DeleteContainerAsync(
        string directory,
        string manifestFileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(directory);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        var meaningfulFiles = Directory
            .EnumerateFiles(
                fullPath,
                "*",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                var name = Path.GetFileName(file);

                if (name.Equals(
                        manifestFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (name.Equals(
                        WorkspaceLayout.GlobalQuickNotesFileName,
                        StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(
                        WorkspaceLayout.GlobalGlossaryFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var text = File.ReadAllText(file);
                    return text
                        .Split(
                            ['\r', '\n'],
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Skip(1)
                        .Any();
                }

                return true;
            })
            .Take(1)
            .ToArray();

        var meaningfulDirectories = Directory
            .EnumerateDirectories(
                fullPath,
                "*",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !name.Equals(
                           WorkspaceLayout.ModulesDirectoryName,
                           StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(
                           WorkspaceLayout.ProjectsDirectoryName,
                           StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(
                           "Documentation",
                           StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(
                           "Technique",
                           StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(
                           "Fonctionnelle",
                           StringComparison.OrdinalIgnoreCase);
            })
            .Take(1)
            .ToArray();

        if (meaningfulFiles.Length > 0 ||
            meaningfulDirectories.Length > 0)
        {
            throw new DomainValidationException(
                "This application or module is not empty and cannot be deleted.");
        }

        Directory.Delete(fullPath, recursive: true);
        return Task.CompletedTask;
    }

    private static string GetRenameDestination(
        string sourceDirectory,
        string newName)
    {
        var parent = Path.GetDirectoryName(sourceDirectory)
            ?? throw new InvalidOperationException(
                "Cannot determine parent directory.");

        var safeName = WindowsPathRules.SanitizeSegment(newName);
        var directDestination = Path.Combine(parent, safeName);

        if (string.Equals(
                sourceDirectory,
                directDestination,
                StringComparison.OrdinalIgnoreCase))
        {
            return sourceDirectory;
        }

        return WindowsPathRules.GetUniqueDirectoryPath(
            parent,
            safeName);
    }

    private static bool IsSameOrDescendant(
        string candidate,
        string parent)
    {
        var normalizedCandidate = candidate
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        var normalizedParent = parent
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(
            normalizedParent,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}
