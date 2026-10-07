using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Quality;
using Nodalis.Infrastructure.Quality;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays explainable, deterministic project coverage findings.
/// </summary>
public partial class ProjectCoverageDialog : Window
{
    private readonly ProjectCoverageAnalysisService _service;
    private readonly string _projectDirectory;

    /// <summary>
    /// Initializes a new coverage dialog.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    /// <param name="projectDirectory">The project directory to analyze.</param>
    public ProjectCoverageDialog(
            string workspaceRoot,
            string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);

        _service =
            new ProjectCoverageAnalysisService(
                workspaceRoot);
        _projectDirectory =
            Path.GetFullPath(
                projectDirectory);

        InitializeComponent();

        Loaded +=
            ProjectCoverageDialog_Loaded;
    }

    /// <summary>
    /// Gets the first source of the finding selected for navigation.
    /// </summary>
    public ProjectCoverageSource? SelectedSource { get; private set; }

    /// <summary>
    /// Runs the first analysis after the dialog is loaded.
    /// </summary>
    /// <param name="sender">The dialog.</param>
    /// <param name="e">The event arguments.</param>
    private async void ProjectCoverageDialog_Loaded(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Couverture projet · chargement",
            async () =>
            {
            Loaded -=
                ProjectCoverageDialog_Loaded;
    
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Re-runs the deterministic coverage analysis.
    /// </summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The event arguments.</param>
    private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Couverture projet · actualiser",
            async () =>
            {
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Enables navigation when the selected finding has a Markdown source.
    /// </summary>
    /// <param name="sender">The results list.</param>
    /// <param name="e">The selection arguments.</param>
    private void ResultsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        OpenSourceButton.IsEnabled =
            ResultsList.SelectedItem is ProjectCoverageFinding finding &&
            finding.IsNavigable;
    }

    /// <summary>
    /// Selects a source on double-click.
    /// </summary>
    /// <param name="sender">The results list.</param>
    /// <param name="e">The mouse arguments.</param>
    private void ResultsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        SelectSourceAndClose();
    }

    /// <summary>
    /// Selects the first source of the current finding.
    /// </summary>
    /// <param name="sender">The source button.</param>
    /// <param name="e">The event arguments.</param>
    private void OpenSource_Click(
            object sender,
            RoutedEventArgs e)
    {
        SelectSourceAndClose();
    }

    /// <summary>
    /// Runs the analysis and refreshes the presentation.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        StatusText.Text =
            "Croisement des sources projet…";

        try
        {
            ProjectCoverageReport report =
                await _service.AnalyzeAsync(
                    _projectDirectory);

            ProjectText.Text =
                report.ProjectName;
            ResultsList.ItemsSource =
                report.Findings;
            SummaryText.Text =
                report.ErrorCount +
                " erreur(s) · " +
                report.WarningCount +
                " avertissement(s) · " +
                (report.Findings.Count -
                 report.ErrorCount -
                 report.WarningCount) +
                " information(s)";
            StatusText.Text =
                report.Findings.Count == 0
                    ? "Aucun trou de couverture déterministe détecté."
                    : "Analyse terminée · aucune modification appliquée.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            ResultsList.ItemsSource =
                null;
            ProjectText.Text =
                Path.GetFileName(
                    _projectDirectory);
            SummaryText.Text =
                "Analyse indisponible";
            StatusText.Text =
                exception.Message;
        }
    }

    /// <summary>
    /// Stores the first navigable source and closes the dialog.
    /// </summary>
    private void SelectSourceAndClose()
    {
        if (ResultsList.SelectedItem is not ProjectCoverageFinding finding ||
            !finding.IsNavigable)
        {
            return;
        }

        SelectedSource =
            finding.Sources[0];
        DialogResult =
            true;
    }
}
