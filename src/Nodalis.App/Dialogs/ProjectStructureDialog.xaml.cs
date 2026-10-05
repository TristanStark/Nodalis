using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Projects;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Provides safe UI operations for editing one project's section structure.
/// </summary>
public partial class ProjectStructureDialog : Window
{
    private readonly WorkspaceProjectStructureService _structure;
    private readonly string _projectDirectory;

    private ProjectStructureState? _state;

    /// <summary>
    /// Initializes a new instance of <see cref="ProjectStructureDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="projectDirectory">The project directory to edit.</param>
    public ProjectStructureDialog(
            string workspaceRoot,
            string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);

        _structure =
            new WorkspaceProjectStructureService(
                workspaceRoot);
        _projectDirectory =
            Path.GetFullPath(
                projectDirectory);

        InitializeComponent();

        Loaded += async (_, _) =>
            await InitializeAsync();
    }

    /// <summary>
    /// Gets a value indicating whether this dialog changed the project structure.
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Loads templates and the current project structure.
    /// </summary>
    /// <returns>A task representing initialization.</returns>
    private async Task InitializeAsync()
    {
        try
        {
            IReadOnlyList<MarkdownTemplateDefinition> templates =
                await _structure.GetAvailableTemplatesAsync();

            List<TemplateChoice> choices =
                new List<TemplateChoice>
                {
                    new TemplateChoice(
                        "Aucun template",
                        null)
                };

            choices.AddRange(
                templates.Select(template =>
                    new TemplateChoice(
                        $"{template.Category} · {template.DisplayName}",
                        template.Key)));

            TemplateComboBox.ItemsSource =
                choices;
            TemplateComboBox.SelectedIndex =
                0;

            await RefreshAsync(
                selectedSectionId: null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Reloads the project manifest and filesystem state.
    /// </summary>
    /// <param name="selectedSectionId">The section to reselect after refresh.</param>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync(
            Guid? selectedSectionId)
    {
        _state =
            await _structure.GetStructureAsync(
                _projectDirectory);

        ProjectTitleText.Text =
            $"Structure · {_state.Project.Name}";

        SectionsList.ItemsSource =
            _state.Sections;

        ProjectSectionState? selected =
            selectedSectionId is Guid id
                ? _state.Sections.FirstOrDefault(section =>
                    section.Id ==
                    id)
                : _state.Sections.FirstOrDefault();

        SectionsList.SelectedItem =
            selected;

        if (selected is not null)
        {
            SectionsList.ScrollIntoView(
                selected);
        }

        RefreshSectionDetails();
        StatusText.Text =
            $"{_state.Sections.Count} section(s) · structure synchronisée avec .project.json";
    }

    /// <summary>
    /// Refreshes documents and valid destination sections for the current selection.
    /// </summary>
    private void RefreshSectionDetails()
    {
        ProjectSectionState? selected =
            SectionsList.SelectedItem as
            ProjectSectionState;

        if (selected is null ||
            _state is null)
        {
            DocumentsTitleText.Text =
                "DOCUMENTS";
            DocumentsList.ItemsSource =
                null;
            TargetSectionComboBox.ItemsSource =
                null;
            return;
        }

        DocumentsTitleText.Text =
            $"DOCUMENTS · {selected.Name}";
        DocumentsList.ItemsSource =
            selected.Documents;

        ProjectSectionState[] targets =
            _state.Sections
                .Where(section =>
                    section.Id !=
                    selected.Id)
                .ToArray();

        TargetSectionComboBox.ItemsSource =
            targets;

        if (targets.Length > 0)
        {
            TargetSectionComboBox.SelectedIndex =
                0;
        }
    }

    /// <summary>
    /// Refreshes right-side content after selecting another section.
    /// </summary>
    /// <param name="sender">The section list.</param>
    /// <param name="e">The selection event.</param>
    private void SectionsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e) =>
        RefreshSectionDetails();

    /// <summary>
    /// Adds a new section with an optional initial template.
    /// </summary>
    /// <param name="sender">The add button.</param>
    /// <param name="e">The routed event.</param>
    private async void AddSection_Click(
            object sender,
            RoutedEventArgs e)
    {
        string name =
            NewSectionNameTextBox.Text.Trim();

        if (name.Length ==
            0)
        {
            MessageBox.Show(
                this,
                "Saisissez un nom de section.",
                "Structure du projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        TemplateChoice? template =
            TemplateComboBox.SelectedItem as
            TemplateChoice;

        try
        {
            ProjectSectionState created =
                await _structure.AddSectionAsync(
                    _projectDirectory,
                    name,
                    template?.Key,
                    SingletonCheckBox.IsChecked ==
                    true);

            Changed =
                true;
            NewSectionNameTextBox.Text =
                string.Empty;
            SingletonCheckBox.IsChecked =
                false;

            await RefreshAsync(
                created.Id);

            StatusText.Text =
                template?.Key is null
                    ? $"Section « {created.Name} » ajoutée."
                    : $"Section « {created.Name} » créée depuis le template « {template.DisplayName} ».";
        }
        catch (Exception exception) when (
            IsExpectedStructureException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Renames the selected section without changing its stable role or identifier.
    /// </summary>
    /// <param name="sender">The rename button.</param>
    /// <param name="e">The routed event.</param>
    private async void RenameSection_Click(
            object sender,
            RoutedEventArgs e)
    {
        ProjectSectionState? selected =
            SectionsList.SelectedItem as
            ProjectSectionState;

        if (selected is null)
        {
            return;
        }

        TextPromptDialog prompt =
            new TextPromptDialog(
                "Renommer la section",
                "Nouveau nom :",
                selected.Name)
            {
                Owner =
                    this
            };

        if (prompt.ShowDialog() !=
                true ||
            string.IsNullOrWhiteSpace(
                prompt.Value))
        {
            return;
        }

        try
        {
            ProjectSectionState renamed =
                await _structure.RenameSectionAsync(
                    _projectDirectory,
                    selected.Id,
                    prompt.Value);

            Changed =
                true;

            await RefreshAsync(
                renamed.Id);

            StatusText.Text =
                $"Section renommée en « {renamed.Name} ».";
        }
        catch (Exception exception) when (
            IsExpectedStructureException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Moves the selected section one position upward in manifest order.
    /// </summary>
    /// <param name="sender">The up button.</param>
    /// <param name="e">The routed event.</param>
    private async void MoveSectionUp_Click(
            object sender,
            RoutedEventArgs e) =>
        await MoveSelectedSectionAsync(
            -1);

    /// <summary>
    /// Moves the selected section one position downward in manifest order.
    /// </summary>
    /// <param name="sender">The down button.</param>
    /// <param name="e">The routed event.</param>
    private async void MoveSectionDown_Click(
            object sender,
            RoutedEventArgs e) =>
        await MoveSelectedSectionAsync(
            1);

    /// <summary>
    /// Reorders the selected section by one position.
    /// </summary>
    /// <param name="delta">The requested position delta.</param>
    /// <returns>A task representing the reorder.</returns>
    private async Task MoveSelectedSectionAsync(
            int delta)
    {
        if (_state is null ||
            SectionsList.SelectedItem is not ProjectSectionState selected)
        {
            return;
        }

        List<ProjectSectionState> ordered =
            _state.Sections.ToList();

        int currentIndex =
            ordered.FindIndex(section =>
                section.Id ==
                selected.Id);

        int targetIndex =
            currentIndex +
            delta;

        if (currentIndex <
                0 ||
            targetIndex <
                0 ||
            targetIndex >=
                ordered.Count)
        {
            return;
        }

        ProjectSectionState temporary =
            ordered[currentIndex];
        ordered[currentIndex] =
            ordered[targetIndex];
        ordered[targetIndex] =
            temporary;

        try
        {
            await _structure.ReorderSectionsAsync(
                _projectDirectory,
                ordered.Select(section =>
                        section.Id)
                    .ToArray());

            Changed =
                true;

            await RefreshAsync(
                selected.Id);

            StatusText.Text =
                $"Ordre mis à jour · « {selected.Name} ».";
        }
        catch (Exception exception) when (
            IsExpectedStructureException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Moves the selected Markdown document to the explicit target section.
    /// </summary>
    /// <param name="sender">The move button.</param>
    /// <param name="e">The routed event.</param>
    private async void MoveDocument_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (SectionsList.SelectedItem is not ProjectSectionState sourceSection ||
            DocumentsList.SelectedItem is not string relativeDocument ||
            TargetSectionComboBox.SelectedItem is not ProjectSectionState targetSection)
        {
            MessageBox.Show(
                this,
                "Sélectionnez un document et une section de destination.",
                "Structure du projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string sourcePath =
            Path.Combine(
                sourceSection.DirectoryPath,
                relativeDocument.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        try
        {
            string destination =
                await _structure.MoveDocumentAsync(
                    _projectDirectory,
                    sourcePath,
                    targetSection.Id);

            Changed =
                true;

            await RefreshAsync(
                sourceSection.Id);

            StatusText.Text =
                $"Document déplacé vers « {targetSection.Name} » · {Path.GetFileName(destination)}";
        }
        catch (Exception exception) when (
            IsExpectedStructureException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Deletes the selected optional section, requiring an explicit destination when content remains.
    /// </summary>
    /// <param name="sender">The delete button.</param>
    /// <param name="e">The routed event.</param>
    private async void DeleteSection_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (SectionsList.SelectedItem is not ProjectSectionState selected)
        {
            return;
        }

        string[] entries =
            Directory.Exists(
                    selected.DirectoryPath)
                ? Directory
                    .EnumerateFileSystemEntries(
                        selected.DirectoryPath)
                    .ToArray()
                : [];

        Guid? destinationId =
            null;

        if (entries.Length >
            0)
        {
            if (TargetSectionComboBox.SelectedItem is not ProjectSectionState target)
            {
                MessageBox.Show(
                    this,
                    "Cette section contient encore des éléments. Choisissez d'abord une section de destination.",
                    "Structure du projet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MessageBoxResult moveConfirmation =
                MessageBox.Show(
                    this,
                    $"La section « {selected.Name} » contient {entries.Length} élément(s).\n\n" +
                    $"Déplacer explicitement tout son contenu vers « {target.Name} », puis supprimer la section ?",
                    "Supprimer la section",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (moveConfirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }

            destinationId =
                target.Id;
        }
        else
        {
            MessageBoxResult confirmation =
                MessageBox.Show(
                    this,
                    $"Supprimer la section vide « {selected.Name} » ?",
                    "Supprimer la section",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirmation !=
                MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            await _structure.RemoveSectionAsync(
                _projectDirectory,
                selected.Id,
                destinationId);

            Changed =
                true;

            await RefreshAsync(
                selectedSectionId: null);

            StatusText.Text =
                $"Section « {selected.Name} » supprimée sans perte de document.";
        }
        catch (Exception exception) when (
            IsExpectedStructureException(
                exception))
        {
            ShowError(
                exception.Message);
        }
    }

    /// <summary>
    /// Identifies failures that should be presented as safe project-structure errors.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> for expected local editing failures.</returns>
    private static bool IsExpectedStructureException(
            Exception exception) =>
        exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            KeyNotFoundException;

    /// <summary>
    /// Shows one project-structure operation error.
    /// </summary>
    /// <param name="message">The error message.</param>
    private void ShowError(
            string message)
    {
        StatusText.Text =
            message;

        MessageBox.Show(
            this,
            message,
            "Structure du projet",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Represents one optional section-template choice in the add-section form.
    /// </summary>
    /// <param name="DisplayName">The label shown in the combo box.</param>
    /// <param name="Key">The template key, or <see langword="null"/> for an empty section.</param>
    private sealed record TemplateChoice(
        string DisplayName,
        string? Key);
}
