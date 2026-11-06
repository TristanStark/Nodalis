using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Tasks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// A source-verified, uniquely identified project task or milestone.
/// A label and source path are navigation hints, not a fallback identity.
/// </summary>
/// <param name="Kind">Stable object category.</param>
/// <param name="Id">Persisted, unambiguous target GUID.</param>
/// <param name="Label">Human label with present status.</param>
/// <param name="SourceRelativePath">Canonical existing Markdown source.</param>
public sealed record ProjectDeploymentReservationTarget(
    ProjectDeploymentReservationRelationKind Kind, Guid Id, string Label,
    string SourceRelativePath);

/// <summary>
/// Read-only candidate lookup; on-demand only, never during UI redraw or
/// background polling. Task ID collisions across applications are excluded.
/// </summary>
public sealed class ProjectDeploymentReservationTargetService
{
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectJourneyMilestoneCatalogService _milestones;

    /// <summary>Builds a workspace-isolated stable ID candidate resolver.</summary>
    /// <param name="workspaceRoot">One canonical Nodalis workspace.</param>
    public ProjectDeploymentReservationTargetService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _milestones = new ProjectJourneyMilestoneCatalogService(
            _workspaceRoot);
    }

    /// <summary>
    /// Scans task/milestone sources only on explicit human lookup. Never
    /// adopts a legacy checkbox identity or creates a missing milestone ID.
    /// </summary>
    /// <param name="projectDirectory">Verified owning project.</param>
    /// <param name="kind">Task or milestone category.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Eligible, stable project-local targets only.</returns>
    public async Task<IReadOnlyList<ProjectDeploymentReservationTarget>>
        ListAsync(string projectDirectory,
        ProjectDeploymentReservationRelationKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("Type de lien inconnu.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string manifestPath = Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet source sans identité.");
        }

        if (kind == ProjectDeploymentReservationRelationKind.Milestone)
        {
            ProjectJourneyMilestoneCatalog milestones =
                await _milestones.ListAsync(project, cancellationToken);
            return milestones.Candidates
                .Select(item => new ProjectDeploymentReservationTarget(
                    kind, item.Id, item.Label, item.SourceRelativePath))
                .ToArray();
        }

        WorkspaceTaskService tasks = new(_workspaceRoot);
        // Entire workspace is inspected ONLY for this explicit command.
        // This prevents a task GUID duplicated in another project from being
        // linked as if it were unique in the selected project's list.
        IReadOnlyList<TaskItem> all = await tasks.GetTasksAsync(
            _workspaceRoot, includeCompleted: true,
            cancellationToken: cancellationToken);
        HashSet<Guid> collisions = all
            .Where(item => item.PersistentId is not null)
            .GroupBy(item => item.PersistentId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        List<ProjectDeploymentReservationTarget> candidates = [];
        foreach (TaskItem task in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (task.PersistentId is not Guid id || id == Guid.Empty ||
                task.ProjectId != manifest.Id || collisions.Contains(id))
            {
                continue;
            }

            string path;
            try
            {
                path = _sources.ResolveExistingFile(task.SourceRelativePath);
            }
            catch (Exception ex) when (ex is IOException or
                UnauthorizedAccessException or InvalidDataException or
                ArgumentException)
            {
                continue;
            }

            if (!IsWithinProject(project, path))
            {
                continue;
            }

            candidates.Add(new ProjectDeploymentReservationTarget(
                kind, id, task.Text +
                (task.IsCompleted ? " · cochée" : " · à traiter"),
                task.SourceRelativePath));
        }

        return candidates.OrderBy(item => item.Label,
            StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>
    /// Revalidates the existing source and stable GUID at commit time, never
    /// accepting a stale user-selected path or a foreign project target.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="kind">Category of the linked target.</param>
    /// <param name="id">Persisted stable GUID.</param>
    /// <param name="expectedPath">Path selected by the user.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current source-verified identity.</returns>
    public async Task<ProjectDeploymentReservationTarget> RequireCurrentAsync(
        string projectDirectory, ProjectDeploymentReservationRelationKind kind,
        Guid id, string expectedPath,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty ||
            !ProjectJourneySourcePath.IsSafe(expectedPath))
        {
            throw new InvalidDataException("Identifiant ou chemin de lien invalide.");
        }

        IReadOnlyList<ProjectDeploymentReservationTarget> candidates =
            await ListAsync(projectDirectory, kind, cancellationToken);
        ProjectDeploymentReservationTarget? current =
            candidates.SingleOrDefault(item => item.Id == id);
        if (current is null ||
            !string.Equals(current.SourceRelativePath, expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Tâche ou jalon supprimé, déplacé, dupliqué ou étranger au " +
                "projet : actualisez les cibles avant d'associer.");
        }

        return current;
    }

    /// <summary>Validates full directory boundary with a slash separator.</summary>
    /// <param name="project">Canonical project root.</param>
    /// <param name="path">Verified existing file.</param>
    /// <returns>Whether this source belongs to the same project.</returns>
    private static bool IsWithinProject(string project, string path)
    {
        string relative = Path.GetRelativePath(project, path);
        return !Path.IsPathRooted(relative) &&
            relative is not ("." or "..") &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal);
    }
}
