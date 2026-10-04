using System.Text.Json;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Settings;

public sealed class UserPreferencesStore : IUserPreferencesStore
{
    public UserPreferencesStore(string? preferencesPath = null)
    {
        PreferencesPath = Path.GetFullPath(
            preferencesPath ?? GetDefaultPreferencesPath());
    }

    public string PreferencesPath { get; }

    public async Task<UserPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PreferencesPath))
        {
            return UserPreferences.Default;
        }

        try
        {
            var preferences = await AtomicJsonFile.ReadAsync<UserPreferences>(
                PreferencesPath,
                cancellationToken);

            if (preferences.SchemaVersion != UserPreferences.CurrentSchemaVersion)
            {
                return UserPreferences.Default;
            }

            return Normalize(preferences);
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException)
        {
            return UserPreferences.Default;
        }
    }

    public Task SaveAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (preferences.SchemaVersion != UserPreferences.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Cannot save preferences schema {preferences.SchemaVersion}; " +
                $"supported schema is {UserPreferences.CurrentSchemaVersion}.");
        }

        return AtomicJsonFile.WriteAsync(
            PreferencesPath,
            Normalize(preferences),
            cancellationToken);
    }

    private static UserPreferences Normalize(UserPreferences preferences)
    {
        var editor = preferences.Editor with
        {
            FontSize = Math.Clamp(preferences.Editor.FontSize, 8, 48),
            AutosaveDelayMilliseconds = Math.Clamp(
                preferences.Editor.AutosaveDelayMilliseconds,
                100,
                10_000)
        };

        return preferences with
        {
            NavigationPanelWidth = Math.Clamp(
                preferences.NavigationPanelWidth,
                180,
                800),
            ContextPanelWidth = Math.Clamp(
                preferences.ContextPanelWidth,
                180,
                800),
            Editor = editor,
            RecentItems = preferences.RecentItems
                .OrderByDescending(item => item.LastOpenedUtc)
                .Take(50)
                .ToList(),
            Favorites = preferences.Favorites
                .DistinctBy(item => $"{item.Kind}\0{item.Key}", StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string GetDefaultPreferencesPath()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Windows did not provide a LocalApplicationData directory.");
        }

        return Path.Combine(
            localApplicationData,
            "Nodalis",
            "preferences.json");
    }
}
