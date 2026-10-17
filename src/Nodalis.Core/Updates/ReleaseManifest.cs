namespace Nodalis.Core.Updates;

public sealed record ReleaseManifest
{
    public const int CurrentManifestSchemaVersion = 1;

    public int ManifestSchemaVersion { get; init; } =
        CurrentManifestSchemaVersion;

    public required string Product { get; init; }

    public required string Version { get; init; }

    public required string TargetRid { get; init; }

    public int MinimumWorkspaceSchemaVersion { get; init; }

    public int MaximumWorkspaceSchemaVersion { get; init; }

    public List<int> MigratableWorkspaceSchemaVersions { get; init; } = [];

    public required string PayloadDirectory { get; init; }

    public List<ReleaseFileEntry> Files { get; init; } = [];
}
