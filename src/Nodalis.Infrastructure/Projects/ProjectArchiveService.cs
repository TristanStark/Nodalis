using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Moves complete projects between their active location and the workspace Archives
/// without changing GUIDs, deleting documents or using the trash.
/// </summary>
public sealed class ProjectArchiveService
{
    private readonly string _root;
    private readonly string _applicationsRoot;
    private readonly string _archivesRoot;

    /// <summary>Creates a lifecycle service bound to a single workspace.</summary>
    /// <param name="workspaceRoot">Workspace directory.</param>
    public ProjectArchiveService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _applicationsRoot = Path.Combine(_root, WorkspaceLayout.ApplicationsDirectoryName);
        _archivesRoot = Path.Combine(_root, WorkspaceLayout.ArchivesDirectoryName);
    }

    /// <summary>Moves an active project and all its descendants to Archives.</summary>
    /// <param name="projectDirectory">Existing active project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The archived project's absolute directory path.</returns>
    public async Task<string> ArchiveAsync(string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateDirectory(projectDirectory, _applicationsRoot);
        string? parentName = Path.GetFileName(Path.GetDirectoryName(source));
        if (!string.Equals(parentName, WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parentName, WorkspaceLayout.SubProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Le dossier n'est pas un projet actif Nodalis.");
        }

        string manifestPath = Path.Combine(source, WorkspaceLayout.ProjectManifestFileName);
        ProjectManifest original = await ReadProjectAsync(manifestPath, cancellationToken);
        if (original.ArchivedUtc.HasValue || original.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("Ce projet est déjà marqué comme archivé.");
        }

        // Never relocate a project while an interrupted first DOCX import
        // still references this path, including imports in subprojects.
        // Moving the tree would otherwise invalidate its recovery journal.
        DocxFirstImportRecoveryService importRecovery =
            new DocxFirstImportRecoveryService(_root);
        IReadOnlyList<string> pendingImports =
            await importRecovery.ListPendingProjectsAsync(cancellationToken);
        if (pendingImports.Any(pending =>
                string.Equals(pending, source, StringComparison.OrdinalIgnoreCase) ||
                IsInside(pending, source)))
        {
            throw new InvalidOperationException(
                "Un import DOCX du projet ou d'un sous-projet est interrompu. " +
                "Récupérez l'import avant de clore et archiver le projet.");
        }

        string destination = Path.Combine(_archivesRoot, original.Id.ToString("N"));
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("Le dossier d'archives de ce projet existe déjà : " + destination);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_archivesRoot);
        ProjectManifest archived = original with
        {
            ArchivedUtc = DateTimeOffset.UtcNow,
            ArchiveOriginalRelativePath = Path.GetRelativePath(_root, source)
        };

        await AtomicJsonFile.WriteAsync(manifestPath, archived, cancellationToken);
        try
        {
            // Same-volume directory rename keeps every document and nested project intact.
            Directory.Move(source, destination);
        }
        catch
        {
            if (Directory.Exists(source))
            {
                await AtomicJsonFile.WriteAsync(manifestPath, original, CancellationToken.None);
            }

            throw;
        }

        return destination;
    }

    /// <summary>Reopens a project previously moved into Archives.</summary>
    /// <param name="archivedDirectory">Direct child of the workspace Archives folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Restored absolute project directory path.</returns>
    public async Task<string> RestoreAsync(string archivedDirectory,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateDirectory(archivedDirectory, _archivesRoot);
        if (!string.Equals(Path.GetDirectoryName(source), _archivesRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sélectionnez le projet racine dans Archives.");
        }

        string manifestPath = Path.Combine(source, WorkspaceLayout.ProjectManifestFileName);
        ProjectManifest archived = await ReadProjectAsync(manifestPath, cancellationToken);
        if (!archived.ArchivedUtc.HasValue ||
            string.IsNullOrWhiteSpace(archived.ArchiveOriginalRelativePath) ||
            Path.IsPathRooted(archived.ArchiveOriginalRelativePath))
        {
            throw new InvalidDataException("Le projet ne contient pas de provenance d'archivage valide.");
        }

        string destination = Path.GetFullPath(
            Path.Combine(_root, archived.ArchiveOriginalRelativePath));
        if (!IsInside(destination, _applicationsRoot))
        {
            throw new InvalidDataException("Chemin de réouverture extérieur aux applications du workspace.");
        }

        string? parent = Path.GetDirectoryName(destination);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                "Le dossier parent d'origine n'existe plus : " + parent);
        }

        EnsureNoReparsePoints(parent, _root);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("La destination du projet existe déjà : " + destination);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ProjectManifest reopened = archived with
        {
            ArchivedUtc = null,
            ArchiveOriginalRelativePath = null
        };
        await AtomicJsonFile.WriteAsync(manifestPath, reopened, cancellationToken);
        try
        {
            Directory.Move(source, destination);
        }
        catch
        {
            if (Directory.Exists(source))
            {
                await AtomicJsonFile.WriteAsync(manifestPath, archived, CancellationToken.None);
            }

            throw;
        }

        return destination;
    }

    /// <summary>Reads only a valid project manifest, rather than moving arbitrary folders.</summary>
    /// <param name="path">JSON manifest path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project metadata.</returns>
    private static async Task<ProjectManifest> ReadProjectAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Manifeste de projet introuvable.", path);
        }

        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, cancellationToken);
        if (manifest.Id == Guid.Empty || string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidDataException("Manifeste de projet invalide.");
        }

        return manifest;
    }

    /// <summary>Validates an existing workspace-contained directory and its real ancestors.</summary>
    /// <param name="path">Candidate directory.</param>
    /// <param name="allowedRoot">Required containing folder.</param>
    /// <returns>Canonical directory path.</returns>
    private string ValidateDirectory(string path, string allowedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!IsInside(fullPath, allowedRoot) || !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("Dossier projet absent ou hors du workspace : " + fullPath);
        }

        EnsureNoReparsePoints(fullPath, _root);
        return fullPath;
    }

    /// <summary>Checks containment with directory boundaries, not string prefixes alone.</summary>
    /// <param name="path">Candidate absolute path.</param>
    /// <param name="root">Required absolute ancestor directory.</param>
    /// <returns>True only if path is strictly below root.</returns>
    private static bool IsInside(string path, string root)
    {
        return path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Rejects junctions/symlinks while traversing workspace directories.</summary>
    /// <param name="path">Existing directory to validate.</param>
    /// <param name="workspaceRoot">Workspace security boundary.</param>
    private static void EnsureNoReparsePoints(string path, string workspaceRoot)
    {
        string current = path;
        while (IsInside(current, workspaceRoot))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Archive interdite à travers un lien symbolique ou une jonction.");
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidDataException("Impossible de vérifier le dossier parent.");
        }
    }
}
