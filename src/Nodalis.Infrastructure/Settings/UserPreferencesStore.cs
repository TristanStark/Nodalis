using System.Text.Json;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Settings;

public sealed class UserPreferencesStore : IUserPreferencesStore
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserPreferencesStore"/>.
    /// </summary>
    /// <param name="preferencesPath">The <c>preferencesPath</c> value.</param>
    public UserPreferencesStore(string? preferencesPath = null)
    {
        PreferencesPath = Path.GetFullPath(
            preferencesPath ?? GetDefaultPreferencesPath());
    }

    public string PreferencesPath { get; }

    /// <summary>
    /// Performs the <c>LoadAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<UserPreferences> LoadAsync(
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PreferencesPath))
        {
            return UserPreferences.Default;
        }

        try
        {
            global::Nodalis.Core.Settings.UserPreferences preferences = await AtomicJsonFile.ReadAsync<UserPreferences>(
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

    /// <summary>
    /// Performs the <c>SaveAsync</c> operation.
    /// </summary>
    /// <param name="preferences">The <c>preferences</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>Normalize</c> operation.
    /// </summary>
    /// <param name="preferences">The <c>preferences</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static UserPreferences Normalize(UserPreferences preferences)
    {
        global::Nodalis.Core.Settings.EditorPreferences sourceEditor = preferences.Editor ?? new EditorPreferences();
        global::Nodalis.Core.Settings.EditorPreferences editor = sourceEditor with
        {
            FontSize = Math.Clamp(sourceEditor.FontSize, 8, 48),
            AutosaveDelayMilliseconds = Math.Clamp(
                sourceEditor.AutosaveDelayMilliseconds,
                100,
                10_000),
            SplitMode = Enum.IsDefined(
                    typeof(EditorSplitMode),
                    sourceEditor.SplitMode)
                ? sourceEditor.SplitMode
                : EditorSplitMode.None,
            SplitRatio = Math.Clamp(
                sourceEditor.SplitRatio,
                0.2,
                0.8)
        };

        global::Nodalis.Core.Settings.BackupPreferences sourceBackup = preferences.Backup ?? new BackupPreferences();
        global::Nodalis.Core.Settings.BackupPreferences backup = sourceBackup with
        {
            DestinationDirectory = string.IsNullOrWhiteSpace(sourceBackup.DestinationDirectory)
                ? null
                : Path.GetFullPath(sourceBackup.DestinationDirectory.Trim()),
            IntervalMinutes = Math.Clamp(
                sourceBackup.IntervalMinutes,
                15,
                10_080),
            RetentionCount = Math.Clamp(
                sourceBackup.RetentionCount,
                1,
                100)
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
            Backup = backup,
            ExpandedNodeIds = preferences.ExpandedNodeIds ?? [],
            RecentItems = (preferences.RecentItems ?? [])
                .Where(item => item?.Item is not null)
                .OrderByDescending(item => item.LastOpenedUtc)
                .Take(50)
                .ToList(),
            RecentSearches = (preferences.RecentSearches ?? [])
                .Where(item =>
                    item is not null &&
                    !string.IsNullOrWhiteSpace(item.Query))
                .Select(item => item with
                {
                    Query = item.Query.Trim(),
                    ContextRelativePath = string.IsNullOrWhiteSpace(
                            item.ContextRelativePath)
                        ? null
                        : item.ContextRelativePath.Trim()
                })
                .OrderByDescending(item => item.LastUsedUtc)
                .DistinctBy(
                    item => $"{item.Query}\0{item.ContextRelativePath}",
                    StringComparer.CurrentCultureIgnoreCase)
                .Take(20)
                .ToList(),
            Favorites = (preferences.Favorites ?? [])
                .Where(item => item is not null)
                .DistinctBy(item => $"{item.Kind}\0{item.Key}", StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Bookmarks = (preferences.Bookmarks ?? [])
                .Where(bookmark =>
                    bookmark is not null &&
                    bookmark.Id != Guid.Empty &&
                    bookmark.DocumentId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(bookmark.DocumentRelativePath) &&
                    !string.IsNullOrWhiteSpace(bookmark.DisplayName) &&
                    !string.IsNullOrWhiteSpace(bookmark.HeadingTitle) &&
                    bookmark.HeadingLevel is >= 1 and <= 6)
                .Select(bookmark => bookmark with
                {
                    DocumentRelativePath = bookmark.DocumentRelativePath
                        .Trim()
                        .Replace('\\', '/'),
                    DisplayName = bookmark.DisplayName.Trim(),
                    HeadingTitle = bookmark.HeadingTitle.Trim(),
                    HeadingOccurrence = Math.Max(0, bookmark.HeadingOccurrence),
                    OriginalOffset = Math.Max(0, bookmark.OriginalOffset),
                    OriginalLineNumber = Math.Max(1, bookmark.OriginalLineNumber)
                })
                .DistinctBy(bookmark => bookmark.Id)
                .Take(200)
                .ToList(),
            OpenDocumentTabs = (preferences.OpenDocumentTabs ?? [])
                .Where(tab =>
                    tab is not null &&
                    !string.IsNullOrWhiteSpace(
                        tab.RelativePath))
                .DistinctBy(
                    tab => tab.DocumentId != Guid.Empty
                        ? tab.DocumentId.ToString("D")
                        : tab.RelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList(),
            ShortcutOverrides = preferences.ShortcutOverrides ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Performs the <c>GetDefaultPreferencesPath</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private static string GetDefaultPreferencesPath()
    {
        string localApplicationData = Environment.GetFolderPath(
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
