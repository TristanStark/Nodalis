using System.Text.Json.Serialization;

namespace Nodalis.Core.Projects;

/// <summary>Human-declared operations on one immutable deployment attempt.</summary>
public enum ProjectDeploymentEventKind
{
    ActionLogged,
    RollbackRequested,
    RollbackStarted,
    RollbackObserved,
    FinishedSucceeded,
    FinishedFailed,
    FinishedInterrupted
}

/// <summary>Observed rollback result, never inferred from a request.</summary>
public enum ProjectDeploymentRollbackOutcome
{
    Succeeded,
    Partial,
    Failed,
    Interrupted
}

/// <summary>
/// One planned MEP, kept as a portable Markdown source; no attempt is ever
/// overwritten by a retry and no execution is performed by Nodalis.
/// </summary>
public sealed record ProjectDeploymentOperation
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid Id { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public List<ProjectDeploymentAttempt> Attempts { get; init; } = [];
    public List<ProjectDeploymentAttemptEvent> Events { get; init; } = [];

    /// <summary>
    /// Optional human closure omitted for old canonical v1 MEP documents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectDeploymentClosure? Closure { get; init; }

    /// <summary>Optional append-only operational handover reviews.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProjectDeploymentHandoverReview>? HandoverReviews { get; init; }
}

/// <summary>
/// Frozen context independently reviewed at the beginning of EACH attempt.
/// The gate GUID and attestations are historical declarations, not automatic
/// deployment permission; the live gate and incident scopes require recheck.
/// </summary>
public sealed record ProjectDeploymentAttempt
{
    public required Guid Id { get; init; }
    public required int Number { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required string StartedBy { get; init; }
    public required string Rationale { get; init; }
    public required string ObservedVersionBefore { get; init; }
    public required string TargetVersion { get; init; }
    public required string Environment { get; init; }
    public required string Scope { get; init; }
    public required ProjectGateEvidenceSnapshot Procedure { get; init; }
    public required ProjectGateEvidenceSnapshot RollbackProcedure { get; init; }
    public required Guid GateDecisionId { get; init; }
    public required string AuthorizationReviewedBy { get; init; }
    public required string AuthorizationRationale { get; init; }
}

/// <summary>
/// Append-only human event. A rollback request is not its execution; a
/// rollback observation captures its actual outcome and observed version.
/// </summary>
public sealed record ProjectDeploymentAttemptEvent
{
    public required Guid Id { get; init; }
    public required Guid AttemptId { get; init; }
    public required ProjectDeploymentEventKind Kind { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
    public ProjectDeploymentRollbackOutcome? RollbackOutcome { get; init; }
    public string? ObservedVersionAfter { get; init; }
    public ProjectGateEvidenceSnapshot? Evidence { get; init; }
}

/// <summary>Calculated attempt state without modifying frozen events.</summary>
public sealed record ProjectDeploymentAttemptState
{
    public required Guid AttemptId { get; init; }
    public required bool IsFinished { get; init; }
    public required bool RollbackRequested { get; init; }
    public required bool RollbackStarted { get; init; }
    public ProjectDeploymentRollbackOutcome? RollbackObserved { get; init; }
    public ProjectDeploymentEventKind? LastEventKind { get; init; }
}


/// <summary>
/// Explicit business verdict for an entire MEP; no attempt or rollback
/// automatically produces a successful operation.
/// </summary>
public enum ProjectDeploymentOutcome
{
    Succeeded,
    SucceededWithReservations,
    Failed,
    Cancelled
}

/// <summary>
/// Human-confirmed immutable closure with separately observed deployment
/// date and later business closure date.
/// </summary>
public sealed record ProjectDeploymentClosure
{
    public required Guid Id { get; init; }
    public required ProjectDeploymentOutcome Outcome { get; init; }
    public Guid? AttemptId { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public DateTimeOffset? EffectiveDeploymentUtc { get; init; }
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
    public string? Reservations { get; init; }
}
