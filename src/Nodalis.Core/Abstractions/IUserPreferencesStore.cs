using Nodalis.Core.Settings;

namespace Nodalis.Core.Abstractions;

public interface IUserPreferencesStore
{
    string PreferencesPath { get; }

    /// <summary>
    /// Performs the <c>LoadAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<UserPreferences> LoadAsync(
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the <c>SaveAsync</c> operation.
    /// </summary>
    /// <param name="preferences">The <c>preferences</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task SaveAsync(
            UserPreferences preferences,
            CancellationToken cancellationToken = default);
}
