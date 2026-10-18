namespace Nodalis.Core.Semantic;

/// <summary>A derived vector without copied Markdown content.</summary>
public sealed record SemanticVectorEntry
{
    /// <summary>Stable canonical document identity.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Stable source chunk identity.</summary>
    public string ChunkId { get; init; } = "";

    /// <summary>Source text fingerprint for targeted invalidation.</summary>
    public string ContentHash { get; init; } = "";

    /// <summary>Workspace relative Markdown document location.</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>Optional source heading, not duplicated source text.</summary>
    public string? Heading { get; init; }

    /// <summary>One-based source position.</summary>
    public int SourceLine { get; init; }

    /// <summary>Embedding vector from the selected local model.</summary>
    public float[] Vector { get; init; } = [];
}

/// <summary>Disposable, versioned local vector index; never a canonical business file.</summary>
public sealed record SemanticVectorIndex
{
    /// <summary>Index format version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Index format discriminator.</summary>
    public int SchemaVersion { get; init; } = CurrentVersion;

    /// <summary>Exact configured embedding model.</summary>
    public string ModelId { get; init; } = "";

    /// <summary>Validated dimensions.</summary>
    public int Dimensions { get; init; }

    /// <summary>Chunker settings fingerprint.</summary>
    public string SegmentationKey { get; init; } = "";

    /// <summary>Derived vectors, omitting all full source text.</summary>
    public List<SemanticVectorEntry> Entries { get; init; } = [];
}

