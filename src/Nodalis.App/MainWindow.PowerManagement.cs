using Microsoft.Win32;
using System.Windows.Threading;

namespace Nodalis.App;

public partial class MainWindow
{
    private bool _dispatcherHeartbeatWasEnabledBeforeSuspend;

    /// <summary>
    /// Subscribes to Windows suspend/resume signals and releases the process-wide
    /// static event subscription when the workspace window closes.
    /// </summary>
    private void RegisterPowerNotifications()
    {
        SystemEvents.PowerModeChanged += PowerEvents_PowerModeChanged;
        Closed += (_, _) =>
        {
            SystemEvents.PowerModeChanged -= PowerEvents_PowerModeChanged;
        };
    }

    /// <summary>
    /// Resets presentation timers on power transitions without cancelling saves
    /// or triggering a full index, navigation or dashboard rebuild on wake.
    /// </summary>
    /// <param name="sender">The operating-system event source.</param>
    /// <param name="e">The Windows power-mode transition.</param>
    private void PowerEvents_PowerModeChanged(
            object? sender,
            PowerModeChangedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        PowerModes mode = e.Mode;
        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (mode == PowerModes.Suspend)
                {
                    _dispatcherHeartbeatWasEnabledBeforeSuspend =
                        _heartbeatMonitor.IsEnabled;
                    _heartbeatMonitor.SetEnabled(false);
                    _backupTimer.Stop();
                    _autosaveRefreshTimer.Stop();
                    _previewTimer.Stop();
                    _secondaryPreviewTimer?.Stop();
                    _previewParseCancellation?.Cancel();
                    _glossaryMatchCancellation?.Cancel();
                }
                else if (mode == PowerModes.Resume)
                {
                    // A suspended heartbeat must not be misreported as a
                    // multi-hour WPF freeze. Preserve the user's diagnostics
                    // enabled/disabled preference for the current session.
                    if (_dispatcherHeartbeatWasEnabledBeforeSuspend)
                    {
                        _heartbeatMonitor.SetEnabled(true);
                    }

                    // Re-arm rather than immediately run a potentially large
                    // backup on the first interactive frame after waking.
                    if (!_workspaceReadOnly)
                    {
                        _backupTimer.Stop();
                        _backupTimer.Start();
                    }

                    if (_pendingAutosaveRefreshes.Count > 0 &&
                        !_autosaveRefreshRunning)
                    {
                        _autosaveRefreshTimer.Start();
                    }
                }
            }),
            DispatcherPriority.Normal);
    }
}
