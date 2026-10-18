using Nodalis.Core.Semantic;
using Nodalis.Infrastructure.Semantic;

/// <summary>Determinstic cosine, deduplication, scope and invalid-vector tests.</summary>
internal static class SemanticRetrievalSmokeTests
{
    /// <summary>Exercises mathematical ordering without a model or network access.</summary>
    public static void Run()
    {
        Guid exact = Guid.NewGuid();
        Guid related = Guid.NewGuid();
        Guid outsider = Guid.NewGuid();
        SemanticVectorIndex index = new()
        {
            ModelId = "fake", Dimensions = 3, SegmentationKey = "v1",
            Entries =
            [
                V(exact, "Applications/Projet/fiche.md", "exact", [1f, 0f, 0f]),
                V(exact, "Applications/Projet/fiche.md", "weaker", [0.8f, 0.6f, 0f]),
                V(related, "Applications/Projet/recette.md", "related", [0.7f, 0.7f, 0f]),
                V(outsider, "Applications/Autre/annexe.md", "other", [0.9f, 0.1f, 0f])
            ]
        };
        IReadOnlyList<SemanticRankedHit> ranked = SemanticVectorRanker.Rank(index,
            [1f, 0f, 0f], topK: 5, threshold: 0.1, scope: "Applications/Projet");
        Assert(ranked.Count == 2 && ranked[0].DocumentId == exact &&
            ranked[0].ChunkId == "exact" && ranked[1].DocumentId == related,
            "Cosine ranking must deduplicate per document, keep best chunk and honor scope.");
        Assert(Math.Abs(ranked[0].Similarity - 1) < 1e-6 &&
            SemanticVectorRanker.Rank(index, [0f, 0f, 0f]).Count == 0 &&
            SemanticVectorRanker.Rank(index, [float.NaN, 0f, 0f]).Count == 0,
            "Scores are similarities, not probability; invalid vectors produce no results.");
        Assert(SemanticVectorRanker.Rank(index, [1f, 0f], topK: 5).Count == 0 &&
            SemanticVectorRanker.Rank(index, [1f, 0f, 0f], threshold: 0.999).Count == 1,
            "Wrong dimensions and strong thresholds cannot invent matches.");
        Assert(!double.IsFinite(SemanticVectorRanker.Cosine([0f, 0f], [1f, 0f])),
            "Zero vectors must be rejected.");
        Console.WriteLine("[Semantic 1.7] cosine scoring, stable rank, scope and deduplication passed.");
    }

    /// <summary>Builds one deterministic synthetic embedding entry.</summary>
    /// <param name="id">Source GUID.</param>
    /// <param name="path">Canonical path.</param>
    /// <param name="key">Chunk key.</param>
    /// <param name="vector">Fake finite vector.</param>
    /// <returns>Valid derived entry.</returns>
    private static SemanticVectorEntry V(Guid id, string path, string key, float[] vector) =>
        new()
        {
            DocumentId = id, RelativePath = path, ChunkId = key,
            ContentHash = "sha", SourceLine = 2, Vector = vector
        };

    /// <summary>Throws a readable smoke error on any failed criterion.</summary>
    /// <param name="ok">Asserted statement.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
