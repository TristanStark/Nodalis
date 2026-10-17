namespace Nodalis.Core.Links;

/// <summary>
/// Represents an opt-in rewrite plan produced before renaming an indexed element.
/// </summary>
public sealed record RenameLinkRewritePlan
{
    /// <summary>
    /// Gets the stable renamed target identifier.
    /// </summary>
    public required Guid TargetId { get; init; }

    /// <summary>
    /// Gets the previous human-readable target name.
    /// </summary>
    public required string OldDisplayName { get; init; }

    /// <summary>
    /// Gets the requested new human-readable target name.
    /// </summary>
    public required string NewDisplayName { get; init; }

    /// <summary>
    /// Gets the affected source files.
    /// </summary>
    public List<RenameLinkFilePlan> Files { get; init; } = [];
}
