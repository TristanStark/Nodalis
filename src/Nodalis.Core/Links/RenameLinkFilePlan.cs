namespace Nodalis.Core.Links;

/// <summary>
/// Groups proposed textual link rewrites for one stable source document.
/// </summary>
public sealed record RenameLinkFilePlan
{
    /// <summary>
    /// Gets the stable source document identifier.
    /// </summary>
    public required Guid SourceId { get; init; }

    /// <summary>
    /// Gets the source display name captured for preview.
    /// </summary>
    public required string SourceDisplayName { get; init; }

    /// <summary>
    /// Gets the source path captured for preview.
    /// </summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>
    /// Gets the file content hash captured when the plan was built.
    /// </summary>
    public required string SourceContentHash { get; init; }

    /// <summary>
    /// Gets the proposed changed lines.
    /// </summary>
    public List<RenameLinkChange> Changes { get; init; } = [];
}
