using System.IO;
using System.Windows;
using Nodalis.Core.Reliability;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays a read-only workspace integrity report and explicit derived-index repair actions.
/// </summary>
public partial class WorkspaceIntegrityDialog : Window
{
    private readonly WorkspaceIntegrityDiagnosticService _service;

    /// <summary>
    /// Initializes the workspace integrity dialog.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceIntegrityDialog(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _service = new WorkspaceIntegrityDiagnosticService(workspaceRoot);

        InitializeComponent();

        Loaded += WorkspaceIntegrityDialog_Loaded;
    }

    /// <summary>
    /// Runs the initial diagnostic after the dialog becomes visible.
    /// </summary>
    /// <param name="sender">The dialog.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void WorkspaceIntegrityDialog_Loaded(
            object sender,
            RoutedEventArgs e)
    {
        Loaded -= WorkspaceIntegrityDialog_Loaded;
        await RefreshAsync();
    }

    /// <summary>
    /// Re-runs the read-only diagnostic at the user's request.
    /// </summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    /// <summary>
    /// Explicitly rebuilds the derived link index and then re-runs the diagnostic.
    /// </summary>
    /// <param name="sender">The rebuild button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void RebuildIndex_Click(
            object sender,
            RoutedEventArgs e)
    {
        SetBusy(true);
        StatusText.Text = "Reconstruction explicite de l'index de liens…";

        try
        {
            await _service.RebuildDerivedIndexesAsync();
            StatusText.Text = "Index de liens reconstruit.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            StatusText.Text = $"Reconstruction impossible · {exception.Message}";
            SetBusy(false);
            return;
        }

        SetBusy(false);
        await RefreshAsync();
    }

    /// <summary>
    /// Refreshes the report without making any workspace change.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        SetBusy(true);
        StatusText.Text = "Analyse du workspace…";

        try
        {
            WorkspaceIntegrityReport report = await _service.ScanAsync();

            ResultsList.ItemsSource = report.Issues;
            SummaryText.Text =
                $"{report.ErrorCount} erreur(s) · {report.WarningCount} avertissement(s)";

            StatusText.Text =
                report.Issues.Count == 0
                    ? "Aucune incohérence détectée."
                    : report.IsStructurallyHealthy
                        ? "Structure valide ; des avertissements restent à examiner."
                        : "Des erreurs structurelles nécessitent une action explicite.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            ResultsList.ItemsSource = null;
            SummaryText.Text = "Diagnostic indisponible";
            StatusText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Enables or disables actions while an asynchronous operation is running.
    /// </summary>
    /// <param name="busy">Whether the dialog is busy.</param>
    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
        RebuildIndexButton.IsEnabled = !busy;
    }
}
