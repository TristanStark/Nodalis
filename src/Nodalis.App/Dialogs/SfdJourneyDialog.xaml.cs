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
    private readonly SfdPromptPreparationService _preparer;
    private readonly SfdMasterPromptService _masters;
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdCompletionReviewService _review;
    private readonly ProjectJourneySourceService _sources;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private ProjectJourneySnapshot? _snapshot;
    private ProjectJourneyDocument? _preview;
    private bool _painting;

    private sealed record StepChoice(ProjectJourneyStep Step, string Label);
    private sealed record DocumentChoice(Guid Id, string Label, string Title);
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
        _preparer = new SfdPromptPreparationService(workspaceRoot);
        _masters = new SfdMasterPromptService(workspaceRoot);
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
        RefreshDocumentSelectors(catalog);
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
            ? "Parcours proposé (aucun fichier créé) · 5 prompts + pilotage"
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

    /// <summary>Refreshes PM/ACT/FCT selectors using canonical document IDs.</summary>
    /// <param name="catalog">Current, unambiguous SFD document catalog.</param>
    private void RefreshDocumentSelectors(SfdDocumentCatalog catalog)
    {
        Guid? priorProcess = (ProcessCombo.SelectedItem as DocumentChoice)?.Id;
        Guid? priorActivity = (ActivityCombo.SelectedItem as DocumentChoice)?.Id;
        Guid? priorFunctionality = (FunctionalityCombo.SelectedItem as DocumentChoice)?.Id;
        DocumentChoice[] processes = catalog.Entries
            .Where(item => item.Card.Kind == SfdDocumentKind.Process)
            .Select(item => new DocumentChoice(item.Card.Id,
                item.Card.Reference + " — " + item.Card.Title,
                item.Card.Title)).ToArray();
        DocumentChoice[] activities = catalog.Entries
            .Where(item => item.Card.Kind == SfdDocumentKind.Activity)
            .Select(item => new DocumentChoice(item.Card.Id,
                item.Card.Reference + " — " + item.Card.Title,
                item.Card.Title)).ToArray();
        DocumentChoice[] functionalities = catalog.Entries
            .Where(item => item.Card.Kind == SfdDocumentKind.Functionality)
            .Select(item => new DocumentChoice(item.Card.Id,
                item.Card.Reference + " — " + item.Card.Title,
                item.Card.Title)).ToArray();
        ProcessCombo.ItemsSource = processes;
        ActivityCombo.ItemsSource = activities;
        FunctionalityCombo.ItemsSource = functionalities;
        ProcessCombo.SelectedItem = processes.FirstOrDefault(item =>
            item.Id == priorProcess) ?? processes.FirstOrDefault();
        ActivityCombo.SelectedItem = activities.FirstOrDefault(item =>
            item.Id == priorActivity) ?? activities.FirstOrDefault();
        FunctionalityCombo.SelectedItem = functionalities.FirstOrDefault(item =>
            item.Id == priorFunctionality) ?? functionalities.FirstOrDefault();
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
        bool needsUpgrade = _snapshot is not null &&
            !_snapshot.Document.Steps.SelectMany(step => step.Operations)
                .Any(operation => operation.PromptKey == "sfd-master-05");
        UpgradeButton.Visibility = needsUpgrade ? Visibility.Visible : Visibility.Collapsed;
        UpgradeButton.IsEnabled = needsUpgrade && !_readOnly;
        AddStepButton.IsEnabled = writable;
        RenameStepButton.IsEnabled = writable && selectedStage;
        MoveUpButton.IsEnabled = writable && selectedStage;
        MoveDownButton.IsEnabled = writable && selectedStage;
        AddOperationButton.IsEnabled = writable && selectedStage;
        RenameOperationButton.IsEnabled = writable && selectedOperation;
        LinkSourcesButton.IsEnabled = writable && selectedOperation;
        SaveStatusButton.IsEnabled = writable && selectedStage;
        PreparePromptButton.IsEnabled = _snapshot is not null &&
            SelectedOperation()?.PromptKey is
                ("sfd-01" or "sfd-02" or "sfd-03" or "sfd-04" or
                 "sfd-master-01" or "sfd-master-02" or "sfd-master-03" or
                 "sfd-master-04" or "sfd-master-05");
        CreateProcessButton.IsEnabled = !_readOnly;
        CreateActivityButton.IsEnabled = !_readOnly &&
            ProcessCombo.SelectedItem is DocumentChoice;
        RenameActivityButton.IsEnabled = !_readOnly &&
            ActivityCombo.SelectedItem is DocumentChoice;
        DetermineProcessesButton.IsEnabled = _snapshot?.Document.Steps
            .SelectMany(step => step.Operations)
            .Any(operation => operation.PromptKey == "sfd-master-01") == true;
        GenerateActivityPromptButton.IsEnabled = _snapshot?.Document.Steps
            .SelectMany(step => step.Operations)
            .Any(operation => operation.PromptKey == "sfd-master-02") == true &&
            ActivityCombo.SelectedItem is DocumentChoice;
        AssembleFeaturesButton.IsEnabled = _snapshot?.Document.Steps
            .SelectMany(step => step.Operations)
            .Any(operation => operation.PromptKey == "sfd-master-03") == true;
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
                    "Créer le parcours SFD avec les cinq prompts maîtres " +
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

    /// <summary>Upgrades a saved legacy SFD journey only after human confirmation.</summary>
    /// <param name="sender">Update master prompts button.</param>
    /// <param name="e">Click event.</param>
    private async void Upgrade_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || _snapshot is null)
            {
                return;
            }

            if (MessageBox.Show(this,
                "Adapter les anciennes étapes aux 5 prompts maîtres ? " +
                "Les sources, étapes personnalisées et opérations libres sont conservées. " +
                "Le statut des étapes modifiées repasse à 'À faire'. " +
                "Les anciennes réponses JSON ne sont pas importables avec ces prompts.",
                "Actualiser le parcours SFD", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await SaveEditAsync(SfdJourneyDefaults.UpgradeToMasterPrompts);
            StatusText.Text = "Cinq prompts maîtres configurés après confirmation.";
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
                "Nom de l'étape personnalisée")
            { Owner = this };
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
                "Nouveau libellé", step.Name)
            { Owner = this };
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
                "Nom de l'opération")
            { Owner = this };
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
                "Nouveau libellé", operation.Name)
            { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await SaveEditAsync(document => ProjectJourneyEditor.RenameOperation(
                    document, step.Id, operation.Id, dialog.Value), step.Id);
            }
        });

    /// <summary>Creates an editable PM folder with presentation Markdown.</summary>
    /// <param name="sender">Create process button.</param>
    /// <param name="e">Click event.</param>
    private async void CreateProcess_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly)
            {
                return;
            }

            TextPromptDialog dialog = new("Créer un processus métier",
                "Intitulé du nouveau processus métier") { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SfdDocumentEntry created = await _documents.CreateAsync(
                _projectDirectory, SfdDocumentKind.Process, dialog.Value);
            Changed = true;
            await RefreshAsync();
            StatusText.Text = "Processus créé : " + created.Card.Reference +
                ". Complétez sa présentation Markdown.";
        });

    /// <summary>Creates one ACT in the selected PM, with a parent-scoped next ID.</summary>
    /// <param name="sender">Create activity button.</param>
    /// <param name="e">Click event.</param>
    private async void CreateActivity_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || ProcessCombo.SelectedItem is not DocumentChoice process)
            {
                return;
            }

            SfdDocumentEntry created = await _documents.CreateAsync(
                _projectDirectory, SfdDocumentKind.Activity, string.Empty, process.Id);
            Changed = true;
            await RefreshAsync();
            DocumentChoice? selected = (ActivityCombo.ItemsSource as
                IEnumerable<DocumentChoice>)?.FirstOrDefault(item =>
                    item.Id == created.Card.Id);
            ActivityCombo.SelectedItem = selected;
            StatusText.Text = "Activité créée : " + created.Card.Reference +
                ". Son titre et son contenu métier sont modifiables.";
        });

    /// <summary>Renames the selected ACT title without losing its canonical ID or body.</summary>
    /// <param name="sender">Rename activity button.</param>
    /// <param name="e">Click event.</param>
    private async void RenameActivity_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (_readOnly || ActivityCombo.SelectedItem is not DocumentChoice activity)
            {
                return;
            }

            TextPromptDialog dialog = new("Renommer l'activité métier",
                "Nouvel intitulé de l'activité", activity.Title)
            { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SfdDocumentEntry renamed = await _documents.RenameActivityAsync(
                _projectDirectory, activity.Id, dialog.Value);
            Changed = true;
            await RefreshAsync();
            ActivityCombo.SelectedItem = (ActivityCombo.ItemsSource as
                IEnumerable<DocumentChoice>)?.FirstOrDefault(item =>
                    item.Id == renamed.Card.Id);
            StatusText.Text = "Activité renommée : " + renamed.Card.Reference;
        });

    /// <summary>Prepares the original master prompt with canonical selected context.</summary>
    /// <param name="key">One specialized prompt identity.</param>
    /// <returns>Review-only preview dialog.</returns>
    private async Task PrepareMasterAsync(string key)
    {
        Guid? activityId = (ActivityCombo.SelectedItem as DocumentChoice)?.Id;
        Guid? functionalityId = (FunctionalityCombo.SelectedItem as DocumentChoice)?.Id;
        SfdPreparedPrompt prepared = await _masters.PrepareAsync(
            _projectDirectory, key, activityId, functionalityId);
        SfdPromptPreviewDialog preview = new(prepared) { Owner = this };
        preview.ShowDialog();
    }

    /// <summary>Generates the PM/ACT cartography prompt for this project.</summary>
    /// <param name="sender">Process prompt button.</param>
    /// <param name="e">Click event.</param>
    private async void DetermineProcesses_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(() => PrepareMasterAsync("sfd-master-01"));

    /// <summary>Generates prompt 02 from the selected ACT and its complete parent PM.</summary>
    /// <param name="sender">Activity prompt button.</param>
    /// <param name="e">Click event.</param>
    private async void GenerateActivityPrompt_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(() => PrepareMasterAsync("sfd-master-02"));

    /// <summary>Generates prompt 03 from the full canonical PM/ACT index.</summary>
    /// <param name="sender">Feature assembly prompt button.</param>
    /// <param name="e">Click event.</param>
    private async void AssembleFeatures_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(() => PrepareMasterAsync("sfd-master-03"));

    /// <summary>Builds one complete SFD prompt for explicit human review.</summary>
    /// <param name="sender">Prepare selected generation.</param>
    /// <param name="e">Click event.</param>
    private async void PreparePrompt_Click(object sender, RoutedEventArgs e) =>
        await RunSafeAsync(async () =>
        {
            if (SelectedStep() is not ProjectJourneyStep stage ||
                SelectedOperation() is not ProjectJourneyOperation operation)
            {
                return;
            }

            if (operation.PromptKey?.StartsWith("sfd-master-",
                    StringComparison.Ordinal) == true)
            {
                await PrepareMasterAsync(operation.PromptKey);
                return;
            }

            SfdPreparedPrompt prepared = await _preparer.PrepareAsync(
                _projectDirectory, stage.Id, operation.Id);
            SfdPromptPreviewDialog preview = new(prepared) { Owner = this };
            preview.ShowDialog();
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
