using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.App.Dashboard;
using Nodalis.Core.Settings;

namespace Nodalis.App;

public partial class MainWindow
{
    private const int RecentHistoryItemLimit = 50;
    private const int RecentSearchLimit = 20;

    /// <summary>
    /// Removes stale recent targets and invalid session paths without touching workspace data.
    /// </summary>
    /// <returns>A task representing local preference cleanup.</returns>
    private async Task CleanupRecentHistoryAsync()
    {
        HashSet<Guid> validTargetIds = _linkIndex.Targets
            .Select(target => target.Id)
            .ToHashSet();

        List<RecentItemReference> recentItems = _preferences.RecentItems
            .Where(item =>
                Guid.TryParse(
                    item.Item.Key,
                    out Guid targetId) &&
                validTargetIds.Contains(targetId))
            .OrderByDescending(item => item.LastOpenedUtc)
            .Take(RecentHistoryItemLimit)
            .ToList();

        List<RecentSearchReference> recentSearches =
            new List<RecentSearchReference>();

        foreach (RecentSearchReference search in _preferences.RecentSearches
                     .OrderByDescending(item => item.LastUsedUtc)
                     .Take(RecentSearchLimit))
        {
            if (string.IsNullOrWhiteSpace(
                    search.ContextRelativePath))
            {
                recentSearches.Add(search);
                continue;
            }

            string? contextPath = ResolveWorkspaceRelativePath(
                search.ContextRelativePath);

            recentSearches.Add(
                contextPath is not null &&
                (File.Exists(contextPath) ||
                Directory.Exists(contextPath))
                    ? search
                    : search with
                    {
                        ContextRelativePath = null
                    });
        }

        List<OpenDocumentTabReference> openDocumentTabs =
            _preferences.OpenDocumentTabs
                .Where(tab =>
                {
                    string? fullPath = ResolveWorkspaceRelativePath(
                        tab.RelativePath);

                    return fullPath is not null &&
                        File.Exists(fullPath);
                })
                .ToList();

        HashSet<Guid> openDocumentIds = openDocumentTabs
            .Where(tab => tab.DocumentId != Guid.Empty)
            .Select(tab => tab.DocumentId)
            .ToHashSet();

        Guid? activeDocumentTabId =
            _preferences.ActiveDocumentTabId.HasValue &&
            openDocumentIds.Contains(
                _preferences.ActiveDocumentTabId.Value)
                ? _preferences.ActiveDocumentTabId
                : null;

        Guid? secondaryDocumentTabId =
            _preferences.Editor.SecondaryDocumentTabId.HasValue &&
            openDocumentIds.Contains(
                _preferences.Editor.SecondaryDocumentTabId.Value)
                ? _preferences.Editor.SecondaryDocumentTabId
                : null;

        bool changed =
            !recentItems.SequenceEqual(
                _preferences.RecentItems) ||
            !recentSearches.SequenceEqual(
                _preferences.RecentSearches) ||
            !openDocumentTabs.SequenceEqual(
                _preferences.OpenDocumentTabs) ||
            activeDocumentTabId !=
                _preferences.ActiveDocumentTabId ||
            secondaryDocumentTabId !=
                _preferences.Editor.SecondaryDocumentTabId;

        if (!changed)
        {
            return;
        }

        _preferences = _preferences with
        {
            RecentItems = recentItems,
            RecentSearches = recentSearches,
            OpenDocumentTabs = openDocumentTabs,
            ActiveDocumentTabId = activeDocumentTabId,
            Editor = _preferences.Editor with
            {
                SecondaryDocumentTabId =
                    secondaryDocumentTabId
            }
        };

        await _preferencesStore.SaveAsync(
            _preferences);
    }

