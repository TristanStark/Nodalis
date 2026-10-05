namespace Nodalis.Core.Links;

/// <summary>
/// Represents one incoming internal link or typed relation for reference exploration.
/// </summary>
public sealed record ReferenceSearchEntry
{
    /// <summary>
    /// Gets the indexed source element.
    /// </summary>
    public required LinkTargetEntry Source { get; init; }

    /// <summary>
    /// Gets the one-based source line.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the readable source excerpt.
    /// </summary>
    public required string Excerpt { get; init; }

    /// <summary>
    /// Gets the display type used by filtering.
    /// </summary>
    public required string TypeLabel { get; init; }

    /// <summary>
    /// Gets the relation type when this entry originated from a typed relation.
    /// </summary>
    public string? RelationType { get; init; }

    /// <summary>
    /// Gets whether the source and target belong to the same indexed scope.
    /// </summary>
    public bool IsSameScope { get; init; }

    /// <summary>
    /// Gets whether the reference is a typed relation instead of a normal internal link.
    /// </summary>
    public bool IsTypedRelation =>
        !string.IsNullOrWhiteSpace(
            RelationType);
}
