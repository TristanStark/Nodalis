using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Importing;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Lets the user review a bulk Markdown source set, choose its target, inspect the exact write plan, and explicitly validate the import.
/// </summary>
public partial class MarkdownBulkImportDialog : Window
{
    private readonly MarkdownBulkImportPreview _preview;
    private readonly Func<MarkdownBulkImportRequest, Task<MarkdownBulkImportPlan>> _planBuilder;

    private MarkdownBulkImportRequest? _plannedRequest;
    private MarkdownBulkImportPlan? _lastPlan;
    private bool _refreshingPlan;

    /// <summary>
    /// Initializes the bulk Markdown import preview dialog.
    /// </summary>
    /// <param name="preview">The immutable source preview.</param>
    /// <param name="planBuilder">The callback used to compute the deterministic write plan.</param>
    public MarkdownBulkImportDialog(
            MarkdownBulkImportPreview preview,
            Func<MarkdownBulkImportRequest, Task<MarkdownBulkImportPlan>> planBuilder)
    {
        ArgumentNullException.ThrowIfNull(
            preview);
        ArgumentNullException.ThrowIfNull(
            planBuilder);

        InitializeComponent();

        _preview =
            preview;
        _planBuilder =
            planBuilder;

        SourceText.Text =
            preview.SourceRoot;
        SummaryText.Text =
            preview.MarkdownCount +
            " document(s) Markdown · " +
            preview.AttachmentCount +
            " pièce(s) jointe(s) référencée(s) · " +
            preview.RelativeLinkCount +
            " lien(s) relatif(s) détecté(s)";

        SourceItemsList.ItemsSource =
            preview.Items;

        SourceWarningsList.ItemsSource =
            preview.Warnings.Count == 0
                ? new[]
                {
                    "Aucun lien cassé ou fichier hors périmètre détecté."
                }
                : preview.Warnings;

        ApplicationComboBox.ItemsSource =
            preview.Applications;

        MarkdownBulkImportTargetOption? suggestedApplication =
            preview.SuggestedApplicationId is Guid applicationId
                ? preview.Applications.FirstOrDefault(application =>
                    application.Id ==
                    applicationId)
                : null;

        ApplicationComboBox.SelectedItem =
            suggestedApplication ??
            preview.Applications.FirstOrDefault();

        SectionTextBox.Text =
            preview.SuggestedSection;

        RefreshProjectChoices(
            preserveSelection: false);
    }

    /// <summary>
    /// Gets the target request accepted by the user when the dialog closes successfully.
    /// </summary>
    public MarkdownBulkImportRequest? CommitRequest { get; private set; }

