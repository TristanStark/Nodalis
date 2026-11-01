namespace Nodalis.Core.Risks;

/// <summary>
/// The explicit response to a risk, distinct from its severity, lifecycle
/// and human business qualification decision.
/// </summary>
public enum ProjectRiskTreatmentStrategy
{
    Avoid,
    Reduce,
    Transfer,
    Accept
}

/// <summary>
/// One human-owned treatment plan. Text fields describe actual measures or an
/// explicit reason why a measure does not apply; no automatic task is created.
/// </summary>
/// <param name="Strategy">Avoid, reduce, transfer or accept.</param>
/// <param name="Owner">Accountable person or team.</param>
/// <param name="Prevention">Prevention and probability reduction measures.</param>
/// <param name="Mitigation">Impact reduction or transfer measures.</param>
/// <param name="Contingency">Action to take if the risk occurs.</param>
public sealed record ProjectRiskTreatmentPlan(
    ProjectRiskTreatmentStrategy Strategy,
    string Owner,
    string Prevention,
    string Mitigation,
    string Contingency);

/// <summary>
/// Immutable event documenting one initial treatment or a later revised plan.
/// PreviousId links revisions and exposes missing intermediate events.
/// </summary>
public sealed record ProjectRiskTreatmentEvent
{
    public required Guid Id { get; init; }

    public required Guid RiskId { get; init; }

    public Guid? PreviousId { get; init; }

    public required ProjectRiskTreatmentPlan Plan { get; init; }

    public required string Actor { get; init; }

    public required string Rationale { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Read-only projection of a validated risk and its full human plan history.
/// The current plan is absent until a person has explicitly recorded one.
/// </summary>
/// <param name="Card">Verified canonical risk identity and scores.</param>
/// <param name="Events">Ordered, append-only treatment history.</param>
public sealed record ProjectRiskTreatmentHistory(
    ProjectRiskCard Card,
    IReadOnlyList<ProjectRiskTreatmentEvent> Events)
{
    /// <summary>Latest explicitly reviewed plan, or none.</summary>
    public ProjectRiskTreatmentPlan? Current =>
        Events.Count == 0 ? null : Events[^1].Plan;
}
