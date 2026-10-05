namespace Nodalis.Core.Settings;

/// <summary>
/// Stores a local bookmark to a precise Markdown heading inside a workspace document.
/// </summary>
public sealed record DocumentBookmarkReference
{
    /// <summary>
    /// Gets the stable local bookmark identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the stable document navigation identifier.
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Gets the workspace-relative document path used as a readable fallback after moves or repairs.
    /// </summary>
    public required string DocumentRelativePath { get; init; }

    /// <summary>
    /// Gets the user-defined bookmark name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the saved Markdown heading level.
    /// </summary>
    public required int HeadingLevel { get; init; }

    /// <summary>
    /// Gets the saved Markdown heading title.
    /// </summary>
    public required string HeadingTitle { get; init; }

    /// <summary>
    /// Gets the zero-based occurrence among headings sharing the same level and title.
    /// </summary>
    public int HeadingOccurrence { get; init; }

    /// <summary>
    /// Gets the original source offset used to disambiguate duplicate headings after normal edits.
    /// </summary>
    public int OriginalOffset { get; init; }

    /// <summary>
    /// Gets the original one-based line number shown when a bookmark becomes unresolved.
    /// </summary>
    public int OriginalLineNumber { get; init; }
}
