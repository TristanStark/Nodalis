namespace Nodalis.App.Dashboard;

/// <summary>
/// Represents one lightweight consistency alert displayed by the project dashboard.
/// </summary>
public sealed record ProjectDashboardAlertViewModel
{
    /// <summary>
    /// Gets the short alert category.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets the human-readable alert message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the workspace-relative source path used for navigation.
    /// </summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>
    /// Gets the one-based source line when one is known.
    /// </summary>
    public int? LineNumber { get; init; }
}
