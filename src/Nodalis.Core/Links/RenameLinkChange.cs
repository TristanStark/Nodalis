namespace Nodalis.Core.Links;

/// <summary>
/// Describes one textual internal-link rewrite shown in a rename preview.
/// </summary>
public sealed record RenameLinkChange
{
    /// <summary>
    /// Gets the one-based source line.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the source line before rewriting.
    /// </summary>
    public required string Before { get; init; }

    /// <summary>
    /// Gets the source line after rewriting.
    /// </summary>
    public required string After { get; init; }
}
