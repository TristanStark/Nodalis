using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Semantic;

/// <summary>Tests end-to-end indexing against a purely synthetic local embedding provider.</summary>
internal static class SemanticWorkspaceSearchSmokeTests
{
    /// <summary>Verifies explicit consent, incremental reindexing and stale-source safety.</summary>
    /// <param name="root">Disposable root.</param>
    /// <returns>Completed end-to-end fake-model tests.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "semantic-workspace");
        Directory.CreateDirectory(workspace);
        string source = Path.Combine(workspace, "report.md");
        await File.WriteAllTextAsync(source, "# Banque\nTraitement des mouvements\n");
        Guid guid = Guid.NewGuid();
        SemanticSearchPreferences enabled = new()
        {
            IsEnabled = true,
            ModelId = "fake-vector-model",
            Dimensions = 3, MaxBatchSize = 4
        };
        FakeProvider fake = new();
        SemanticWorkspaceSearchService service = new(workspace, enabled,
            fake, cacheDirectory: Path.Combine(root, "vector-cache"));
        IReadOnlyList<SemanticSource> selected = [new(guid, "report.md")];
        SemanticIndexSummary rejected = await service.IndexAsync(selected, false);
        Assert(!rejected.WasApproved && fake.Calls == 0 &&
            !File.Exists(service.CachePath),
            "Without explicit user approval, no source is indexed or processed.");

        SemanticIndexSummary created = await service.IndexAsync(selected, true);
        Assert(created.EmbeddedChunks == 1 && fake.Calls == 1,
            "Index a single canonical fragment with a real provider contract.");
        SemanticIndexStatus indexed = await service.InspectAsync();
        Assert(indexed.IsCompatible && indexed.DocumentCount == 1 &&
            indexed.ChunkCount == 1 && indexed.VectorBytes == 12,
            "User-facing status is accessible independently of provider execution.");
        SemanticIndexSummary unchanged = await service.IndexAsync(selected, true);
        Assert(unchanged.EmbeddedChunks == 0 && fake.Calls == 1,
            "Unchanged source does not call embeddings again.");

        IReadOnlyList<SemanticSearchResult> hits = await service.SearchAsync(
            "anomalie au nominatif", true);
        Assert(hits.Count == 1 && hits[0].DocumentId == guid &&
            hits[0].SourceLine == 2 &&
            hits[0].Excerpt.Contains("mouvements") &&
            fake.Calls == 2,
            "Semantic query vector finds a different-worded, sourced passage.");

        await File.WriteAllTextAsync(source, "# Banque\nUn texte changé\n");
        hits = await service.SearchAsync("anomalie au nominatif", true);
        Assert(hits.Count == 0,
            "Stale cached content must never be returned after a source edit.");
        SemanticIndexSummary updated = await service.IndexAsync(selected, true);
        Assert(updated.EmbeddedChunks == 1,
            "Editing one Markdown source reindexes its changed passage.");
        string moved = Path.Combine(workspace, "renamed-report.md");
        File.Move(source, moved);
        SemanticIndexSummary renamed = await service.IndexAsync(
            [new SemanticSource(guid, "renamed-report.md")], true);
        Assert(renamed.DocumentsChanged == 1 && renamed.EmbeddedChunks == 0,
            "Moving a canonical source must refresh provenance without new embeddings.");
        hits = await service.SearchAsync("anomalie au nominatif", true);
        Assert(hits.Count == 1 && hits[0].RelativePath == "renamed-report.md",
            "Results must navigate to the new canonical Markdown path.");

        File.Delete(moved);
        await service.IndexAsync([new SemanticSource(guid, "renamed-report.md")], true);
        Assert((await service.SearchAsync("requête", true)).Count == 0,
            "Deleted sources must disappear from the derived cache.");
        await service.PurgeAsync();
        Assert(!File.Exists(service.CachePath), "Purge must delete only local vectors.");

