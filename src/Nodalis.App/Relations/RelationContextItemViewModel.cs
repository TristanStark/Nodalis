namespace Nodalis.App.Relations;

/// <summary>
/// Represents one incoming or outgoing typed relation in the context panel.
/// </summary>
public sealed record RelationContextItemViewModel
{
    /// <summary>
    /// Gets the relation type as authored in Markdown.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// Gets the arrow indicating whether the relation is incoming or outgoing.
    /// </summary>
    public required string Direction { get; init; }

    /// <summary>
    /// Gets the related element display name or a broken-target label.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets contextual information displayed below the relation.
    /// </summary>
    public required string Detail { get; init; }

    /// <summary>
    /// Gets the target used when navigating from the relation row.
    /// </summary>
    public Guid? NavigationTargetId { get; init; }

    /// <summary>
    /// Gets the source line used when navigating to an incoming relation.
    /// </summary>
    public int? NavigationLineNumber { get; init; }

    /// <summary>
    /// Gets whether the relation currently references a missing or malformed target.
    /// </summary>
    public bool IsBroken { get; init; }
}
