using System.ComponentModel;
using System.IO;
using System.Windows;
using Nodalis.App.Commands;
using Nodalis.App.Dialogs;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Inbox;

namespace Nodalis.App;

public partial class MainWindow
{
    private GlobalInboxHotkey? _globalInboxHotkey;
    private bool _inboxCaptureOpen;

    /// <summary>Hooks the native global Inbox capture shortcut into the window lifetime.</summary>
    private void InitializeInboxGlobalShortcut()
    {
        SourceInitialized += Inbox_SourceInitialized;
        Closed += Inbox_Closed;
    }

    /// <summary>Registers the shortcut only after Windows has created the HWND.</summary>
    /// <param name="sender">Window sender.</param>
    /// <param name="e">Source-initialization event.</param>
    private void Inbox_SourceInitialized(object? sender, EventArgs e)
    {
        if (_workspaceReadOnly)
        {
            return;
        }

        try
        {
            _globalInboxHotkey = new GlobalInboxHotkey(this);
            _globalInboxHotkey.Triggered += InboxHotkey_Triggered;
            _globalInboxHotkey.Register(GetInboxGesture());
        }
        catch (Exception exception) when (
            exception is Win32Exception or ArgumentException or InvalidOperationException)
        {
            ReportRecoverableUiError("Enregistrement du raccourci global Inbox",
                exception);
        }
    }

    /// <summary>Releases the native registration on window destruction.</summary>
    /// <param name="sender">Window sender.</param>
    /// <param name="e">Closed event.</param>
    private void Inbox_Closed(object? sender, EventArgs e)
    {
        if (_globalInboxHotkey is null)
        {
            return;
        }

        _globalInboxHotkey.Triggered -= InboxHotkey_Triggered;
        _globalInboxHotkey.Dispose();
        _globalInboxHotkey = null;
    }

    /// <summary>Dispatches an explicit capture command using the standard error boundary.</summary>
    /// <param name="sender">Global registration source.</param>
    /// <param name="e">Shortcut trigger.</param>
    private async void InboxHotkey_Triggered(object? sender, EventArgs e)
    {
        await RunUiActionAsync("Capture globale Inbox", ShowInboxCaptureAsync);
    }

    /// <summary>Retrieves the locally configured Inbox shortcut, preserving the default.</summary>
    /// <returns>The preferred gesture.</returns>
    private string GetInboxGesture()
    {
        if (_preferences.ShortcutOverrides is not null &&
            _preferences.ShortcutOverrides.TryGetValue(
                GlobalInboxHotkey.PreferenceKey, out string? gesture) &&
            !string.IsNullOrWhiteSpace(gesture))
        {
            return gesture;
        }

        return GlobalInboxHotkey.DefaultGesture;
    }

    /// <summary>
    /// Opens one voluntary user-initiated capture and persists selected content.
    /// The clipboard is read only after the user clicks its import button.
    /// </summary>
    /// <returns>A task representing the explicit capture.</returns>
    private async Task ShowInboxCaptureAsync()
    {
        if (_workspaceReadOnly || _inboxCaptureOpen)
        {
            return;
        }

        _inboxCaptureOpen = true;
        InboxCaptureDialog? dialog = null;
        try
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
            dialog = new InboxCaptureDialog(_root.FullPath)
            {
                Owner = this,
                Topmost = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            InboxEntry created = dialog.SavedEntry ??
                throw new InvalidOperationException(
                    "La capture n'a pas été enregistrée par la fenêtre Inbox.");

            StatusText.Text = "Capture Inbox enregistrée · " +
                created.CreatedUtc.ToLocalTime().ToString("HH:mm");
            await RefreshInboxDashboardCountAsync();
        }
        finally
        {
            if (dialog is not null)
            {
                try
                {
                    dialog.CleanupTemporaryCapture();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    ReportRecoverableUiError(
                        "Nettoyage de la capture temporaire",
                        exception,
                        showDialog: false);
                }
            }

            _inboxCaptureOpen = false;
        }
    }

    /// <summary>Opens the native Inbox triage window from workspace navigation.</summary>
    /// <param name="sender">Clicked control.</param>
    /// <param name="e">Routed click event.</param>
    private async void OpenInbox_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Inbox · consulter", ShowInboxAsync);
    }

    /// <summary>Opens the Inbox capture dialog from a menu command.</summary>
    /// <param name="sender">Clicked control.</param>
    /// <param name="e">Routed click event.</param>
    private async void CaptureInbox_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Inbox · capturer", ShowInboxCaptureAsync);
    }

    /// <summary>Shows the dedicated Inbox window without changing workspaces.</summary>
    /// <returns>Task representing the user action.</returns>
    private async Task ShowInboxAsync()
    {
        if (_workspaceReadOnly)
        {
            StatusText.Text = "Inbox indisponible dans le mode de secours en lecture seule.";
            return;
        }

        InboxDialog dialog = new InboxDialog(
            _root.FullPath, _selectedNode?.FullPath, FlushMaintenanceTabAsync)
        {
            Owner = this
        };

        dialog.ShowDialog();
        if (dialog.WorkspaceChanged)
        {
            // A converted Markdown document/task must become immediately
            // visible to existing navigation, link search and task readers.
            await RefreshNavigationAsync();
            await RefreshDashboardTasksAsync();
            StatusText.Text = "Inbox mise à jour · navigation et tâches actualisées.";
        }

        await RefreshInboxDashboardCountAsync();
    }

    /// <summary>Refreshes the discreet count of unprocessed captures.</summary>
    /// <returns>Task for the asynchronous workspace-local count.</returns>
    private async Task RefreshInboxDashboardCountAsync()
    {
        if (_workspaceReadOnly)
        {
            InboxDashboardButton.Content = "Inbox";
            return;
        }

        WorkspaceInboxService service = new WorkspaceInboxService(_root.FullPath);
        InboxReadResult result = await service.ReadAllAsync();
        int pendingCount = result.Entries.Count(entry =>
            entry.State == InboxItemState.Pending);
        InboxDashboardButton.Content = "Inbox (" + pendingCount + ")";
        InboxDashboardButton.ToolTip = result.Warnings.Count > 0
            ? result.Warnings.Count + " avertissement(s), ouvrir Inbox pour détails."
            : "Captures non traitées · ouvrir Inbox";
    }
}
