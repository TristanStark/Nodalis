using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;

namespace Nodalis.App.Dialogs;

public partial class DocxImportPreviewDialog : Window
{
    private readonly DocxImportPreview _preview;
    private readonly List<SectionRow> _sections;

    public DocxImportPreviewDialog(
        DocxImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        _preview = preview;
        _sections = preview.Sections
            .Select(section =>
                new SectionRow
                {
                    Index = section.Index,
                    SourceHeading = section.SourceHeading,
                    TargetSection = section.SuggestedTargetSection,
                    BlockCount = section.BlockCount,
                    MarkdownPreview = section.MarkdownPreview
                })
            .ToList();

        InitializeComponent();

        SourceText.Text =
            Path.GetFileName(
                preview.StagedImport.OriginalSourcePath);

        DetectionNotesItems.ItemsSource =
            preview.Analysis.DetectionNotes;

        WarningsItems.ItemsSource =
            preview.Conflicts.Count == 0
                ? ["Aucun conflit bloquant détecté."]
                : preview.Conflicts;

        ApplicationComboBox.ItemsSource =
            preview.Applications;

        ComplexityComboBox.ItemsSource =
        [
            new ComplexityChoice(
                ProjectComplexity.Simple,
                "Simple"),
            new ComplexityChoice(
                ProjectComplexity.Medium,
                "Moyen"),
            new ComplexityChoice(
                ProjectComplexity.Complex,
                "Complexe")
        ];

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

        var createNew =
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

    private void SelectSuggestedApplication()
    {
        var suggested = _preview.SuggestedApplicationId is Guid applicationId
            ? _preview.Applications.FirstOrDefault(application =>
                application.Id == applicationId)
            : null;

        ApplicationComboBox.SelectedItem =
            suggested ??
            _preview.Applications.FirstOrDefault();
    }

    private void ApplicationComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        RefreshProjectChoices(
            preserveSelection: false);
    }

    private void CreateNewProjectCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        ApplyProjectMode();
    }

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

        var previousId = preserveSelection &&
                         ProjectComboBox.SelectedItem is
                             DocxImportTargetOption previous
            ? previous.Id
            : (Guid?)null;

        var projects = _preview.Projects
            .Where(project =>
                project.ApplicationId ==
                application.Id)
            .OrderBy(project =>
                project.QualifiedName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ProjectComboBox.ItemsSource =
            projects;

        var selected = previousId is Guid id
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

    private void ApplyProjectMode()
    {
        var createNew =
            CreateNewProjectCheckBox.IsChecked == true;

        ProjectComboBox.IsEnabled =
            !createNew;
        NewProjectNameTextBox.IsEnabled =
            createNew;
        ComplexityComboBox.IsEnabled =
            createNew;
    }

    private void Import_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ApplicationComboBox.SelectedItem is not
            DocxImportTargetOption application)
        {
            MessageBox.Show(
                this,
                "Sélectionnez une application cible.",
                "Import DOCX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var createNew =
            CreateNewProjectCheckBox.IsChecked == true;

        DocxImportTargetOption? project = null;

        if (createNew)
        {
            if (string.IsNullOrWhiteSpace(
                    NewProjectNameTextBox.Text))
            {
                MessageBox.Show(
                    this,
                    "Indiquez le nom du nouveau projet.",
                    "Import DOCX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                NewProjectNameTextBox.Focus();
                return;
            }
        }
        else
        {
            project =
                ProjectComboBox.SelectedItem as
                    DocxImportTargetOption;

            if (project is null)
            {
                MessageBox.Show(
                    this,
                    "Sélectionnez un projet existant ou activez la création d'un nouveau projet.",
                    "Import DOCX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var included = _sections
            .Where(section =>
                section.Include)
            .ToArray();

        if (included.Length == 0)
        {
            MessageBox.Show(
                this,
                "Sélectionnez au moins une section à importer.",
                "Import DOCX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (included.Any(section =>
                string.IsNullOrWhiteSpace(
                    section.TargetSection)))
        {
            MessageBox.Show(
                this,
                "Chaque section incluse doit avoir une section Nodalis cible.",
                "Import DOCX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var complexity =
            ComplexityComboBox.SelectedItem is
                ComplexityChoice choice
                ? choice.Complexity
                : ProjectComplexity.Medium;

        CommitRequest = new DocxImportCommitRequest
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
                            section.TargetSection.Trim()
                    })
                .ToList()
        };

        DialogResult = true;
    }

    public sealed class SectionRow
    {
        public required int Index { get; init; }

        public required string SourceHeading { get; init; }

        public required int BlockCount { get; init; }

        public required string MarkdownPreview { get; init; }

        public bool Include { get; set; } = true;

        public required string TargetSection { get; set; }
    }

    public sealed record ComplexityChoice(
        ProjectComplexity Complexity,
        string DisplayName);
}
