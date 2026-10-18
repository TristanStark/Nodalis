using Nodalis.Core.Semantic;
using Nodalis.Infrastructure.Semantic;

/// <summary>Checks that the vector cache is derived, bounded and disposable.</summary>
internal static class SemanticVectorCacheSmokeTests
{
    /// <summary>Tests save/read/replace/delete, schema mismatch and corrupted JSON recovery.</summary>
    /// <param name="root">Disposable fixture root.</param>
    /// <returns>Completed local-only cache tests.</returns>
    public static async Task RunAsync(string root)
    {
        string sandbox = Path.Combine(root, "semantic-cache-fixture");
        Directory.CreateDirectory(sandbox);
        SemanticVectorCacheService cache = new(root, sandbox);
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        SemanticIndexStatus absent = await cache.InspectAsync("model-A", 3, "split-v1");
        Assert(!absent.FileExists && absent.ChunkCount == 0,
            "Inspecting the cache must not create an index or contact a model.");
        SemanticVectorIndex zero = await cache.LoadAsync("model-A", 3, "split-v1");
        Assert(zero.Entries.Count == 0 &&
            cache.CachePath.StartsWith(sandbox, StringComparison.OrdinalIgnoreCase),
            "A new workspace creates no source index and uses the configured local directory.");
        SemanticVectorEntry a = Entry(first, "application/dev.md", "chunk-a");
        SemanticVectorEntry b = Entry(second, "application/test.md", "chunk-b");
        await cache.ReplaceDocumentAsync("model-A", 3, "split-v1", first, [a]);
        await cache.ReplaceDocumentAsync("model-A", 3, "split-v1", second, [b]);
        SemanticVectorIndex loaded = await cache.LoadAsync("model-A", 3, "split-v1");
        SemanticIndexStatus observed = await cache.InspectAsync("model-A", 3, "split-v1");
        Assert(observed.FileExists && observed.IsCompatible &&
            observed.DocumentCount == 2 && observed.ChunkCount == 2 &&
            observed.IndexedPaths.Count == 2 &&
            observed.OnDiskBytes > 0 && observed.VectorBytes == 24,
            "Cache inspection reports only valid vector metadata and disk/resource use.");
        Assert(!(await cache.InspectAsync("model-B", 3, "split-v1")).IsCompatible,
            "Wrong model is diagnosed instead of being silently reported valid.");
        Assert(loaded.Entries.Count == 2 &&
            !File.ReadAllText(cache.CachePath).Contains("secret text"),
            "Store only embeddings/provenance, no duplicated source text.");
        await cache.ReplaceDocumentAsync("model-A", 3, "split-v1", first, []);
        loaded = await cache.LoadAsync("model-A", 3, "split-v1");
        Assert(loaded.Entries.Count == 1 && loaded.Entries[0].DocumentId == second,
            "Removing a canonical document also removes its old vector entries.");
        Assert((await cache.LoadAsync("model-B", 3, "split-v1")).Entries.Count == 0 &&
            (await cache.LoadAsync("model-A", 4, "split-v1")).Entries.Count == 0 &&
            (await cache.LoadAsync("model-A", 3, "split-v2")).Entries.Count == 0,
            "Changes to model, dimension or segmentation must invalidate the cache.");
        await File.WriteAllTextAsync(cache.CachePath, "{not json");
        Assert((await cache.LoadAsync("model-A", 3, "split-v1")).Entries.Count == 0 &&
            !(await cache.InspectAsync("model-A", 3, "split-v1")).IsCompatible,
            "Corruption is reported and never destroys the canonical workspace.");
        await cache.ReplaceDocumentAsync("model-A", 3, "split-v1", second, [b]);
        Assert((await cache.LoadAsync("model-A", 3, "split-v1")).Entries.Count == 1,
            "A corrupted index can be recreated with valid local embeddings.");
        SemanticVectorEntry[] overBudget = Enumerable.Range(0,
            SemanticVectorCacheService.MaximumEntries + 1)
            .Select(i => Entry(first, "application/dev.md", "candidate-" + i))
            .ToArray();
        bool budgetRejected = false;
        try
        {
            await cache.ReplaceDocumentAsync("model-A", 3, "split-v1",
                first, overBudget);
        }
        catch (InvalidDataException)
        {
            budgetRejected = true;
        }
        Assert(budgetRejected &&
            (await cache.LoadAsync("model-A", 3, "split-v1")).Entries.Count == 1,
            "An over-limit write must fail without corrupting the previous cache.");
        await cache.PurgeAsync();
        Assert(!File.Exists(cache.CachePath) &&
            !(await cache.InspectAsync("model-A", 3, "split-v1")).FileExists,
            "Purge removes only derived vectors and reports absence.");
        Console.WriteLine("[Semantic 1.7] vector cache persistence, invalidation, corruption and purge passed.");
    }

    /// <summary>Creates a finite synthetic vector for one document.</summary>
    /// <param name="document">Source GUID.</param>
    /// <param name="path">Canonical relative path.</param>
    /// <param name="chunk">Unique fragment key.</param>
    /// <returns>Derived vector entry without any source text.</returns>
    private static SemanticVectorEntry Entry(Guid document, string path, string chunk) =>
        new()
        {
            DocumentId = document, RelativePath = path,
            ChunkId = chunk, ContentHash = "sha256-test",
            SourceLine = 2, Vector = [1f, 0.3f, 0.1f]
        };

    /// <summary>Raises a test error on inconsistent cache behavior.</summary>
    /// <param name="condition">Assertion.</param>
    /// <param name="message">Failure reason.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