    /// <summary>
    /// Refreshes project choices when the application changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selection event arguments.</param>
    private void ApplicationComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        RefreshProjectChoices(
            preserveSelection: false);
        InvalidatePlan();
    }

    /// <summary>
    /// Invalidates the cached write plan when the project selection changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selection event arguments.</param>
    private void TargetSelection_Changed(
            object sender,
            SelectionChangedEventArgs e)
    {
        InvalidatePlan();
    }

    /// <summary>
    /// Invalidates the cached write plan when the target section changes.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The text change event arguments.</param>
    private void TargetText_Changed(
            object sender,
            TextChangedEventArgs e)
    {
        InvalidatePlan();
    }

    /// <summary>
    /// Calculates the plan automatically when the user opens the plan tab.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The selection event arguments.</param>
    private async void ImportTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Import Markdown · prévisualiser",
            async () =>
            {
            if (!ReferenceEquals(
                    e.Source,
                    ImportTabs) ||
                !IsLoaded ||
                PlanTab.IsSelected !=
                true)
            {
                return;
            }
    
            await RefreshPlanAsync(
                showValidationMessages: false);
            });
    }

    /// <summary>
    /// Calculates the plan on first activation, then accepts the unchanged plan on the second activation.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void Import_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Import Markdown · importer",
            async () =>
            {
            MarkdownBulkImportRequest? request =
                BuildCurrentRequest(
                    showValidationMessages: true);
    
            if (request is null)
            {
                return;
            }
    
            if (_lastPlan is null ||
                _plannedRequest is null ||
                !RequestsEquivalent(
                    _plannedRequest,
                    request))
            {
                PlanTab.IsSelected =
                    true;
    
                if (!await RefreshPlanAsync(
                        showValidationMessages: true))
                {
                    return;
                }
    
                StatusText.Text =
                    "Plan prêt : vérifiez les destinations puis cliquez de nouveau sur « Valider l'import ».";
                return;
            }
    
            CommitRequest =
                request;
            DialogResult =
                true;
            });
    }

    /// <summary>
    /// Closes the dialog without returning an import request.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Close_Click(
            object sender,
            RoutedEventArgs e)
    {
        DialogResult =
            false;
    }

    /// <summary>
    /// Refreshes projects belonging to the selected application and applies the preview suggestion when possible.
    /// </summary>
    /// <param name="preserveSelection">Whether the current project selection should be preserved.</param>
    private void RefreshProjectChoices(
            bool preserveSelection)
    {
        if (ApplicationComboBox.SelectedItem is not
            MarkdownBulkImportTargetOption application)
        {
            ProjectComboBox.ItemsSource =
                Array.Empty<MarkdownBulkImportTargetOption>();
            ProjectComboBox.SelectedItem =
                null;
            return;
        }

        Guid? previousProjectId =
            preserveSelection &&
            ProjectComboBox.SelectedItem is
                MarkdownBulkImportTargetOption previous
                ? previous.Id
                : null;

        MarkdownBulkImportTargetOption[] projects =
            _preview.Projects
                .Where(project =>
                    project.ApplicationId ==
                    application.Id)
                .OrderBy(
                    project => project.QualifiedName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        ProjectComboBox.ItemsSource =
            projects;

        MarkdownBulkImportTargetOption? selected =
            previousProjectId is Guid previousId
                ? projects.FirstOrDefault(project =>
                    project.Id ==
                    previousId)
                : null;

        if (selected is null &&
            _preview.SuggestedProjectId is Guid suggestedId)
        {
            selected =
                projects.FirstOrDefault(project =>
                    project.Id ==
                    suggestedId);
        }

        ProjectComboBox.SelectedItem =
            selected ??
            projects.FirstOrDefault();
    }

    /// <summary>
    /// Clears the cached plan after any target change.
    /// </summary>
    private void InvalidatePlan()
    {
        _plannedRequest =
            null;
        _lastPlan =
            null;

        if (ImportButton is not null)
        {
            ImportButton.Content =
                "Prévisualiser les fichiers";
        }

        if (PlannedChangesList is not null)
        {
            PlannedChangesList.ItemsSource =
                null;
        }

        if (PlanWarningsList is not null)
        {
            PlanWarningsList.ItemsSource =
                null;
        }

        if (PlanTargetText is not null)
        {
            PlanTargetText.Text =
                "Le plan doit être recalculé après les modifications.";
        }

        if (StatusText is not null)
        {
            StatusText.Text =
                "Aucune écriture n'est effectuée avant validation.";
        }
    }

    /// <summary>
    /// Builds and validates the target request represented by the current controls.
    /// </summary>
    /// <param name="showValidationMessages">Whether validation errors should be shown to the user.</param>
    /// <returns>The valid target request, or <see langword="null"/>.</returns>
    private MarkdownBulkImportRequest? BuildCurrentRequest(
            bool showValidationMessages)
    {
        if (ApplicationComboBox.SelectedItem is not
            MarkdownBulkImportTargetOption application)
        {
            ShowValidationMessage(
                "Sélectionnez une application cible.",
                ApplicationComboBox,
                showValidationMessages);
            return null;
        }

        if (ProjectComboBox.SelectedItem is not
            MarkdownBulkImportTargetOption project)
        {
            ShowValidationMessage(
                "Sélectionnez un projet cible.",
                ProjectComboBox,
                showValidationMessages);
            return null;
        }

        string section =
            SectionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                section))
        {
            ShowValidationMessage(
                "Indiquez une section cible.",
                SectionTextBox,
                showValidationMessages);
            return null;
        }

        return new MarkdownBulkImportRequest
        {
            ApplicationId =
                application.Id,
            ProjectId =
                project.Id,
            TargetSection =
                section
        };
    }

    /// <summary>
    /// Shows one validation message and optionally focuses the related control.
    /// </summary>
    /// <param name="message">The validation message.</param>
    /// <param name="focus">The control to focus.</param>
    /// <param name="showValidationMessages">Whether the message should be displayed.</param>
    private void ShowValidationMessage(
            string message,
            FrameworkElement focus,
            bool showValidationMessages)
    {
        if (!showValidationMessages)
        {
            return;
        }

        MessageBox.Show(
            this,
            message,
            "Import Markdown",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        focus.Focus();
    }

    /// <summary>
    /// Calculates and displays the write plan for the current target.
    /// </summary>
    /// <param name="showValidationMessages">Whether target or planning errors should be shown.</param>
    /// <returns><see langword="true"/> when a plan is available.</returns>
    private async Task<bool> RefreshPlanAsync(
            bool showValidationMessages)
    {
        if (_refreshingPlan)
        {
            return false;
        }

        MarkdownBulkImportRequest? request =
            BuildCurrentRequest(
                showValidationMessages);

        if (request is null)
        {
            PlanTargetText.Text =
                "Sélectionnez une application, un projet et une section pour calculer le plan.";
            PlannedChangesList.ItemsSource =
                null;
            PlanWarningsList.ItemsSource =
                null;
            return false;
        }

        if (_lastPlan is not null &&
            _plannedRequest is not null &&
            RequestsEquivalent(
                _plannedRequest,
                request))
        {
            DisplayPlan(
                _lastPlan);
            return true;
        }

        try
        {
            _refreshingPlan =
                true;
            ImportButton.IsEnabled =
                false;
            ImportButton.Content =
                "Calcul du plan…";
            PlanTargetText.Text =
                "Analyse des collisions, doublons et destinations…";

            MarkdownBulkImportPlan plan =
                await _planBuilder(
                    request);

            _plannedRequest =
                request;
            _lastPlan =
                plan;

            DisplayPlan(
                plan);

            ImportButton.Content =
                "Valider l'import";

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            _plannedRequest =
                null;
            _lastPlan =
                null;

            PlannedChangesList.ItemsSource =
                null;
            PlanWarningsList.ItemsSource =
                new[]
                {
                    "Le plan n'a pas pu être calculé : " +
                    exception.Message
                };
            PlanTargetText.Text =
                "Plan indisponible.";

            if (showValidationMessages)
            {
                MessageBox.Show(
                    this,
                    "Le plan d'import n'a pas pu être calculé.\n\n" +
                    exception.Message,
                    "Import Markdown",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return false;
        }
        finally
        {
            _refreshingPlan =
                false;
            ImportButton.IsEnabled =
                true;

            if (_lastPlan is null)
            {
                ImportButton.Content =
                    "Prévisualiser les fichiers";
            }
        }
    }

    /// <summary>
    /// Displays a calculated write plan and its explicit warnings.
    /// </summary>
    /// <param name="plan">The plan to display.</param>
    private void DisplayPlan(
            MarkdownBulkImportPlan plan)
    {
        PlanTargetText.Text =
            plan.TargetProjectDisplayName +
            "\n" +
            plan.ImportDirectoryRelativePath +
            (plan.CreatesSection
                ? "\nLa section cible sera ajoutée au projet."
                : string.Empty);

        PlannedChangesList.ItemsSource =
            plan.Changes;

        PlanWarningsList.ItemsSource =
            plan.Warnings.Count ==
            0
                ? new[]
                {
                    "Aucun conflit ni doublon détecté pour ce plan."
                }
                : plan.Warnings;

        StatusText.Text =
            "Plan prêt · " +
            plan.Changes.Count +
            " opération(s) · " +
            plan.Warnings.Count +
            " avertissement(s)";
    }

    /// <summary>
    /// Determines whether two target requests describe the same write plan inputs.
    /// </summary>
    /// <param name="left">The first request.</param>
    /// <param name="right">The second request.</param>
    /// <returns><see langword="true"/> when both requests are equivalent.</returns>
    private static bool RequestsEquivalent(
            MarkdownBulkImportRequest left,
            MarkdownBulkImportRequest right) =>
            left.ApplicationId ==
                right.ApplicationId &&
            left.ProjectId ==
                right.ProjectId &&
            string.Equals(
                left.TargetSection,
                right.TargetSection,
                StringComparison.CurrentCultureIgnoreCase);
}
