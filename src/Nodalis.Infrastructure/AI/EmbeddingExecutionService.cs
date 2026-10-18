using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Privacy-first semantic embedding boundary; never invokes the provider
/// without both persisted opt-in and approval of this indexing batch.
/// </summary>
public sealed class EmbeddingExecutionService
{
    private readonly SemanticSearchPreferences _options;
    private readonly IEmbeddingProvider _provider;

    /// <summary>Constructs the inert-by-default embedding execution boundary.</summary>
    /// <param name="options">User-controlled semantic settings.</param>
    /// <param name="provider">Provider, not invoked during construction.</param>
    public EmbeddingExecutionService(
        SemanticSearchPreferences options, IEmbeddingProvider provider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);
        _options = options;
        _provider = provider;
    }

    /// <summary>Returns null without invoking any provider when consent is absent.</summary>
    /// <param name="texts">Selected source passages.</param>
    /// <param name="approved">Explicit approval of this batch's scope and contents.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Validated batch or null when disabled or approval declined.</returns>
    public async Task<EmbeddingBatch?> EmbedApprovedAsync(
        IReadOnlyList<string> texts, bool approved,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsEnabled || !approved)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        EmbeddingCapabilities capabilities = _provider.Capabilities;
        if (capabilities.Dimensions != _options.Dimensions ||
            !string.Equals(capabilities.ModelId, _options.ModelId, StringComparison.Ordinal))
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.IncompatibleModel,
                "Le modèle d'embeddings configuré ne correspond pas au fournisseur.");
        }

        return await _provider.EmbedAsync(texts, cancellationToken);
    }
}
