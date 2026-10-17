using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.App.Reliability;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Workspace-local Inbox for browsing, filtering, batch triage and recovery.
/// No document is converted or overwritten by this view.
/// </summary>
public partial class InboxDialog : Window
{
    private readonly WorkspaceInboxService _store;
    private readonly string _workspaceRoot;
    private readonly string? _preferredContextPath;
    private readonly Func<string, Task<bool>>? _prepareMaintenanceDocument;
    private IReadOnlyList<InboxEntry> _entries = [];
    private IReadOnlyList<InboxItemRow> _filteredRows = [];

    /// <summary>Creates an Inbox view connected to the current workspace.</summary>
    /// <param name="workspaceRoot">Existing Nodalis workspace.</param>
    /// <param name="preferredContextPath">Last selected project or document, if any.</param>
    /// <param name="prepareMaintenanceDocument">Safe editor autosave gate before maintenance writes.</param>
    public InboxDialog(string workspaceRoot, string? preferredContextPath = null,
        Func<string, Task<bool>>? prepareMaintenanceDocument = null)
    {
        _workspaceRoot = workspaceRoot;
        _preferredContextPath = preferredContextPath;
        _prepareMaintenanceDocument = prepareMaintenanceDocument;
        _store = new WorkspaceInboxService(workspaceRoot);
        InitializeComponent();
        Loaded += async (_, _) =>
            await UiActionGuard.RunAsync(this, "Inbox · ouverture", RefreshAsync,
                workspaceRoot);
    }

    public bool WorkspaceChanged { get; private set; }

    /// <summary>Reloads persisted Inbox entries or recoverable trash without data loss.</summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        InboxReadResult read = TrashModeCheckBox.IsChecked == true
            ? await _store.ReadTrashAsync()
            : await _store.ReadAllAsync();

        _entries = read.Entries;
        ApplyFilters();
        string total = _entries.Count + " capture(s)";
        if (read.Warnings.Count > 0)
        {
            string warningText = string.Join("\n",
                read.Warnings.Take(8).Select(warning =>
                    warning.Item + " : " + warning.Message));
            StatusText.Text = total + " · " + read.Warnings.Count +
                " avertissement(s) de récupération.";
            if (Application.Current is App application)
            {
                application.DiagnosticsService.LogInformation(
                    "Inbox : " + warningText);
            }

            MessageBox.Show(this, warningText,
                "Inbox · récupération et données à vérifier",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            StatusText.Text = total;
        }
    }

