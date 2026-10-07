using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Quality;
using Nodalis.Infrastructure.Quality;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays the deterministic read-only health report for one project.
/// </summary>
public partial class ProjectHealthDialog : Window
{
    private readonly ProjectHealthCheckService _service;
    private readonly string _projectDirectory;

    /// <summary>
    /// Initializes the project health dialog.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    /// <param name="projectDirectory">The project directory to analyze.</param>
    public ProjectHealthDialog(
            string workspaceRoot,
            string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);

        _service =
            new ProjectHealthCheckService(
                workspaceRoot);
        _projectDirectory =
            Path.GetFullPath(
                projectDirectory);

        InitializeComponent();

        Loaded +=
            ProjectHealthDialog_Loaded;
    }

    /// <summary>
    /// Gets the finding selected for navigation after the dialog closes.
    /// </summary>
    public ProjectHealthIssue? SelectedIssue { get; private set; }

    /// <summary>
    /// Runs the initial health check after the dialog becomes visible.
    /// </summary>
    /// <param name="sender">The dialog.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void ProjectHealthDialog_Loaded(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Santé projet · chargement",
            async () =>
            {
            Loaded -=
                ProjectHealthDialog_Loaded;
    
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Re-runs the health check with the current deterministic thresholds.
    /// </summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Santé projet · actualiser",
            async () =>
            {
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Enables source navigation only for findings attached to Markdown files.
    /// </summary>
    /// <param name="sender">The results list.</param>
    /// <param name="e">The selection change arguments.</param>
    private void ResultsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        OpenSourceButton.IsEnabled =
            ResultsList.SelectedItem is ProjectHealthIssue issue &&
            issue.IsNavigable;
    }

    /// <summary>
    /// Opens a selected navigable finding when the user double-clicks it.
    /// </summary>
    /// <param name="sender">The results list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void ResultsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        SelectSourceAndClose();
    }

    /// <summary>
    /// Opens the currently selected navigable finding.
    /// </summary>
    /// <param name="sender">The source button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void OpenSource_Click(
            object sender,
            RoutedEventArgs e)
    {
        SelectSourceAndClose();
    }

    /// <summary>
    /// Executes the deterministic read-only project health scan and refreshes the presentation.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        if (!int.TryParse(
                MeetingDaysTextBox.Text.Trim(),
                out int meetingDays) ||
            meetingDays < 1 ||
            meetingDays > 3650)
        {
            SummaryText.Text =
                "Paramètre invalide";
            StatusText.Text =
                "Le seuil de réunion récente doit être compris entre 1 et 3650 jours.";
            return;
        }

        SetBusy(
            true);
        StatusText.Text =
            "Analyse locale du projet…";

        try
        {
            ProjectHealthOptions options =
                new ProjectHealthOptions
                {
                    MeetingRecencyDays =
                        meetingDays
                };

            ProjectHealthReport report =
                await _service.ScanAsync(
                    _projectDirectory,
                    options);

            ProjectText.Text =
                report.ProjectName;
            ResultsList.ItemsSource =
                report.Issues;
            SummaryText.Text =
                report.ErrorCount +
                " erreur(s) · " +
                report.WarningCount +
                " avertissement(s)";

            StatusText.Text =
                report.Issues.Count == 0
                    ? "Aucun problème actionnable détecté."
                    : report.IsHealthy
                        ? "Aucune erreur ; des avertissements restent à examiner."
                        : "Des problèmes nécessitent une intervention manuelle.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentOutOfRangeException)
        {
            ResultsList.ItemsSource =
                null;
            ProjectText.Text =
                Path.GetFileName(
                    _projectDirectory);
            SummaryText.Text =
                "Health Check indisponible";
            StatusText.Text =
                exception.Message;
        }
        finally
        {
            SetBusy(
                false);
        }
    }

    /// <summary>
    /// Stores the selected Markdown finding and closes the modal dialog for navigation by the main window.
    /// </summary>
    private void SelectSourceAndClose()
    {
        if (ResultsList.SelectedItem is not ProjectHealthIssue issue ||
            !issue.IsNavigable)
        {
            return;
        }

        SelectedIssue =
            issue;
        DialogResult =
            true;
    }

    /// <summary>
    /// Enables or disables interactive controls while the scan is running.
    /// </summary>
    /// <param name="busy">Whether the dialog is busy.</param>
    private void SetBusy(
            bool busy)
    {
        RefreshButton.IsEnabled =
            !busy;
        MeetingDaysTextBox.IsEnabled =
            !busy;
        OpenSourceButton.IsEnabled =
            !busy &&
            ResultsList.SelectedItem is ProjectHealthIssue issue &&
            issue.IsNavigable;
    }
}
