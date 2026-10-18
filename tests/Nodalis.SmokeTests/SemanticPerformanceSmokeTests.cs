using System.Diagnostics;
using Nodalis.Core.Semantic;
using Nodalis.Core.Search;

/// <summary>Small/medium/large synthetic latency baselines for offline semantic retrieval.</summary>
internal static class SemanticPerformanceSmokeTests
{
    /// <summary>Prints deterministic local throughput without imposing flaky runner-specific deadlines.</summary>
    public static void Run()
    {
        foreach (int count in new[] { 50, 500, 5_000 })
        {
            Guid source = Guid.NewGuid();
            SemanticVectorIndex index = new()
            {
                ModelId = "fake-benchmark", Dimensions = 32,
                SegmentationKey = "chunker-v1"
            };
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                float[] vector = new float[32];
                vector[0] = 1f;
                vector[1] = (float)(i + 1) / count;
                index.Entries.Add(new SemanticVectorEntry
                {
                    DocumentId = source,
                    ChunkId = "chunk-" + i,
                    ContentHash = "synthetic",
                    RelativePath = "reports/demo.md",
                    SourceLine = i + 1, Vector = vector
                });
            }

            float[] query = new float[32];
            query[0] = 1f;
            query[1] = 0.5f;
            IReadOnlyList<SemanticRankedHit> ranked = SemanticVectorRanker.Rank(
                index, query, 20, threshold: -1);
            timer.Stop();
            if (ranked.Count != 1 || !double.IsFinite(ranked[0].Similarity))
            {
                throw new InvalidOperationException(
                    "Semantic benchmark returned invalid source ranking.");
            }

            Console.WriteLine("[Semantic perf] " + count +
                " embeddings x 32 dimensions: " + timer.ElapsedMilliseconds + " ms");
        }

        string markdown = "# Une note volumineuse\n" +
            string.Join("\n\n", Enumerable.Repeat(
                "Compte rendu avec plusieurs tâches et anomalies de mouvements.", 5_000));
        Stopwatch chunksTimer = Stopwatch.StartNew();
        IReadOnlyList<SemanticChunk> chunks = SemanticMarkdownChunker.Split(
            Guid.NewGuid(), "benchmarks/grand-document.md", markdown);
        chunksTimer.Stop();
        if (chunks.Count < 100 || chunks.Any(chunk => chunk.Content.Length > 1200))
        {
            throw new InvalidOperationException(
                "Large Markdown chunking failed bounded source segmentation.");
        }

        Console.WriteLine("[Semantic perf] Large Markdown " + markdown.Length +
            " characters -> " + chunks.Count +
            " passages in " + chunksTimer.ElapsedMilliseconds + " ms");
        MeasureHybridRankFusion();
    }
    /// <summary>
    /// Measures synthetic Small/Medium/Large hybrid rank fusion with no provider,
    /// network or user documents; results are observations, not release budgets.
    /// </summary>
    private static void MeasureHybridRankFusion()
    {
        foreach (int documents in new[] { 50, 500, 5_000 })
        {
            List<HybridSearchCandidate> lexical = [];
            List<HybridSearchCandidate> semantic = [];
            for (int i = 0; i < documents; i++)
            {
                Guid documentId = new Guid(i + 1, 0, 0, new byte[8]);
                string path = "Applications/Demo/Projets/Benchmark/note-" + i + ".md";
                lexical.Add(new HybridSearchCandidate(documentId, path, 1,
                    null, "Scénario lexical fictif", IsExactIdentifier: i == 0));
                semantic.Add(new HybridSearchCandidate(documentId, path, 3,
                    "Recette", "Correspondance vectorielle simulée",
                    Similarity: 0.8));
            }

            semantic.Reverse();
            List<double> elapsed = [];
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 11; iteration++)
            {
                long started = Stopwatch.GetTimestamp();
                IReadOnlyList<HybridRankedHit> hits = HybridSearchRanker.Fuse(
                    lexical, semantic, 20);
                elapsed.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (hits.Count != 20 ||
                    hits[0].Candidate.DocumentId != lexical[0].DocumentId ||
                    hits.Any(hit => !double.IsFinite(hit.RankScore)))
                    throw new InvalidOperationException(
                        "Synthetic hybrid benchmark returned invalid ranking.");
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            elapsed.Sort();
            double p50 = elapsed[5];
            double p95 = elapsed[10];
            Console.WriteLine("[Hybrid 1.8 perf] " + documents +
                " simulated documents, 11 warm queries, p50=" +
                p50.ToString("0.000") + "ms, p95=" + p95.ToString("0.000") +
                "ms, allocated=" + allocated + " bytes (current thread).");
        }
    }

}
