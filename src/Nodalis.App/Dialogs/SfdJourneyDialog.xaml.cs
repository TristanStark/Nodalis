using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-controlled SFD journey editor, isolated from legacy v2 journeys.
/// No source import, no AI invocation, no automatic status promotion.
/// </summary>
public partial class SfdJourneyDialog : Window
{
    private readonly SfdJourneyService _service;
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdCompletionReviewService _review;
    private readonly ProjectJourneySourceService _sources;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private ProjectJourneySnapshot? _snapshot;
    private ProjectJourneyDocument? _preview;
    private bool _painting;

    private sealed record StepChoice(ProjectJourneyStep Step, string Label);
    private sealed record OperationChoice(ProjectJourneyOperation Operation, string Label);
    private sealed record StatusChoice(ProjectJourneyStepStatus Status, string Label);

    private readonly StatusChoice[] _statuses =
    [
        new(ProjectJourneyStepStatus.Todo, "À faire"),
        new(ProjectJourneyStepStatus.InProgress, "En cours"),
        new(ProjectJourneyStepStatus.Completed, "Terminé (déclaré)"),
        new(ProjectJourneyStepStatus.NotApplicable, "Non applicable")
    ];

    /// <summary>Creates an editor scoped to one SFD project and workspace.</summary>
    /// <param name="workspaceRoot">Active Nodalis workspace.</param>
    /// <param name="projectDirectory">Selected SFD project.</param>
    /// <param name="readOnly">Workspace or project is read-only.</param>
    public SfdJourneyDialog(string workspaceRoot, string projectDirectory,
        bool readOnly)
    {
        _service = new SfdJourneyService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _review = new SfdCompletionReviewService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
        InitializeComponent();
        StatusCombo.ItemsSource = _statuses;
        Loaded += async (_, _) => await RunSafeAsync(() => RefreshAsync());
    }

    /// <summary>Reports whether user-reviewed journey metadata was persisted.</summary>
    public bool Changed { get; private set; }

    /// <summary>Selected persisted stage, not the preview proposal.</summary>
    private ProjectJourneyStep? SelectedStep() =>
        (StepsList.SelectedItem as StepChoice)?.Step;

    /// <summary>Selected non-executing journey operation.</summary>
    private ProjectJourneyOperation? SelectedOperation() =>
        (OperationsList.SelectedItem as OperationChoice)?.Operation;

    /// <summary>Refreshes the SFD journey and the independent completeness summary.</summary>
    /// <param name="preferredStage">Previous or newly created stage GUID.</param>
    /// <returns>Read-only reload of source files.</returns>
    private async Task RefreshAsync(Guid? preferredStage = null)
    {
        _snapshot = await _service.ReadAsync(_projectDirectory);
        _preview = _snapshot is null
            ? await _service.PreviewAsync(_projectDirectory)
            : null;
        SfdDocumentCatalog catalog = await _documents.ListAsync(_projectDirectory);
        SfdCompletionSnapshot assessment = await _review.ReviewAsync(
            _projectDirectory);
        ProjectJourneyStep[] stages = (_snapshot?.Document ?? _preview)?.Steps
            .OrderBy(step => step.Order).ToArray() ?? [];
        _painting = true;
        try
        {
            Guid? previous = preferredStage ?? SelectedStep()?.Id;
            StepChoice[] steps = stages.Select(item => new StepChoice(
                item, item.Name + " [" + LabelFor(item.Status) + "]")).ToArray();
            StepsList.ItemsSource = steps;
            StepsList.SelectedItem = steps.FirstOrDefault(item =>
                item.Step.Id == previous) ?? steps.FirstOrDefault();
        }
        finally
        {
            _painting = false;
        }

        RenderStep();
        int done = stages.Count(step =>
            step.Status == ProjectJourneyStepStatus.Completed);
        SummaryText.Text = _snapshot is null
            ? "Parcours proposé (aucun fichier créé) · 5 étapes SFD"
            : done + " / " + stages.Length +
              " étape(s) déclarée(s) terminée(s) · aucun Go implicite";
        StageEvidenceText.Text = "Documents : " + catalog.Entries.Count +
            " · incomplets : " + assessment.IncompleteCount +
            " · invalides : " + assessment.InvalidCount;
        StatusText.Text = _snapshot is null
            ? "Initialisation explicite requise."
            : _readOnly ? "Lecture seule." :
                "Statuts déclaratifs, distincts de la complétude et de la validation.";
        UpdateButtons();
    }

