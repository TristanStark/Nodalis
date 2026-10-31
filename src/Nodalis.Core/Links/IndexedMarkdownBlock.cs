using Nodalis.Core.Markdown;

namespace Nodalis.Core.Links;

/// <summary>
/// Derived identity of a heading inside a Markdown document. The current
/// document location is resolved through the existing document identity index.
/// </summary>
public sealed record IndexedMarkdownBlock
{
    /// <summary>Stable GUID from the canonical Markdown comment.</summary>
    public required Guid Id { get; init; }

    /// <summary>Stable owning document ID from the workspace link index.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Current heading label, not part of the identity.</summary>
    public required string HeadingTitle { get; init; }

    /// <summary>Current ATX heading level.</summary>
    public required int HeadingLevel { get; init; }

    /// <summary>Current one-based Markdown line number.</summary>
    public required int HeadingLine { get; init; }

    /// <summary>Current zero-based character offset of the heading line.</summary>
    public required int HeadingOffset { get; init; }
}

/// <summary>
/// Persistent derived diagnostic for one malformed or repeated block marker.
/// </summary>
public sealed record IndexedMarkdownBlockIssue
{
    /// <summary>Document containing the problematic marker.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Classification of the source diagnostic.</summary>
    public required MarkdownBlockIdentityIssueKind Kind { get; init; }

    /// <summary>Parsed ID, when a syntactically valid ID was repeated.</summary>
    public Guid? Id { get; init; }

    /// <summary>One-based line of the owning heading.</summary>
    public required int HeadingLine { get; init; }

    /// <summary>One-based line of the suspicious marker.</summary>
    public required int MarkerLine { get; init; }
}

/// <summary>
/// Resolution of a block ID against one complete derived workspace index.
/// </summary>
public sealed record MarkdownBlockIndexResolution
{
    /// <summary>Resolved, missing or ambiguous (never arbitrary first-match).</summary>
    public required LinkResolutionStatus Status { get; init; }

    /// <summary>Unique block only when the status is Resolved.</summary>
    public IndexedMarkdownBlock? Block { get; init; }

    /// <summary>Current document target of a uniquely resolved block.</summary>
    public LinkTargetEntry? Document { get; init; }

    /// <summary>All block candidates; kept for conflict diagnostics.</summary>
    public IReadOnlyList<IndexedMarkdownBlock> Candidates { get; init; } = [];
}
