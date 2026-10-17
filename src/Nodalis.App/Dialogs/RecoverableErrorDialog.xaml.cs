using System.Windows;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Presents one recoverable application error without exposing a raw stack trace as the primary message.
/// </summary>
public partial class RecoverableErrorDialog : Window
{
    private readonly LocalDiagnosticsService _diagnosticsService;
    private readonly string? _workspaceRoot;
    private readonly string _technicalDetails;

    /// <summary>
    /// Initializes a new recoverable-error dialog.
    /// </summary>
    /// <param name="diagnosticsService">The local diagnostics service.</param>
    /// <param name="workspaceRoot">The current workspace root when available.</param>
    /// <param name="context">The functional action context.</param>
    /// <param name="errorId">The stable local error identifier.</param>
    /// <param name="exception">The captured recoverable exception.</param>
    public RecoverableErrorDialog(
            LocalDiagnosticsService diagnosticsService,
            string? workspaceRoot,
            string context,
            string errorId,
            Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            diagnosticsService);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            context);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorId);
        ArgumentNullException.ThrowIfNull(
            exception);

        InitializeComponent();

        _diagnosticsService =
            diagnosticsService;
        _workspaceRoot =
            workspaceRoot;

        MessageText.Text =
            RecoverableExceptionPolicy.GetUserMessage(
                exception);
        ContextText.Text =
            context;
        ErrorIdTextBox.Text =
            errorId;

        _technicalDetails =
            "Identifiant : " +
            errorId +
            Environment.NewLine +
            "Contexte : " +
            context +
            Environment.NewLine +
            "Type : " +
            exception.GetType().FullName +
            Environment.NewLine +
            "Message : " +
            exception.Message +
            Environment.NewLine +
            "Journal : " +
            diagnosticsService.ActiveLogPath +
            Environment.NewLine +
            Environment.NewLine +
            exception;
    }

    /// <summary>
    /// Copies the error identifier and technical details to the Windows clipboard.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void CopyDetails_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(
                _technicalDetails);

            MessageText.Text =
                "Les détails techniques et l'identifiant d'erreur ont été copiés.";
        }
        catch (Exception exception) when (
            exception is
                System.Runtime.InteropServices.ExternalException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                "Le presse-papiers Windows n'est pas disponible pour le moment.",
                "Erreur récupérée",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Opens the existing local diagnostics view for logs, recovery information and report copying.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void OpenDiagnostics_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            DiagnosticsDialog dialog =
                new DiagnosticsDialog(
                    _diagnosticsService,
                    _workspaceRoot)
                {
                    Owner =
                        this
                };

            dialog.ShowDialog();
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinue(
                exception))
        {
            _diagnosticsService.LogRecoverableException(
                "Ouverture des diagnostics depuis une erreur récupérée",
                exception);

            MessageBox.Show(
                this,
                "La fenêtre Diagnostics n'a pas pu être ouverte. " +
                "Le journal reste disponible dans : " +
                _diagnosticsService.DiagnosticsDirectory,
                "Diagnostics Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Closes the error dialog and lets the user continue working.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Continue_Click(
            object sender,
            RoutedEventArgs e)
    {
        DialogResult =
            true;
    }
}
