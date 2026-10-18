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

        SemanticIndexPreview before = await service.PreviewIndexAsync(
            selected, new HashSet<Guid> { guid });
        Assert(before.DocumentsVisited == 1 && before.DocumentsChanged == 1 &&
            before.ChunksToEmbed == 1 && before.ReusedChunks == 0 &&
            before.ProjectedTotalChunks == 1 && !before.ExceedsChunkBudget &&
            fake.Calls == 0 && !File.Exists(service.CachePath),
            "Preflight must estimate incremental work without consent, provider calls or cache writes.");
        SemanticIndexSummary created = await service.IndexAsync(selected, true);
        Assert(created.EmbeddedChunks == 1 && fake.Calls == 1,
            "Index a single canonical fragment with a real provider contract.");
        SemanticIndexStatus indexed = await service.InspectAsync();
        Assert(indexed.IsCompatible && indexed.DocumentCount == 1 &&
            indexed.ChunkCount == 1 && indexed.VectorBytes == 12,
            "User-facing status is accessible independently of provider execution.");
        SemanticIndexPreview warm = await service.PreviewIndexAsync(selected);
        Assert(warm.DocumentsChanged == 0 && warm.ChunksToEmbed == 0 &&
            warm.ReusedChunks == 1 && warm.RemovedChunks == 0 && fake.Calls == 1,
            "Warm preview must recognize reused vectors without invoking embeddings.");
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
        SemanticIndexPreview modified = await service.PreviewIndexAsync(selected);
        Assert(modified.DocumentsChanged == 1 && modified.ChunksToEmbed == 1 &&
            modified.ReusedChunks == 0 && modified.RemovedChunks == 1,
            "Edited-source preview must distinguish replacement from reuse.");
        SemanticIndexSummary updated = await service.IndexAsync(selected, true);
        Assert(updated.EmbeddedChunks == 1,
            "Editing one Markdown source reindexes its changed passage.");
        string moved = Path.Combine(workspace, "renamed-report.md");
        File.Move(source, moved);
        SemanticIndexPreview movedPreview = await service.PreviewIndexAsync(
            [new SemanticSource(guid, "renamed-report.md")]);
        Assert(movedPreview.DocumentsChanged == 1 &&
            movedPreview.ChunksToEmbed == 0 && movedPreview.ReusedChunks == 1,
            "A moved document needs provenance refresh but no new embeddings.");
        SemanticIndexSummary renamed = await service.IndexAsync(
            [new SemanticSource(guid, "renamed-report.md")], true);
        Assert(renamed.DocumentsChanged == 1 && renamed.EmbeddedChunks == 0,
            "Moving a canonical source must refresh provenance without new embeddings.");
        hits = await service.SearchAsync("anomalie au nominatif", true);
        Assert(hits.Count == 1 && hits[0].RelativePath == "renamed-report.md",
            "Results must navigate to the new canonical Markdown path.");

        File.Delete(moved);
        SemanticIndexPreview deleted = await service.PreviewIndexAsync(
            [new SemanticSource(guid, "renamed-report.md")]);
        Assert(deleted.DocumentsSkipped == 1 && deleted.RemovedChunks == 1 &&
            deleted.ProjectedTotalChunks == 0 && deleted.ChunksToEmbed == 0,
            "Missing canonical sources appear as removable cached vectors without model calls.");
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
            "# Premier\nCompte rendu durable");
        await File.WriteAllTextAsync(Path.Combine(resumedWorkspace, "second.md"),
            "# Second\nContrôle supplémentaire");
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
        int deniedPrune = await resumed.PruneRemovedDocumentsAsync(
            new HashSet<Guid> { pair[0].DocumentId }, false);
        Assert(deniedPrune == 0 && (await resumed.InspectAsync()).DocumentCount == 2,
            "Catalog cleanup cannot run without an explicit approved operation.");
        File.Delete(Path.Combine(resumedWorkspace, "second.md"));
        SemanticIndexPreview orphanPlan = await resumed.PreviewIndexAsync(
            [pair[0]], new HashSet<Guid> { pair[0].DocumentId });
        Assert(orphanPlan.OrphanedChunks == 1 &&
            orphanPlan.ProjectedTotalChunks == 1 && orphanPlan.ChunksToEmbed == 0 &&
            interrupted.Calls == 3,
            "Preview reconciliation uses the full catalog, not the selected project alone.");
        int staleRemoved = await resumed.PruneRemovedDocumentsAsync(
            new HashSet<Guid> { pair[0].DocumentId }, true);
        Assert(staleRemoved == 1 &&
            (await resumed.InspectAsync()).DocumentCount == 1 &&
            File.Exists(Path.Combine(resumedWorkspace, "first.md")),
            "Approved catalog reconciliation removes deleted documents' vectors only.");

        // Regression: more than 100 stale chunks in one document may score
        // above a valid document. Exhaustive candidate validation must still
        // return the valid canonical source without leaking stale excerpts.
        string staleMarkdown = string.Join("\n\n", Enumerable.Range(0, 130)
            .Select(i => "Fragment périmé " + i + " : opération en attente."));
        string stalePath = Path.Combine(workspace, "AAA-stale.md");
        string freshPath = Path.Combine(workspace, "ZZZ-fresh.md");
        await File.WriteAllTextAsync(stalePath, "# Historique\n" + staleMarkdown);
        await File.WriteAllTextAsync(freshPath,
            "# Recette\nContrôle valide des inscriptions nominatives.");
        Guid staleId = Guid.NewGuid();
        Guid freshId = Guid.NewGuid();
        await service.IndexAsync(
        [
            new SemanticSource(staleId, "AAA-stale.md"),
            new SemanticSource(freshId, "ZZZ-fresh.md")
        ], true);
        await File.WriteAllTextAsync(stalePath,
            "# Historique\nArchives remplacées après validation.");
        IReadOnlyList<SemanticSearchResult> beyondFirstHundred =
            await service.SearchAsync("contrôle nominatif", true);
        Assert(beyondFirstHundred.Any(hit => hit.DocumentId == freshId) &&
            beyondFirstHundred.All(hit => hit.DocumentId != staleId),
            "More than 100 stale chunks must never suppress a valid source.");
        Console.WriteLine("[Semantic 1.8] exhaustive stale-fragment validation passed.");

        // Adding a paragraph shifts source lines and chunk identities.
        // Embeddings of unchanged content must survive this edit.
        string shiftedRoot = Path.Combine(root, "semantic-shifted-lines");
        Directory.CreateDirectory(shiftedRoot);
        string shiftedPath = Path.Combine(shiftedRoot, "meeting.md");
        await File.WriteAllTextAsync(shiftedPath,
            "# Réunion\nDécision stable A.\n\nDécision stable B.\n");
        Guid shiftedId = Guid.NewGuid();
        FakeProvider shiftProvider = new();
        SemanticWorkspaceSearchService shiftService = new(shiftedRoot, enabled,
            shiftProvider, cacheDirectory: Path.Combine(root, "vector-cache-shift"));
        IReadOnlyList<SemanticSource> shiftedSelection =
            [new(shiftedId, "meeting.md")];
        SemanticIndexSummary baseline = await shiftService.IndexAsync(
            shiftedSelection, true);
        Assert(baseline.EmbeddedChunks == 2 && shiftProvider.Calls == 1,
            "Two stable passages must be embedded initially.");

        await File.WriteAllTextAsync(shiftedPath,
            "# Réunion\nNouvelle introduction.\n\nDécision stable A.\n\nDécision stable B.\n");
        SemanticIndexPreview shiftedPreview = await shiftService.PreviewIndexAsync(
            shiftedSelection);
        Assert(shiftedPreview.ChunksToEmbed == 1 &&
            shiftedPreview.ReusedChunks == 2 &&
            shiftedPreview.RemovedChunks == 2 &&
            shiftedPreview.ProjectedTotalChunks == 3 &&
            shiftProvider.Calls == 1,
            "Preflight must count content reuse and obsolete source positions.");
        SemanticIndexSummary afterInsert = await shiftService.IndexAsync(
            shiftedSelection, true);
        Assert(afterInsert.EmbeddedChunks == 1 && shiftProvider.Calls == 2 &&
            (await shiftService.InspectAsync()).ChunkCount == 3,
            "A shifted Markdown document must only embed its new paragraph.");

        await File.AppendAllTextAsync(shiftedPath, "\nDécision stable A.\n");
        SemanticIndexPreview duplicatePreview = await shiftService.PreviewIndexAsync(
            shiftedSelection);
        Assert(duplicatePreview.ChunksToEmbed == 0 &&
            duplicatePreview.ReusedChunks == 4 && shiftProvider.Calls == 2,
            "A repeated identical source passage can reuse its vector.");
        SemanticIndexSummary afterDuplicate = await shiftService.IndexAsync(
            shiftedSelection, true);
        Assert(afterDuplicate.EmbeddedChunks == 0 && shiftProvider.Calls == 2 &&
            (await shiftService.InspectAsync()).ChunkCount == 4,
            "Duplicate content in one document needs no new model calls.");
        Console.WriteLine("[Semantic 1.8] shifted paragraphs reuse indexed content.");

        // Full 10 001 fragment fixture: reject the scope before invoking a
        // provider, rather than consuming tokens and failing to persist.
        string largeWorkspace = Path.Combine(root, "semantic-over-budget");
        Directory.CreateDirectory(largeWorkspace);
        await File.WriteAllTextAsync(Path.Combine(largeWorkspace, "large.md"),
            "# Test de quota\n" + string.Join("\n\n",
                Enumerable.Range(0, 10_001).Select(i =>
                    "Passage unique de test " + i)));
        FakeProvider quotaFake = new();
        SemanticWorkspaceSearchService quotaService = new(
            largeWorkspace, enabled, quotaFake,
            cacheDirectory: Path.Combine(root, "vector-cache-budget"));
        IReadOnlyList<SemanticSource> largeSelection =
            [new(Guid.NewGuid(), "large.md")];
        SemanticIndexPreview tooLarge = await quotaService.PreviewIndexAsync(
            largeSelection);
        Assert(tooLarge.ExceedsChunkBudget &&
            tooLarge.ProjectedTotalChunks > SemanticVectorCacheService.MaximumEntries &&
            tooLarge.PeakProjectedChunks == tooLarge.ProjectedTotalChunks &&
            quotaFake.Calls == 0 && !File.Exists(quotaService.CachePath),
            "Quota preview must reject 10 001 fragments without writing cache or calling a model.");

        bool quotaFailed = false;
        try
        {
            await quotaService.IndexAsync(largeSelection, true);
        }
        catch (InvalidDataException)
        {
            quotaFailed = true;
        }
        Assert(quotaFailed && quotaFake.Calls == 0,
            "The real indexing boundary also blocks over-budget sources before embeddings.");

        using CancellationTokenSource previewCancel = new();
        previewCancel.Cancel();
        bool previewCanceled = false;
        try
        {
            await service.PreviewIndexAsync(selected, token: previewCancel.Token);
        }
        catch (OperationCanceledException)
        {
            previewCanceled = true;
        }
        Assert(previewCanceled, "Read-only preview must respect cancellation.");
        Console.WriteLine("[Semantic 1.8] local preflight quota, cancellation and model isolation passed.");

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
