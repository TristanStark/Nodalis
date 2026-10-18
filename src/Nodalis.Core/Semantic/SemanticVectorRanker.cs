namespace Nodalis.Core.Semantic;

/// <summary>A comparable, bounded cosine score: not a probability or truth confidence.</summary>
public sealed record SemanticRankedHit(
    Guid DocumentId, string ChunkId, string RelativePath,
    int SourceLine, string? Heading, double Similarity);

/// <summary>
/// Performs vector retrieval on the actual local model's embeddings.
/// Exact/fuzzy search remains a separate feature without any provider dependency.
/// </summary>
public static class SemanticVectorRanker
{
    /// <summary>Ranks matches with numeric validation, stable tie-breaking and deduplication.</summary>
    /// <param name="index">Compatible derived cache.</param>
    /// <param name="queryVector">Embedding of the search expression with the same model.</param>
    /// <param name="topK">Maximum distinct source documents to return.</param>
    /// <param name="threshold">Minimum cosine similarity in [-1, 1].</param>
    /// <param name="scope">Optional canonical relative path prefix.</param>
    /// <param name="deduplicateDocuments">False lets callers verify successive source passages before deduplication.</param>
    /// <returns>One highest-scoring source passage per eligible document.</returns>
    public static IReadOnlyList<SemanticRankedHit> Rank(SemanticVectorIndex index,
        float[] queryVector, int topK = 20, double threshold = 0.2,
        string? scope = null, bool deduplicateDocuments = true)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(queryVector);
        if (index.Dimensions is < 1 or > 4096 ||
            queryVector.Length != index.Dimensions ||
            !queryVector.All(float.IsFinite) ||
            queryVector.All(value => value == 0))
            return [];

        int limit = Math.Clamp(topK, 1, 100);
        double minimum = double.IsFinite(threshold)
            ? Math.Clamp(threshold, -1d, 1d) : 0.2d;
        string? normalizedScope = string.IsNullOrWhiteSpace(scope)
            ? null : scope.Trim().Replace('\\', '/').TrimEnd('/') + "/";

        List<SemanticRankedHit> candidates = [];
        foreach (SemanticVectorEntry entry in index.Entries)
        {
            if (entry.Vector is null || entry.Vector.Length != index.Dimensions ||
                (normalizedScope is not null &&
                 !entry.RelativePath.Replace('\\', '/').StartsWith(normalizedScope,
                     StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            double similarity = Cosine(queryVector, entry.Vector);
            if (!double.IsFinite(similarity) || similarity < minimum)
                continue;

            candidates.Add(new SemanticRankedHit(entry.DocumentId,
                entry.ChunkId, entry.RelativePath, entry.SourceLine,
                entry.Heading, similarity));
        }

        IEnumerable<SemanticRankedHit> ordered = candidates
            .OrderByDescending(hit => hit.Similarity)
            .ThenBy(hit => hit.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(hit => hit.SourceLine)
            .ThenBy(hit => hit.ChunkId, StringComparer.Ordinal);
        return (deduplicateDocuments
                ? ordered.DistinctBy(hit => hit.DocumentId)
                : ordered).Take(limit).ToArray();
    }

    /// <summary>Computes cosine with double accumulators and checks for invalid vectors.</summary>
    /// <param name="left">Query vector.</param>
    /// <param name="right">Indexed passage vector.</param>
    /// <returns>Cosine similarity or NaN for zero/invalid inputs.</returns>
    public static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || right.Length != left.Length) return double.NaN;
        double dot = 0, a = 0, b = 0;
        for (int i = 0; i < left.Length; i++)
        {
            if (!float.IsFinite(left[i]) || !float.IsFinite(right[i]))
                return double.NaN;
            dot += (double)left[i] * right[i];
            a += (double)left[i] * left[i];
            b += (double)right[i] * right[i];
        }

        if (a <= 0 || b <= 0) return double.NaN;
        return Math.Clamp(dot / Math.Sqrt(a * b), -1d, 1d);
    }
}
