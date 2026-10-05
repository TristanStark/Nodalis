using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Nodalis.Core.Backups;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Backups;

namespace Nodalis.App.Dialogs;

public partial class BackupDialog : Window
{
    private readonly WorkspaceBackupService _backup;
    private IReadOnlyList<WorkspaceBackupInfo> _entries = Array.Empty<WorkspaceBackupInfo>();
    private bool _busy;

    /// <summary>
    /// Initializes a new instance of <see cref="BackupDialog"/>.
    /// </summary>
    /// <param name="backup">The workspace backup service.</param>
    /// <param name="preferences">The current local backup preferences.</param>
    public BackupDialog(
            WorkspaceBackupService backup,
            BackupPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(preferences);

        _backup = backup;

        InitializeComponent();

        DestinationTextBox.Text =
            preferences.DestinationDirectory ?? string.Empty;
        AutomaticCheckBox.IsChecked =
            preferences.AutomaticEnabled;
        IntervalTextBox.Text =
            preferences.IntervalMinutes.ToString();
        RetentionTextBox.Text =
            preferences.RetentionCount.ToString();

        Loaded += async (_, _) => await RefreshAsync();
    }

    public BackupPreferences? Preferences { get; private set; }

    /// <summary>
    /// Marks the list as stale when the configured destination changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The text change event arguments.</param>
    private void DestinationTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        StatusText.Text =
            "Destination modifiée · cliquez sur Actualiser ou Sauvegarder.";
    }

    /// <summary>
    /// Lets the user choose the external backup directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void BrowseDestination_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Microsoft.Win32.OpenFolderDialog dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de sauvegarde Nodalis",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        DestinationTextBox.Text =
            dialog.FolderName;
        await RefreshAsync();
    }

    /// <summary>
    /// Reloads and verifies the configured backup directory.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Refresh_Click(
            object sender,
            RoutedEventArgs e) =>
            await RefreshAsync();

    /// <summary>
    /// Creates a manual verified backup.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void BackupNow_Click(
            object sender,
            RoutedEventArgs e)
    {
        BackupPreferences? preferences = ReadPreferences(
            requireDestination: true);

        if (preferences is null ||
            preferences.DestinationDirectory is null)
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                global::Nodalis.Core.Backups.WorkspaceBackupInfo created = await _backup.CreateBackupAsync(
                    preferences.DestinationDirectory,
                    preferences.RetentionCount);

                await RefreshCoreAsync(
                    preferences.DestinationDirectory);
                StatusText.Text =
                    $"Sauvegarde créée · {created.FileName} · {created.DisplaySize}";
            });
    }

    /// <summary>
    /// Restores the selected backup into a separate empty folder.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void RestoreSelected_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not WorkspaceBackupInfo backup)
        {
            return;
        }

        await RestoreArchiveAsync(
            backup.ArchivePath);
    }

    /// <summary>
    /// Lets the user choose and restore an arbitrary Nodalis backup ZIP.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void RestoreArchive_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Microsoft.Win32.OpenFileDialog dialog = new OpenFileDialog
        {
            Title = "Choisir une sauvegarde Nodalis",
            Filter = "Archive ZIP (*.zip)|*.zip|Tous les fichiers (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RestoreArchiveAsync(
            dialog.FileName);
    }

    /// <summary>
    /// Saves the automatic backup configuration locally.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Save_Click(
            object sender,
            RoutedEventArgs e)
    {
        BackupPreferences? preferences = ReadPreferences(
            requireDestination:
                AutomaticCheckBox.IsChecked == true);

        if (preferences is null)
        {
            return;
        }

        Preferences = preferences;
        DialogResult = true;
    }

    /// <summary>
    /// Updates restore availability for the selected archive.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selection event arguments.</param>
    private void BackupsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        RestoreSelectedButton.IsEnabled =
            !_busy &&
            BackupsList.SelectedItem is WorkspaceBackupInfo backup &&
            backup.Status == BackupValidationStatus.Valid;
    }

    /// <summary>
    /// Validates the controls and creates a normalized preference value.
    /// </summary>
    /// <param name="requireDestination">Whether an external destination is required.</param>
    /// <returns>The preference value, or <see langword="null"/> after a validation error.</returns>
    private BackupPreferences? ReadPreferences(
            bool requireDestination)
    {
        if (!int.TryParse(
                IntervalTextBox.Text.Trim(),
                out int intervalMinutes) ||
            intervalMinutes is < 15 or > 10_080)
        {
            ShowValidation(
                "La cadence doit être comprise entre 15 minutes et 10080 minutes (7 jours).");
            IntervalTextBox.Focus();
            return null;
        }

        if (!int.TryParse(
                RetentionTextBox.Text.Trim(),
                out int retentionCount) ||
            retentionCount is < 1 or > 100)
        {
            ShowValidation(
                "La rétention doit être comprise entre 1 et 100 sauvegardes.");
            RetentionTextBox.Focus();
            return null;
        }

        string rawDestination =
            DestinationTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(rawDestination))
        {
            if (requireDestination)
            {
                ShowValidation(
                    "Choisissez un dossier de sauvegarde situé hors du workspace.");
                DestinationTextBox.Focus();
                return null;
            }

            return new BackupPreferences
            {
                AutomaticEnabled = false,
                DestinationDirectory = null,
                IntervalMinutes = intervalMinutes,
                RetentionCount = retentionCount
            };
        }

        try
        {
            string destination = _backup.ValidateBackupDestinationPath(
                rawDestination);

            return new BackupPreferences
            {
                AutomaticEnabled =
                    AutomaticCheckBox.IsChecked == true,
                DestinationDirectory = destination,
                IntervalMinutes = intervalMinutes,
                RetentionCount = retentionCount
            };
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidOperationException or
            ArgumentException)
        {
            ShowValidation(
                exception.Message);
            DestinationTextBox.Focus();
            return null;
        }
    }

    /// <summary>
    /// Reloads and fully validates visible backups.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(
                DestinationTextBox.Text))
        {
            _entries = Array.Empty<WorkspaceBackupInfo>();
            BackupsList.ItemsSource = _entries;
            RestoreSelectedButton.IsEnabled = false;
            StatusText.Text =
                "Configurez une destination externe pour commencer.";
            return;
        }

        BackupPreferences? preferences = ReadPreferences(
            requireDestination: true);

        if (preferences?.DestinationDirectory is null)
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                await RefreshCoreAsync(
                    preferences.DestinationDirectory);
            });
    }

    /// <summary>
    /// Reloads the backup list without changing busy state.
    /// </summary>
    /// <param name="destinationDirectory">The validated backup directory.</param>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshCoreAsync(
            string destinationDirectory)
    {
        _entries = await _backup.ListBackupsAsync(
            destinationDirectory);

        BackupsList.ItemsSource = _entries;
        RestoreSelectedButton.IsEnabled = false;
        StatusText.Text = _entries.Count == 0
            ? "Aucune sauvegarde pour ce workspace."
            : $"{_entries.Count} sauvegarde(s) · validation ZIP terminée.";
    }

    /// <summary>
    /// Validates an archive before asking for a restore destination and extracting it.
    /// </summary>
    /// <param name="archivePath">The archive to restore.</param>
    /// <returns>A task representing the restore.</returns>
    private async Task RestoreArchiveAsync(
            string archivePath)
    {
        await RunBusyAsync(
            async () =>
            {
                global::Nodalis.Core.Backups.WorkspaceBackupInfo validation = await _backup.ValidateBackupAsync(
                    archivePath);

                if (validation.Status != BackupValidationStatus.Valid)
                {
                    MessageBox.Show(
                        this,
                        $"L'archive est invalide et ne sera pas restaurée.\n\n{validation.StatusMessage}",
                        "Sauvegarde invalide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                global::Microsoft.Win32.OpenFolderDialog folderDialog = new OpenFolderDialog
                {
                    Title = "Choisir un nouveau dossier vide pour la restauration",
                    Multiselect = false
                };

                if (folderDialog.ShowDialog(this) != true)
                {
                    return;
                }

                global::System.Windows.MessageBoxResult answer = MessageBox.Show(
                    this,
                    $"Restaurer la sauvegarde dans :\n\n{folderDialog.FolderName}\n\nLe dossier doit être vide et le workspace actuellement ouvert ne sera pas modifié.",
                    "Restaurer le workspace",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }

                string restored = await _backup.RestoreBackupAsync(
                    archivePath,
                    folderDialog.FolderName);

                MessageBox.Show(
                    this,
                    $"Restauration terminée dans :\n\n{restored}",
                    "Restauration terminée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusText.Text =
                    $"Restauré · {validation.FileName}";
            });
    }

    /// <summary>
    /// Runs an asynchronous backup action while disabling destructive UI actions.
    /// </summary>
    /// <param name="action">The asynchronous operation.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunBusyAsync(
            Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        ActionsPanel.IsEnabled = false;
        RestoreSelectedButton.IsEnabled = false;

        try
        {
            await action();
        }
        catch (BackupRestoreCollisionException exception)
        {
            MessageBox.Show(
                this,
                $"La restauration est impossible car le dossier cible n'est pas vide.\n\n{exception.DestinationPath}",
                "Collision de restauration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Sauvegardes",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text =
                "Opération de sauvegarde interrompue.";
        }
        finally
        {
            _busy = false;
            ActionsPanel.IsEnabled = true;
            BackupsList_SelectionChanged(
                BackupsList,
                new SelectionChangedEventArgs(
                    Selector.SelectionChangedEvent,
                    Array.Empty<object>(),
                    Array.Empty<object>()));
        }
    }

    /// <summary>
    /// Displays a local settings validation message.
    /// </summary>
    /// <param name="message">The validation message.</param>
    private void ShowValidation(
            string message)
    {
        MessageBox.Show(
            this,
            message,
            "Sauvegardes",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
