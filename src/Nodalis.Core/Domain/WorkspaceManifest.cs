namespace Nodalis.Core.Domain;

public sealed record WorkspaceManifest
{
    public const int CurrentSchemaVersion = 1;

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
