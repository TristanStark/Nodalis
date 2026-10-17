namespace Nodalis.Core.Relations;

/// <summary>
/// Represents one relation indexed from a source document toward a stable workspace target.
/// </summary>
public sealed record TypedRelationEntry
{
    /// <summary>
    /// Gets the stable source document identifier.
    /// </summary>
    public required Guid SourceId { get; init; }

    /// <summary>
    /// Gets the stable target identifier when it could be parsed.
    /// </summary>
    public Guid? TargetId { get; init; }

    /// <summary>
    /// Gets the raw target identifier stored in Markdown.
    /// </summary>
    public required string RawTargetId { get; init; }

    /// <summary>
    /// Gets the relation type. Unknown user-defined values are preserved.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// Gets the optional human-readable target label stored in Markdown.
    /// </summary>
    public string? TargetLabel { get; init; }

    /// <summary>
    /// Gets the one-based source line.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets a compact readable excerpt of the source relation.
    /// </summary>
    public required string Excerpt { get; init; }
}
