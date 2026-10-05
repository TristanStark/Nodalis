using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;

namespace Nodalis.App.Dialogs;

public partial class DocxImportPreviewDialog : Window
{
    private readonly DocxImportPreview _preview;
    private readonly Func<DocxImportCommitRequest, Task<DocxImportPlan>>
        _planBuilder;
    private readonly List<SectionRow> _sections;
    private readonly DocxImportSelectionNode _documentSelectionRoot;

    private DocxImportCommitRequest? _plannedRequest;
    private DocxImportPlan? _lastPlan;
    private bool _refreshingPlan;

    /// <summary>
    /// Initializes a new instance of <see cref="DocxImportPreviewDialog"/>.
    /// </summary>
    /// <param name="preview">The <c>preview</c> value.</param>
    /// <param name="planBuilder">The <c>planBuilder</c> value.</param>
    public DocxImportPreviewDialog(
            DocxImportPreview preview,
            Func<DocxImportCommitRequest, Task<DocxImportPlan>> planBuilder)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(planBuilder);

        _preview = preview;
        _planBuilder = planBuilder;
        _sections = preview.Sections
            .Select(
                CreateSectionRow)
            .ToList();

        _documentSelectionRoot =
            new DocxImportSelectionNode
            {
                KindLabel =
                    "Document",
                DisplayText =
                    Path.GetFileName(
                        preview.StagedImport.OriginalSourcePath)
            };

        foreach (global::Nodalis.App.Dialogs.DocxImportPreviewDialog.SectionRow section in _sections)
        {
            section.PropertyChanged += Section_PropertyChanged;
            _documentSelectionRoot.AddChild(
                section.SelectionRoot);
        }

        _documentSelectionRoot.SelectionChanged += DocumentSelectionRoot_SelectionChanged;

        InitializeComponent();

        DocumentSelectionCheckBox.DataContext =
            _documentSelectionRoot;

        SourceText.Text =
            Path.GetFileName(
                preview.StagedImport.OriginalSourcePath);

        DetectionNotesItems.ItemsSource =
            BuildDetectionDetails(
                preview);

        WarningsItems.ItemsSource =
            preview.Conflicts.Count == 0
                ? new[] { "Aucun conflit bloquant détecté." }
                : preview.Conflicts;

        ApplicationComboBox.ItemsSource =
            preview.Applications;

        ComplexityComboBox.ItemsSource =
            new ComplexityChoice[]
            {
                new ComplexityChoice(
                ProjectComplexity.Simple,
                "Simple"),
                new ComplexityChoice(
                    ProjectComplexity.Medium,
                    "Moyen"),
                new ComplexityChoice(
                    ProjectComplexity.Complex,
                    "Complexe")
            };

        ComplexityComboBox.SelectedIndex = 1;

        SectionsListBox.ItemsSource =
            _sections;

        if (_sections.Count > 0)
        {
            SectionsListBox.SelectedIndex = 0;
        }

        NewProjectNameTextBox.Text =
            preview.SuggestedNewProjectName ??
            string.Empty;

        SelectSuggestedApplication();

        bool createNew =
            preview.SuggestedProjectId is null;

        CreateNewProjectCheckBox.IsChecked =
            createNew;

        RefreshProjectChoices(
            preserveSelection: false);

        if (!createNew &&
            preview.SuggestedProjectId is Guid projectId)
        {
            ProjectComboBox.SelectedItem =
                ((IEnumerable<DocxImportTargetOption>)
                    ProjectComboBox.ItemsSource)
                .FirstOrDefault(project =>
                    project.Id == projectId);
        }

        ApplyProjectMode();

