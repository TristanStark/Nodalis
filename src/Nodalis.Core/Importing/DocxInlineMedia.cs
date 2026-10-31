namespace Nodalis.Core.Importing;

/// <summary>
/// A referenced Word image or a visible diagnostic for an unsupported embedded object.
/// Never contains executable content or a remote URL.
/// </summary>
public sealed record DocxInlineMedia
{
    /// <summary>Relationship identifier or an unsupported object's category.</summary>
    public required string SourceId { get; init; }

    /// <summary>Character position in the original paragraph text before Markdown conversion.</summary>
    public int TextOffset { get; init; }

    /// <summary>User-provided alternative description, not interpreted as markup.</summary>
    public string AlternativeText { get; init; } = string.Empty;

    /// <summary>Original archive entry path, only for approved local images.</summary>
    public string? ArchiveEntryPath { get; init; }

    /// <summary>Full SHA256 of the image's original bytes.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Safe Markdown reference relative to a project's section folder.</summary>
    public string? MarkdownRelativePath { get; init; }

    /// <summary>Explicit message for images or Word objects that cannot be rendered.</summary>
    public string? Warning { get; init; }

    /// <summary>Whether this media can be extracted as a verified local image.</summary>
    public bool IsSupported => ArchiveEntryPath is not null &&
                               Sha256 is not null &&
                               MarkdownRelativePath is not null;
}
