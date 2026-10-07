using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Dashboard;
using Nodalis.App.Dialogs;
using Nodalis.App.Editor;
using Nodalis.App.Navigation;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Core.Navigation;
using Nodalis.Core.Settings;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens the add/edit bookmark workflow for the selected or current Markdown heading.
    /// </summary>
    /// <param name="sender">The bookmark button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void BookmarkHeadingButton_Click(
            object sender,
            RoutedEventArgs e) =>
            await EditSelectedBookmarkAsync();

    /// <summary>
    /// Adds, renames or deletes the local bookmark attached to the selected Markdown heading.
    /// </summary>
    /// <returns>A task representing preference persistence.</returns>
    private async Task EditSelectedBookmarkAsync()
    {
        await RunUiActionAsync(
            "Signets · navigation",
            async () =>
            {
            DocumentTabViewModel? tab =
                GetFocusedDocumentTab();
    
            if (tab is null)
            {
                StatusText.Text =
                    "Ouvrez un document Markdown avant de créer un signet.";
                return;
            }
    
            DocumentOutlineItemViewModel? heading =
                DocumentOutlineTree.SelectedItem as
                    DocumentOutlineItemViewModel ??
                _currentDocumentOutlineItem;
    
            if (heading is null)
            {
                StatusText.Text =
                    "Sélectionnez un titre dans le plan du document avant de créer un signet.";
                return;
            }
    
            int occurrence =
                GetHeadingOccurrence(
                    heading);
    
            DocumentBookmarkReference? existing =
                FindBookmark(
                    tab.DocumentId,
                    heading.Level,
                    heading.Title,
                    occurrence);
    
            BookmarkDialog dialog =
                new BookmarkDialog(
                    heading.Title,
                    existing?.DisplayName ??
                    heading.Title,
                    existing is not null)
                {
                    Owner =
                        this
                };
    
            if (dialog.ShowDialog() != true)
            {
                return;
            }
    
            global::System.Collections.Generic.List<global::Nodalis.Core.Settings.DocumentBookmarkReference> bookmarks =
                _preferences.Bookmarks
                    .Where(bookmark =>
                        existing is null ||
                        bookmark.Id != existing.Id)
                    .ToList();
    
            global::System.Collections.Generic.List<global::Nodalis.Core.Settings.UserItemReference> favorites =
                _preferences.Favorites
                    .Where(reference =>
                        existing is null ||
                        !string.Equals(
                            reference.Key,
                            existing.Id.ToString("D"),
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
    
            if (dialog.DeleteRequested)
            {
                _preferences =
                    _preferences with
                    {
                        Bookmarks =
                            bookmarks,
                        Favorites =
                            favorites
                    };
    
                await _preferencesStore.SaveAsync(
                    _preferences);
    
                RefreshDashboard();
                StatusText.Text =
                    $"Signet supprimé · {existing?.DisplayName ?? heading.Title}";
                return;
            }
    
            Guid bookmarkId =
                existing?.Id ??
                Guid.NewGuid();
    
            string relativePath =
                Path.GetRelativePath(
                        _root.FullPath,
                        tab.FullPath)
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/');
    
            DocumentBookmarkReference bookmark =
                new DocumentBookmarkReference
                {
                    Id =
                        bookmarkId,
                    DocumentId =
                        tab.DocumentId,
                    DocumentRelativePath =
                        relativePath,
                    DisplayName =
                        dialog.BookmarkName,
                    HeadingLevel =
                        heading.Level,
                    HeadingTitle =
                        heading.Title,
                    HeadingOccurrence =
                        occurrence,
                    OriginalOffset =
                        heading.Offset,
                    OriginalLineNumber =
                        heading.LineNumber
                };
    
            bookmarks.Add(
                bookmark);
    
            favorites.Add(
                new UserItemReference
                {
                    Kind =
                        "Bookmark",
                    Key =
                        bookmark.Id.ToString("D"),
                    DisplayName =
                        bookmark.DisplayName
                });
    
            _preferences =
                _preferences with
                {
                    Bookmarks =
                        bookmarks,
                    Favorites =
                        favorites
                };
    
            await _preferencesStore.SaveAsync(
                _preferences);
    
            RefreshDashboard();
            StatusText.Text =
                existing is null
                    ? $"Signet ajouté · {bookmark.DisplayName}"
                    : $"Signet renommé · {bookmark.DisplayName}";
            });
    }

    /// <summary>
    /// Returns the zero-based duplicate occurrence for one visible outline heading.
    /// </summary>
    /// <param name="heading">The selected outline item.</param>
    /// <returns>The occurrence among headings sharing the same level and title.</returns>
    private int GetHeadingOccurrence(
            DocumentOutlineItemViewModel heading)
    {
        int occurrence =
            0;

        foreach (DocumentOutlineItemViewModel candidate in _flatDocumentOutlineItems)
        {
            if (ReferenceEquals(
                    candidate,
                    heading))
            {
                break;
            }

            if (candidate.Level == heading.Level &&
                string.Equals(
                    candidate.Title,
                    heading.Title,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                occurrence++;
            }
        }

        return occurrence;
    }

    /// <summary>
    /// Finds the existing bookmark attached to one exact heading identity.
    /// </summary>
    /// <param name="documentId">The stable document identifier.</param>
    /// <param name="headingLevel">The Markdown heading level.</param>
    /// <param name="headingTitle">The visible Markdown heading title.</param>
    /// <param name="occurrence">The duplicate heading occurrence.</param>
    /// <returns>The matching bookmark, or <see langword="null"/>.</returns>
    private DocumentBookmarkReference? FindBookmark(
            Guid documentId,
            int headingLevel,
            string headingTitle,
            int occurrence) =>
            _preferences.Bookmarks.FirstOrDefault(bookmark =>
                bookmark.DocumentId == documentId &&
                bookmark.HeadingLevel == headingLevel &&
                bookmark.HeadingOccurrence == occurrence &&
                string.Equals(
                    bookmark.HeadingTitle,
                    headingTitle,
                    StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Creates one dashboard item for a persisted section bookmark.
    /// </summary>
    /// <param name="bookmark">The bookmark to display.</param>
    /// <param name="lastOpenedUtc">Optional recent-item timestamp.</param>
    /// <returns>A dashboard item carrying both document and bookmark identities.</returns>
    private DashboardItemViewModel CreateBookmarkDashboardItem(
            DocumentBookmarkReference bookmark,
            DateTimeOffset? lastOpenedUtc)
    {
        NavigationNodeViewModel? node =
            ResolveBookmarkDocumentNode(
                bookmark);

        LinkTargetEntry? target =
            node is null
                ? null
                : FindIndexedTarget(
                    node);

        bool broken =
            IsBookmarkBroken(
                bookmark,
                node);

        string context =
            target is null
                ? $"{bookmark.DocumentRelativePath} › {bookmark.HeadingTitle}"
                : $"{target.QualifiedName} › {bookmark.HeadingTitle}";

        if (broken)
        {
            context =
                $"⚠ Signet à réparer · {context}";
        }

        return new DashboardItemViewModel
        {
            TargetId =
                target?.Id ??
                bookmark.DocumentId,
            BookmarkId =
                bookmark.Id,
            DisplayName =
                bookmark.DisplayName,
            KindLabel =
                broken
                    ? "Signet cassé"
                    : "Signet",
            Context =
                context,
            LastOpenedUtc =
                lastOpenedUtc
        };
    }

    /// <summary>
    /// Creates one searchable Quick Open entry for a persisted section bookmark.
    /// </summary>
    /// <param name="bookmark">The bookmark to expose.</param>
    /// <returns>The Quick Open entry, including broken-state information.</returns>
    private QuickOpenEntry CreateBookmarkQuickOpenEntry(
            DocumentBookmarkReference bookmark)
    {
        NavigationNodeViewModel? node =
            ResolveBookmarkDocumentNode(
                bookmark);

        bool broken =
            IsBookmarkBroken(
                bookmark,
                node);

        string fullPath =
            node?.FullPath ??
            Path.GetFullPath(
                Path.Combine(
                    _root.FullPath,
                    bookmark.DocumentRelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        return new QuickOpenEntry
        {
            NodeId =
                node?.Id ??
                bookmark.DocumentId,
            FullPath =
                fullPath,
            DisplayName =
                bookmark.DisplayName,
            KindLabel =
                broken
                    ? "Signet cassé"
                    : "Signet",
            QualifiedPath =
                $"{bookmark.DocumentRelativePath} › {bookmark.HeadingTitle}",
            IsFavorite =
                true,
            BookmarkId =
                bookmark.Id,
            IsBroken =
                broken,
            RecentRank =
                -1
        };
    }

    /// <summary>
    /// Navigates to a section bookmark and refuses to guess when the saved heading disappeared.
    /// </summary>
    /// <param name="bookmark">The bookmark to open.</param>
    /// <returns><see langword="true"/> when the heading was resolved and selected.</returns>
    private async Task<bool> NavigateToBookmarkAsync(
            DocumentBookmarkReference bookmark)
    {
        NavigationNodeViewModel? node =
            ResolveBookmarkDocumentNode(
                bookmark);

        if (node is null)
        {
            await RefreshNavigationAsync();
            node =
                ResolveBookmarkDocumentNode(
                    bookmark);
        }

        if (node is null)
        {
            StatusText.Text =
                $"⚠ Signet cassé · document introuvable : {bookmark.DocumentRelativePath}";
            return false;
        }

        _restoringSelection =
            true;
        node.IsSelected =
            true;
        _restoringSelection =
            false;

        _selectedNode =
            node;

        await DisplayNodeAsync(
            node);

        TextBox editor =
            GetFocusedEditorTextBox();

        MarkdownOutlineEntry? resolved =
            DocumentBookmarkResolver.Resolve(
                bookmark,
                editor.Text);

        if (resolved is null)
        {
            StatusText.Text =
                $"⚠ Signet cassé · titre « {bookmark.HeadingTitle} » introuvable " +
                $"(ancienne ligne {bookmark.OriginalLineNumber}).";
            return false;
        }

        int caretIndex =
            Math.Clamp(
                resolved.Offset,
                0,
                editor.Text.Length);

        editor.Focus();
        editor.CaretIndex =
            caretIndex;
        editor.SelectionLength =
            0;
        editor.ScrollToLine(
            Math.Max(
                0,
                resolved.LineNumber - 1));

        StatusText.Text =
            $"Signet · {bookmark.DisplayName} · ligne {resolved.LineNumber}";
        return true;
    }

    /// <summary>
    /// Resolves the bookmarked document by stable identifier first and readable relative path second.
    /// </summary>
    /// <param name="bookmark">The persisted bookmark.</param>
    /// <returns>The current document node, or <see langword="null"/>.</returns>
    private NavigationNodeViewModel? ResolveBookmarkDocumentNode(
            DocumentBookmarkReference bookmark)
    {
        NavigationNodeViewModel? byId =
            _root
                .DescendantsAndSelf()
                .FirstOrDefault(candidate =>
                    candidate.Kind == WorkspaceNodeKind.Document &&
                    candidate.Id == bookmark.DocumentId);

        if (byId is not null)
        {
            return byId;
        }

        string fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _root.FullPath,
                    bookmark.DocumentRelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        string relativeCheck =
            Path.GetRelativePath(
                _root.FullPath,
                fullPath);

        if (relativeCheck.Equals(
                "..",
                StringComparison.Ordinal) ||
            relativeCheck.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return null;
        }

        return _root
            .DescendantsAndSelf()
            .FirstOrDefault(candidate =>
                candidate.Kind == WorkspaceNodeKind.Document &&
                string.Equals(
                    Path.GetFullPath(
                        candidate.FullPath),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether a bookmark currently resolves to its saved Markdown heading.
    /// </summary>
    /// <param name="bookmark">The bookmark to inspect.</param>
    /// <param name="node">The already-resolved document node, when available.</param>
    /// <returns><see langword="true"/> when the document or heading is currently unresolved.</returns>
    private bool IsBookmarkBroken(
            DocumentBookmarkReference bookmark,
            NavigationNodeViewModel? node)
    {
        if (node is null)
        {
            return true;
        }

        DocumentTabViewModel? openTab =
            _documentTabs.FirstOrDefault(candidate =>
                candidate.DocumentId == node.Id ||
                string.Equals(
                    candidate.FullPath,
                    node.FullPath,
                    StringComparison.OrdinalIgnoreCase));

        string markdown;

        if (openTab is not null)
        {
            markdown =
                openTab.Content;
        }
        else
        {
            try
            {
                markdown =
                    File.ReadAllText(
                        node.FullPath);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                return true;
            }
        }

        return DocumentBookmarkResolver.Resolve(
                   bookmark,
                   markdown) is null;
    }
}
