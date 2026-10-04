using Nodalis.Core.Domain;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Applications;

public sealed class ApplicationStructureService
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="ApplicationStructureService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public ApplicationStructureService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>LoadModuleAsync</c> operation.
    /// </summary>
    /// <param name="moduleDirectory">The <c>moduleDirectory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>CreateApplicationAsync</c> operation.
    /// </summary>
    /// <param name="name">The <c>name</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string> CreateApplicationAsync(
            string name,
            CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeName(name);
        string applicationsRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ApplicationsDirectoryName);

        Directory.CreateDirectory(applicationsRoot);

        string finalDirectory = WindowsPathRules.GetUniqueDirectoryPath(
            applicationsRoot,
            normalizedName);

        string stagingDirectory = Path.Combine(
            applicationsRoot,
            $".nodalis-application-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            global::Nodalis.Core.Domain.ApplicationManifest manifest = new ApplicationManifest
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

    /// <summary>
    /// Performs the <c>CreateModuleAsync</c> operation.
    /// </summary>
    /// <param name="parentDirectory">The <c>parentDirectory</c> value.</param>
    /// <param name="applicationId">The <c>applicationId</c> value.</param>
    /// <param name="parentModuleId">The <c>parentModuleId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string> CreateModuleAsync(
            string parentDirectory,
            Guid applicationId,
            Guid? parentModuleId,
            string name,
            CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeName(name);
        string modulesRoot = Path.Combine(
            Path.GetFullPath(parentDirectory),
            WorkspaceLayout.ModulesDirectoryName);

        Directory.CreateDirectory(modulesRoot);

        string finalDirectory = WindowsPathRules.GetUniqueDirectoryPath(
            modulesRoot,
            normalizedName);

        string stagingDirectory = Path.Combine(
            modulesRoot,
            $".nodalis-module-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            global::Nodalis.Core.Domain.ModuleManifest manifest = new ModuleManifest
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

    /// <summary>
    /// Performs the <c>RenameApplicationAsync</c> operation.
    /// </summary>
    /// <param name="applicationDirectory">The <c>applicationDirectory</c> value.</param>
    /// <param name="newName">The <c>newName</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string> RenameApplicationAsync(
            string applicationDirectory,
            string newName,
            CancellationToken cancellationToken = default)
    {
        string directory = Path.GetFullPath(applicationDirectory);
        string manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ApplicationManifestFileName);

        global::Nodalis.Core.Domain.ApplicationManifest manifest = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
            manifestPath,
            cancellationToken);

        string destination = GetRenameDestination(
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

    /// <summary>
    /// Performs the <c>RenameModuleAsync</c> operation.
    /// </summary>
    /// <param name="moduleDirectory">The <c>moduleDirectory</c> value.</param>
    /// <param name="newName">The <c>newName</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string> RenameModuleAsync(
            string moduleDirectory,
            string newName,
            CancellationToken cancellationToken = default)
    {
        string directory = Path.GetFullPath(moduleDirectory);
        string manifestPath = Path.Combine(
            directory,
            WorkspaceLayout.ModuleManifestFileName);

        global::Nodalis.Core.Domain.ModuleManifest manifest = await AtomicJsonFile.ReadAsync<ModuleManifest>(
            manifestPath,
            cancellationToken);

        string destination = GetRenameDestination(
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

    /// <summary>
    /// Performs the <c>MoveModuleAsync</c> operation.
    /// </summary>
    /// <param name="moduleDirectory">The <c>moduleDirectory</c> value.</param>
    /// <param name="newParentDirectory">The <c>newParentDirectory</c> value.</param>
    /// <param name="newParentModuleId">The <c>newParentModuleId</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string> MoveModuleAsync(
            string moduleDirectory,
            string newParentDirectory,
            Guid? newParentModuleId,
            CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(moduleDirectory);
        string parent = Path.GetFullPath(newParentDirectory);

        if (IsSameOrDescendant(parent, source))
        {
            throw new DomainValidationException(
                "A module cannot be moved inside itself or one of its descendants.");
        }

        global::Nodalis.Core.Domain.ModuleManifest manifest = await AtomicJsonFile.ReadAsync<ModuleManifest>(
            Path.Combine(
                source,
                WorkspaceLayout.ModuleManifestFileName),
            cancellationToken);

        if (newParentModuleId is Guid parentModuleId)
        {
            global::Nodalis.Core.Domain.ModuleManifest parentManifest = await AtomicJsonFile.ReadAsync<ModuleManifest>(
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
            global::Nodalis.Core.Domain.ApplicationManifest applicationManifest = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
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

        string destinationRoot = Path.Combine(
            parent,
            WorkspaceLayout.ModulesDirectoryName);

        Directory.CreateDirectory(destinationRoot);

        string destination = WindowsPathRules.GetUniqueDirectoryPath(
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

    /// <summary>
    /// Performs the <c>DeleteApplicationAsync</c> operation.
    /// </summary>
    /// <param name="applicationDirectory">The <c>applicationDirectory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public Task DeleteApplicationAsync(
            string applicationDirectory,
            CancellationToken cancellationToken = default) =>
            DeleteContainerAsync(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName,
                cancellationToken);

    /// <summary>
    /// Performs the <c>DeleteModuleAsync</c> operation.
    /// </summary>
    /// <param name="moduleDirectory">The <c>moduleDirectory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public Task DeleteModuleAsync(
            string moduleDirectory,
            CancellationToken cancellationToken = default) =>
            DeleteContainerAsync(
                moduleDirectory,
                WorkspaceLayout.ModuleManifestFileName,
                cancellationToken);

    /// <summary>
    /// Performs the <c>InitializeContainerAsync</c> operation.
    /// </summary>
    /// <param name="directory">The <c>directory</c> value.</param>
    /// <param name="includeDocumentation">The <c>includeDocumentation</c> value.</param>
    /// <param name="includeScopeFiles">The <c>includeScopeFiles</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>DeleteContainerAsync</c> operation.
    /// </summary>
    /// <param name="directory">The <c>directory</c> value.</param>
    /// <param name="manifestFileName">The <c>manifestFileName</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Task DeleteContainerAsync(
            string directory,
            string manifestFileName,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(directory);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        string[] meaningfulFiles = Directory
            .EnumerateFiles(
                fullPath,
                "*",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                string name = Path.GetFileName(file);

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
                    string text = File.ReadAllText(file);
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

        string[] meaningfulDirectories = Directory
            .EnumerateDirectories(
                fullPath,
                "*",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                string name = Path.GetFileName(path);
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

    /// <summary>
    /// Performs the <c>GetRenameDestination</c> operation.
    /// </summary>
    /// <param name="sourceDirectory">The <c>sourceDirectory</c> value.</param>
    /// <param name="newName">The <c>newName</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string GetRenameDestination(
            string sourceDirectory,
            string newName)
    {
        string parent = Path.GetDirectoryName(sourceDirectory)
            ?? throw new InvalidOperationException(
                "Cannot determine parent directory.");

        string safeName = WindowsPathRules.SanitizeSegment(newName);
        string directDestination = Path.Combine(parent, safeName);

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

    /// <summary>
    /// Performs the <c>IsSameOrDescendant</c> operation.
    /// </summary>
    /// <param name="candidate">The <c>candidate</c> value.</param>
    /// <param name="parent">The <c>parent</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsSameOrDescendant(
            string candidate,
            string parent)
    {
        string normalizedCandidate = candidate
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        string normalizedParent = parent
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(
            normalizedParent,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>NormalizeName</c> operation.
    /// </summary>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}
