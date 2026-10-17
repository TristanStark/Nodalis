using Nodalis.Core.AI;
using Nodalis.Core.Settings;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Enforces explicit enablement and exact-payload approval before invoking a local AI provider.
/// </summary>
public sealed class LocalAiExecutionService
{
    private readonly AiPreferences _preferences;
    private readonly ILocalAiProvider _provider;

    /// <summary>
    /// Initializes the local AI execution coordinator.
    /// </summary>
    /// <param name="preferences">The explicit local AI preferences.</param>
    /// <param name="provider">The local provider implementation.</param>
    public LocalAiExecutionService(
            AiPreferences preferences,
            ILocalAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(
            preferences);
        ArgumentNullException.ThrowIfNull(
            provider);

        _preferences =
            preferences;
        _provider =
            provider;
    }

    /// <summary>
    /// Displays the exact payload through the supplied approval callback before any local model execution.
    /// </summary>
    /// <param name="request">The local AI request.</param>
    /// <param name="approval">Callback that must explicitly approve the exact payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider response, or null when the user rejects the payload.</returns>
    public async Task<LocalAiResponse?> ExecuteWithApprovalAsync(
            LocalAiRequest request,
            Func<LocalAiPayloadPreview, CancellationToken, Task<bool>> approval,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);
        ArgumentNullException.ThrowIfNull(
            approval);

        if (!_preferences.IsEnabled)
        {
            throw new InvalidOperationException(
                "L'assistant IA local est désactivé dans les préférences.");
        }

        LocalAiPayloadPreview preview =
            LocalAiPayloadBuilder.CreatePreview(
                request);

        bool approved =
            await approval(
                preview,
                cancellationToken);

        if (!approved)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await _provider.ExecuteAsync(
            request,
            cancellationToken);
    }
}
