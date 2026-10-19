namespace Nodalis.Core.Search;

/// <summary>One deterministic, annotated query for measuring search relevance.</summary>
/// <param name="Name">Stable fixture or query identifier, never private note content.</param>
/// <param name="RelevantDocuments">Ground-truth canonical document identifiers.</param>
/// <param name="RankedDocuments">Best-first document GUIDs returned by a search mode.</param>
/// <param name="ElapsedMilliseconds">Observed query duration in milliseconds.</param>
public sealed record SearchEvaluationCase(
    string Name, IReadOnlyCollection<Guid> RelevantDocuments,
    IReadOnlyList<Guid> RankedDocuments, long ElapsedMilliseconds);

/// <summary>Macro-averaged relevance and measured latency percentiles.</summary>
/// <param name="QueryCount">Number of queries with at least one known relevant source.</param>
/// <param name="PrecisionAt5">Mean hits within top five divided by five.</param>
/// <param name="PrecisionAt10">Mean hits within top ten divided by ten.</param>
/// <param name="RecallAt10">Mean fraction of relevant documents found in top ten.</param>
/// <param name="P50Milliseconds">Median end-to-end elapsed milliseconds.</param>
/// <param name="P95Milliseconds">Nearest-rank 95th percentile elapsed milliseconds.</param>
public sealed record SearchEvaluationSummary(int QueryCount, double PrecisionAt5,
    double PrecisionAt10, double RecallAt10, long P50Milliseconds, long P95Milliseconds);

/// <summary>Measures lexical, vector and hybrid results with identical reproducible rules.</summary>
public static class SearchEvaluationMetrics
{
    /// <summary>
    /// Scores only queries with known nonempty ground truth. Removes duplicate GUID
    /// hits to avoid inflated recall. Empty or invalid identifiers never count.
    /// </summary>
    /// <param name="cases">Annotated queries and measured result rankings.</param>
    /// <returns>Macro precision/recall and nearest-rank p50/p95 latencies.</returns>
    public static SearchEvaluationSummary Evaluate(IReadOnlyList<SearchEvaluationCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        List<SearchEvaluationCase> valid = cases
            .Where(item => item.RelevantDocuments is not null &&
                item.RankedDocuments is not null &&
                item.RelevantDocuments.Any(id => id != Guid.Empty))
            .ToList();
        if (valid.Count == 0)
            return new SearchEvaluationSummary(0, 0, 0, 0, 0, 0);

        double precision5 = 0;
        double precision10 = 0;
        double recall10 = 0;
        List<long> durations = [];
        foreach (SearchEvaluationCase sample in valid)
        {
            HashSet<Guid> relevant = sample.RelevantDocuments
                .Where(id => id != Guid.Empty).ToHashSet();
            Guid[] ranked = sample.RankedDocuments
                .Where(id => id != Guid.Empty).Distinct().Take(10).ToArray();
            int at5 = ranked.Take(5).Count(relevant.Contains);
            int at10 = ranked.Count(relevant.Contains);
            precision5 += at5 / 5.0;
            precision10 += at10 / 10.0;
            recall10 += (double)at10 / relevant.Count;
            durations.Add(Math.Max(0, sample.ElapsedMilliseconds));
        }

        durations.Sort();
        return new SearchEvaluationSummary(valid.Count,
            precision5 / valid.Count, precision10 / valid.Count,
            recall10 / valid.Count,
            Percentile(durations, 0.50), Percentile(durations, 0.95));
    }

    /// <summary>Uses the deterministic nearest-rank percentile on sorted latencies.</summary>
    /// <param name="sorted">Nonempty ascending list of measured durations.</param>
    /// <param name="fraction">Requested quantile between zero and one.</param>
    /// <returns>Observed duration without inventing interpolated latencies.</returns>
    private static long Percentile(IReadOnlyList<long> sorted, double fraction)
    {
        int rank = (int)Math.Ceiling(sorted.Count * fraction) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
