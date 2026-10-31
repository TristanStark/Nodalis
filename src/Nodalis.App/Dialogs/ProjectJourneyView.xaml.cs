using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// User-confirmed editor for one portable journey, without touching project
/// Markdown, tasks, milestones or the existing worktree and document tabs.
/// </summary>
public partial class ProjectJourneyView : UserControl
{
    private readonly ProjectJourneyService _service;
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private readonly JourneyStatusChoice[] _statuses =
    [
        new(ProjectJourneyStepStatus.Todo, "À faire"),
        new(ProjectJourneyStepStatus.InProgress, "En cours"),
        new(ProjectJourneyStepStatus.Completed, "Terminée"),
        new(ProjectJourneyStepStatus.NotApplicable, "Non applicable")
    ];

    /// <summary>Raised when the enclosing project tab should close.</summary>
    public event EventHandler? CloseRequested;

    private ProjectJourneySnapshot? _snapshot;
    private ProjectJourneyTemplate? _preview;
    private bool _painting;
    private bool _busy;

    /// <summary>Creates a project-local journey view, read-only if requested.</summary>
    /// <param name="workspaceRoot">Single workspace root.</param>
    /// <param name="projectDirectory">Existing project folder.</param>
    /// <param name="readOnly">Whether the workspace is read-only.</param>
    public ProjectJourneyView(string workspaceRoot, string projectDirectory,
        bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        string relative = Path.GetRelativePath(_workspaceRoot, _projectDirectory);
        _readOnly = readOnly || relative.StartsWith(
            "Archives" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        _service = new ProjectJourneyService(_workspaceRoot);
        InitializeComponent();
        StatusCombo.ItemsSource = _statuses;
        ProjectNameText.Text = "Parcours · " + Path.GetFileName(_projectDirectory);
        Loaded += async (_, _) => await RunActionAsync(
            () => ReloadAsync(null), "Charger le parcours");
    }

    /// <summary>Whether at least one user-approved change was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>Refreshes state without creating files or modifying any project.</summary>
    /// <param name="selectedStepId">Optional stage to preserve in selection.</param>
    /// <returns>Completed read-only refresh.</returns>
    private async Task ReloadAsync(Guid? selectedStepId)
    {
        _snapshot = await _service.ReadAsync(_projectDirectory);
        ProjectJourneyTemplate[] applicable = _snapshot is null
            ? (await _service.ListApplicableTemplatesAsync(_projectDirectory)).ToArray()
            : [];
        ProjectJourneyTemplate? suggested = _snapshot is null
            ? await _service.PreviewDefaultAsync(_projectDirectory)
            : null;
        _painting = true;
        try
        {
            ModelCombo.ItemsSource = applicable;
            ModelCombo.SelectedItem = applicable.FirstOrDefault(item =>
                suggested is not null &&
                string.Equals(item.Key, suggested.Key,
                    StringComparison.OrdinalIgnoreCase));
            _preview = ModelCombo.SelectedItem as ProjectJourneyTemplate;
        }
        finally
        {
            _painting = false;
        }

        Render(selectedStepId);
        StatusText.Text = _snapshot is null
            ? _preview is not null
                ? "Aucun parcours créé · modèle proposé sans écriture."
                : applicable.Length > 0
                    ? "Choisissez explicitement un modèle avant d'initialiser."
                    : "Aucun modèle applicable. Vous pouvez personnaliser les modèles."
            : _readOnly
                ? "Projet archivé ou workspace en lecture seule : consultation uniquement."
                : "Modifications individuelles soumises à confirmation et contrôle SHA-256.";
    }

    /// <summary>Renders stages from one immutable selection snapshot.</summary>
    /// <param name="selectedStepId">Stage to highlight.</param>
    private void Render(Guid? selectedStepId)
    {
        _painting = true;
        try
        {
            ProjectJourneyStep[] steps = _snapshot?.Document.Steps
                .OrderBy(step => step.Order).ToArray() ?? [];
            JourneyStepRow[] rows = steps.Select(step => new JourneyStepRow(
                step, step.Name, _statuses.Single(status =>
                    status.Status == step.Status).Label)).ToArray();
            StepsList.ItemsSource = rows;
            StepsList.SelectedItem = rows.FirstOrDefault(row =>
                row.Step.Id == selectedStepId) ?? rows.FirstOrDefault();
            RenderSelection();
            int completed = steps.Count(step =>
                step.Status == ProjectJourneyStepStatus.Completed);
            int applicable = steps.Count(step =>
                step.Status != ProjectJourneyStepStatus.NotApplicable);
            ProgressText.Text = _snapshot is null
                ? _preview is null
                    ? "Parcours facultatif"
                    : _preview.Steps.Count + " étape(s) proposées par le modèle"
                : completed + " / " + applicable + " étape(s) terminée(s) · " +
                  steps.Length + " étape(s) au total";
        }
        finally
        {
            _painting = false;
        }

        UpdateButtons();
    }

    /// <summary>Displays stage operations and progress without triggering writes.</summary>
    private void RenderSelection()
    {
        _painting = true;
        try
        {
            ProjectJourneyStep? step = SelectedStep();
            SelectedStepText.Text = step?.Name ?? "Sélectionnez une étape";
            OperationsList.ItemsSource = step?.Operations;
            OperationsList.SelectedIndex = step?.Operations.Count > 0 ? 0 : -1;
            StatusCombo.SelectedItem = step is null
                ? null
                : _statuses.Single(item => item.Status == step.Status);
        }
        finally
        {
            _painting = false;
        }

        UpdateButtons();
    }

    /// <summary>Enables actions only for editable and selected objects.</summary>
    private void UpdateButtons()
    {
        bool editable = !_busy && !_readOnly && _snapshot is not null;
        bool stage = editable && SelectedStep() is not null;
        bool operation = stage && SelectedOperation() is not null;
        InitializeButton.IsEnabled = !_busy && !_readOnly &&
            _snapshot is null && _preview is not null;
        ModelCombo.IsEnabled = !_busy && !_readOnly && _snapshot is null;
        ManageTemplatesButton.IsEnabled = !_busy;
        AddStepButton.IsEnabled = editable;
        RenameStepButton.IsEnabled = stage;
        RemoveStepButton.IsEnabled = stage;
        MoveStepUpButton.IsEnabled = stage;
        MoveStepDownButton.IsEnabled = stage;
        StatusCombo.IsEnabled = stage;
        AddOperationButton.IsEnabled = stage;
        RenameOperationButton.IsEnabled = operation;
        RemoveOperationButton.IsEnabled = operation;
        MoveOperationUpButton.IsEnabled = operation;
        MoveOperationDownButton.IsEnabled = operation;
    }

    /// <summary>Gets the currently selected stable stage.</summary>
    /// <returns>Stage or null.</returns>
    private ProjectJourneyStep? SelectedStep() =>
        (StepsList.SelectedItem as JourneyStepRow)?.Step;

    /// <summary>Gets the currently selected stable operation.</summary>
    /// <returns>Operation or null.</returns>
    private ProjectJourneyOperation? SelectedOperation() =>
        OperationsList.SelectedItem as ProjectJourneyOperation;

    /// <summary>Uses the shared recoverable-error boundary and serializes UI changes.</summary>
    /// <param name="action">One user-initiated async action.</param>
    /// <param name="context">Diagnostic context.</param>
    /// <returns>Completed guarded action.</returns>
    private async Task RunActionAsync(Func<Task> action, string context)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await UiActionGuard.RunAsync(Window.GetWindow(this)!, "Parcours projet · " + context,
                action, _workspaceRoot);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Submits a pure edit with explicit confirmation and optimistic commit.</summary>
    /// <param name="description">Human-readable modification preview.</param>
    /// <param name="edit">Pure Core edit.</param>
    /// <param name="selectedStepId">Stage to reselect.</param>
    /// <returns>Completed, approved edit or unchanged project.</returns>
    private async Task CommitAsync(string description,
        Func<ProjectJourneyDocument, ProjectJourneyDocument> edit,
        Guid? selectedStepId)
    {
        if (_readOnly || _snapshot is null)
        {
            return;
        }

        ProjectJourneyDocument proposed = edit(_snapshot.Document);
        if (ReferenceEquals(proposed, _snapshot.Document))
        {
            return;
        }

        if (MessageBox.Show(Window.GetWindow(this),
            description + "\n\nSeul le fichier .project-journey.json sera modifié. " +
            "Les documents, tâches, jalons et preuves ne seront pas touchés.\n\n" +
            "Confirmer cette modification ?",
            "Parcours projet · confirmer", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            Render(selectedStepId);
            return;
        }

        try
        {
            _snapshot = await _service.SaveAsync(_projectDirectory,
                proposed, _snapshot.Sha256);
            Changed = true;
            Render(selectedStepId);
            StatusText.Text = "Modification enregistrée · identifiants et références conservés.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Le fichier a pu changer depuis son chargement. Aucune " +
                "réécriture automatique ne sera tentée.\n\n" +
                exception.Message + "\n\nCliquez « Recharger depuis le disque » " +
                "pour réexaminer l'état actuel.",
                "Parcours projet · écriture refusée", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Render(selectedStepId);
            StatusText.Text = "Écriture refusée · rechargement manuel nécessaire.";
        }
    }

    /// <summary>Prompts the user for a label, without modifying the snapshot.</summary>
    /// <param name="title">Prompt title.</param>
    /// <param name="initial">Original label.</param>
    /// <returns>Reviewed name or null when canceled.</returns>
    private string? PromptName(string title, string initial = "")
    {
        TextPromptDialog dialog = new(title, "Nom :", initial)
        {
            Owner = Window.GetWindow(this)
        };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    /// <summary>Recomputes details after a stage selection.</summary>
    /// <param name="sender">Stage selector.</param>
    /// <param name="e">Selection change.</param>
    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderSelection();
        }
    }

    /// <summary>Updates operation action availability.</summary>
    /// <param name="sender">Operations selector.</param>
    /// <param name="e">Selection change.</param>
    private void OperationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            UpdateButtons();
        }
    }

    /// <summary>Updates the non-persisted model preview when a user chooses it.</summary>
    /// <param name="sender">Model selector.</param>
    /// <param name="e">Selection change.</param>
    private void ModelCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (_painting)
        {
            return;
        }

        _preview = ModelCombo.SelectedItem as ProjectJourneyTemplate;
        Render(SelectedStep()?.Id);
    }

    /// <summary>Opens the shared workspace template editor on user request.</summary>
    /// <param name="sender">Manage models button.</param>
    /// <param name="e">Click event.</param>
    private async void ManageTemplates_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyTemplatesDialog dialog = new(_workspaceRoot, _readOnly)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
            if (dialog.Changed && _snapshot is null)
            {
                await ReloadAsync(null);
            }
        }, "Personnaliser les modèles");

    /// <summary>Creates a journey only after explicit confirmation.</summary>
    /// <param name="sender">Initialize command.</param>
    /// <param name="e">Click event.</param>
    private async void Initialize_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            if (_readOnly || _snapshot is not null || _preview is null ||
                MessageBox.Show(Window.GetWindow(this),
                    "Créer un parcours depuis le modèle « " + _preview.Name +
                    " » (" + _preview.Steps.Count + " étapes) ?\n\n" +
                    "Aucun document existant ne sera modifié.",
                    "Initialiser le parcours", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _snapshot = await _service.InitializeAsync(
                _projectDirectory, _preview.Key);
            Changed = true;
            _preview = null;
            ModelCombo.SelectedItem = null;
            Render(null);
            StatusText.Text = "Parcours initialisé · " +
                _snapshot.Document.Steps.Count + " étape(s).";
        }, "Initialiser");

    /// <summary>Refreshes a potentially changed external revision.</summary>
    /// <param name="sender">Refresh button.</param>
    /// <param name="e">Click event.</param>
    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () => await ReloadAsync(SelectedStep()?.Id),
            "Recharger");

    /// <summary>Adds a user-named stage.</summary>
    /// <param name="sender">Add button.</param>
    /// <param name="e">Click event.</param>
    private async void AddStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            string? name = PromptName("Ajouter une étape");
            if (name is not null)
            {
                await CommitAsync("Ajouter l'étape « " + name + " »",
                    doc => ProjectJourneyEditor.AddStep(doc, name), null);
            }
        }, "Ajouter une étape");

    /// <summary>Renames the selected stage.</summary>
    /// <param name="sender">Rename button.</param>
    /// <param name="e">Click event.</param>
    private async void RenameStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            if (step is null)
            {
                return;
            }

            string? name = PromptName("Renommer l'étape", step.Name);
            if (name is not null)
            {
                await CommitAsync("Renommer « " + step.Name + " » en « " + name + " »",
                    doc => ProjectJourneyEditor.RenameStep(doc, step.Id, name), step.Id);
            }
        }, "Renommer une étape");

    /// <summary>Moves a selected stage up.</summary>
    /// <param name="sender">Up button.</param>
    /// <param name="e">Click event.</param>
    private async void MoveStepUp_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveStepAsync(-1), "Monter une étape");

    /// <summary>Moves a selected stage down.</summary>
    /// <param name="sender">Down button.</param>
    /// <param name="e">Click event.</param>
    private async void MoveStepDown_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveStepAsync(1), "Descendre une étape");

    /// <summary>Plans a one-step move.</summary>
    /// <param name="direction">-1 or +1.</param>
    /// <returns>Approved change.</returns>
    private async Task MoveStepAsync(int direction)
    {
        ProjectJourneyStep? step = SelectedStep();
        if (step is not null)
        {
            await CommitAsync("Déplacer l'étape « " + step.Name + " »",
                doc => ProjectJourneyEditor.MoveStep(doc, step.Id, direction), step.Id);
        }
    }

    /// <summary>Removes a stage reference, never its documents.</summary>
    /// <param name="sender">Remove button.</param>
    /// <param name="e">Click event.</param>
    private async void RemoveStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            if (step is not null)
            {
                await CommitAsync("Retirer l'étape « " + step.Name +
                    " » et ses " + step.Operations.Count +
                    " opération(s) du parcours (références seulement)",
                    doc => ProjectJourneyEditor.RemoveStep(doc, step.Id), null);
            }
        }, "Retirer une étape");

    /// <summary>Changes the user-selected stage status only on actual input.</summary>
    /// <param name="sender">Status selector.</param>
    /// <param name="e">Selection change.</param>
    private async void StatusCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (_painting || _busy ||
            StatusCombo.SelectedItem is not JourneyStatusChoice choice ||
            SelectedStep() is not ProjectJourneyStep step ||
            step.Status == choice.Status)
        {
            return;
        }

        await RunActionAsync(async () =>
        {
            await CommitAsync("Passer « " + step.Name + " » à « " +
                choice.Label + " »",
                doc => ProjectJourneyEditor.SetStatus(
                    doc, step.Id, choice.Status), step.Id);
            Render(step.Id);
        }, "Statut d'étape");
    }

    /// <summary>Adds a repeatable operation.</summary>
    /// <param name="sender">Add operation button.</param>
    /// <param name="e">Click event.</param>
    private async void AddOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            if (step is null)
            {
                return;
            }

            string? name = PromptName("Ajouter une opération");
            if (name is not null)
            {
                await CommitAsync("Ajouter l'opération « " + name +
                    " » à « " + step.Name + " »",
                    doc => ProjectJourneyEditor.AddOperation(doc, step.Id, name),
                    step.Id);
            }
        }, "Ajouter une opération");

    /// <summary>Renames a repeatable operation.</summary>
    /// <param name="sender">Rename operation button.</param>
    /// <param name="e">Click event.</param>
    private async void RenameOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            ProjectJourneyOperation? operation = SelectedOperation();
            if (step is null || operation is null)
            {
                return;
            }

            string? name = PromptName("Renommer l'opération", operation.Name);
            if (name is not null)
            {
                await CommitAsync("Renommer l'opération « " + operation.Name +
                    " » en « " + name + " »",
                    doc => ProjectJourneyEditor.RenameOperation(
                        doc, step.Id, operation.Id, name), step.Id);
            }
        }, "Renommer une opération");

    /// <summary>Moves the selected operation up.</summary>
    /// <param name="sender">Up button.</param>
    /// <param name="e">Click event.</param>
    private async void MoveOperationUp_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveOperationAsync(-1), "Monter une opération");

    /// <summary>Moves the selected operation down.</summary>
    /// <param name="sender">Down button.</param>
    /// <param name="e">Click event.</param>
    private async void MoveOperationDown_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveOperationAsync(1), "Descendre une opération");

    /// <summary>Plans the operation move.</summary>
    /// <param name="direction">-1 or +1.</param>
    /// <returns>Approved change.</returns>
    private async Task MoveOperationAsync(int direction)
    {
        ProjectJourneyStep? step = SelectedStep();
        ProjectJourneyOperation? operation = SelectedOperation();
        if (step is not null && operation is not null)
        {
            await CommitAsync("Déplacer l'opération « " + operation.Name + " »",
                doc => ProjectJourneyEditor.MoveOperation(
                    doc, step.Id, operation.Id, direction), step.Id);
        }
    }

    /// <summary>Removes an operation reference without deleting sources.</summary>
    /// <param name="sender">Remove operation button.</param>
    /// <param name="e">Click event.</param>
    private async void RemoveOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            ProjectJourneyOperation? operation = SelectedOperation();
            if (step is not null && operation is not null)
            {
                await CommitAsync("Retirer l'opération « " + operation.Name +
                    " » (aucune preuve ni document supprimé)",
                    doc => ProjectJourneyEditor.RemoveOperation(
                        doc, step.Id, operation.Id), step.Id);
            }
        }, "Retirer une opération");

    private sealed record JourneyStepRow(
        ProjectJourneyStep Step, string Name, string StatusLabel);

    private sealed record JourneyStatusChoice(
        ProjectJourneyStepStatus Status, string Label);
    /// <summary>Requests closing this journey tab without affecting documents.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void CloseView_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

}
