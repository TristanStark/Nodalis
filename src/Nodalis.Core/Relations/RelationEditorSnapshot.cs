namespace Nodalis.Core.Relations;

/// <summary>
/// Immutable user-approval boundary for inserting a typed relation in Markdown.
/// It is independent of WPF and leaves filesystem revision checks to the caller.
/// </summary>
/// <param name="DocumentId">Stable source document ID.</param>
/// <param name="DocumentPath">Canonical absolute source path.</param>
/// <param name="Markdown">Exact editor text before choosing a target.</param>
/// <param name="SelectionStart">Approved insertion/selection start.</param>
/// <param name="SelectionLength">Approved selected text length.</param>
public sealed record RelationEditorSnapshot(
    Guid DocumentId,
    string DocumentPath,
    string Markdown,
    int SelectionStart,
    int SelectionLength)
{
    /// <summary>Captures the source revision prior to showing a modal target picker.</summary>
    /// <param name="documentId">Stable document ID.</param>
    /// <param name="documentPath">Original full source path.</param>
    /// <param name="markdown">Unmodified source editor text.</param>
    /// <param name="selectionStart">Current selection start.</param>
    /// <param name="selectionLength">Current selection length.</param>
    /// <returns>Immutable, self-contained approval snapshot.</returns>
    public static RelationEditorSnapshot Capture(
        Guid documentId,
        string documentPath,
        string markdown,
        int selectionStart,
        int selectionLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(markdown);

        if (documentId == Guid.Empty ||
            selectionStart < 0 ||
            selectionLength < 0 ||
            selectionStart > markdown.Length ||
            selectionLength > markdown.Length - selectionStart)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionStart), "Invalid relation source or editor selection.");
        }

        return new RelationEditorSnapshot(
            documentId,
            Path.GetFullPath(documentPath),
            markdown,
            selectionStart,
            selectionLength);
    }

    /// <summary>
    /// Rejects source changes, tab switches, caret movements and selection
    /// changes between showing a dialog and committing the approved relation.
    /// </summary>
    /// <param name="documentId">Current stable document ID.</param>
    /// <param name="documentPath">Current document path.</param>
    /// <param name="markdown">Current editor source text.</param>
    /// <param name="selectionStart">Current selection start.</param>
    /// <param name="selectionLength">Current selection length.</param>
    /// <returns>True if the entire approved in-memory revision still matches.</returns>
    public bool Matches(
        Guid documentId,
        string documentPath,
        string markdown,
        int selectionStart,
        int selectionLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(markdown);

        return documentId == DocumentId &&
            string.Equals(Path.GetFullPath(documentPath), DocumentPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(markdown, Markdown, StringComparison.Ordinal) &&
            selectionStart == SelectionStart &&
            selectionLength == SelectionLength;
    }
}
