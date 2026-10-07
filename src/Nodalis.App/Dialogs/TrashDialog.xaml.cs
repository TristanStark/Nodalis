using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Trash;
using Nodalis.Infrastructure.Trash;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

public partial class TrashDialog : Window
{
    private readonly WorkspaceTrashService _trash;
    private IReadOnlyList<TrashEntry> _entries = Array.Empty<TrashEntry>();

    /// <summary>
    /// Initializes a new instance of <see cref="TrashDialog"/>.
    /// </summary>
    /// <param name="trash">The workspace trash service.</param>
    public TrashDialog(
            WorkspaceTrashService trash)
    {
        ArgumentNullException.ThrowIfNull(trash);

        _trash = trash;

        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    public bool WorkspaceChanged { get; private set; }

    /// <summary>
    /// Updates restore availability when the selected trash entry changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selection event arguments.</param>
    private void EntriesList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        RestoreButton.IsEnabled =
            EntriesList.SelectedItem is TrashEntry;
    }

    /// <summary>
    /// Restores the selected item after checking that the original path is still free.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Restore_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Corbeille · restaurer",
            async () =>
            {
            if (EntriesList.SelectedItem is not TrashEntry entry)
            {
                return;
            }
    
            global::System.Windows.MessageBoxResult answer = MessageBox.Show(
                this,
                $"Restaurer « {entry.DisplayName} » vers :\n\n{entry.OriginalRelativePath}",
                "Restaurer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
    
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
    
            try
            {
                await _trash.RestoreAsync(
                    entry.EntryId);
    
                WorkspaceChanged = true;
                await RefreshAsync();
                StatusText.Text = $"Restauré · {entry.DisplayName}";
            }
            catch (TrashRestoreCollisionException exception)
            {
                MessageBox.Show(
                    this,
                    "La restauration est impossible car l'emplacement d'origine est déjà occupé.\n\n" +
                    exception.DestinationPath,
                    "Collision de restauration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException)
            {
                MessageBox.Show(
                    this,
                    $"La restauration a échoué.\n\n{exception.Message}",
                    "Corbeille",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            });
    }

    /// <summary>
    /// Permanently empties the trash after explicit confirmation.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Empty_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Corbeille · vider",
            async () =>
            {
            if (_entries.Count == 0)
            {
                return;
            }
    
            global::System.Windows.MessageBoxResult answer = MessageBox.Show(
                this,
                $"Vider définitivement la corbeille ?\n\n{_entries.Count} élément(s) seront supprimés sans possibilité de restauration.",
                "Vider la corbeille",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
    
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
    
            try
            {
                await _trash.EmptyAsync();
    
                WorkspaceChanged = true;
                await RefreshAsync();
                StatusText.Text = "Corbeille vidée.";
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                MessageBox.Show(
                    this,
                    $"La corbeille n'a pas pu être vidée.\n\n{exception.Message}",
                    "Corbeille",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            });
    }

    /// <summary>
    /// Reloads the current trash contents.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        try
        {
            _entries = await _trash.ListAsync();
            EntriesList.ItemsSource = _entries;
            RestoreButton.IsEnabled = false;
            EmptyButton.IsEnabled = _entries.Count > 0;
            StatusText.Text = _entries.Count == 0
                ? "La corbeille est vide."
                : $"{_entries.Count} élément(s) récupérable(s).";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _entries = Array.Empty<TrashEntry>();
            EntriesList.ItemsSource = null;
            RestoreButton.IsEnabled = false;
            EmptyButton.IsEnabled = false;
            StatusText.Text = exception.Message;
        }
    }
}
