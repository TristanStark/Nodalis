namespace Nodalis.Core.Semantic;

/// <summary>Known failures of the optional local embedding service.</summary>
public enum EmbeddingFailureKind
{
    Disabled,
    Unavailable,
    InvalidConfiguration,
    IncompatibleModel,
    InvalidResponse,
    Timeout
}

/// <summary>Non-sensitive error from an optional local embedding provider.</summary>
public sealed class EmbeddingProviderException : Exception
{
    /// <summary>Constructs an error without retaining any indexed source text.</summary>
    /// <param name="kind">Failure category.</param>
    /// <param name="message">Short non-sensitive French diagnostic.</param>
    public EmbeddingProviderException(EmbeddingFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>Category usable for displaying a safe fallback message.</summary>
    public EmbeddingFailureKind Kind { get; }
}

/// <summary>Declared capabilities, without calling a provider at startup.</summary>
public sealed record EmbeddingCapabilities(string ModelId, int Dimensions, int MaxBatch);

/// <summary>One batch of real vectors, never text-generation output.</summary>
public sealed record EmbeddingBatch(
    string ModelId,
    int Dimensions,
    IReadOnlyList<float[]> Vectors);

/// <summary>Optional embedding contract, independent of Nodalis' existing chat LLM.</summary>
public interface IEmbeddingProvider
{
    /// <summary>Provider configuration; reading it has no side effects.</summary>
    EmbeddingCapabilities Capabilities { get; }

    /// <summary>Embeds an explicitly approved local batch using a compatible model.</summary>
    /// <param name="texts">Bounded source passages, never sent to an Internet service by Nodalis.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Finite numeric vectors aligned with the requested passages.</returns>
    Task<EmbeddingBatch> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
