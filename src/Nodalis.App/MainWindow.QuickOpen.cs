using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Links;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens the keyboard-first Quick Open dialog and navigates to the accepted target.
    /// </summary>
    /// <returns>A task representing target navigation.</returns>
    private async Task ShowQuickOpenAsync()
    {
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.App.Navigation.QuickOpenEntry> entries =
            BuildQuickOpenEntries();

        QuickOpenDialog dialog =
            new QuickOpenDialog(entries)
            {
                Owner = this
            };

        if (dialog.ShowDialog() != true ||
            dialog.SelectedEntry is null)
        {
            return;
        }

        QuickOpenEntry selected =
            dialog.SelectedEntry;

        if (selected.BookmarkId is Guid bookmarkId)
        {
            global::Nodalis.Core.Settings.DocumentBookmarkReference? bookmark =
                _preferences.Bookmarks.FirstOrDefault(candidate =>
                    candidate.Id == bookmarkId);

            if (bookmark is null)
            {
                StatusText.Text =
                    $"Quick Open · signet introuvable : {selected.DisplayName}";
                return;
            }

            await NavigateToBookmarkAsync(
                bookmark);
            return;
        }

        NavigationNodeViewModel? target =
            _root
                .DescendantsAndSelf()
                .FirstOrDefault(node =>
                    node.Id == selected.NodeId &&
                    node.Kind is
                        WorkspaceNodeKind.Application or
                        WorkspaceNodeKind.Module or
                        WorkspaceNodeKind.Project or
                        WorkspaceNodeKind.Document);

        target ??= FindAndExpand(
            _root,
            Path.GetFullPath(
                selected.FullPath));

        if (target is null)
        {
            StatusText.Text =
                $"Quick Open · cible introuvable ou déplacée : {selected.QualifiedPath}";
            return;
        }

        NavigationNodeViewModel? expanded =
            FindAndExpand(
                _root,
                Path.GetFullPath(
                    target.FullPath));

        target =
            expanded ??
            target;

        _restoringSelection = true;
        target.IsSelected = true;
        _restoringSelection = false;

        _selectedNode = target;
        await DisplayNodeAsync(
            target);
    }

    /// <summary>
    /// Builds the in-memory Quick Open index from the already-loaded navigation tree.
    /// </summary>
    /// <returns>The searchable Quick Open targets.</returns>
    private IReadOnlyList<QuickOpenEntry> BuildQuickOpenEntries()
    {
        // Build source lookup maps once. The previous implementation called
        // FindIndexedTarget for every navigation VM, linearly searching the
        // complete link index each time (quadratic on large workspaces).
        Dictionary<Guid, LinkTargetEntry> targetsById = [];
        Dictionary<string, LinkTargetEntry> documentsByRelativePath =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (LinkTargetEntry target in _linkIndex.Targets)
        {
            targetsById.TryAdd(target.Id, target);
            if (target.Kind == LinkTargetKind.Document &&
                !string.IsNullOrWhiteSpace(target.RelativePath))
            {
                documentsByRelativePath.TryAdd(target.RelativePath, target);
            }
        }

        global::System.Collections.Generic.HashSet<string> favoriteKeys =
            _preferences.Favorites
                .Select(reference =>
                    reference.Key)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        global::System.Collections.Generic.Dictionary<string, int> recentRanks =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        int recentRank = 0;

        foreach (global::Nodalis.Core.Settings.RecentItemReference recent in _preferences.RecentItems
                     .OrderByDescending(item =>
                         item.LastOpenedUtc))
        {
            if (!recentRanks.ContainsKey(
                    recent.Item.Key))
            {
                recentRanks[recent.Item.Key] =
                    recentRank;

                recentRank++;
            }
        }

        global::System.Collections.Generic.List<global::Nodalis.App.Navigation.QuickOpenEntry> entries =
            new List<QuickOpenEntry>();

        foreach (NavigationNodeViewModel node in _root
                     .DescendantsAndSelf()
                     .Where(candidate =>
                         candidate.Kind is
                             WorkspaceNodeKind.Application or
                             WorkspaceNodeKind.Module or
                             WorkspaceNodeKind.Project or
                             WorkspaceNodeKind.Document))
        {
            string qualifiedPath =
                Path.GetRelativePath(
                        _root.FullPath,
                        node.FullPath)
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/');

            LinkTargetEntry? indexedTarget = node.Kind ==
                WorkspaceNodeKind.Document
                ? documentsByRelativePath.GetValueOrDefault(qualifiedPath)
                : targetsById.GetValueOrDefault(node.Id);

            string preferenceKey =
                indexedTarget?.Id.ToString("D") ??
                node.Id.ToString("D");

            int rank =
                recentRanks.TryGetValue(
                    preferenceKey,
                    out int knownRank)
                    ? knownRank
                    : -1;

            entries.Add(
                new QuickOpenEntry
                {
                    NodeId = node.Id,
                    PreferenceKey = preferenceKey,
                    FullPath = node.FullPath,
                    DisplayName = node.DisplayName,
                    KindLabel = GetKindLabel(
                        node.Kind),
                    QualifiedPath = qualifiedPath,
                    IsFavorite = favoriteKeys.Contains(
                        preferenceKey),
                    RecentRank = rank
                });
        }

        foreach (global::Nodalis.Core.Settings.DocumentBookmarkReference bookmark in _preferences.Bookmarks)
        {
            QuickOpenEntry bookmarkEntry =
                CreateBookmarkQuickOpenEntry(
                    bookmark);

            entries.Add(
                bookmarkEntry);
        }

        return entries;
    }
}
