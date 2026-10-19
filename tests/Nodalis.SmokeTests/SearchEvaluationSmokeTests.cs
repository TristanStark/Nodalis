using Nodalis.Core.Search;

/// <summary>Reproducible macro-relevance and latency tests for the 1.8 benchmark gate.</summary>
internal static class SearchEvaluationSmokeTests
{
    /// <summary>Verifies P@5/P@10, R@10, deduplication and observed p50/p95.</summary>
    public static void Run()
    {
        Guid a = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        Guid b = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        Guid c = Guid.Parse("00000000-0000-0000-0000-00000000000c");
        SearchEvaluationSummary summary = SearchEvaluationMetrics.Evaluate(
        [
            new SearchEvaluationCase("inscription", [a, b], [a, c, a, b], 10),
            new SearchEvaluationCase("mouvement", [b], [c], 30)
        ]);
        Assert(summary.QueryCount == 2 &&
            Math.Abs(summary.PrecisionAt5 - 0.2) < 0.000001 &&
            Math.Abs(summary.PrecisionAt10 - 0.1) < 0.000001 &&
            Math.Abs(summary.RecallAt10 - 0.5) < 0.000001 &&
            summary.P50Milliseconds == 10 && summary.P95Milliseconds == 30,
            "Metrics must use macro P@k/R@k and measured nearest-rank latency.");
        Assert(SearchEvaluationMetrics.Evaluate([]).QueryCount == 0 &&
            SearchEvaluationMetrics.Evaluate([
                new SearchEvaluationCase("unlabeled", [], [a], 100)
            ]).QueryCount == 0,
            "Unlabeled cases must not produce imaginary relevance scores.");
        Console.WriteLine("[Semantic 1.8] macro P@5/P@10/R@10 and p50/p95 passed.");
    }

    /// <summary>Reports inconsistent evaluation arithmetic.</summary>
    /// <param name="ok">Assertion result.</param>
    /// <param name="reason">Failure explanation.</param>
    private static void Assert(bool ok, string reason)
    {
        if (!ok) throw new InvalidOperationException(reason);
    }
}
