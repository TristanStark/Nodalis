namespace Nodalis.App.Dashboard;

/// <summary>
/// Represents one navigable document displayed by the project dashboard.
/// </summary>
public sealed record ProjectDashboardDocumentViewModel
{
    /// <summary>
    /// Gets the indexed document identifier used for navigation.
    /// </summary>
    public required Guid TargetId { get; init; }

    /// <summary>
    /// Gets the document display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the secondary description rendered below the document name.
    /// </summary>
    public required string Detail { get; init; }

    /// <summary>
    /// Gets the workspace-relative source path.
    /// </summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>
    /// Gets the optional local timestamp shown for recently modified documents.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }
}