    /// <summary>
    /// Adds a query to the bounded local search history.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="contextPath">The search scope path, when any.</param>
    /// <returns>A task representing preference persistence.</returns>
    private async Task TrackRecentSearchAsync(
            string query,
            string? contextPath)
    {
        string normalizedQuery = query.Trim();

        if (string.IsNullOrWhiteSpace(
                normalizedQuery))
        {
            return;
        }

        string? contextRelativePath =
            NormalizeRecentSearchContextRelativePath(
                contextPath);

        List<RecentSearchReference> recentSearches =
            _preferences.RecentSearches
                .Where(item =>
                    !string.Equals(
                        item.Query,
                        normalizedQuery,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    !string.Equals(
                        item.ContextRelativePath,
                        contextRelativePath,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        recentSearches.Insert(
            0,
            new RecentSearchReference
            {
                Query = normalizedQuery,
                ContextRelativePath =
                    contextRelativePath,
                LastUsedUtc = DateTimeOffset.UtcNow
            });

        _preferences = _preferences with
        {
            RecentSearches = recentSearches
                .Take(RecentSearchLimit)
                .ToList()
        };

        await _preferencesStore.SaveAsync(
            _preferences);

        RefreshDashboard();
    }

    /// <summary>
    /// Refreshes the recent-search section of the dashboard.
    /// </summary>
    private void RefreshRecentSearchesDashboard()
    {
        RecentSearchesList.ItemsSource =
            _preferences.RecentSearches
                .OrderByDescending(item =>
                    item.LastUsedUtc)
                .Take(10)
                .Select(item =>
                    new RecentSearchViewModel
                    {
                        Query = item.Query,
                        ContextRelativePath =
                            item.ContextRelativePath,
                        ContextLabel =
                            string.IsNullOrWhiteSpace(
                                item.ContextRelativePath)
                                ? "Workspace / contexte global"
                                : item.ContextRelativePath,
                        LastUsedUtc =
                            item.LastUsedUtc
                    })
                .ToArray();
    }

    /// <summary>
    /// Ensures a right-clicked recent-history row becomes the selected row.
    /// </summary>
    /// <param name="sender">The recent-history list.</param>
    /// <param name="e">The mouse event.</param>
    private void RecentHistoryList_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        ListBoxItem? item =
            FindVisualParent<ListBoxItem>(
                e.OriginalSource as DependencyObject);

        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    /// <summary>
    /// Builds the context menu used to remove one recent item or search.
    /// </summary>
    /// <param name="sender">The recent-history list.</param>
    /// <param name="e">The context-menu event.</param>
    private void RecentHistoryList_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e)
    {
        if (sender is not ListBox list ||
            list.SelectedItem is null)
        {
            e.Handled = true;
            return;
        }

        ContextMenu menu = new ContextMenu();
        MenuItem removeItem = new MenuItem
        {
            Header = "Retirer de l'historique"
        };

        if (list.SelectedItem is DashboardItemViewModel dashboardItem)
        {
            removeItem.Click += async (_, _) =>
                await RemoveRecentHistoryItemAsync(
                    dashboardItem);
        }
        else if (list.SelectedItem is RecentSearchViewModel searchItem)
        {
            removeItem.Click += async (_, _) =>
                await RemoveRecentSearchAsync(
                    searchItem);
        }
        else
        {
            e.Handled = true;
            return;
        }

        menu.Items.Add(removeItem);
        list.ContextMenu = menu;
    }

    /// <summary>
    /// Removes one target from the local recent-item history.
    /// </summary>
    /// <param name="item">The dashboard item to remove.</param>
    /// <returns>A task representing preference persistence.</returns>
    private async Task RemoveRecentHistoryItemAsync(
            DashboardItemViewModel item)
    {
        string key = item.TargetId.ToString("D");

        _preferences = _preferences with
        {
            RecentItems = _preferences.RecentItems
                .Where(recent =>
                    !string.Equals(
                        recent.Item.Key,
                        key,
                        StringComparison.OrdinalIgnoreCase))
                .ToList()
        };

        await _preferencesStore.SaveAsync(
            _preferences);

        RefreshDashboard();

        StatusText.Text =
            $"Retiré de l'historique · {item.DisplayName}";
    }

    /// <summary>
    /// Removes one query from the local recent-search history.
    /// </summary>
    /// <param name="item">The recent search to remove.</param>
    /// <returns>A task representing preference persistence.</returns>
    private async Task RemoveRecentSearchAsync(
            RecentSearchViewModel item)
    {
        _preferences = _preferences with
        {
            RecentSearches =
                _preferences.RecentSearches
                    .Where(search =>
                        !string.Equals(
                            search.Query,
                            item.Query,
                            StringComparison.CurrentCultureIgnoreCase) ||
                        !string.Equals(
                            search.ContextRelativePath,
                            item.ContextRelativePath,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList()
        };

        await _preferencesStore.SaveAsync(
            _preferences);

        RefreshDashboard();

        StatusText.Text =
            $"Recherche retirée de l'historique · {item.Query}";
    }

    /// <summary>
    /// Clears all recent items and searches while preserving favorites and the current open session.
    /// </summary>
    /// <param name="sender">The clear-history button.</param>
    /// <param name="e">The routed event.</param>
    private async void ClearRecentHistory_Click(
            object sender,
            RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Historique récent · nettoyage",
            async () =>
            {
                if (_preferences.RecentItems.Count == 0 &&
                    _preferences.RecentSearches.Count == 0)
                {
                    StatusText.Text =
                        "L'historique récent est déjà vide.";
                    return;
                }

                MessageBoxResult result = MessageBox.Show(
                    "Effacer les documents, projets, réunions et recherches récents ?\n\nLes favoris et les onglets actuellement ouverts seront conservés.",
                    "Effacer l'historique",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                _preferences = _preferences with
                {
                    RecentItems = [],
                    RecentSearches = []
                };

                await _preferencesStore.SaveAsync(
                    _preferences);

                RefreshDashboard();

                StatusText.Text =
                    "Historique récent effacé.";
            });
    }

    /// <summary>
    /// Reopens a stored search using its last valid local scope.
    /// </summary>
    /// <param name="sender">The recent-search list.</param>
    /// <param name="e">The mouse event.</param>
    private async void RecentSearchList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        await RunUiActionAsync(
            "Recherche récente · navigation",
            async () =>
            {
                if (sender is not ListBox list ||
                    list.SelectedItem is not RecentSearchViewModel search)
                {
                    return;
                }

                string? contextPath =
                    string.IsNullOrWhiteSpace(
                        search.ContextRelativePath)
                        ? null
                        : ResolveWorkspaceRelativePath(
                            search.ContextRelativePath);

                if (contextPath is not null &&
                    !File.Exists(contextPath) &&
                    !Directory.Exists(contextPath))
                {
                    contextPath = null;
                }

                await SearchAsync(
                    search.Query,
                    contextPath);
            });
    }

    /// <summary>
    /// Converts a local absolute context path into a workspace-relative preference value.
    /// </summary>
    /// <param name="contextPath">The selected local context path.</param>
    /// <returns>The normalized relative path, or <see langword="null"/> for the workspace root or paths outside it.</returns>
    private string? NormalizeRecentSearchContextRelativePath(
            string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(
                contextPath))
        {
            return null;
        }

        string rootPath = Path.GetFullPath(
            _root.FullPath);
        string fullContextPath = Path.GetFullPath(
            contextPath);

        string relativePath = Path.GetRelativePath(
                rootPath,
                fullContextPath)
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

        if (relativePath == "." ||
            relativePath == ".." ||
            relativePath.StartsWith(
                "../",
                StringComparison.Ordinal))
        {
            return null;
        }

        return relativePath;
    }

    /// <summary>
    /// Resolves a workspace-relative local path without accessing the network or allowing traversal outside the workspace.
    /// </summary>
    /// <param name="relativePath">The stored workspace-relative path.</param>
    /// <returns>The normalized absolute path, or <see langword="null"/> when the stored path is invalid.</returns>
    private string? ResolveWorkspaceRelativePath(
            string relativePath)
    {
        try
        {
            string rootPath = Path.GetFullPath(
                _root.FullPath);

            string normalizedRelativePath =
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            string fullPath = Path.GetFullPath(
                Path.Combine(
                    rootPath,
                    normalizedRelativePath));

            string relativeToRoot = Path.GetRelativePath(
                    rootPath,
                    fullPath)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');

            if (relativeToRoot == ".." ||
                relativeToRoot.StartsWith(
                    "../",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return fullPath;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }
}
