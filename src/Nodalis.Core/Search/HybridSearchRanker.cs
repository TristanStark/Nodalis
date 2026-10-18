namespace Nodalis.Core.Search;

/// <summary>Explains which independently ranked search channels found a document.</summary>
public enum HybridMatchSource
{
    Lexical,
    Semantic,
    Combined
}

/// <summary>One navigable canonical source occurrence from either search engine.</summary>
/// <param name="DocumentId">Stable canonical document identity, not a file-name guess.</param>
/// <param name="RelativePath">Workspace-relative Markdown path.</param>
/// <param name="SourceLine">One-based line number of the matching passage.</param>
/// <param name="Heading">Optional section title.</param>
/// <param name="Excerpt">Current source excerpt.</param>
/// <param name="IsExactIdentifier">Whether lexical matching found an exact filename/reference.</param>
/// <param name="Similarity">Semantic cosine, if supplied by the vector engine.</param>
public sealed record HybridSearchCandidate(
    Guid DocumentId, string RelativePath, int SourceLine, string? Heading,
    string Excerpt, bool IsExactIdentifier = false, double? Similarity = null);

/// <summary>Explainable reciprocal-rank result, without pretending to be a probability.</summary>
/// <param name="Candidate">Navigable canonical source occurrence.</param>
/// <param name="MatchSource">Channel or channels which found this document.</param>
/// <param name="RankScore">Weighted reciprocal-rank score.</param>
/// <param name="Similarity">Optional original cosine similarity, independent of rank score.</param>
public sealed record HybridRankedHit(
    HybridSearchCandidate Candidate, HybridMatchSource MatchSource,
    double RankScore, double? Similarity);

/// <summary>
/// Combines independent lexical and semantic document rankings without requiring
/// a local embedding model or changing the legacy search engine.
/// </summary>
public static class HybridSearchRanker
{
    /// <summary>Standard reciprocal-rank offset: prevents one top result dominating all others.</summary>
    public const int RankOffset = 60;

    /// <summary>Preserves a small preference for precise textual matches.</summary>
    public const double LexicalWeight = 1.25;

    /// <summary>Weight given to semantic passages once the user enables embeddings.</summary>
    public const double SemanticWeight = 1.0;

    /// <summary>Priority for confirmed lexical filename/reference matches.</summary>
    public const double ExactIdentifierBonus = 0.04;

    /// <summary>
    /// Fuses pre-ranked channels using weighted reciprocal rank, one result per
    /// stable document identity. No embeddings are calculated in this method.
    /// </summary>
    /// <param name="lexical">Best-first lexical matches, possibly empty.</param>
    /// <param name="semantic">Best-first real vector matches, possibly empty.</param>
    /// <param name="limit">Maximum returned documents.</param>
    /// <returns>Stable explainable ranking. An empty semantic channel yields lexical-only results.</returns>
    public static IReadOnlyList<HybridRankedHit> Fuse(
        IReadOnlyList<HybridSearchCandidate> lexical,
        IReadOnlyList<HybridSearchCandidate> semantic, int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        ArgumentNullException.ThrowIfNull(semantic);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));

        Dictionary<Guid, RankingState> candidates = new();
        HashSet<Guid> seenLexical = new();
        int lexicalRank = 0;
        foreach (HybridSearchCandidate item in lexical)
        {
            if (item.DocumentId == Guid.Empty || !seenLexical.Add(item.DocumentId)) continue;
            lexicalRank++;
            if (!candidates.TryGetValue(item.DocumentId, out RankingState? state))
            {
                state = new RankingState();
                candidates.Add(item.DocumentId, state);
            }

            state.Lexical = item;
            state.Score += LexicalWeight / (RankOffset + lexicalRank);
            if (item.IsExactIdentifier)
                state.Score += ExactIdentifierBonus;
        }

        HashSet<Guid> seenSemantic = new();
        int semanticRank = 0;
        foreach (HybridSearchCandidate item in semantic)
        {
            if (item.DocumentId == Guid.Empty || !seenSemantic.Add(item.DocumentId)) continue;
            semanticRank++;
            if (!candidates.TryGetValue(item.DocumentId, out RankingState? state))
            {
                state = new RankingState();
                candidates.Add(item.DocumentId, state);
            }

            state.Semantic = item;
            state.Score += SemanticWeight / (RankOffset + semanticRank);
        }

        return candidates
            .Select(entry =>
            {
                RankingState state = entry.Value;
                HybridSearchCandidate candidate = state.Lexical is
                { IsExactIdentifier: true } exact ? exact :
                    state.Semantic ?? state.Lexical!;
                HybridMatchSource source = state.Lexical is not null &&
                    state.Semantic is not null ? HybridMatchSource.Combined :
                    state.Lexical is not null ? HybridMatchSource.Lexical :
                    HybridMatchSource.Semantic;
                return new HybridRankedHit(candidate, source, state.Score,
                    state.Semantic?.Similarity);
            })
            .OrderByDescending(hit => hit.RankScore)
            .ThenBy(hit => hit.Candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(hit => hit.Candidate.DocumentId)
            .Take(limit)
            .ToArray();
    }

    /// <summary>Accumulates independent channels for one stable canonical document.</summary>
    private sealed class RankingState
    {
        /// <summary>Best available legacy text result.</summary>
        public HybridSearchCandidate? Lexical { get; set; }

        /// <summary>Best available vector result.</summary>
        public HybridSearchCandidate? Semantic { get; set; }

        /// <summary>Weighted reciprocal-rank sum, with explicit exact match bonus.</summary>
        public double Score { get; set; }
    }
}
