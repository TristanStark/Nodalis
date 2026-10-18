namespace Nodalis.Core.Settings;

/// <summary>
/// Local semantic search is separately opt-in and never shares the
/// existing text-generation AI enablement flag.
/// </summary>
public sealed record SemanticSearchPreferences
{
    /// <summary>Disabled unless the user expressly enables semantic indexing.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Trusted absolute path to a locally installed embedding adapter.</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Optional arguments accepted only by a user-configured local adapter.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Identifier of a real embedding model, not a conversational LLM.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Expected vector dimensionality, validated for every response.</summary>
    public int Dimensions { get; init; }

    /// <summary>Bounded number of input passages per provider call.</summary>
    public int MaxBatchSize { get; init; } = 16;

    /// <summary>Maximum execution seconds, excluding process startup.</summary>
    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>Excluded workspace-relative Markdown path patterns (never embedded).</summary>
    public IReadOnlyList<string> ExcludedPathPatterns { get; init; } = [];
}
