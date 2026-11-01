namespace Nodalis.Core.Risks;

/// <summary>Review result independent of a risk's lifecycle.</summary>
public enum ProjectRiskQualification
{
    ToReview,
    Validated,
    Rejected
}

/// <summary>Risk lifecycle is distinct from qualification and severity.</summary>
public enum ProjectRiskLifecycle
{
    Open,
    InProgress,
    Monitoring,
    Closed
}

/// <summary>Unknown is not low: it means no human evaluation exists.</summary>
public enum ProjectRiskSeverity
{
    Unassessed,
    Low,
    Moderate,
    High
}

/// <summary>Explicit probability (1..3) and impact (1..3) assessment.</summary>
/// <param name="Probability">Assessed probability, or null.</param>
/// <param name="Impact">Assessed impact, or null.</param>
public sealed record ProjectRiskRating(int? Probability, int? Impact);

/// <summary>
/// Canonical metadata of one Markdown risk document, with a stable GUID
/// independent of its portable, human-readable RIS-0001 reference.
/// </summary>
public sealed record ProjectRiskCard
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Reference { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public ProjectRiskQualification Qualification { get; init; } =
        ProjectRiskQualification.ToReview;

    public ProjectRiskLifecycle Lifecycle { get; init; } =
        ProjectRiskLifecycle.Open;

    /// <summary>First validated rating. Never inferred from the residual score.</summary>
    public ProjectRiskRating Initial { get; init; } = new(null, null);

    /// <summary>Later human reassessment. Never inferred from a closed task.</summary>
    public ProjectRiskRating Residual { get; init; } = new(null, null);
}

/// <summary>Pure, deterministic matrix: no score is generated for unknowns.</summary>
public static class ProjectRiskMatrix
{
    /// <summary>Validates an assessment and classifies its 1..9 product.</summary>
    /// <param name="rating">Paired probability and impact, possibly unknown.</param>
    /// <returns>Unknown, low, moderate or high without any side effects.</returns>
    public static ProjectRiskSeverity Evaluate(ProjectRiskRating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        if (rating.Probability is null && rating.Impact is null)
        {
            return ProjectRiskSeverity.Unassessed;
        }

        if (rating.Probability is not int probability ||
            rating.Impact is not int impact ||
            probability is < 1 or > 3 || impact is < 1 or > 3)
        {
            throw new InvalidDataException(
                "Probabilité et impact doivent être évalués ensemble, entre 1 et 3.");
        }

        int score = probability * impact;
        return score <= 2
            ? ProjectRiskSeverity.Low
            : score <= 4
                ? ProjectRiskSeverity.Moderate
                : ProjectRiskSeverity.High;
    }
}
