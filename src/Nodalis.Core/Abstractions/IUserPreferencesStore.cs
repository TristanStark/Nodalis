using Nodalis.Core.Settings;

namespace Nodalis.Core.Abstractions;

public interface IUserPreferencesStore
{
    string PreferencesPath { get; }

    Task<UserPreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken = default);
}
