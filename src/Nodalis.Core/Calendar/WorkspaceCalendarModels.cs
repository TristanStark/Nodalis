namespace Nodalis.Core.Calendar;

/// <summary>
/// Identifies the source kind of one calendar event.
/// </summary>
public enum CalendarEventKind
{
    Task,
    Milestone,
    Meeting
}

/// <summary>
/// Represents one dated workspace item without introducing secondary calendar storage.
/// </summary>
public sealed record CalendarEventItem
{
    /// <summary>Gets the source event kind.</summary>
    public required CalendarEventKind Kind { get; init; }

    /// <summary>Gets the event date.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Gets the human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the workspace-relative source path.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Gets the optional one-based source line.</summary>
    public int? LineNumber { get; init; }

    /// <summary>Gets the optional application identifier.</summary>
    public Guid? ApplicationId { get; init; }

    /// <summary>Gets the optional application name.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>Gets the optional project identifier.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Gets the optional project name.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Gets whether the date can be rewritten through an existing explicit source field.</summary>
    public bool CanReschedule { get; init; }

    /// <summary>Gets a localized type label.</summary>
    public string KindLabel =>
        Kind switch
        {
            CalendarEventKind.Task => "Tâche",
            CalendarEventKind.Milestone => "Jalon",
            _ => "Réunion"
        };

    /// <summary>Gets a compact scope label.</summary>
    public string ScopeLabel =>
        !string.IsNullOrWhiteSpace(
            ProjectName)
            ? ProjectName!
            : !string.IsNullOrWhiteSpace(
                ApplicationName)
                ? ApplicationName!
                : "Workspace";

    /// <summary>Gets a stable key for source relocation during safe rescheduling.</summary>
    public string SourceKey =>
        Kind +
        "|" +
        SourceRelativePath +
        "|" +
        (LineNumber?.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty) +
        "|" +
        Title;
}

/// <summary>
/// Contains all currently derived calendar items.
/// </summary>
public sealed record WorkspaceCalendarSnapshot
{
    /// <summary>Gets the generation timestamp.</summary>
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets all dated source items.</summary>
    public IReadOnlyList<CalendarEventItem> Events { get; init; } = [];
}
