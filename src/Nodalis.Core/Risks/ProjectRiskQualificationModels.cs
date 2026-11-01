namespace Nodalis.Core.Risks;

/// <summary>
/// One human choice on the risk's business validity, independently from the
/// probability × impact score and the operational lifecycle.
/// </summary>
public sealed record ProjectRiskQualificationEvent
{
    public required Guid Id { get; init; }

    public required Guid RiskId { get; init; }

    public required ProjectRiskQualification Previous { get; init; }

    public required ProjectRiskQualification Status { get; init; }

    public required string Actor { get; init; }

    public required string Rationale { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Strict reading of the current human qualification and its audited decisions.
/// </summary>
/// <param name="Card">Validated frontmatter, including qualification state.</param>
/// <param name="Events">Ordered history; no automatic decision is synthesized.</param>
public sealed record ProjectRiskQualificationHistory(
    ProjectRiskCard Card,
    IReadOnlyList<ProjectRiskQualificationEvent> Events);
