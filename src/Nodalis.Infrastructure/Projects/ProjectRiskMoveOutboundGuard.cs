using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// A move must not carry live risk→task assignments into another project.
/// Historical link/unlink events are retained intact, but current treatment
/// associations require human unlink/requalification before changing owner.
/// </summary>
public static class ProjectRiskMoveOutboundGuard
{
    /// <summary>Replays the current plan's human-reviewed task associations.</summary>
    /// <param name="currentTreatmentId">Latest immutable treatment version.</param>
    /// <param name="events">Verified chronological task link journal.</param>
    /// <returns>True if any task still belongs to a live measure slot.</returns>
    public static bool HasActiveTaskLinks(
        Guid? currentTreatmentId,
        IReadOnlyList<ProjectRiskTaskLinkEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (currentTreatmentId is null)
        {
            return false;
        }

        HashSet<(ProjectRiskMeasureKind Kind, Guid TaskId)> active = [];
        foreach (ProjectRiskTaskLinkEvent link in events.Where(item =>
            item.TreatmentId == currentTreatmentId))
        {
            (ProjectRiskMeasureKind, Guid) key = (link.Kind, link.TaskId);
            if (link.Action == ProjectRiskTaskLinkAction.Link)
            {
                active.Add(key);
            }
            else
            {
                active.Remove(key);
            }
        }

        return active.Count > 0;
    }
}
