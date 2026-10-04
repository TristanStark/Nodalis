namespace Nodalis.Core.Domain;

public sealed record ModuleManifest
{
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    public Guid? ParentModuleId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
