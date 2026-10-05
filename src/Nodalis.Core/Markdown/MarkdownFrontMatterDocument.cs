namespace Nodalis.Core.Markdown;

/// <summary>
/// Represents an optional portable front matter block and the untouched Markdown body.
/// </summary>
public sealed record MarkdownFrontMatterDocument
{
    /// <summary>
    /// Gets the parsed front matter properties using case-insensitive keys.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the Markdown content located after the front matter block.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// Gets a value indicating whether a complete front matter block was detected.
    /// </summary>
    public bool HasFrontMatter { get; init; }

    /// <summary>
    /// Gets the newline sequence detected from the source document.
    /// </summary>
    public required string NewLine { get; init; }
}
