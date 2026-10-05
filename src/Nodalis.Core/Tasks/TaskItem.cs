namespace Nodalis.Core.Tasks;

/// <summary>
/// Represents one Markdown checkbox task discovered in the local workspace.
/// </summary>
public sealed record TaskItem
{
    /// <summary>
    /// Gets the derived stable identifier for this task occurrence.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the identifier of the source Markdown document.
    /// </summary>
    public required Guid SourceDocumentId { get; init; }

    /// <summary>
    /// Gets the source path relative to the workspace root.
    /// </summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>
    /// Gets the one-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the complete source line used for safe relocation before edits.
    /// </summary>
    public required string RawLine { get; init; }

    /// <summary>
    /// Gets the human-readable task text without metadata segments.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Markdown checkbox is checked.
    /// </summary>
    public bool IsCompleted { get; init; }

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
    /// Gets normalized task tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets a comma-separated tag label suitable for compact UI rendering.
    /// </summary>
    public string TagsDisplay =>
        string.Join(
            ", ",
            Tags);

    /// <summary>
    /// Gets a value indicating whether the open task is past its due date.
    /// </summary>
    public bool IsOverdue =>
        !IsCompleted &&
        DueDate is DateOnly dueDate &&
        dueDate <
        DateOnly.FromDateTime(
            DateTime.Today);

    /// <summary>
    /// Gets the compact overdue marker used by task views.
    /// </summary>
    public string OverdueDisplay =>
        IsOverdue
            ? "EN RETARD"
            : string.Empty;

    /// <summary>
    /// Gets the containing application identifier when one exists.
    /// </summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>
    /// Gets the containing application name when one exists.
    /// </summary>
    public string? ApplicationName { get; init; }

    /// <summary>
    /// Gets the containing project identifier when one exists.
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// Gets the containing project name when one exists.
    /// </summary>
    public string? ProjectName { get; init; }
}
