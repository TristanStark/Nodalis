namespace Nodalis.Core.Links;

public sealed record LinkIndexCatalog
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<LinkTargetEntry> Targets { get; init; } = [];

    public List<LinkReferenceEntry> References { get; init; } = [];

    public List<global::Nodalis.Core.Relations.TypedRelationEntry> Relations { get; init; } = [];

    /// <summary>Derived stable heading identities, rebuilt from canonical Markdown.</summary>
    public List<IndexedMarkdownBlock> Blocks { get; init; } = [];

    /// <summary>Invalid or duplicated block markers that require manual review.</summary>
    public List<IndexedMarkdownBlockIssue> BlockIssues { get; init; } = [];
}
