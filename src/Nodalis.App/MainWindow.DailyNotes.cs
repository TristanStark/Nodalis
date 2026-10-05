using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Links;
using Nodalis.Core.Notes;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens the daily journal browser and navigates to the chosen Markdown note.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
    private async void ShowDailyNotes_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            await RefreshLinkIndexAsync();

            DateTimeOffset localNow =
                DateTimeOffset.Now;
            IReadOnlyList<DailyNoteReference> openedToday =
                BuildDailyNoteReferences(
                    localNow);

            DailyNotesDialog dialog =
                new DailyNotesDialog(
                    _root.FullPath,
                    _templateStore,
                    openedToday)
                {
                    Owner =
                        this
                };

            if (dialog.ShowDialog() !=
                    true ||
                string.IsNullOrWhiteSpace(
                    dialog.SelectedPath))
            {
                return;
            }

            await RefreshNavigationAsync(
                dialog.SelectedPath);

            StatusText.Text =
                $"Journal ouvert · {Path.GetFileNameWithoutExtension(dialog.SelectedPath)}";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            KeyNotFoundException)
        {
            MessageBox.Show(
                this,
                $"Le journal quotidien n'a pas pu être ouvert.\n\n{exception.Message}",
                "Journal quotidien",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Resolves recent-history entries opened during one local day into stable Nodalis links.
    /// </summary>
    /// <param name="localNow">The current local date and UTC offset.</param>
    /// <returns>The journal link references for the current day.</returns>
    private IReadOnlyList<DailyNoteReference> BuildDailyNoteReferences(
            DateTimeOffset localNow)
    {
        string journalPrefix =
            WorkspaceLayout.JournalDirectoryName +
            "/";

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Links.LinkTargetEntry> targetsById =
            _linkIndex.Targets
                .GroupBy(target =>
                    target.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());

        global::System.Collections.Generic.List<global::Nodalis.Core.Notes.DailyNoteReference> result =
            new List<DailyNoteReference>();

        foreach (RecentItemReference recent in
                 _preferences.RecentItems
                     .OrderBy(item =>
                         item.LastOpenedUtc))
        {
            DateTimeOffset openedLocal =
                recent.LastOpenedUtc.ToOffset(
                    localNow.Offset);

            if (DateOnly.FromDateTime(
                    openedLocal.DateTime) !=
                DateOnly.FromDateTime(
                    localNow.DateTime))
            {
                continue;
            }

            if (!Guid.TryParse(
                    recent.Item.Key,
                    out Guid targetId) ||
                !targetsById.TryGetValue(
                    targetId,
                    out LinkTargetEntry? target))
            {
                continue;
            }

            if (target.RelativePath.StartsWith(
                    journalPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(
                new DailyNoteReference
                {
                    QualifiedName =
                        target.QualifiedName,
                    DisplayName =
                        target.DisplayName,
                    RelativePath =
                        target.RelativePath,
                    Kind =
                        target.Kind == LinkTargetKind.Project
                            ? "Projet"
                            : "Document"
                });
        }

        return result
            .DistinctBy(
                item => item.QualifiedName,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                item => item.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
