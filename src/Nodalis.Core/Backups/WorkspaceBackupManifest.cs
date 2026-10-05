namespace Nodalis.Core.Backups;

public sealed record WorkspaceBackupManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid WorkspaceId { get; init; }

    public required string WorkspaceName { get; init; }

    public required int WorkspaceSchemaVersion { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required int FileCount { get; init; }
}
