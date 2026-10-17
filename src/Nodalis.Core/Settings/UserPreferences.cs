namespace Nodalis.Core.Settings;

public sealed record UserPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? WorkspaceRootPath { get; init; }

    public HashSet<Guid> ExpandedNodeIds { get; init; } = [];

    public bool IsContextPanelOpen { get; init; } = true;

    public double NavigationPanelWidth { get; init; } = 280;

    /// <summary>Worktree row density: Comfortable (default) or Compact.</summary>
    public string NavigationDensity { get; init; } = "Comfortable";

    /// <summary>Only show the selected application's subtree when possible.</summary>
    public bool NavigationCurrentApplicationOnly { get; init; }

    /// <summary>Keep favorite shortcuts expanded in the explorer.</summary>
    public bool NavigationFavoritesExpanded { get; init; }

    /// <summary>Keep recent document shortcuts expanded in the explorer.</summary>
    public bool NavigationRecentsExpanded { get; init; }

    public double ContextPanelWidth { get; init; } = 300;

    public EditorPreferences Editor { get; init; } = new();

    public BackupPreferences Backup { get; init; } = new();

    public AiPreferences Ai { get; init; } = new();

    public List<RecentItemReference> RecentItems { get; init; } = [];

    public List<RecentSearchReference> RecentSearches { get; init; } = [];

    public List<UserItemReference> Favorites { get; init; } = [];

    public List<DocumentBookmarkReference> Bookmarks { get; init; } = [];

    public List<OpenDocumentTabReference> OpenDocumentTabs { get; init; } = [];

    public Guid? ActiveDocumentTabId { get; init; }

    /// <summary>Last selected worktree node; optional and local to this PC.</summary>
    public Guid? ActiveNavigationNodeId { get; init; }

    public Dictionary<string, string> ShortcutOverrides { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static UserPreferences Default => new();
}
