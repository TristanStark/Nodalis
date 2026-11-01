namespace Nodalis.Core.Risks;

/// <summary>
/// Each treatment version has exactly three named measure slots, matching
/// its prevention, mitigation and contingency fields without splitting tasks.
/// </summary>
public enum ProjectRiskMeasureKind
{
    Prevention,
    Mitigation,
    Contingency
}

/// <summary>
/// Progress is a human statement and never inferred from a Markdown checkbox.
/// Pending is the initial projection for an unreviewed slot, not an event.
/// </summary>
public enum ProjectRiskMeasureStatus
{
    Pending,
    InProgress,
    Blocked,
    Completed
}

/// <summary>
/// One audited human update of a measure belonging to a particular immutable
/// treatment revision. Older plan versions keep their own progress history.
/// </summary>
public sealed record ProjectRiskMeasureEvent
{
    public required Guid Id { get; init; }

    public required Guid RiskId { get; init; }

    public required Guid TreatmentId { get; init; }

    public Guid? PreviousId { get; init; }

    public required ProjectRiskMeasureKind Kind { get; init; }

    public required ProjectRiskMeasureStatus PreviousStatus { get; init; }

    public required ProjectRiskMeasureStatus Status { get; init; }

    public required string Actor { get; init; }

    public required string Rationale { get; init; }

    /// <summary>Human-entered evidence description required for completion.</summary>
    public string? Evidence { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// A read-only view of the progress log. Current treatment version is selected
/// explicitly, so obsolete evidence is not carried to a revised plan.
/// </summary>
/// <param name="Card">Canonical risk card.</param>
/// <param name="Events">All historical measure declarations.</param>
public sealed record ProjectRiskMeasureHistory(
    ProjectRiskCard Card, IReadOnlyList<ProjectRiskMeasureEvent> Events)
{
    /// <summary>Returns the human status or Pending for an unreviewed slot.</summary>
    /// <param name="treatmentId">Stable GUID of a treatment revision.</param>
    /// <param name="kind">Named measure slot.</param>
    /// <returns>Last human statement or Pending.</returns>
    public ProjectRiskMeasureStatus StatusFor(
        Guid treatmentId, ProjectRiskMeasureKind kind) =>
        Events.LastOrDefault(item =>
            item.TreatmentId == treatmentId && item.Kind == kind)?.Status ??
        ProjectRiskMeasureStatus.Pending;
}
