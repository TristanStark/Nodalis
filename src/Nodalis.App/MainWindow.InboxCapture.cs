using System.ComponentModel;
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
    /// Opens one modal user-initiated text capture and persists it as an Inbox entry.
    /// No clipboard read or screenshot is performed on a hotkey press.
    /// </summary>
    /// <returns>A task representing the explicit capture.</returns>
    private async Task ShowInboxCaptureAsync()
    {
        if (_workspaceReadOnly || _inboxCaptureOpen)
        {
            return;
        }

        _inboxCaptureOpen = true;
        try
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
            InboxCaptureDialog dialog = new InboxCaptureDialog
            {
                Owner = this,
                Topmost = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            WorkspaceInboxService inbox = new WorkspaceInboxService(_root.FullPath);
            InboxEntry created = await inbox.CaptureAsync(new InboxCaptureRequest
            {
                Kind = InboxCaptureKind.Text,
                ContentMarkdown = dialog.CapturedMarkdown,
                Origin = "global-shortcut"
            });

            StatusText.Text = "Capture Inbox enregistrée · " +
                created.CreatedUtc.ToLocalTime().ToString("HH:mm");
        }
        finally
        {
            _inboxCaptureOpen = false;
        }
    }
}
