namespace Nodalis.Core.Domain;

public sealed record ApplicationManifest
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
