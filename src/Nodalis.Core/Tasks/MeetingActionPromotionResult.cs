namespace Nodalis.Core.Tasks;

/// <summary>
/// Describes the project task produced when a meeting action is promoted to project tracking.
/// </summary>
public sealed record MeetingActionPromotionResult
{
    /// <summary>
    /// Gets the deterministic identifier shared by the meeting action and the promoted task.
    /// </summary>
    public required Guid PromotionId { get; init; }

    /// <summary>
    /// Gets the promoted task document path relative to the workspace root.
    /// </summary>
    public required string ProjectTaskRelativePath { get; init; }

    /// <summary>
    /// Gets the one-based line number of the promoted checkbox in the project task document.
    /// </summary>
    public required int ProjectTaskLineNumber { get; init; }

    /// <summary>
    /// Gets a value indicating whether this call created a new promoted task rather than updating an existing link.
    /// </summary>
    public bool Created { get; init; }
}
