using Nodalis.Core.Projects;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Resolves non-owning journey sources strictly within one workspace.
/// Never follows directory junctions or symbolic links to read evidence.
/// </summary>
public sealed class ProjectJourneySourceService
{
    private readonly string _workspaceRoot;

    /// <summary>Creates an isolated workspace source resolver.</summary>
    /// <param name="workspaceRoot">Current workspace directory.</param>
    public ProjectJourneySourceService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
    }

    /// <summary>
    /// Converts a real workspace file selected by the user to a portable
    /// workspace-relative reference. External files are never imported.
    /// </summary>
    /// <param name="fullPath">Existing file selected by a picker.</param>
    /// <returns>Canonical forward-slash workspace-relative reference.</returns>
    public string GetReferenceForExistingFile(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        string resolved = Path.GetFullPath(fullPath);
        string reference = Path.GetRelativePath(_workspaceRoot, resolved)
            .Replace(Path.DirectorySeparatorChar, '/');
        ProjectJourneySourcePath.RequireSafe(reference);
        if (!IsWithinWorkspace(resolved))
        {
            throw new InvalidDataException("La source est hors du workspace.");
        }

        EnsureNoReparsePoints(resolved);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException(
                "Sélectionnez un fichier existant du workspace.", resolved);
        }

        return reference;
    }

    /// <summary>
    /// Resolves one stored non-owning reference. Validates traversal,
    /// containment, file existence and any symbolic-link ancestry.
    /// </summary>
    /// <param name="reference">Workspace-relative source reference.</param>
    /// <returns>Validated existing file path.</returns>
    public string ResolveExistingFile(string reference)
    {
        ProjectJourneySourcePath.RequireSafe(reference);
        string full = Path.GetFullPath(Path.Combine(_workspaceRoot,
            reference.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinWorkspace(full))
        {
            throw new InvalidDataException("La source sort du workspace.");
        }

        EnsureNoReparsePoints(full);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException(
                "La source du parcours a été déplacée ou supprimée.", full);
        }

        return full;
    }

    /// <summary>Indicates whether an existing source can safely be resolved.</summary>
    /// <param name="reference">Stored source reference.</param>
    /// <returns>Whether the source exists without unsafe path traversal.</returns>
    public bool IsAvailable(string reference)
    {
        try
        {
            ResolveExistingFile(reference);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Checks boundary-aware workspace containment.</summary>
    /// <param name="path">Absolute candidate path.</param>
    /// <returns>Whether the path is under the configured workspace.</returns>
    private bool IsWithinWorkspace(string path)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, path);
        return !Path.IsPathRooted(relative) &&
            relative is not (".." or ".") &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    /// <summary>Rejects junctions and links in the entire existing ancestry.</summary>
    /// <param name="path">Candidate file.</param>
    private static void EnsureNoReparsePoints(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null &&
               (File.Exists(current) || Directory.Exists(current)))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Les sources via liens symboliques ou jonctions sont interdites.");
            }

            current = Path.GetDirectoryName(current);
        }
    }
}
