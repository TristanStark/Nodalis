using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Settings;

/// <summary>Validates the inert-by-default 1.7 local embedding provider contract.</summary>
internal static class SemanticEmbeddingSmokeTests
{
    /// <summary>Confirms privacy gates, model compatibility, no-network configuration and persistence.</summary>
    /// <param name="root">Existing disposable test root.</param>
    /// <returns>Completed deterministic smoke cases without starting any model.</returns>
    public static async Task RunAsync(string root)
    {
        SemanticSearchPreferences disabled = new();
        FakeEmbeddingProvider fake = new();
        EmbeddingExecutionService disabledService = new(disabled, fake);
        EmbeddingBatch? skipped = await disabledService.EmbedApprovedAsync(
            ["information confidentielle"], true);
        Assert(skipped is null && fake.Calls == 0,
            "No embedding must occur by default, even when a request was otherwise approved.");

        SemanticSearchPreferences approved = new()
        {
            IsEnabled = true, ModelId = "embeddings-test-only",
            Dimensions = 3, MaxBatchSize = 4
        };
        EmbeddingExecutionService service = new(approved, fake);
        EmbeddingBatch? rejected = await service.EmbedApprovedAsync(["secret"], false);
        Assert(rejected is null && fake.Calls == 0,
            "An indexing batch requires explicit consent distinct from saved enablement.");
        EmbeddingBatch? result = await service.EmbedApprovedAsync(["titre"], true);
        Assert(result is not null && result.Vectors.Count == 1 &&
            result.Vectors[0].Length == 3 && fake.Calls == 1,
            "An explicitly approved fake model must return real numeric vectors.");

        bool mismatched = false;
        try
        {
            EmbeddingExecutionService invalid = new(
                approved with { Dimensions = 768 }, fake);
            await invalid.EmbedApprovedAsync(["titre"], true);
        }
        catch (EmbeddingProviderException error)
        {
            mismatched = error.Kind == EmbeddingFailureKind.IncompatibleModel;
        }

        Assert(mismatched && fake.Calls == 1,
            "Model dimension mismatch must abort before reaching a provider.");

        ProcessEmbeddingProvider remote = new(approved with
        {
            ExecutablePath = "https://remote.example/embeddings"
        });
        bool blockedRemote = false;
        try
        {
            await remote.EmbedAsync(["titre"]);
        }
        catch (EmbeddingProviderException error)
        {
            blockedRemote = error.Kind == EmbeddingFailureKind.InvalidConfiguration;
        }

        Assert(blockedRemote, "Remote URLs must not be allowed as embedding executables.");
        ProcessEmbeddingProvider inert = new(disabled);
        bool blockedDisabled = false;
        try
        {
            await inert.EmbedAsync(["contenu"]);
        }
        catch (EmbeddingProviderException error)
        {
            blockedDisabled = error.Kind == EmbeddingFailureKind.Disabled;
        }

        Assert(blockedDisabled, "Provider must reject requests without explicit activation.");

        string preferencesFile = Path.Combine(root,
            "semantic-local-preferences-" + Guid.NewGuid().ToString("N") + ".json");
        UserPreferencesStore store = new(preferencesFile);
        UserPreferences initial = await store.LoadAsync();
        Assert(!initial.SemanticSearch.IsEnabled,
            "New or old preferences must never enable semantic indexing.");
        await store.SaveAsync(initial with { SemanticSearch = approved });
        UserPreferences restored = await store.LoadAsync();
        Assert(restored.SemanticSearch.IsEnabled &&
            restored.SemanticSearch.ModelId == approved.ModelId &&
            restored.SemanticSearch.Dimensions == 3,
            "Explicit preferences must survive local JSON persistence.");
        Console.WriteLine("[Semantic 1.7] opt-in, consent, model checks, remote refusal, local preferences passed.");
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }

        public EmbeddingCapabilities Capabilities { get; } =
            new("embeddings-test-only", 3, 4);

        /// <summary>Returns a deterministic numeric vector without model/network access.</summary>
        /// <param name="texts">Approved synthetic inputs.</param>
        /// <param name="cancellationToken">Cancellation request.</param>
        /// <returns>Deterministic vectors aligned with input order.</returns>
        public Task<EmbeddingBatch> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new EmbeddingBatch(
                "embeddings-test-only", 3,
                texts.Select(_ => new[] { 1f, 0.5f, 0.25f }).ToArray()));
        }
    }

    /// <summary>Raises an actionable deterministic regression failure.</summary>
    /// <param name="condition">Assertion result.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
