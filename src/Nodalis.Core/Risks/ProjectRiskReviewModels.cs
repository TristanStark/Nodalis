namespace Nodalis.Core.Risks;

/// <summary>Initial rating is reviewed once; residual can be requalified.</summary>
public enum ProjectRiskReviewKind
{
    Initial,
    Residual
}

/// <summary>
/// Human-confirmed, append-only rating event embedded in one risk Markdown.
/// Older assessments are never replaced by later evaluations.
/// </summary>
public sealed record ProjectRiskReviewEvent
{
    public required Guid Id { get; init; }

    public required Guid RiskId { get; init; }

    public required ProjectRiskReviewKind Kind { get; init; }

    public required ProjectRiskRating Rating { get; init; }

    public required string Actor { get; init; }

    public required string Rationale { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Verified projection of one source file and its chronological human reviews.
/// A score in YAML without an event is never considered qualified.
/// </summary>
/// <param name="Card">Current canonical metadata.</param>
/// <param name="Events">Immutable chronological human reviews.</param>
public sealed record ProjectRiskReviewHistory(
    ProjectRiskCard Card, IReadOnlyList<ProjectRiskReviewEvent> Events);
