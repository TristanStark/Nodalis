namespace Nodalis.Core.Tasks;

/// <summary>
/// Describes editable metadata attached to a Markdown checkbox task.
/// </summary>
public sealed record TaskMetadataUpdate
{
    /// <summary>
    /// Gets the optional textual owner.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Gets the optional due date.
    /// </summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// Gets the optional lightweight priority label.
    /// </summary>
    public string? Priority { get; init; }

    /// <summary>
    /// Gets the optional lightweight status label.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Gets the optional task tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
