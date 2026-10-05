namespace Nodalis.App.Navigation;

/// <summary>
/// Represents one in-memory navigation target exposed by Quick Open.
/// </summary>
public sealed record QuickOpenEntry
{
    /// <summary>
    /// Gets the stable navigation identifier.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>
    /// Gets the absolute target path.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Gets the visible target name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the localized target kind.
    /// </summary>
    public required string KindLabel { get; init; }

    /// <summary>
    /// Gets the workspace-relative qualified path used to disambiguate names.
    /// </summary>
    public required string QualifiedPath { get; init; }

    /// <summary>
    /// Gets whether the target is currently a favorite.
    /// </summary>
    public bool IsFavorite { get; init; }

    /// <summary>
    /// Gets the optional favorite marker displayed beside the name.
    /// </summary>
    public string FavoriteMarker =>
        IsFavorite
            ? "  ★"
            : string.Empty;

    /// <summary>
    /// Gets the recent-item rank, or -1 when the target is not recent.
    /// </summary>
    public int RecentRank { get; init; } = -1;
}