        SummaryText.Text =
            $"{_sections.Count} section(s) détectée(s) · " +
            $"{preview.StagedImport.Document.Blocks.Count} bloc(s) Word";
    }

    public DocxImportCommitRequest? CommitRequest { get; private set; }

    /// <summary>
    /// Performs the <c>BuildDetectionDetails</c> operation.
    /// </summary>
    /// <param name="preview">The <c>preview</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IReadOnlyList<string> BuildDetectionDetails(
            DocxImportPreview preview)
    {
        global::System.Collections.Generic.List<string> details = preview.Analysis.DetectionNotes.ToList();

        foreach (global::Nodalis.Core.Importing.DocxDetectedTarget candidate in preview.Analysis.ApplicationCandidates)
        {
            string evidence = candidate.Evidence.Count == 0
                ? "aucune raison détaillée"
                : string.Join(
                    " ; ",
                    candidate.Evidence);

            details.Add(
                $"Application candidate : {candidate.DisplayName} · " +
                $"{candidate.Confidence} % · {evidence}");
        }

        foreach (global::Nodalis.Core.Importing.DocxDetectedTarget candidate in preview.Analysis.ProjectCandidates)
        {
            string evidence = candidate.Evidence.Count == 0
                ? "aucune raison détaillée"
                : string.Join(
                    " ; ",
                    candidate.Evidence);

            details.Add(
                $"Projet candidat : {candidate.DisplayName} · " +
                $"{candidate.Confidence} % · {evidence}");
        }

        if (!string.IsNullOrWhiteSpace(
                preview.Analysis.ProposedApplicationName))
        {
            details.Add(
                $"Application mentionnée à créer ou corriger : " +
                preview.Analysis.ProposedApplicationName);
        }

        if (!string.IsNullOrWhiteSpace(
                preview.Analysis.ProposedProjectName))
        {
            details.Add(
                $"Projet mentionné à créer : " +
                preview.Analysis.ProposedProjectName);
        }

        return details;
    }

    /// <summary>
    /// Performs the <c>SelectSuggestedApplication</c> operation.
    /// </summary>
    private void SelectSuggestedApplication()
    {
        global::Nodalis.Core.Importing.DocxImportTargetOption? suggested = _preview.SuggestedApplicationId is Guid applicationId
            ? _preview.Applications.FirstOrDefault(application =>
                application.Id == applicationId)
            : null;

        ApplicationComboBox.SelectedItem =
            suggested ??
            _preview.Applications.FirstOrDefault();
    }

    /// <summary>
    /// Performs the <c>ApplicationComboBox_SelectionChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void ApplicationComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        RefreshProjectChoices(
            preserveSelection: false);
        InvalidatePlan();
    }

    /// <summary>
    /// Performs the <c>TargetSelection_Changed</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void TargetSelection_Changed(
            object sender,
            SelectionChangedEventArgs e)
    {
        InvalidatePlan();
    }

    /// <summary>
    /// Performs the <c>TargetText_Changed</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void TargetText_Changed(
            object sender,
            TextChangedEventArgs e)
    {
        InvalidatePlan();
    }

    /// <summary>
    /// Performs the <c>CreateNewProjectCheckBox_Changed</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void CreateNewProjectCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
    {
        ApplyProjectMode();
        InvalidatePlan();
    }

    /// <summary>
    /// Performs the <c>Section_PropertyChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Section_PropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(SectionRow.Include) or
            nameof(SectionRow.TargetSection))
        {
            InvalidatePlan();
        }
    }

    /// <summary>
    /// Performs the <c>RefreshProjectChoices</c> operation.
    /// </summary>
    /// <param name="preserveSelection">The <c>preserveSelection</c> value.</param>
    private void RefreshProjectChoices(
            bool preserveSelection)
    {
        if (ApplicationComboBox.SelectedItem is not
            DocxImportTargetOption application)
        {
            ProjectComboBox.ItemsSource =
                Array.Empty<DocxImportTargetOption>();
            ProjectComboBox.SelectedItem = null;
            return;
        }

        global::System.Guid? previousId = preserveSelection &&
                         ProjectComboBox.SelectedItem is
                             DocxImportTargetOption previous
            ? previous.Id
            : (Guid?)null;

        global::Nodalis.Core.Importing.DocxImportTargetOption[] projects = _preview.Projects
            .Where(project =>
                project.ApplicationId ==
                application.Id)
            .OrderBy(project =>
                project.QualifiedName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ProjectComboBox.ItemsSource =
            projects;

        global::Nodalis.Core.Importing.DocxImportTargetOption? selected = previousId is Guid id
            ? projects.FirstOrDefault(project =>
                project.Id == id)
            : null;

        if (selected is null &&
            _preview.SuggestedProjectId is Guid suggestedId)
        {
            selected = projects.FirstOrDefault(project =>
                project.Id == suggestedId);
        }

        ProjectComboBox.SelectedItem =
            selected ??
            projects.FirstOrDefault();
    }

    /// <summary>
    /// Performs the <c>ApplyProjectMode</c> operation.
    /// </summary>
    private void ApplyProjectMode()
    {
        bool createNew =
            CreateNewProjectCheckBox.IsChecked == true;

        ProjectComboBox.IsEnabled =
            !createNew;
        NewProjectNameTextBox.IsEnabled =
            createNew;
        ComplexityComboBox.IsEnabled =
            createNew;
    }

    /// <summary>
    /// Performs the <c>InvalidatePlan</c> operation.
    /// </summary>
    private void InvalidatePlan()
    {
        _plannedRequest = null;
        _lastPlan = null;

        if (ImportButton is not null)
        {
            ImportButton.Content =
                "Prévisualiser les fichiers";
        }

        if (PlannedChangesList is not null)
        {
            PlannedChangesList.ItemsSource = null;
        }

        if (PlanWarningsItems is not null)
        {
            PlanWarningsItems.ItemsSource = null;
        }

        if (PlanTargetText is not null)
        {
            PlanTargetText.Text =
                "Le plan doit être recalculé après les modifications.";
        }
    }

    /// <summary>
    /// Performs the <c>ImportTabs_SelectionChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void ImportTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(
                e.Source,
                ImportTabs) ||
            !IsLoaded ||
            FilesTab.IsSelected != true)
        {
            return;
        }

        await RefreshPlanAsync(
            showValidationMessages: false);
    }

    /// <summary>
    /// Performs the <c>Import_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private async void Import_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Nodalis.Core.Importing.DocxImportCommitRequest? request = BuildCurrentRequest(
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
            FilesTab.IsSelected = true;

            if (!await RefreshPlanAsync(
                    showValidationMessages: true))
            {
                return;
            }

            SummaryText.Text =
                "Vérifiez le plan d'écriture puis cliquez de nouveau sur « Valider l'import ».";
            return;
        }

        CommitRequest = request;
        DialogResult = true;
    }

    /// <summary>
    /// Performs the <c>RefreshPlanAsync</c> operation.
    /// </summary>
    /// <param name="showValidationMessages">The <c>showValidationMessages</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task<bool> RefreshPlanAsync(
            bool showValidationMessages)
    {
        if (_refreshingPlan)
        {
            return false;
        }

        global::Nodalis.Core.Importing.DocxImportCommitRequest? request = BuildCurrentRequest(
            showValidationMessages);

        if (request is null)
        {
            PlanTargetText.Text =
                "Complétez la cible et sélectionnez au moins une section pour calculer le plan.";
            PlannedChangesList.ItemsSource = null;
            PlanWarningsItems.ItemsSource = null;
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
            _refreshingPlan = true;
            ImportButton.IsEnabled = false;
            ImportButton.Content =
                "Calcul du plan…";
            PlanTargetText.Text =
                "Calcul des fichiers qui seront créés ou modifiés…";

            global::Nodalis.Core.Importing.DocxImportPlan plan = await _planBuilder(
                request);

            _plannedRequest = request;
            _lastPlan = plan;

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
            _plannedRequest = null;
            _lastPlan = null;

            PlannedChangesList.ItemsSource = null;
            PlanWarningsItems.ItemsSource =
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
                    $"Le plan d'import n'a pas pu être calculé.\n\n{exception.Message}",
                    "Import DOCX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return false;
        }
        finally
        {
            _refreshingPlan = false;
            ImportButton.IsEnabled = true;

            if (_lastPlan is null)
            {
                ImportButton.Content =
                    "Prévisualiser les fichiers";
            }
        }
    }

    /// <summary>
    /// Performs the <c>DisplayPlan</c> operation.
    /// </summary>
    /// <param name="plan">The <c>plan</c> value.</param>
    private void DisplayPlan(
            DocxImportPlan plan)
    {
        PlanTargetText.Text =
            $"{(plan.CreatesProject ? "Nouveau projet" : "Projet existant")} · " +
            $"{plan.TargetProjectDisplayName}\n" +
            plan.TargetProjectRelativePath;

        PlannedChangesList.ItemsSource =
            plan.Changes;

        PlanWarningsItems.ItemsSource =
            plan.Warnings.Count == 0
                ? new[] { "Aucun conflit détecté pour ce plan." }
                : plan.Warnings;

        SummaryText.Text =
            $"Plan prêt · {plan.Changes.Count} opération(s) fichier · " +
            $"{plan.Warnings.Count} avertissement(s)";
    }

    /// <summary>
    /// Performs the <c>BuildCurrentRequest</c> operation.
    /// </summary>
    /// <param name="showValidationMessages">The <c>showValidationMessages</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private DocxImportCommitRequest? BuildCurrentRequest(
            bool showValidationMessages)
    {
        void Show(
            string message,
            FrameworkElement? focus = null)
        {
            if (!showValidationMessages)
            {
                return;
            }

            MessageBox.Show(
                this,
                message,
                "Import DOCX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            focus?.Focus();
        }

        if (ApplicationComboBox.SelectedItem is not
            DocxImportTargetOption application)
        {
            Show(
                "Sélectionnez une application cible.",
                ApplicationComboBox);
            return null;
        }

        bool createNew =
            CreateNewProjectCheckBox.IsChecked == true;

        DocxImportTargetOption? project = null;

        if (createNew)
        {
            if (string.IsNullOrWhiteSpace(
                    NewProjectNameTextBox.Text))
            {
                Show(
                    "Indiquez le nom du nouveau projet.",
                    NewProjectNameTextBox);
                return null;
            }
        }
        else
        {
            project =
                ProjectComboBox.SelectedItem as
                    DocxImportTargetOption;

            if (project is null)
            {
                Show(
                    "Sélectionnez un projet existant ou activez la création d'un nouveau projet.",
                    ProjectComboBox);
                return null;
            }
        }

        global::Nodalis.App.Dialogs.DocxImportPreviewDialog.SectionRow[] included = _sections
            .Where(section =>
                section.Include)
            .ToArray();

        if (included.Length == 0)
        {
            Show(
                "Sélectionnez au moins une section à importer.",
                SectionsListBox);
            return null;
        }

        if (included.Any(section =>
                string.IsNullOrWhiteSpace(
                    section.TargetSection)))
        {
            Show(
                "Chaque section incluse doit avoir une section Nodalis cible.",
                SectionsListBox);
            return null;
        }

        global::Nodalis.Core.Domain.ProjectComplexity complexity =
            ComplexityComboBox.SelectedItem is
                ComplexityChoice choice
                ? choice.Complexity
                : ProjectComplexity.Medium;

        return new DocxImportCommitRequest
        {
            ApplicationId = application.Id,
            ProjectId = createNew
                ? null
                : project!.Id,
            NewProjectName = createNew
                ? NewProjectNameTextBox.Text.Trim()
                : null,
            NewProjectComplexity = complexity,
            Sections = _sections
                .Select(section =>
                    new DocxImportSectionSelection
                    {
                        SectionIndex = section.Index,
                        Include = section.Include,
                        TargetSection =
                            section.TargetSection.Trim(),
                        SelectedBlockIndexes =
                            section.SelectionRoot
                                .GetSelectedBlockIndexes()
                                .ToList()
                    })
                .ToList()
        };
    }

    /// <summary>
    /// Performs the <c>RequestsEquivalent</c> operation.
    /// </summary>
    /// <param name="left">The <c>left</c> value.</param>
    /// <param name="right">The <c>right</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool RequestsEquivalent(
            DocxImportCommitRequest left,
            DocxImportCommitRequest right)
    {
        if (left.ApplicationId != right.ApplicationId ||
            left.ProjectId != right.ProjectId ||
            !string.Equals(
                left.NewProjectName,
                right.NewProjectName,
                StringComparison.CurrentCulture) ||
            left.NewProjectComplexity !=
            right.NewProjectComplexity ||
            left.Sections.Count !=
            right.Sections.Count)
        {
            return false;
        }

        for (int index = 0;
             index < left.Sections.Count;
             index++)
        {
            global::Nodalis.Core.Importing.DocxImportSectionSelection leftSection =
                left.Sections[index];
            global::Nodalis.Core.Importing.DocxImportSectionSelection rightSection =
                right.Sections[index];

            if (leftSection.SectionIndex !=
                    rightSection.SectionIndex ||
                leftSection.Include !=
                    rightSection.Include ||
                !string.Equals(
                    leftSection.TargetSection,
                    rightSection.TargetSection,
                    StringComparison.CurrentCultureIgnoreCase) ||
                !BlockSelectionsEquivalent(
                    leftSection.SelectedBlockIndexes,
                    rightSection.SelectedBlockIndexes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether two optional fine-grained block selections have identical semantics.
    /// </summary>
    /// <param name="left">The first optional block selection.</param>
    /// <param name="right">The second optional block selection.</param>
    /// <returns><see langword="true"/> when both are null or contain the same ordered indexes.</returns>
    private static bool BlockSelectionsEquivalent(
            IReadOnlyList<int>? left,
            IReadOnlyList<int>? right)
    {
        if (left is null ||
            right is null)
        {
            return left is null &&
                   right is null;
        }

        return left.SequenceEqual(
            right);
    }

    public sealed class SectionRow : INotifyPropertyChanged
    {
        private string _targetSection = string.Empty;
        private string _markdownPreview = string.Empty;

        public required int Index { get; init; }

        public required string SourceHeading { get; init; }

        public required int BlockCount { get; init; }

        public required DocxImportSectionPreview Preview { get; init; }

        public required DocxImportSelectionNode SelectionRoot { get; init; }

        public bool Include =>
            SelectionRoot.IsSelected != false;

        public string MarkdownPreview
        {
            get => _markdownPreview;
            private set
            {
                if (string.Equals(
                        _markdownPreview,
                        value,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _markdownPreview = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(MarkdownPreview)));
            }
        }

        public required string TargetSection
        {
            get => _targetSection;
            set
            {
                if (string.Equals(
                        _targetSection,
                        value,
                        StringComparison.CurrentCulture))
                {
                    return;
                }

                _targetSection = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(
                        nameof(TargetSection)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Refreshes the live Markdown preview and notifies bindings that the section's tri-state inclusion changed.
        /// </summary>
        /// <param name="markdownPreview">The Markdown produced by the current block selection.</param>
        public void RefreshSelection(
                string markdownPreview)
        {
            MarkdownPreview =
                markdownPreview;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    nameof(Include)));
        }

        /// <summary>
        /// Initializes the section's live Markdown preview without changing its selection state.
        /// </summary>
        /// <param name="markdownPreview">The initial all-selected preview.</param>
        public void InitializeMarkdownPreview(
                string markdownPreview)
        {
            _markdownPreview =
                markdownPreview;
        }
    }

    public sealed record ComplexityChoice(
        ProjectComplexity Complexity,
        string DisplayName);
}
