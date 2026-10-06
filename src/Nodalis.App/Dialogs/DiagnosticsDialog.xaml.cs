using System.Diagnostics;
using System.IO;
using System.Windows;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays the local diagnostics path and an explicitly copyable technical report.
/// </summary>
public partial class DiagnosticsDialog : Window
{
    private readonly LocalDiagnosticsService _diagnosticsService;
    private readonly string? _workspaceRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="DiagnosticsDialog"/>.
    /// </summary>
    /// <param name="diagnosticsService">The local diagnostics service.</param>
    /// <param name="workspaceRoot">Optional workspace root included as technical context.</param>
    public DiagnosticsDialog(
            LocalDiagnosticsService diagnosticsService,
            string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(diagnosticsService);

        InitializeComponent();

        _diagnosticsService = diagnosticsService;
        _workspaceRoot = workspaceRoot;

        SessionStateText.Text =
            diagnosticsService.PreviousSessionEndedUnexpectedly
                ? "Un arrêt anormal précédent a été détecté. Les éventuels fichiers temporaires récupérables ont été placés en quarantaine."
                : "Aucun arrêt anormal précédent n'a été détecté pour cette session.";

        DiagnosticsPathText.Text =
            $"Dossier : {diagnosticsService.DiagnosticsDirectory}";

        RefreshReport();
    }

    /// <summary>
    /// Opens the local diagnostics directory with the Windows shell.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void OpenFolder_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(
                _diagnosticsService.DiagnosticsDirectory);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = _diagnosticsService.DiagnosticsDirectory,
                    UseShellExecute = true
                });
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                $"Le dossier de diagnostics n'a pas pu être ouvert.\n\n{exception.Message}",
                "Diagnostics Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copies the technical report to the Windows clipboard.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void CopyReport_Click(
            object sender,
            RoutedEventArgs e)
    {
        try
        {
            RefreshReport();
            Clipboard.SetText(
                ReportTextBox.Text);

            SessionStateText.Text =
                "Rapport technique copié dans le presse-papiers.";
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException or
            InvalidOperationException)
        {
            MessageBox.Show(
                $"Le rapport n'a pas pu être copié.\n\n{exception.Message}",
                "Diagnostics Nodalis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Regenerates the technical report shown in the dialog.
    /// </summary>
    private void RefreshReport()
    {
        ReportTextBox.Text =
            _diagnosticsService.BuildTechnicalReport(
                _workspaceRoot);
    }
}
