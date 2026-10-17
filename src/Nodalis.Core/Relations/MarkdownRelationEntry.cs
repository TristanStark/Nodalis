namespace Nodalis.Core.Relations;

/// <summary>
/// Represents one readable typed relation parsed from a Markdown document.
/// </summary>
public sealed record MarkdownRelationEntry
{
    /// <summary>
    /// Gets the relation type exactly as written by the user.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// Gets the stable target identifier when the ID is syntactically valid.
    /// </summary>
    public Guid? TargetId { get; init; }

    /// <summary>
    /// Gets the raw target ID text so malformed relations remain inspectable.
    /// </summary>
    public required string RawTargetId { get; init; }

    /// <summary>
    /// Gets the optional human-readable target label stored beside the stable ID.
    /// </summary>
    public string? TargetLabel { get; init; }

    /// <summary>
    /// Gets the one-based Markdown source line.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the original relation line.
    /// </summary>
    public required string RawLine { get; init; }
}