    /// <summary>Applies search and state filters to the current collection.</summary>
    private void ApplyFilters()
    {
        string query = SearchTextBox?.Text.Trim() ?? string.Empty;
        int stateFilter = StateFilterComboBox?.SelectedIndex ?? 0;
        bool trashMode = TrashModeCheckBox?.IsChecked == true;
        IEnumerable<InboxEntry> candidates = _entries;

        if (!trashMode && stateFilter > 0)
        {
            InboxItemState state = stateFilter switch
            {
                1 => InboxItemState.Pending,
                2 => InboxItemState.Processed,
                3 => InboxItemState.Archived,
                _ => InboxItemState.Pending
            };
            candidates = candidates.Where(entry => entry.State == state);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            candidates = candidates.Where(entry =>
                entry.ContentMarkdown.Contains(
                    query, StringComparison.CurrentCultureIgnoreCase) ||
                (entry.Origin?.Contains(
                    query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (entry.ConvertedTargetRelativePath?.Contains(
                    query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                entry.Kind.ToString().Contains(
                    query, StringComparison.OrdinalIgnoreCase) ||
                entry.Attachments.Any(attachment =>
                    attachment.OriginalFileName.Contains(
                        query, StringComparison.CurrentCultureIgnoreCase)));
        }

        _filteredRows = candidates
            .OrderByDescending(entry => entry.CreatedUtc)
            .ThenBy(entry => entry.Id)
            .Select(entry => new InboxItemRow(entry))
            .ToArray();
        EntriesList.ItemsSource = _filteredRows;
        PreviewTextBox.Text = string.Empty;
        UpdateButtons();
    }

    /// <summary>Filters the Inbox after the user edits the search box.</summary>
    /// <param name="sender">Search box.</param>
    /// <param name="e">Text changed event.</param>
    private void SearchTextBox_TextChanged(
            object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilters();
    }

    /// <summary>Filters entries by the selected Inbox state.</summary>
    /// <param name="sender">State combo box.</param>
    /// <param name="e">Combo selection event.</param>
    private void StateFilterComboBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilters();
    }

    /// <summary>Switches between active Inbox and recoverable Inbox trash.</summary>
    /// <param name="sender">Trash checkbox.</param>
    /// <param name="e">Routed check event.</param>
    private async void TrashMode_Changed(
            object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        StateFilterComboBox.IsEnabled = TrashModeCheckBox.IsChecked != true;
        await UiActionGuard.RunAsync(this, "Inbox · changer de vue", RefreshAsync);
    }

    /// <summary>Refreshes preview and action availability on selection changes.</summary>
    /// <param name="sender">Entry list.</param>
    /// <param name="e">Selection event.</param>
    private void EntriesList_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
    {
        InboxItemRow? item = EntriesList.SelectedItem as InboxItemRow;
        PreviewTextBox.Text = item?.Entry.ContentMarkdown ?? string.Empty;
        UpdateButtons();
    }

    /// <summary>Enables only actions allowed for the selected view.</summary>
    private void UpdateButtons()
    {
        if (EntriesList is null || TrashModeCheckBox is null)
        {
            return;
        }

        bool anySelected = EntriesList.SelectedItems.Count > 0;
        bool trash = TrashModeCheckBox.IsChecked == true;
        ConvertButton.IsEnabled = !trash && EntriesList.SelectedItems.Count == 1 &&
            EntriesList.SelectedItem is InboxItemRow selected &&
            selected.Entry.ConvertedTargetRelativePath is null;
        MaintenanceConvertButton.IsEnabled = ConvertButton.IsEnabled;
        PendingButton.IsEnabled = anySelected && !trash;
        ProcessedButton.IsEnabled = anySelected && !trash;
        ArchiveButton.IsEnabled = anySelected && !trash;
        DeleteButton.IsEnabled = anySelected && !trash;
        RestoreButton.IsEnabled = anySelected && trash;
    }

    /// <summary>Focuses the full, read-only Markdown preview.</summary>
    /// <param name="sender">Open action.</param>
    /// <param name="e">Routed event.</param>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not InboxItemRow)
        {
            return;
        }

        PreviewTextBox.Focus();
        PreviewTextBox.CaretIndex = 0;
    }

    /// <summary>Handles double-click to inspect the selected capture.</summary>
    /// <param name="sender">Entry list.</param>
    /// <param name="e">Mouse event.</param>
    private void EntriesList_MouseDoubleClick(
            object sender, MouseButtonEventArgs e)
    {
        Open_Click(sender, e);
    }

    /// <summary>Opens a new explicit capture without navigating workspaces.</summary>
    /// <param name="sender">New capture button.</param>
    /// <param name="e">Routed click event.</param>
    private async void CaptureNew_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Inbox · nouvelle capture", async () =>
        {
            InboxCaptureDialog dialog = new InboxCaptureDialog(
                _workspaceRoot, "inbox-view")
            { Owner = this };
            try
            {
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                if (dialog.SavedEntry is null)
                {
                    throw new InvalidOperationException(
                        "Capture Inbox non enregistrée.");
                }

                WorkspaceChanged = true;
                TrashModeCheckBox.IsChecked = false;
                await RefreshAsync();
            }
            finally
            {
                dialog.CleanupTemporaryCapture();
            }
        });
    }

    /// <summary>
    /// Converts a single capture through the standard Markdown formats,
    /// keeping the source item recoverable and refreshing workspace navigation.
    /// </summary>
    /// <param name="sender">Conversion button or context menu.</param>
    /// <param name="e">Click event.</param>
    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (TrashModeCheckBox.IsChecked == true ||
            EntriesList.SelectedItems.Count != 1 ||
            EntriesList.SelectedItem is not InboxItemRow selected ||
            selected.Entry.ConvertedTargetRelativePath is not null)
        {
            return;
        }

