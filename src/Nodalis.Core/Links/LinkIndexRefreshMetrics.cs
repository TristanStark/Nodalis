namespace Nodalis.Core.Links;

/// <summary>
/// Exposes deterministic work counters for one link-index refresh.
/// </summary>
public sealed record LinkIndexRefreshMetrics
{
    public required int DocumentCount { get; init; }

    public required int ChangedDocuments { get; init; }

    public required int HashedDocuments { get; init; }

    public required int ParsedDocuments { get; init; }

    public required int ReusedDocuments { get; init; }

    public required long ElapsedMilliseconds { get; init; }
}

/// <summary>
/// Returns both the refreshed catalog and transparent performance counters.
/// </summary>
public sealed record LinkIndexRefreshResult
{
    public required LinkIndexCatalog Catalog { get; init; }

    public required LinkIndexRefreshMetrics Metrics { get; init; }
}
