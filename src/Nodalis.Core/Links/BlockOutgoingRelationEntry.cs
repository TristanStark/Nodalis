namespace Nodalis.Core.Links;

/// <summary>One outgoing relation with an explicit target resolution.</summary>
public sealed record BlockOutgoingRelationEntry
{
    /// <summary>Relation type exactly as written in the source.</summary>
    public required string RelationType { get; init; }

    /// <summary>One-based line of the relation in its source document.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Parsed target GUID, or null for a malformed identifier.</summary>
    public Guid? TargetId { get; init; }

    /// <summary>Missing, ambiguous or uniquely resolved destination.</summary>
    public required LinkResolutionStatus TargetStatus { get; init; }

    /// <summary>Unique target block, when the relation targets a block.</summary>
    public IndexedMarkdownBlock? TargetBlock { get; init; }

    /// <summary>Destination document or other workspace target, when unique.</summary>
    public LinkTargetEntry? TargetEntity { get; init; }

    /// <summary>Best available readable target label.</summary>
    public required string TargetLabel { get; init; }
}

/// <summary>Outgoing typed relations for a uniquely resolved source block.</summary>
/// <param name="Source">Identity resolution of the requested source block.</param>
/// <param name="Relations">Outgoing relation occurrences, including broken targets.</param>
public sealed record BlockOutgoingRelationResult(
    MarkdownBlockIndexResolution Source,
    IReadOnlyList<BlockOutgoingRelationEntry> Relations);
