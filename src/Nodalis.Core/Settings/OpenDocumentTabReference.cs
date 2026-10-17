namespace Nodalis.Core.Settings;

/// <summary>
/// Describes one document tab that should be restored for a workspace session.
/// </summary>
public sealed record OpenDocumentTabReference
{
    /// <summary>
    /// Gets the stable document identifier when one is available in navigation.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Gets the document path relative to the workspace root.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Gets the caret position to restore.
    /// </summary>
    public int CaretIndex { get; init; }

    /// <summary>
    /// Gets the selection start position to restore.
    /// </summary>
    public int SelectionStart { get; init; }

    /// <summary>
    /// Gets the selection length to restore.
    /// </summary>
    public int SelectionLength { get; init; }
}
