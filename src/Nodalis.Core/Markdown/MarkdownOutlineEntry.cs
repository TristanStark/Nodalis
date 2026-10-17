namespace Nodalis.Core.Markdown;

/// <summary>
/// Describes one Markdown ATX heading at its exact position in the source document.
/// </summary>
/// <param name="Level">Heading depth from 1 to 6.</param>
/// <param name="Title">Visible heading text with optional closing markers removed.</param>
/// <param name="Offset">Zero-based character offset of the heading line in the original Markdown source.</param>
/// <param name="LineNumber">One-based source line number.</param>
public sealed record MarkdownOutlineEntry(
    int Level,
    string Title,
    int Offset,
    int LineNumber);
