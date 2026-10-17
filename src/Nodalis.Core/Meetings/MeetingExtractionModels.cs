using Nodalis.Core.Tasks;

namespace Nodalis.Core.Meetings;

/// <summary>
/// Describes one action detected in a meeting report.
/// </summary>
public sealed record MeetingActionExtractionCandidate
{
    /// <summary>Gets the source task.</summary>
    public required TaskItem Task { get; init; }

    /// <summary>Gets a value indicating whether the action was already promoted.</summary>
    public bool IsDuplicate { get; init; }
}

/// <summary>
/// Describes one decision detected in a meeting report.
/// </summary>
public sealed record MeetingDecisionExtractionCandidate
{
    /// <summary>Gets a deterministic candidate identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the detected decision text.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the one-based source line.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Gets a value indicating whether an equivalent Decision Record already exists.</summary>
    public bool IsDuplicate { get; init; }
}

/// <summary>
/// Read-only preview of actionable content detected in one meeting report.
/// </summary>
public sealed record MeetingExtractionPreview
{
    /// <summary>Gets the absolute meeting report path.</summary>
    public required string MeetingPath { get; init; }

    /// <summary>Gets the meeting date used for created Decision Records.</summary>
    public required DateOnly MeetingDate { get; init; }

    /// <summary>Gets detected actions.</summary>
    public IReadOnlyList<MeetingActionExtractionCandidate> Actions { get; init; } = [];

    /// <summary>Gets detected decisions.</summary>
    public IReadOnlyList<MeetingDecisionExtractionCandidate> Decisions { get; init; } = [];
}

/// <summary>
/// User-approved selection to materialize from an extraction preview.
/// </summary>
public sealed record MeetingExtractionRequest
{
    /// <summary>Gets action task identifiers selected for promotion.</summary>
    public IReadOnlySet<Guid> ActionIds { get; init; } = new HashSet<Guid>();

    /// <summary>Gets decision candidate identifiers selected for creation.</summary>
    public IReadOnlySet<Guid> DecisionIds { get; init; } = new HashSet<Guid>();
}

/// <summary>
/// Result of a user-approved meeting extraction.
/// </summary>
public sealed record MeetingExtractionResult
{
    /// <summary>Gets the number of newly promoted actions.</summary>
    public int PromotedActionCount { get; init; }

    /// <summary>Gets the number of newly created Decision Records.</summary>
    public int CreatedDecisionCount { get; init; }

    /// <summary>Gets the number of selected candidates skipped as duplicates.</summary>
    public int SkippedDuplicateCount { get; init; }
}
