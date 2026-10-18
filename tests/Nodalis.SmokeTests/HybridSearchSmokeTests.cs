using Nodalis.Core.Search;
using Nodalis.Infrastructure.Search;

/// <summary>Deterministic ranking tests for the optional 1.8 hybrid search.</summary>
internal static class HybridSearchSmokeTests
{
    /// <summary>Checks rank fusion, exact-match priority, duplicates and offline fallback.</summary>
    public static void Run()
    {
        Guid exact = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid synonym = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid both = Guid.Parse("00000000-0000-0000-0000-000000000003");
        HybridSearchCandidate exactText = new(exact, "Projet/ABC-123.md",
            1, null, "Référence ABC-123", IsExactIdentifier: true);
        HybridSearchCandidate textAndVector = new(both, "Projet/Anomalies.md",
            5, "Incidents", "inscriptions de titres");
        HybridSearchCandidate synonymVector = new(synonym, "Projet/Recette.md",
            7, "Contrôles", "anomalie de mouvement au nominatif",
            Similarity: 0.83);
        HybridSearchCandidate overlapVector = new(both, "Projet/Anomalies.md",
            8, "Diagnostic", "incident connu", Similarity: 0.75);

        IReadOnlyList<HybridRankedHit> mixed = HybridSearchRanker.Fuse(
            [textAndVector, exactText, textAndVector],
            [synonymVector, overlapVector, synonymVector]);
        Assert(mixed.Count == 3 &&
            mixed[0].Candidate.DocumentId == exact &&
            mixed[0].MatchSource == HybridMatchSource.Lexical,
            "A precise identifier must remain first even when the vector channel differs.");
        Assert(mixed.Single(hit => hit.Candidate.DocumentId == both)
                .MatchSource == HybridMatchSource.Combined &&
            mixed.Single(hit => hit.Candidate.DocumentId == synonym)
                .MatchSource == HybridMatchSource.Semantic,
            "Duplicate passages must fuse by GUID with an explainable origin.");
        Assert(mixed.Single(hit => hit.Candidate.DocumentId == synonym)
                .Similarity == 0.83,
            "The original cosine must not be overwritten with reciprocal-rank score.");

        IReadOnlyList<HybridRankedHit> offline = HybridSearchRanker.Fuse(
            [textAndVector, exactText], []);
        Assert(offline.Count == 2 &&
            offline.All(hit => hit.MatchSource == HybridMatchSource.Lexical) &&
            offline[0].Candidate.DocumentId == exact,
            "No provider means real lexical results, never fabricated semantic matches.");
        Assert(HybridSearchRanker.Fuse([], [synonymVector]).Single()
                .MatchSource == HybridMatchSource.Semantic &&
            HybridSearchRanker.Fuse([], []).Count == 0 &&
            HybridSearchRanker.Fuse([textAndVector], [], 1).Count == 1,
            "Semantic-only, empty and truncated rankings must be deterministic.");
        Console.WriteLine("[Semantic 1.8] weighted RRF, exact identifiers, provenance and offline fallback passed.");
    }

    /// <summary>Confirms that hybrid matching uses the classic query filter rules.</summary>
    /// <param name="root">Smoke-test parent path.</param>
    /// <returns>Completed on-disk filter checks without invoking an embedding provider.</returns>
    public static async Task RunFilterChecksAsync(string root)
    {
        string workspace = Path.Combine(root, "HybridFilterFixture");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "Source.md"),
            "---\ntags: api\nstatus: active\n---\n# Source\nDoublons au nominatif");
        await File.WriteAllTextAsync(Path.Combine(workspace, "Other.md"),
            "---\ntags: archive\nstatus: closed\n---\n# Source\nDoublons au nominatif");

        Assert(WorkspaceSearchService.HasStructuredFilters("tag:api doublons") &&
            !WorkspaceSearchService.HasStructuredFilters("doublons inscription") &&
            WorkspaceSearchService.GetFreeTextQuery("tag:api doublons inscription") ==
                "doublons inscription",
            "Semantic embeddings must receive only genuine free text, not filter syntax.");

        WorkspaceSearchService service = new();
        IReadOnlySet<string> accepted = await service.GetFilteredRelativePathsAsync(
            workspace, "tag:api status:active doublons inscription");
        Assert(accepted.Count == 1 && accepted.Contains("Source.md"),
            "Vector results must share the exact legacy metadata predicates.");
        IReadOnlySet<string> noMatch = await service.GetFilteredRelativePathsAsync(
            workspace, "tag:inexistant doublons");
        Assert(noMatch.Count == 0,
            "Structured filters may reject every vector hit; none should be invented.");
        Console.WriteLine("[Semantic 1.8] shared metadata filters and free-text query passed.");
    }

    /// <summary>Rejects an invalid deterministic ranking expectation.</summary>
    /// <param name="condition">Asserted invariant.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
