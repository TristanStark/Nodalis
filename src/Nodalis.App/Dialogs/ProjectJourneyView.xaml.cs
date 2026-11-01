using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
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
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectJourneyMilestoneCatalogService _milestones;
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

    /// <summary>Requests opening a verified workspace source in the main editor.</summary>
    public event EventHandler<ProjectJourneySourceOpenRequestedEventArgs>? SourceOpenRequested;

    private ProjectJourneySnapshot? _snapshot;
    private ProjectJourneyTemplate? _preview;
    private ProjectJourneyMilestoneCatalog? _milestoneCatalog;
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
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _milestones = new ProjectJourneyMilestoneCatalogService(_workspaceRoot);
        InitializeComponent();
        StatusCombo.ItemsSource = _statuses;
        ProjectNameText.Text = "Parcours · " + Path.GetFileName(_projectDirectory);
        Loaded += async (_, _) => await RunActionAsync(
            () => ReloadAsync(null), "Charger le parcours");
    }

    /// <summary>Whether at least one user-approved change was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Refreshes an attached journey after a workspace template edit.
    /// Detached views refresh themselves on their next WPF Loaded event.
    /// </summary>
    /// <returns>Completed read-only refresh.</returns>
    public Task RefreshTemplatesAsync()
    {
        if (Window.GetWindow(this) is null)
        {
            return Task.CompletedTask;
        }

        return RunActionAsync(() => ReloadAsync(SelectedStep()?.Id),
            "Actualiser les modèles");
    }

    /// <summary>Refreshes state without creating files or modifying any project.</summary>
    /// <param name="selectedStepId">Optional stage to preserve in selection.</param>
    /// <returns>Completed read-only refresh.</returns>
    private async Task ReloadAsync(Guid? selectedStepId)
    {
        _snapshot = await _service.ReadAsync(_projectDirectory);
        _milestoneCatalog = null;
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
    private void Render(Guid? selectedStepId, Guid? selectedOperationId = null)
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
            RenderSelection(selectedOperationId);
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
    private void RenderSelection(Guid? selectedOperationId = null)
    {
        _painting = true;
        try
        {
            ProjectJourneyStep? step = SelectedStep();
            SelectedStepText.Text = step?.Name ?? "Sélectionnez une étape";
            OperationsList.ItemsSource = step?.Operations;
            OperationsList.SelectedItem = step?.Operations.FirstOrDefault(item =>
                item.Id == selectedOperationId) ?? step?.Operations.FirstOrDefault();
            RenderEvidence();
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
        bool viewableOperation = !_busy && _snapshot is not null &&
            SelectedOperation() is not null;
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
        LinkSourceButton.IsEnabled = operation;
        OpenSourceButton.IsEnabled = viewableOperation && SelectedSource() is not null;
        UnlinkSourceButton.IsEnabled = operation && SelectedSource() is not null;
        RefreshRelationsButton.IsEnabled = viewableOperation;
        LinkMilestoneButton.IsEnabled = operation;
        OpenRelationButton.IsEnabled = viewableOperation &&
            SelectedRelation() is JourneyRelationRow selected &&
            selected.Kind == JourneyRelationKind.Milestone &&
            selected.Milestone is not null;
        UnlinkRelationButton.IsEnabled = operation && SelectedRelation() is not null;
        SetOperationPromptButton.IsEnabled = operation;
        PrepareOperationPromptButton.IsEnabled = viewableOperation;
    }

    /// <summary>Gets the currently selected stable stage.</summary>
    /// <returns>Stage or null.</returns>
    private ProjectJourneyStep? SelectedStep() =>
        (StepsList.SelectedItem as JourneyStepRow)?.Step;

    /// <summary>Gets the currently selected stable operation.</summary>
    /// <returns>Operation or null.</returns>
    private ProjectJourneyOperation? SelectedOperation() =>
        OperationsList.SelectedItem as ProjectJourneyOperation;

    /// <summary>Gets the selected non-owning source reference.</summary>
    private JourneySourceRow? SelectedSource() =>
        SourcesList.SelectedItem as JourneySourceRow;

    /// <summary>Gets the selected existing relation or unresolved gate reference.</summary>
    private JourneyRelationRow? SelectedRelation() =>
        RelationsList.SelectedItem as JourneyRelationRow;

    /// <summary>Displays availability of source references without reading their bytes.</summary>
    private void RenderEvidence()
    {
        bool wasPainting = _painting;
        _painting = true;
        try
        {
            ProjectJourneyOperation? operation = SelectedOperation();
            SourcesList.ItemsSource = operation?.SourcePaths.Select(path =>
                new JourneySourceRow(path, _sources.IsAvailable(path)
                    ? "Fichier présent dans le workspace"
                    : "Introuvable ou chemin non sûr")).ToArray();
            SourcesList.SelectedIndex = operation?.SourcePaths.Count > 0 ? 0 : -1;
            RenderRelations();
            PromptKeyText.Text = operation is null
                ? "Prompt : —"
                : "Prompt : " + (operation.PromptKey ?? "aucun");
        }
        finally
        {
            _painting = wasPainting;
        }

        UpdateButtons();
    }

    /// <summary>
    /// Renders current references without scanning the workspace. An explicit
    /// "Actualiser" click resolves canonical milestone identities off-thread.
    /// Gates remain unverified until a real gate registry is delivered.
    /// </summary>
    private void RenderRelations()
    {
        bool wasPainting = _painting;
        _painting = true;
        try
        {
            ProjectJourneyOperation? operation = SelectedOperation();
            Guid? selectedId = SelectedRelation()?.Id;
            List<JourneyRelationRow> rows = [];
            if (operation is not null)
            {
                foreach (Guid id in operation.MilestoneIds)
                {
                    ProjectJourneyMilestoneCandidate? found =
                        _milestoneCatalog?.Candidates.FirstOrDefault(candidate =>
                            candidate.Id == id);
                    string label = found is not null
                        ? "Jalon · " + found.Label
                        : "Jalon · " + id.ToString("D");
                    string detail = found is not null
                        ? found.Location
                        : _milestoneCatalog is null
                            ? "Non vérifié · cliquer Actualiser"
                            : "Introuvable ou ID non persistant · lien à vérifier";
                    rows.Add(new JourneyRelationRow(
                        JourneyRelationKind.Milestone, id, label, detail, found));
                }

                foreach (Guid id in operation.GateIds)
                {
                    rows.Add(new JourneyRelationRow(
                        JourneyRelationKind.Gate, id,
                        "Gate · " + id.ToString("D"),
                        "Référence conservée · registre de gates #202 non disponible",
                        null));
                }
            }

            RelationsList.ItemsSource = rows;
            RelationsList.SelectedItem = rows.FirstOrDefault(row =>
                row.Id == selectedId) ?? rows.FirstOrDefault();
            int legacy = _milestoneCatalog?.LegacyWithoutStableId ?? 0;
            int duplicates = _milestoneCatalog?.DuplicateIds.Count ?? 0;
            int missing = _milestoneCatalog is null || operation is null ? 0 :
                operation.MilestoneIds.Count(id => !_milestoneCatalog.Candidates
                    .Any(item => item.Id == id));
            RelationsDiagnosticsText.Text = operation is null
                ? "Sélectionnez une opération."
                : operation.MilestoneIds.Count + " jalon(s) · " +
                  operation.GateIds.Count + " gate(s). " +
                  (_milestoneCatalog is null
                    ? "Cliquez Actualiser pour vérifier les jalons."
                    : missing + " référence(s) de jalon à vérifier · " +
                      legacy + " ancienne(s) ligne(s) sans GUID stable · " +
                      duplicates + " GUID dupliqué(s).") +
                  (operation.GateIds.Count > 0
                    ? " Gates : aucun Go/No-Go implicite."
                    : "");
        }
        finally
        {
            _painting = wasPainting;
        }
    }

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
    /// <param name="selectedOperationId">Operation to preserve when editing its sources.</param>
    /// <returns>Completed, approved edit or unchanged project.</returns>
    private async Task CommitAsync(string description,
        Func<ProjectJourneyDocument, ProjectJourneyDocument> edit,
        Guid? selectedStepId, Guid? selectedOperationId = null)
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
            Render(selectedStepId, selectedOperationId);
            return;
        }

        try
        {
            _snapshot = await _service.SaveAsync(_projectDirectory,
                proposed, _snapshot.Sha256);
            Changed = true;
            Render(selectedStepId, selectedOperationId);
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
            Render(selectedStepId, selectedOperationId);
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
            RenderEvidence();
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

    /// <summary>Refreshes the open/unlink button for the selected source.</summary>
    /// <param name="sender">Source list.</param>
    /// <param name="e">Selection change.</param>
    private void SourcesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            UpdateButtons();
        }
    }

    /// <summary>Opens a source only on a user's explicit double-click.</summary>
    /// <param name="sender">Source list.</param>
    /// <param name="e">Mouse action.</param>
    private void SourcesList_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) => RequestOpenSource();

    /// <summary>Links an existing file without importing or copying it.</summary>
    /// <param name="sender">Link button.</param>
    /// <param name="e">Click event.</param>
    private async void LinkSource_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            ProjectJourneyOperation? operation = SelectedOperation();
            if (_readOnly || step is null || operation is null)
            {
                return;
            }

            OpenFileDialog dialog = new()
            {
                Title = "Lier une preuve ou un document existant du workspace",
                InitialDirectory = _projectDirectory,
                CheckFileExists = true,
                Multiselect = false,
                Filter = "Tous les fichiers (*.*)|*.*|Documents Markdown (*.md)|*.md"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            string reference = _sources.GetReferenceForExistingFile(dialog.FileName);
            if (operation.SourcePaths.Any(item => string.Equals(item,
                    reference, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText.Text = "Cette source est déjà reliée à l'opération.";
                return;
            }

            await CommitAsync("Lier le fichier existant « " + reference +
                " » à l'opération « " + operation.Name + " »",
                doc => ProjectJourneyEditor.AddSource(doc, step.Id,
                    operation.Id, reference), step.Id, operation.Id);
        }, "Lier une source existante");

    /// <summary>Opens the selected source without editing the journey file.</summary>
    /// <param name="sender">Open button.</param>
    /// <param name="e">Click event.</param>
    private void OpenSource_Click(object sender, RoutedEventArgs e) =>
        RequestOpenSource();

    /// <summary>
    /// Routes a deliberate open request to the workspace shell, which must
    /// revalidate file containment, existence and type before opening.
    /// </summary>
    private void RequestOpenSource()
    {
        JourneySourceRow? source = SelectedSource();
        if (source is null)
        {
            return;
        }

        if (SourceOpenRequested is null)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Ouvrez le Parcours dans l'espace de travail pour consulter " +
                "les fichiers liés à ses opérations.",
                "Source du Parcours", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SourceOpenRequested.Invoke(this,
            new ProjectJourneySourceOpenRequestedEventArgs(source.RelativePath));
    }

    /// <summary>Removes only the source reference after confirmation.</summary>
    /// <param name="sender">Unlink button.</param>
    /// <param name="e">Click event.</param>
    private async void UnlinkSource_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            ProjectJourneyOperation? operation = SelectedOperation();
            JourneySourceRow? source = SelectedSource();
            if (_readOnly || step is null || operation is null || source is null)
            {
                return;
            }

            await CommitAsync("Délier « " + source.RelativePath +
                " » de « " + operation.Name + " » (le fichier sera conservé)",
                doc => ProjectJourneyEditor.RemoveSource(doc, step.Id,
                    operation.Id, source.RelativePath),
                step.Id, operation.Id);
        }, "Délier une source");

    /// <summary>Changes the prompt reference, without invoking a model.</summary>
    /// <param name="sender">Prompt assignment button.</param>
    /// <param name="e">Click event.</param>
    private async void SetOperationPrompt_Click(
        object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            ProjectJourneyOperation? operation = SelectedOperation();
            if (_readOnly || step is null || operation is null)
            {
                return;
            }

            TextPromptDialog dialog = new("Associer un prompt local",
                "Clé d'un prompt Markdown local ; « - » pour retirer le lien :",
                operation.PromptKey ?? "-")
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string? promptKey = dialog.Value == "-" ? null : dialog.Value;
            await CommitAsync("Changer la référence de prompt de « " +
                operation.Name + " » en « " + (promptKey ?? "aucun") +
                " » (aucune IA ne sera exécutée)",
                doc => ProjectJourneyEditor.SetPromptKey(doc, step.Id,
                    operation.Id, promptKey), step.Id, operation.Id);
        }, "Associer un prompt");

    /// <summary>
    /// Creates a bounded local draft for manual inspection/copy only.
    /// This never writes a workspace file or starts an AI request.
    /// </summary>
    /// <param name="sender">Prepare button.</param>
    /// <param name="e">Click event.</param>
    private async void PrepareOperationPrompt_Click(
        object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            ProjectJourneyStep? step = SelectedStep();
            ProjectJourneyOperation? operation = SelectedOperation();
            if (step is null || operation is null)
            {
                return;
            }

            ProjectJourneyPromptPreparationService preparer = new(
                _workspaceRoot, Path.Combine(AppContext.BaseDirectory, "Prompts"));
            ProjectJourneyPromptDraft draft = await preparer.PrepareAsync(
                step, operation);
            ProjectJourneyPromptDraftDialog dialog = new(draft)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }, "Préparer un prompt sans IA");

    private sealed record JourneySourceRow(string RelativePath, string Availability);

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

/// <summary>Explicit request to open a stored source path in the main workspace.</summary>
public sealed class ProjectJourneySourceOpenRequestedEventArgs : EventArgs
{
    /// <summary>Creates a source navigation request.</summary>
    /// <param name="relativePath">Portable workspace-relative source.</param>
    public ProjectJourneySourceOpenRequestedEventArgs(string relativePath)
    {
        RelativePath = ProjectJourneySourcePath.RequireSafe(relativePath);
    }

    /// <summary>Gets the stored, non-owning relative source reference.</summary>
    public string RelativePath { get; }
}