        // A changed top-scoring passage must not mask an unchanged second
        // fragment in the same document until the next explicit reindex.
        string multi = Path.Combine(workspace, "multi.md");
        Guid multiId = Guid.NewGuid();
        await File.WriteAllTextAsync(multi,
            "# Banque\nPassage périmé\n\nFragment encore valide\n");
        await service.IndexAsync([new SemanticSource(multiId, "multi.md")], true);
        await File.WriteAllTextAsync(multi,
            "# Banque\nPassage modifié\n\nFragment encore valide\n");
        IReadOnlyList<SemanticSearchResult> validFallback =
            await service.SearchAsync("anomalies de paiements", true);
        Assert(validFallback.Count == 1 &&
            validFallback[0].Excerpt.Contains("Fragment encore valide"),
            "A stale first-ranked passage must not hide a valid second source passage.");
        SemanticSearchPreferences excludedPrefs = enabled with
        {
            ExcludedPathPatterns = ["*.md"]
        };
        FakeProvider excludedFake = new();
        SemanticWorkspaceSearchService excludedService = new(workspace, excludedPrefs,
            excludedFake,
            new SemanticChunkingOptions
            {
                ExcludedPathPatterns = excludedPrefs.ExcludedPathPatterns
            }, Path.Combine(root, "vector-cache-excluded"));
        SemanticIndexSummary excludedSummary = await excludedService.IndexAsync(
            [new SemanticSource(Guid.NewGuid(), "multi.md")], true);
        Assert(excludedSummary.EmbeddedChunks == 0 &&
            excludedFake.Calls == 0 &&
            (await excludedService.SearchAsync("indexation interdite", true)).Count == 0,
            "Explicit exclusions prevent sending restricted Markdown to the provider.");
        // Emulate a user interruption during the next provider batch.
        // A whole document is committed atomically; a second explicit run resumes
        // by reusing it rather than embedding the same passage again.
        string resumedWorkspace = Path.Combine(root, "semantic-resume-workspace");
        Directory.CreateDirectory(resumedWorkspace);
        await File.WriteAllTextAsync(Path.Combine(resumedWorkspace, "first.md"),
            "# Premier\\nCompte rendu durable".Replace("\\\\n", "\\n"));
        await File.WriteAllTextAsync(Path.Combine(resumedWorkspace, "second.md"),
            "# Second\\nContrôle supplémentaire".Replace("\\\\n", "\\n"));
        FakeProvider interrupted = new() { CancelOnCall = 2 };
        SemanticWorkspaceSearchService resumed = new(resumedWorkspace, enabled,
            interrupted, cacheDirectory: Path.Combine(root, "vector-cache-resume"));
        IReadOnlyList<SemanticSource> pair =
        [
            new(Guid.NewGuid(), "first.md"), new(Guid.NewGuid(), "second.md")
        ];
        bool canceled = false;
        try
        {
            await resumed.IndexAsync(pair, true);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        SemanticIndexStatus afterStop = await resumed.InspectAsync();
        Assert(canceled && afterStop.IsCompatible && afterStop.DocumentCount == 1 &&
            afterStop.ChunkCount == 1,
            "Interrupted indexing must keep previously committed documents intact.");
        interrupted.CancelOnCall = 0;
        SemanticIndexSummary continuation = await resumed.IndexAsync(pair, true);
        Assert(continuation.EmbeddedChunks == 1 && interrupted.Calls == 3 &&
            (await resumed.InspectAsync()).DocumentCount == 2,
            "Resume must compute only incomplete passages after an interruption.");

        Console.WriteLine("[Semantic 1.7/1.8] consent, incremental resume, source navigation and deletion passed.");
    }

    private sealed class FakeProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }

        /// <summary>Optional synthetic interruption on a specific provider invocation.</summary>
        public int CancelOnCall { get; set; }

        public EmbeddingCapabilities Capabilities { get; } =
            new("fake-vector-model", 3, 4);

        /// <summary>Maps different terms to a similar deterministic numeric direction.</summary>
        /// <param name="texts">Approved input batch.</param>
        /// <param name="cancellationToken">Cancellation request.</param>
        /// <returns>Synthetic unit-aligned embeddings for deterministic assertions.</returns>
        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (CancelOnCall == Calls)
                throw new OperationCanceledException("Synthetic user interruption.", cancellationToken);
            return Task.FromResult(new EmbeddingBatch("fake-vector-model", 3,
                texts.Select(_ => new[] { 1f, 0.2f, 0.1f }).ToArray()));
        }
    }

    /// <summary>Fails immediately when semantic index safety or functionality regresses.</summary>
    /// <param name="condition">Assertion.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
