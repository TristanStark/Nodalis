namespace Nodalis.Core.Risks;

/// <summary>
/// A task association is an explicit, reversible relation, never an
/// instruction to update a checkbox or a measure's execution status.
/// </summary>
public enum ProjectRiskTaskLinkAction
{
    Link,
    Unlink
}

/// <summary>
/// One human-reviewed link change. Every task is referenced exclusively by
/// its portable PersistentId, never its line-derived occurrence identity.
/// </summary>
public sealed record ProjectRiskTaskLinkEvent
{
    public required Guid Id { get; init; }

    public required Guid RiskId { get; init; }

    public required Guid TreatmentId { get; init; }

    public required ProjectRiskMeasureKind Kind { get; init; }

    public required Guid TaskId { get; init; }

    public Guid? PreviousId { get; init; }

    public required ProjectRiskTaskLinkAction Action { get; init; }

    public required string Actor { get; init; }

    public required string Rationale { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Read-only projections of explicitly confirmed task associations. Links
/// from prior treatment versions remain historical and never carry forward.
/// </summary>
/// <param name="Card">Validated, unmodified risk identity.</param>
/// <param name="Events">Chronological audit trail of links and unlinks.</param>
public sealed record ProjectRiskTaskLinkHistory(
    ProjectRiskCard Card, IReadOnlyList<ProjectRiskTaskLinkEvent> Events)
{
    /// <summary>Resolves active stable task IDs in one immutable measure slot.</summary>
    /// <param name="treatmentId">Exact plan-revision GUID.</param>
    /// <param name="kind">Named prevention, mitigation or contingency slot.</param>
    /// <returns>Task GUIDs, not dereferenced or assumed valid on disk.</returns>
    public IReadOnlyList<Guid> ActiveTaskIds(
        Guid treatmentId, ProjectRiskMeasureKind kind)
    {
        HashSet<Guid> active = [];
        foreach (ProjectRiskTaskLinkEvent item in Events.Where(item =>
            item.TreatmentId == treatmentId && item.Kind == kind))
        {
            if (item.Action == ProjectRiskTaskLinkAction.Link)
            {
                active.Add(item.TaskId);
            }
            else
            {
                active.Remove(item.TaskId);
            }
        }

        return active.OrderBy(id => id).ToArray();
    }
}
