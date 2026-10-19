namespace Nodalis.Core.Importing;

/// <summary>
/// Classifies source elements without depending on any vendor document format.
/// </summary>
public enum ImportDocumentNodeKind
{
    Section,
    Paragraph,
    ListItem,
    Table,
    Image,
    Bookmark,
    Unsupported
}

/// <summary>
/// An ordered source element. Identity is stable within a parsed source snapshot.
/// </summary>
public sealed record ImportDocumentNode
{
    public required string SourceId { get; init; }

    public required ImportDocumentNodeKind Kind { get; init; }

    public required int SourceOrder { get; init; }

    public string Text { get; init; } = string.Empty;

    public int? HeadingLevel { get; init; }

    public int? SuggestedHeadingLevel { get; init; }

    public string? HeadingSuggestionReason { get; init; }

    public string? SourceStyle { get; init; }

    public List<ImportDocumentNode> Children { get; init; } = [];
}

/// <summary>
/// Format-neutral representation used by the advanced import assistant.
/// </summary>
public sealed record ImportDocumentTree
{
    public required string FormatId { get; init; }

    public required string SourceFileName { get; init; }

    public required string SourceSha256 { get; init; }

    public List<ImportDocumentNode> Roots { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Contract implemented by first-party source adapters; it has no workspace side effects.
/// </summary>
public interface IImportDocumentReader
{
    /// <summary>
    /// Gets the unique identifier of the reader's source format.
    /// </summary>
    string FormatId { get; }

    /// <summary>
    /// Reads source content into the same ordered document model.
    /// </summary>
    /// <param name="sourcePath">Existing source file path.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The normalized, read-only-in-practice source tree.</returns>
    Task<ImportDocumentTree> ReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An explicit inclusion/exclusion and destination override.
/// Unmapped content is never included automatically.
/// </summary>
public sealed record ImportSelectionRule
{
    public required string SourceId { get; init; }

    public required bool Include { get; init; }

    public string? DestinationRelativePath { get; init; }

    public string? TargetSection { get; init; }
}

/// <summary>
/// One source fragment's projected destination; no file has been written.
/// </summary>
public sealed record ImportPlannedFragment
{
    public required string SourceId { get; init; }

    public required ImportDocumentNodeKind Kind { get; init; }

    public required int SourceOrder { get; init; }

    public required string DestinationRelativePath { get; init; }

    public string? TargetSection { get; init; }

    public required string Text { get; init; }
}

/// <summary>
/// Portable, versioned dry-run input for the later preview/transaction stages.
/// </summary>
public sealed record ImportDocumentPlan
{
    public int SchemaVersion { get; init; } = 1;

    public required string FormatId { get; init; }

    public required string SourceFileName { get; init; }

    public required string SourceSha256 { get; init; }

    public List<ImportSelectionRule> Selections { get; init; } = [];

    public List<ImportPlannedFragment> Fragments { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}