    /// <summary>Resolves a status name without treating it as business approval.</summary>
    /// <param name="status">Persisted user-entered journey status.</param>
    /// <returns>Localized status label.</returns>
    private string LabelFor(ProjectJourneyStepStatus status) =>
        _statuses.Single(item => item.Status == status).Label;

    /// <summary>Projects selected stage and operation without persistence.</summary>
    private void RenderStep()
    {
        _painting = true;
        try
        {
            ProjectJourneyStep? step = SelectedStep();
            OperationChoice[] operations = step?.Operations.Select(operation =>
                new OperationChoice(operation, operation.Name +
                    (operation.PromptKey is null ? "" :
                        " · " + operation.PromptKey))).ToArray() ?? [];
            OperationsList.ItemsSource = operations;
            OperationsList.SelectedItem = operations.FirstOrDefault();
            StatusCombo.SelectedItem = step is null ? null :
                _statuses.Single(item => item.Status == step.Status);
        }
        finally
        {
            _painting = false;
        }

        RenderSources();
        UpdateButtons();
    }

    /// <summary>Projects source pointers, without opening or copying source contents.</summary>
    private void RenderSources()
    {
        ProjectJourneyOperation? operation = SelectedOperation();
        SourcesList.ItemsSource = operation?.SourcePaths.Select(path =>
            path + (_sources.IsAvailable(path) ? "" : " [indisponible]")).ToArray();
        UpdateButtons();
    }

    /// <summary>Sets controls according to selected stage and archive state.</summary>
    private void UpdateButtons()
    {
        if (InitializeButton is null)
        {
            return;
        }

        bool writable = !_readOnly && _snapshot is not null;
        bool selectedStage = SelectedStep() is not null;
        bool selectedOperation = SelectedOperation() is not null;
        InitializeButton.IsEnabled = !_readOnly && _snapshot is null;
        AddStepButton.IsEnabled = writable;
        RenameStepButton.IsEnabled = writable && selectedStage;
        MoveUpButton.IsEnabled = writable && selectedStage;
        MoveDownButton.IsEnabled = writable && selectedStage;
        AddOperationButton.IsEnabled = writable && selectedStage;
        RenameOperationButton.IsEnabled = writable && selectedOperation;
        LinkSourcesButton.IsEnabled = writable && selectedOperation;
        SaveStatusButton.IsEnabled = writable && selectedStage;
        StatusCombo.IsEnabled = writable && selectedStage;
    }