        await UiActionGuard.RunAsync(this, "Inbox · choisir la conversion", async () =>
        {
            InboxConversionDialog dialog = new InboxConversionDialog(
                _workspaceRoot, selected.Entry, _preferredContextPath)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            WorkspaceChanged = true;
            await RefreshAsync();
            StatusText.Text = dialog.Result.AlreadyConverted
                ? "Conversion existante reprise sans doublon · " +
                    dialog.Result.TargetRelativePath
                : "Capture convertie · " + dialog.Result.TargetRelativePath;
        });
    }

    /// <summary>
    /// Converts an Inbox capture into one or two structured maintenance blocks,
    /// preserving its original text, attachments and recoverable source.
    /// </summary>
    /// <param name="sender">Toolbar or contextual Inbox action.</param>
    /// <param name="e">WPF click event.</param>
    private async void ConvertMaintenance_Click(object sender, RoutedEventArgs e)
    {
        if (TrashModeCheckBox.IsChecked == true ||
            EntriesList.SelectedItems.Count != 1 ||
            EntriesList.SelectedItem is not InboxItemRow selected ||
            selected.Entry.ConvertedTargetRelativePath is not null)
        {
            return;
        }

        await UiActionGuard.RunAsync(this, "Inbox · maintenance", async () =>
        {
            InboxMaintenanceConversionDialog dialog = new InboxMaintenanceConversionDialog(
                _workspaceRoot, selected.Entry, _preferredContextPath,
                _prepareMaintenanceDocument) { Owner = this };

            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            WorkspaceChanged = true;
            await RefreshAsync();
            StatusText.Text = dialog.Result.AlreadyConverted
                ? "Reprise de maintenance confirmée sans doublon · " +
                    dialog.Result.TargetRelativePath
                : "Historique de maintenance enrichi · " + dialog.Result.TargetRelativePath;
        }, _workspaceRoot);
    }

    /// <summary>Marks selected captures as pending.</summary>
    /// <param name="sender">Action source.</param>
    /// <param name="e">Routed click event.</param>
    private async void Pending_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchAsync(InboxBatchAction.Pending);
    }

    /// <summary>Marks selected captures as processed.</summary>
    /// <param name="sender">Action source.</param>
    /// <param name="e">Routed click event.</param>
    private async void Processed_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchAsync(InboxBatchAction.Processed);
    }

    /// <summary>Archives selected Inbox entries, retaining their history.</summary>
    /// <param name="sender">Action source.</param>
    /// <param name="e">Routed click event.</param>
    private async void Archive_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchAsync(InboxBatchAction.Archived);
    }

    /// <summary>Moves selected entries into the recoverable Inbox trash.</summary>
    /// <param name="sender">Action source.</param>
    /// <param name="e">Routed click event.</param>
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchAsync(InboxBatchAction.Delete);
    }

    /// <summary>Restores one or several selected recoverable entries.</summary>
    /// <param name="sender">Action source.</param>
    /// <param name="e">Routed click event.</param>
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        await RunBatchAsync(InboxBatchAction.Restore);
    }

    /// <summary>
    /// Runs each chosen operation independently and logs each individual failure.
    /// A failure on one item cannot silently discard other selected items.
    /// </summary>
    /// <param name="action">Batch triage operation.</param>
    /// <returns>Task for the protected batch.</returns>
    private async Task RunBatchAsync(InboxBatchAction action)
    {
        if (EntriesList.SelectedItems.Count == 0)
        {
            return;
        }

        bool trash = TrashModeCheckBox.IsChecked == true;
        if ((action == InboxBatchAction.Restore) != trash)
        {
            return;
        }

        Guid[] ids = EntriesList.SelectedItems
            .OfType<InboxItemRow>()
            .Select(item => item.Entry.Id)
            .Distinct()
            .ToArray();

        if (action == InboxBatchAction.Delete)
        {
            MessageBoxResult consent = MessageBox.Show(
                this, "Déplacer " + ids.Length +
                " capture(s) dans la corbeille Inbox ?\nElles resteront récupérables.",
                "Supprimer des captures",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (consent != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await UiActionGuard.RunAsync(this, "Inbox · traitement par lot", async () =>
        {
            List<string> failures = new List<string>();
            int successCount = 0;
            foreach (Guid id in ids)
            {
                try
                {
                    switch (action)
                    {
                        case InboxBatchAction.Delete:
                            await _store.MoveToTrashAsync(id);
                            break;
                        case InboxBatchAction.Restore:
                            await _store.RestoreAsync(id);
                            break;
                        default:
                            InboxItemState newState = action switch
                            {
                                InboxBatchAction.Pending => InboxItemState.Pending,
                                InboxBatchAction.Processed => InboxItemState.Processed,
                                _ => InboxItemState.Archived
                            };
                            await _store.UpdateStateAsync(id, newState);
                            break;
                    }

                    successCount++;
                }
                catch (Exception exception) when (
                    RecoverableExceptionPolicy.CanContinueAtActionBoundary(exception))
                {
                    failures.Add(id.ToString("N") + " : " + exception.Message);
                    if (Application.Current is App app)
                    {
                        app.DiagnosticsService.LogRecoverableException(
                            "Inbox · opération par lot · " + action, exception);
                    }
                }
            }

            if (successCount > 0)
            {
                WorkspaceChanged = true;
            }

            await RefreshAsync();
            StatusText.Text = successCount + " opération(s) effectuée(s), " +
                failures.Count + " échec(s).";
            if (failures.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", failures.Take(10)),
                    "Inbox · échecs isolés",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    /// <summary>Available non-destructive and recoverable batch actions.</summary>
    private enum InboxBatchAction
    {
        Pending,
        Processed,
        Archived,
        Delete,
        Restore
    }

    /// <summary>Presentation-only projection of one canonical Inbox capture.</summary>
    public sealed record InboxItemRow(InboxEntry Entry)
    {
        public string DateDisplay => Entry.CreatedUtc.ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm");

        public string KindDisplay => Entry.Kind switch
        {
            InboxCaptureKind.Link => "Lien",
            InboxCaptureKind.Image => "Image",
            InboxCaptureKind.File => "Fichier",
            _ => "Texte"
        };

        public string StateDisplay => Entry.State switch
        {
            InboxItemState.Pending => "À traiter",
            InboxItemState.Processed => "Traité",
            _ => "Archivé"
        };

        public string Preview => Entry.ContentMarkdown.Length > 200
            ? Entry.ContentMarkdown[..200].Replace('\n', ' ') + "…"
            : Entry.ContentMarkdown.Replace('\n', ' ');

        public string TargetDisplay => string.IsNullOrWhiteSpace(
                Entry.ConvertedTargetRelativePath)
            ? Entry.Origin ?? "Capture sans origine"
            : "Cible : " + Entry.ConvertedTargetRelativePath;
    }
}
