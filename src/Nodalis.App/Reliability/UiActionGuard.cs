using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App.Reliability;

/// <summary>
/// Executes asynchronous window actions behind the shared recoverable-error boundary.
/// </summary>
internal static class UiActionGuard
{
    /// <summary>
    /// Executes one asynchronous user action, reports recoverable failures locally and keeps the window usable.
    /// </summary>
    /// <param name="owner">The window that initiated the action.</param>
    /// <param name="context">Short functional context written to the local diagnostics log.</param>
    /// <param name="action">The asynchronous user action.</param>
    /// <param name="workspaceRoot">Optional workspace root used only as technical path context.</param>
    /// <returns>A task representing the protected action.</returns>
    public static async Task RunAsync(
            Window owner,
            string context,
            Func<Task> action,
            string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(
            owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            context);
        ArgumentNullException.ThrowIfNull(
            action);

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            if (Application.Current is App application)
            {
                application.DiagnosticsService.LogInformation(
                    "Action annulée · " +
                    context);
            }
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinueAtActionBoundary(
                exception))
        {
            ReportRecoverableError(
                owner,
                context,
                exception,
                workspaceRoot);
        }
    }

    /// <summary>
    /// Logs and presents one recoverable action failure without allowing the reporting UI to become a second crash source.
    /// </summary>
    /// <param name="owner">The window that initiated the action.</param>
    /// <param name="context">Functional action context.</param>
    /// <param name="exception">The captured recoverable exception.</param>
    /// <param name="workspaceRoot">Optional workspace root used only as technical path context.</param>
    private static void ReportRecoverableError(
            Window owner,
            string context,
            Exception exception,
            string? workspaceRoot)
    {
        if (Application.Current is not App application)
        {
            MessageBox.Show(
                owner,
                RecoverableExceptionPolicy.GetUserMessage(
                    exception),
                "Erreur récupérée",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        LocalDiagnosticsService diagnosticsService =
            application.DiagnosticsService;

        string errorId =
            diagnosticsService.LogRecoverableException(
                context,
                exception);

        try
        {
            RecoverableErrorDialog dialog =
                new RecoverableErrorDialog(
                    diagnosticsService,
                    workspaceRoot,
                    context,
                    errorId,
                    exception);

            if (owner.IsLoaded &&
                owner.IsVisible)
            {
                dialog.Owner =
                    owner;
            }

            dialog.ShowDialog();
        }
        catch (Exception dialogException) when (
            RecoverableExceptionPolicy.CanContinueAtActionBoundary(
                dialogException))
        {
            diagnosticsService.LogException(
                "Échec de la boîte de dialogue d'erreur récupérée",
                dialogException);

            MessageBox.Show(
                owner,
                RecoverableExceptionPolicy.GetUserMessage(
                    exception) +
                Environment.NewLine +
                Environment.NewLine +
                "Identifiant : " +
                errorId +
                Environment.NewLine +
                "Journal : " +
                diagnosticsService.ActiveLogPath,
                "Erreur récupérée",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
