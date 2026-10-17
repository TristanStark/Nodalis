namespace Nodalis.Core.AI;

/// <summary>
/// Identifies one explicit source fragment included in a local AI request.
/// </summary>
public sealed record LocalAiContextItem
{
    /// <summary>Gets the human-readable source label.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the exact text sent to the local provider.</summary>
    public required string Content { get; init; }
}

/// <summary>
/// Represents one local AI request before user approval.
/// </summary>
public sealed record LocalAiRequest
{
    /// <summary>Gets the versioned prompt identifier.</summary>
    public required string PromptId { get; init; }

    /// <summary>Gets the system instruction loaded from a readable prompt file.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Gets the user instruction.</summary>
    public required string UserPrompt { get; init; }

    /// <summary>Gets the explicit source fragments selected as context.</summary>
    public IReadOnlyList<LocalAiContextItem> Context { get; init; } = [];
}

/// <summary>
/// Contains the exact text payload shown to the user before local AI execution.
/// </summary>
public sealed record LocalAiPayloadPreview
{
    /// <summary>Gets the prompt identifier.</summary>
    public required string PromptId { get; init; }

    /// <summary>Gets the exact serialized payload that will be written to the local process stdin.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the labels of every selected context fragment.</summary>
    public IReadOnlyList<string> ContextLabels { get; init; } = [];
}

/// <summary>
/// Contains a completed local AI response.
/// </summary>
public sealed record LocalAiResponse
{
    /// <summary>Gets the provider output.</summary>
    public required string Content { get; init; }

    /// <summary>Gets optional provider diagnostic stderr output.</summary>
    public string Diagnostics { get; init; } = string.Empty;
}

/// <summary>
/// Abstracts one local-only AI provider.
/// </summary>
public interface ILocalAiProvider
{
    /// <summary>
    /// Executes an already-approved request locally.
    /// </summary>
    /// <param name="request">The approved request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The local provider response.</returns>
    Task<LocalAiResponse> ExecuteAsync(
        LocalAiRequest request,
        CancellationToken cancellationToken = default);
}