    /// <summary>Refreshes operations on explicit stage selection.</summary>
    /// <param name="sender">Stages list.</param>
    /// <param name="e">Selection event.</param>
    private void Step_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderStep();
        }
    }

    /// <summary>Refreshes source references on explicit operation selection.</summary>
    /// <param name="sender">Operation list.</param>
    /// <param name="e">Selection event.</param>
    private void Operation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderSources();
        }
    }

    /// <summary>Creates only the project-local SFD journey after user confirmation.</summary>
    /// <param name="sender">Initialize button.</param>
    /// <param name="e">Click event.</param>
    private async void Initialize_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || _snapshot is not null ||
                MessageBox.Show(this,
                    "Créer le parcours SFD avec les quatre étapes de génération " +
                    "et le pilotage transversal ? Aucun document métier ne sera créé.",
                    "Initialiser le parcours SFD", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            await _service.InitializeAsync(_projectDirectory);
            Changed = true;
            await RefreshAsync();
        });

    /// <summary>Persists a user-reviewed pure journey edit with revision protection.</summary>
    /// <param name="edit">Pure transformation using shared journey editor.</param>
    /// <param name="focus">Optional stage to highlight.</param>
    /// <returns>Committed new revision.</returns>
    private async Task SaveEditAsync(
        Func<ProjectJourneyDocument, ProjectJourneyDocument> edit,
        Guid? focus = null)
    {
        if (_readOnly || _snapshot is null)
        {
            return;
        }

        ProjectJourneyDocument updated = edit(_snapshot.Document);
        await _service.SaveAsync(_projectDirectory, updated, _snapshot.Sha256);
        Changed = true;
        await RefreshAsync(focus);
    }

    /// <summary>Explicitly sets an independent, declarative progress status.</summary>
    /// <param name="sender">Status button.</param>
    /// <param name="e">Click event.</param>
    private async void SaveStatus_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (SelectedStep() is not ProjectJourneyStep step ||
                StatusCombo.SelectedItem is not StatusChoice status)
            {
                return;
            }

            await SaveEditAsync(document => ProjectJourneyEditor.SetStatus(
                document, step.Id, status.Status), step.Id);
        });

    /// <summary>Adds one user-named stage, preserving all other GUIDs.</summary>
    /// <param name="sender">Add stage button.</param>
    /// <param name="e">Click event.</param>
    private async void AddStep_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            TextPromptDialog dialog = new("Nouvelle étape SFD",
                "Nom de l'étape personnalisée") { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await SaveEditAsync(document =>
                    ProjectJourneyEditor.AddStep(document, dialog.Value));
            }
        });

    /// <summary>Renames the selected stage without changing identity.</summary>
    /// <param name="sender">Rename stage button.</param>
    /// <param name="e">Click event.</param>
    private async void RenameStep_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (SelectedStep() is not ProjectJourneyStep step)
            {
                return;
            }

            TextPromptDialog dialog = new("Renommer l'étape SFD",
                "Nouveau libellé", step.Name) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await SaveEditAsync(document => ProjectJourneyEditor.RenameStep(
                    document, step.Id, dialog.Value), step.Id);
            }
        });

    /// <summary>Moves the stage up without changing stable identity.</summary>
    /// <param name="sender">Move up.</param>
    /// <param name="e">Click event.</param>
    private async void MoveUp_Click(object sender, RoutedEventArgs e) =>
        await MoveSelectedAsync(-1);

    /// <summary>Moves the stage down without changing stable identity.</summary>
    /// <param name="sender">Move down.</param>
    /// <param name="e">Click event.</param>
    private async void MoveDown_Click(object sender, RoutedEventArgs e) =>
        await MoveSelectedAsync(1);

    /// <summary>Runs a guarded reordering based on the selected stage GUID.</summary>
    /// <param name="direction">-1 or +1.</param>
    /// <returns>Committed reordering.</returns>
    private Task MoveSelectedAsync(int direction) =>
        RunSafeAsync(async () =>
        {
            if (SelectedStep() is ProjectJourneyStep step)
            {
                await SaveEditAsync(document => ProjectJourneyEditor.MoveStep(
                    document, step.Id, direction), step.Id);
            }
        });

    /// <summary>Adds a user-named operation to an existing SFD stage.</summary>
    /// <param name="sender">Add operation button.</param>
    /// <param name="e">Click event.</param>
    private async void AddOperation_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (SelectedStep() is not ProjectJourneyStep step)
            {
                return;
            }

            TextPromptDialog dialog = new("Nouvelle opération SFD",
                "Nom de l'opération") { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await SaveEditAsync(document => ProjectJourneyEditor.AddOperation(
                    document, step.Id, dialog.Value), step.Id);
            }
        });

    /// <summary>Renames the selected non-executing operation.</summary>
    /// <param name="sender">Rename operation button.</param>
    /// <param name="e">Click event.</param>
    private async void RenameOperation_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (SelectedStep() is not ProjectJourneyStep step ||
                SelectedOperation() is not ProjectJourneyOperation operation)
            {
                return;
            }

            TextPromptDialog dialog = new("Renommer l'opération SFD",
                "Nouveau libellé", operation.Name) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await SaveEditAsync(document => ProjectJourneyEditor.RenameOperation(
                    document, step.Id, operation.Id, dialog.Value), step.Id);
            }
        });

    /// <summary>
    /// Links many selected local Markdown/text sources in one reviewed save;
    /// never imports DOCX or copies a source into the project directory.
    /// </summary>
    /// <param name="sender">Add sources button.</param>
    /// <param name="e">Click event.</param>
    private async void LinkSources_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (SelectedStep() is not ProjectJourneyStep step ||
                SelectedOperation() is not ProjectJourneyOperation operation)
            {
                return;
            }

            OpenFileDialog picker = new()
            {
                Title = "Sélectionner des sources du workspace",
                Filter = "Markdown et texte (*.md;*.txt)|*.md;*.txt",
                Multiselect = true,
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) != true)
            {
                return;
            }

            string[] paths = picker.FileNames;
            if (paths.Length is < 1 or > 500 ||
                paths.Any(path => Path.GetExtension(path) is not
                    (".md" or ".txt")))
            {
                throw new InvalidDataException(
                    "Sélectionnez uniquement 1 à 500 sources Markdown/texte.");
            }

            string[] sources = paths.Select(path =>
                _sources.GetReferenceForExistingFile(path)).ToArray();
            await SaveEditAsync(document => ProjectJourneyEditor.AddSources(
                document, step.Id, operation.Id, sources), step.Id);
            StatusText.Text = sources.Length +
                " source(s) liées par référence, sans import.";
        });

    /// <summary>Prevents unhandled async WPF exceptions and keeps user edits intact.</summary>
    /// <param name="action">Scoped asynchronous user operation.</param>
    /// <returns>Finished operation or presented error.</returns>
    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidOperationException or
            ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Parcours SFD",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
