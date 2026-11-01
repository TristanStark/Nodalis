using Nodalis.Core.Links;
using Nodalis.Core.Risks;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Tasks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One navigable risk reference to a particular task's immutable GUID.
/// A previous plan remains historical even when its checkbox still exists.
/// </summary>
/// <param name="RiskId">Stable risk GUID.</param>
/// <param name="RiskReference">Portable RIS human reference.</param>
/// <param name="RiskTitle">Human-readable risk label.</param>
/// <param name="RiskRelativePath">Verified Markdown source for navigation.</param>
/// <param name="TreatmentId">Immutable treatment revision.</param>
/// <param name="Kind">Measure linked by explicit human event.</param>
/// <param name="IsCurrentPlan">Whether this is the risk's latest plan.</param>
/// <param name="IsLinked">Whether this exact plan/measure link remains active.</param>
public sealed record ProjectRiskTaskBacklink(
    Guid RiskId,
    string RiskReference,
    string RiskTitle,
    string RiskRelativePath,
    Guid TreatmentId,
    ProjectRiskMeasureKind Kind,
    bool IsCurrentPlan,
    bool IsLinked)
{
    /// <summary>Brief status distinguishing live, historic and unlinked edges.</summary>
    public string Status => !IsLinked
        ? "Délié · historique uniquement"
        : IsCurrentPlan ? "Lié · plan actuel" : "Lié · ancien plan";

    /// <summary>Compact entry suitable for a project-local WPF list.</summary>
    public string Label => RiskReference + " · " + RiskTitle +
        " · " + Kind + " · " + Status;
}

/// <summary>
/// One read-only backlink lookup. Diagnostics never authorize navigation to an
/// ambiguous source or creation of a missing task/risk.
/// </summary>
/// <param name="TaskId">Verified unique task PersistentId, if still valid.</param>
/// <param name="Links">Historical and active relations targeting the task.</param>
/// <param name="Diagnostics">Risk catalog or identity validation warnings.</param>
public sealed record ProjectRiskTaskBacklinkCatalog(
    Guid? TaskId,
    IReadOnlyList<ProjectRiskTaskBacklink> Links,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Resolves task-to-risk backlinks only on explicit request, reusing the
/// shared workspace task/link indices and the canonical project risk catalog.
/// No workspace tree scans occur during a WPF list redraw or hover.
/// </summary>
public sealed class ProjectRiskTaskBacklinkService
{
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Builds a read-only backlink resolver scoped to one workspace.</summary>
    /// <param name="workspaceRoot">Existing workspace root.</param>
    public ProjectRiskTaskBacklinkService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Revalidates the selected task by its persistent GUID, finds its actual
    /// owning project (not the caller's old line/path), then resolves only
    /// verified RIS Markdown references from that project.
    /// </summary>
    /// <param name="selected">Previously selected task snapshot.</param>
    /// <param name="cancellationToken">Optional cancellation.</param>
    /// <returns>Verified backlink catalog, with explicit diagnostics.</returns>
    public async Task<ProjectRiskTaskBacklinkCatalog> ListAsync(
        TaskItem selected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.PersistentId is not Guid taskId || taskId == Guid.Empty ||
            selected.ProjectId is not Guid projectId || projectId == Guid.Empty)
        {
            return new ProjectRiskTaskBacklinkCatalog(null, [],
                ["Cette tâche n'a pas d'identifiant persistant de projet."]);
        }

        WorkspaceTaskService taskService = new(_workspaceRoot);
        IReadOnlyList<TaskItem> all = await taskService.GetTasksAsync(
            _workspaceRoot, includeCompleted: true,
            cancellationToken: cancellationToken);
        TaskItem[] matches = all.Where(task =>
            task.PersistentId == taskId).ToArray();
        if (matches.Length != 1 || matches[0].ProjectId != projectId)
        {
            return new ProjectRiskTaskBacklinkCatalog(null, [],
                ["Tâche supprimée, déplacée vers un autre projet ou " +
                 "GUID ambigu : réactualisez la liste avant de naviguer."]);
        }

        // The source path comes from a freshly rebuilt source catalog, never
        // the stale item from the task list. Reject junctions and symlinks.
        if (!_sources.IsAvailable(matches[0].SourceRelativePath))
        {
            return new ProjectRiskTaskBacklinkCatalog(null, [],
                ["Source de tâche absente ou non sécurisée."]);
        }

        WorkspaceLinkIndexService linkService = new(_workspaceRoot);
        LinkIndexCatalog catalog = await linkService.RefreshAsync(
            cancellationToken);
        LinkTargetEntry[] projects = catalog.Targets.Where(target =>
            target.Kind == LinkTargetKind.Project &&
            target.Id == projectId).ToArray();
        if (projects.Length != 1 ||
            !IsDescendant(projects[0].RelativePath,
                matches[0].SourceRelativePath))
        {
            return new ProjectRiskTaskBacklinkCatalog(null, [],
                ["Projet propriétaire ou localisation de la tâche ambiguë."]);
        }

        string projectPath = Path.GetFullPath(Path.Combine(_workspaceRoot,
            projects[0].RelativePath.Replace('/',
                Path.DirectorySeparatorChar)));
        ProjectRiskRegistryService registry = new(_workspaceRoot);
        ProjectRiskCatalog risks = await registry.ListAsync(
            projectPath, cancellationToken);
        List<ProjectRiskTaskBacklink> backlinks = [];
        foreach (ProjectRiskEntry risk in risks.Risks)
        {
            // A linked task can also appear in a superseded treatment or
            // have been deliberately unlinked. The last event per exact
            // version + measure determines its historical state.
            foreach (IGrouping<(Guid TreatmentId, ProjectRiskMeasureKind Kind),
                ProjectRiskTaskLinkEvent> group in risk.TaskLinks
                .Where(link => link.TaskId == taskId)
                .GroupBy(link => (link.TreatmentId, link.Kind)))
            {
                ProjectRiskTaskLinkEvent last = group.Last();
                backlinks.Add(new ProjectRiskTaskBacklink(
                    risk.Card.Id,
                    risk.Card.Reference,
                    risk.Card.Title,
                    risk.RelativePath,
                    group.Key.TreatmentId,
                    group.Key.Kind,
                    risk.Treatments.Count > 0 &&
                        risk.Treatments[^1].Id == group.Key.TreatmentId,
                    last.Action == ProjectRiskTaskLinkAction.Link));
            }
        }

        return new ProjectRiskTaskBacklinkCatalog(
            taskId,
            backlinks.OrderByDescending(item => item.IsCurrentPlan)
                .ThenByDescending(item => item.IsLinked)
                .ThenBy(item => item.RiskReference, StringComparer.Ordinal)
                .ThenBy(item => item.Kind).ToArray(),
            risks.Diagnostics);
    }

    /// <summary>Checks a scope boundary using complete relative path segments.</summary>
    /// <param name="parent">Workspace-relative project path.</param>
    /// <param name="child">Workspace-relative Markdown task document path.</param>
    /// <returns>Whether the child belongs to the exact project folder.</returns>
    private static bool IsDescendant(string parent, string child)
    {
        string normalizedParent = parent.Replace('\\', '/').TrimEnd('/');
        string normalizedChild = child.Replace('\\', '/');
        return normalizedChild.StartsWith(
            normalizedParent + "/", StringComparison.OrdinalIgnoreCase);
    }
}
