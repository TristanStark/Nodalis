namespace Nodalis.Core.Settings;

/// <summary>
/// Configures the optional local-only AI process integration.
/// </summary>
public sealed record AiPreferences
{
    /// <summary>Gets whether local AI features are explicitly enabled.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Gets the absolute path of the local executable or wrapper.</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Gets optional command-line arguments passed to the local process.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Gets an optional model identifier exposed to the local process through NODALIS_AI_MODEL.</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>Gets the maximum execution duration in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 120;
}
