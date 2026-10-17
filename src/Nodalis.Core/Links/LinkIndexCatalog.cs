namespace Nodalis.Core.Links;

public sealed record LinkIndexCatalog
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<LinkTargetEntry> Targets { get; init; } = [];

    public List<LinkReferenceEntry> References { get; init; } = [];

    public List<global::Nodalis.Core.Relations.TypedRelationEntry> Relations { get; init; } = [];
}
