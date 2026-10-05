namespace Nodalis.Core.Settings;

public sealed record UserPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? WorkspaceRootPath { get; init; }

    public HashSet<Guid> ExpandedNodeIds { get; init; } = [];

    public bool IsContextPanelOpen { get; init; } = true;

    public double NavigationPanelWidth { get; init; } = 280;

    public double ContextPanelWidth { get; init; } = 300;

    public EditorPreferences Editor { get; init; } = new();

    public BackupPreferences Backup { get; init; } = new();

    public List<RecentItemReference> RecentItems { get; init; } = [];

    public List<UserItemReference> Favorites { get; init; } = [];

    public Dictionary<string, string> ShortcutOverrides { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static UserPreferences Default => new();
}
