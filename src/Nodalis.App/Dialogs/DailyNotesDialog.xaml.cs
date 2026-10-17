using System.IO;
using System.Windows;
using System.Windows.Input;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Notes;
using Nodalis.Infrastructure.Notes;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

public partial class DailyNotesDialog : Window
{
    private readonly WorkspaceDailyNoteService _service;
    private readonly IReadOnlyList<DailyNoteReference> _openedToday;

    /// <summary>
    /// Initializes a new instance of <see cref="DailyNotesDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="templateStore">The workspace template store.</param>
    /// <param name="openedToday">Items opened during the current local day.</param>
    public DailyNotesDialog(
            string workspaceRoot,
            ITemplateStore templateStore,
            IReadOnlyList<DailyNoteReference> openedToday)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentNullException.ThrowIfNull(
            templateStore);
        ArgumentNullException.ThrowIfNull(
            openedToday);

        _service =
            new WorkspaceDailyNoteService(
                workspaceRoot,
                templateStore);
        _openedToday =
            openedToday;

        InitializeComponent();

        Loaded += async (_, _) =>
            await RefreshAsync();
    }

    public string? SelectedPath { get; private set; }

    /// <summary>
    /// Creates or refreshes today's journal entry and opens it.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
    private async void Today_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Notes quotidiennes · aujourd'hui",
            async () =>
            {
                try
                {
                    DateTimeOffset now =
                        DateTimeOffset.Now;
                    DailyNoteItem today =
                        await _service.GetOrCreateAsync(
                            now,
                            _openedToday);

                    await _service.SyncOpenedItemsAsync(
                        today.Date,
                        _openedToday);

                    SelectedPath =
                        today.FullPath;
                    DialogResult =
                        true;
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    KeyNotFoundException)
                {
                    ShowError(
                        exception.Message);
                }
            });
    }

    /// <summary>
    /// Updates today's generated opened-items block and keeps the recent-days view open.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
    private async void SyncOpenedItems_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Notes quotidiennes · synchroniser",
            async () =>
            {
                try
                {
                    DateTimeOffset now =
                        DateTimeOffset.Now;
                    DailyNoteItem today =
                        await _service.GetOrCreateAsync(
                            now,
                            _openedToday);

                    await _service.SyncOpenedItemsAsync(
                        today.Date,
                        _openedToday);

                    await RefreshAsync();

                    RecentNotesList.SelectedItem =
                        RecentNotesList.Items
                            .Cast<DailyNoteItem>()
                            .FirstOrDefault(item =>
                                item.Date ==
                                today.Date);

                    StatusText.Text =
                        $"{_openedToday.Count} élément(s) ouvert(s) synchronisé(s) dans le journal du jour.";
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    KeyNotFoundException)
                {
                    ShowError(
                        exception.Message);
                }
            });
    }

    /// <summary>
    /// Opens the selected recent journal entry.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
    private void OpenSelected_Click(
            object sender,
            RoutedEventArgs e) =>
        OpenSelected();

    /// <summary>
    /// Opens a recent journal entry by double-clicking it.
    /// </summary>
    /// <param name="sender">The list box.</param>
    /// <param name="e">The mouse event.</param>
    private void RecentNotesList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
        OpenSelected();

    /// <summary>
    /// Refreshes the recent journal list.
    /// </summary>
    private async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<DailyNoteItem> recent =
                await _service.GetRecentAsync(
                    DateTimeOffset.Now);

            RecentNotesList.ItemsSource =
                recent;

            DailyNoteItem? today =
                recent.FirstOrDefault(item =>
                    item.IsToday);

            RecentNotesList.SelectedItem =
                today ??
                recent.FirstOrDefault();

            StatusText.Text =
                recent.Count == 0
                    ? "Aucune note quotidienne pour le moment."
                    : $"{recent.Count} jour(s) récent(s).";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            RecentNotesList.ItemsSource =
                null;
            StatusText.Text =
                exception.Message;
        }
    }

    /// <summary>
    /// Selects the current list entry as the journal path returned to the main window.
    /// </summary>
    private void OpenSelected()
    {
        if (RecentNotesList.SelectedItem is not
            DailyNoteItem item)
        {
            MessageBox.Show(
                this,
                "Sélectionnez une journée à ouvrir.",
                "Journal quotidien",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedPath =
            item.FullPath;
        DialogResult =
            true;
    }

    /// <summary>
    /// Shows an expected journal error.
    /// </summary>
    /// <param name="message">The error message.</param>
    private void ShowError(
            string message)
    {
        MessageBox.Show(
            this,
            message,
            "Journal quotidien",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
