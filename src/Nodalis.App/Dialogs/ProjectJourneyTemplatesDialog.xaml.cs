using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit workspace-level journey template editor. Edits are staged in
/// memory and committed only with a user confirmation and a byte-level hash.
/// Existing projects are never rewritten when their templates change.
/// </summary>
public partial class ProjectJourneyTemplatesDialog : Window
{
    private readonly ProjectJourneyService _service;
    private readonly string _workspaceRoot;
    private readonly bool _readOnly;
    private ProjectJourneyTemplateCatalog? _catalog;
    private string? _revision;
    private bool _dirty;
    private bool _busy;
    private bool _rendering;

    /// <summary>Creates the template editor for one workspace.</summary>
    /// <param name="workspaceRoot">Workspace root.</param>
    /// <param name="readOnly">Read-only fallback state.</param>
    public ProjectJourneyTemplatesDialog(string workspaceRoot, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _readOnly = readOnly;
        _service = new ProjectJourneyService(_workspaceRoot);
        InitializeComponent();
        ComplexityCombo.ItemsSource =
            Enum.GetValues<ProjectComplexity>()
                .Select(value => new ComplexityChoice(value, value switch
                {
                    ProjectComplexity.UltraSimple => "Ultra-simple (opt-in)",
                    ProjectComplexity.Simple => "Simple",
                    ProjectComplexity.Medium => "Moyen",
                    ProjectComplexity.Complex => "Complexe",
                    _ => value.ToString()
                })).ToArray();
        ComplexityCombo.SelectedIndex = 0;
        Loaded += async (_, _) => await RunActionAsync(
            () => ReloadAsync(false), "Chargement des modèles");
    }

    /// <summary>Whether a template override was successfully written.</summary>
    public bool Changed { get; private set; }

    /// <summary>Prevents nested save actions and routes failures to the shared UI guard.</summary>
    /// <param name="action">User-initiated action.</param>
    /// <param name="context">Diagnostics context.</param>
    /// <returns>Completed action.</returns>
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
            await UiActionGuard.RunAsync(this,
                "Modèles Parcours · " + context, action, _workspaceRoot);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Loads the exact catalog bytes and their revision without writes.</summary>
    /// <param name="confirmDiscard">Whether to ask before dropping staged edits.</param>
    /// <returns>Completed read.</returns>
    private async Task ReloadAsync(bool confirmDiscard)
    {
        if (confirmDiscard && _dirty &&
            MessageBox.Show(this,
                "Les modifications non enregistrées seront abandonnées. Recharger ?",
                "Modèles de Parcours", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        ProjectJourneyTemplateSnapshot snapshot =
            await _service.ReadTemplatesSnapshotAsync();
        _catalog = snapshot.Catalog;
        _revision = snapshot.Sha256;
        _dirty = false;
        Render(null, null, null);
        StatusText.Text = snapshot.Sha256 is null
            ? "Modèles intégrés · aucune personnalisation créée."
            : "Catalogue personnalisé chargé · modifications conditionnelles.";
    }

    /// <summary>Draws the catalog and current selection without mutating it.</summary>
    /// <param name="templateKey">Template to select.</param>
    /// <param name="stepKey">Stage to select.</param>
    /// <param name="operationKey">Operation to select.</param>
    private void Render(string? templateKey, string? stepKey, string? operationKey)
    {
        _rendering = true;
        try
        {
            TemplateRow[] templates = _catalog?.Templates.Select(item =>
                new TemplateRow(item, item.Name,
                    item.Complexity + " · " + item.Key)).ToArray() ?? [];
            TemplatesList.ItemsSource = templates;
            TemplatesList.SelectedItem = templates.FirstOrDefault(row =>
                string.Equals(row.Template.Key, templateKey,
                    StringComparison.OrdinalIgnoreCase)) ??
                templates.FirstOrDefault();
            RenderSteps(stepKey, operationKey);
            SummaryText.Text = _catalog is null
                ? "Chargement…"
                : _catalog.Templates.Count + " modèle(s) · " +
                  _catalog.Templates.Sum(item => item.Steps.Count) + " étape(s) proposées" +
                  (_dirty ? " · Modifications non enregistrées" : "");
        }
        finally
        {
            _rendering = false;
        }

        UpdateButtons();
    }

    /// <summary>Draws the stages of the selected model.</summary>
    /// <param name="stepKey">Stage key to select.</param>
    /// <param name="operationKey">Operation key to select.</param>
    private void RenderSteps(string? stepKey, string? operationKey)
    {
        _rendering = true;
        try
        {
            StepRow[] steps = SelectedTemplate()?.Steps
                .OrderBy(item => item.Order)
                .Select(item => new StepRow(item, item.Name, item.Key))
                .ToArray() ?? [];
            StepsList.ItemsSource = steps;
            StepsList.SelectedItem = steps.FirstOrDefault(row =>
                string.Equals(row.Step.Key, stepKey,
                    StringComparison.OrdinalIgnoreCase)) ??
                steps.FirstOrDefault();
            RenderOperations(operationKey);
        }
        finally
        {
            _rendering = false;
        }

        UpdateButtons();
    }

    /// <summary>Draws the operation definitions of the selected stage.</summary>
    /// <param name="operationKey">Operation key to select.</param>
    private void RenderOperations(string? operationKey)
    {
        _rendering = true;
        try
        {
            OperationRow[] operations = SelectedStep()?.Operations
                .Select(item => new OperationRow(item, item.Name,
                    item.PromptKey is null
                        ? "Aucun prompt · " + item.Key
                        : "Prompt : " + item.PromptKey + " · " + item.Key))
                .ToArray() ?? [];
            OperationsList.ItemsSource = operations;
            OperationsList.SelectedItem = operations.FirstOrDefault(row =>
                string.Equals(row.Operation.Key, operationKey,
                    StringComparison.OrdinalIgnoreCase)) ??
                operations.FirstOrDefault();
        }
        finally
        {
            _rendering = false;
        }

        UpdateButtons();
    }

    /// <summary>Restricts editing controls to the current selection and workspace mode.</summary>
    private void UpdateButtons()
    {
        bool editable = !_readOnly && !_busy && _catalog is not null;
        bool selectedTemplate = editable && SelectedTemplate() is not null;
        bool selectedStep = selectedTemplate && SelectedStep() is not null;
        bool selectedOperation = selectedStep && SelectedOperation() is not null;
        AddTemplateButton.IsEnabled = editable;
        RenameTemplateButton.IsEnabled = selectedTemplate;
        RemoveTemplateButton.IsEnabled = selectedTemplate;
        AddStepButton.IsEnabled = selectedTemplate;
        RenameStepButton.IsEnabled = selectedStep;
        UpStepButton.IsEnabled = selectedStep;
        DownStepButton.IsEnabled = selectedStep;
        RemoveStepButton.IsEnabled = selectedStep;
        AddOperationButton.IsEnabled = selectedStep;
        RenameOperationButton.IsEnabled = selectedOperation;
        PromptOperationButton.IsEnabled = selectedOperation;
        UpOperationButton.IsEnabled = selectedOperation;
        DownOperationButton.IsEnabled = selectedOperation;
        RemoveOperationButton.IsEnabled = selectedOperation;
        ComplexityCombo.IsEnabled = editable;
        SaveButton.IsEnabled = editable && _dirty;
        ReloadButton.IsEnabled = !_busy;
    }

    /// <summary>Returns the selected model or null.</summary>
    private ProjectJourneyTemplate? SelectedTemplate() =>
        (TemplatesList.SelectedItem as TemplateRow)?.Template;

    /// <summary>Returns the selected stage or null.</summary>
    private ProjectJourneyStepTemplate? SelectedStep() =>
        (StepsList.SelectedItem as StepRow)?.Step;

    /// <summary>Returns the selected operation or null.</summary>
    private ProjectJourneyOperationTemplate? SelectedOperation() =>
        (OperationsList.SelectedItem as OperationRow)?.Operation;

    /// <summary>Applies one pure transformation to the in-memory catalog.</summary>
    /// <param name="edit">A transformation without disk writes.</param>
    /// <param name="description">Human-readable action label.</param>
    /// <param name="templateKey">Template selection.</param>
    /// <param name="stepKey">Stage selection.</param>
    /// <param name="operationKey">Operation selection.</param>
    private void Stage(Func<ProjectJourneyTemplateCatalog,
        ProjectJourneyTemplateCatalog> edit,
        string description, string? templateKey,
        string? stepKey = null, string? operationKey = null)
    {
        if (_catalog is null || _readOnly)
        {
            return;
        }

        ProjectJourneyTemplateCatalog updated = edit(_catalog);
        if (ReferenceEquals(updated, _catalog))
        {
            return;
        }

        _catalog = updated;
        _dirty = true;
        Render(templateKey, stepKey, operationKey);
        StatusText.Text = description + " · À enregistrer.";
    }

    /// <summary>Prompts for a user-facing label.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="initialValue">Suggested label.</param>
    /// <returns>Reviewed label or null.</returns>
    private string? PromptName(string title, string initialValue = "")
    {
        TextPromptDialog dialog = new(title, "Libellé :", initialValue)
        {
            Owner = this
        };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    /// <summary>Creates a stable key independent of an editable label.</summary>
    /// <param name="kind">ASCII key prefix.</param>
    /// <returns>New safe key.</returns>
    private static string NewKey(string kind) =>
        kind + "-" + Guid.NewGuid().ToString("N");

    /// <summary>Handles the selected template changing.</summary>
    private void TemplatesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_rendering)
        {
            RenderSteps(null, null);
        }
    }

    /// <summary>Handles the selected stage changing.</summary>
    private void StepsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_rendering)
        {
            RenderOperations(null);
        }
    }

    /// <summary>Adds a new explicit model for the chosen project complexity.</summary>
    private async void AddTemplate_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            string? name = PromptName("Nouveau modèle de Parcours", "Parcours personnalisé");
            if (name is not null)
            {
                string key = NewKey("custom");
                ProjectComplexity complexity =
                    (ComplexityCombo.SelectedItem as ComplexityChoice)?.Value ??
                    ProjectComplexity.Complex;
                Stage(catalog => ProjectJourneyTemplateEditor.AddTemplate(
                        catalog, key, name, complexity),
                    "Modèle ajouté", key);
            }

            return Task.CompletedTask;
        }, "Ajouter un modèle");

    /// <summary>Renames one model without changing its key.</summary>
    private async void RenameTemplate_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            if (template is not null)
            {
                string? name = PromptName("Renommer le modèle", template.Name);
                if (name is not null)
                {
                    Stage(catalog => ProjectJourneyTemplateEditor.RenameTemplate(
                        catalog, template.Key, name), "Modèle renommé", template.Key);
                }
            }

            return Task.CompletedTask;
        }, "Renommer un modèle");

    /// <summary>Removes one model proposal after a local confirmation.</summary>
    private async void RemoveTemplate_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            if (template is not null && MessageBox.Show(this,
                "Retirer le modèle « " + template.Name + " » du catalogue ?\n\n" +
                "Les parcours déjà créés et leurs documents restent inchangés.",
                "Retirer un modèle", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Stage(catalog => ProjectJourneyTemplateEditor.RemoveTemplate(
                    catalog, template.Key), "Modèle retiré", null);
            }

            return Task.CompletedTask;
        }, "Retirer un modèle");

    /// <summary>Appends a stage to the current template.</summary>
    private async void AddStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            if (template is not null)
            {
                string? name = PromptName("Nouvelle étape");
                if (name is not null)
                {
                    string key = NewKey("step");
                    Stage(catalog => ProjectJourneyTemplateEditor.AddStep(
                        catalog, template.Key, key, name),
                        "Étape ajoutée", template.Key, key);
                }
            }

            return Task.CompletedTask;
        }, "Ajouter une étape");

    /// <summary>Renames the current stage without changing its key.</summary>
    private async void RenameStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            ProjectJourneyStepTemplate? step = SelectedStep();
            if (template is not null && step is not null)
            {
                string? name = PromptName("Renommer l'étape", step.Name);
                if (name is not null)
                {
                    Stage(catalog => ProjectJourneyTemplateEditor.RenameStep(
                        catalog, template.Key, step.Key, name),
                        "Étape renommée", template.Key, step.Key);
                }
            }

            return Task.CompletedTask;
        }, "Renommer une étape");

    /// <summary>Moves a stage up.</summary>
    private async void UpStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveStepAsync(-1), "Monter une étape");

    /// <summary>Moves a stage down.</summary>
    private async void DownStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveStepAsync(1), "Descendre une étape");

    /// <summary>Moves one template stage in memory.</summary>
    /// <param name="direction">-1 or +1.</param>
    /// <returns>Completed synchronous edit.</returns>
    private Task MoveStepAsync(int direction)
    {
        ProjectJourneyTemplate? template = SelectedTemplate();
        ProjectJourneyStepTemplate? step = SelectedStep();
        if (template is not null && step is not null)
        {
            Stage(catalog => ProjectJourneyTemplateEditor.MoveStep(
                catalog, template.Key, step.Key, direction),
                "Étape déplacée", template.Key, step.Key);
        }

        return Task.CompletedTask;
    }

    /// <summary>Removes a stage proposal after approval.</summary>
    private async void RemoveStep_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            ProjectJourneyStepTemplate? step = SelectedStep();
            if (template is not null && step is not null && MessageBox.Show(this,
                "Retirer l'étape « " + step.Name + " » et ses " +
                step.Operations.Count + " opération(s) du modèle ?",
                "Retirer une étape", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Stage(catalog => ProjectJourneyTemplateEditor.RemoveStep(
                    catalog, template.Key, step.Key),
                    "Étape retirée", template.Key);
            }

            return Task.CompletedTask;
        }, "Retirer une étape");

    /// <summary>Adds a repeatable operation definition.</summary>
    private async void AddOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            ProjectJourneyStepTemplate? step = SelectedStep();
            if (template is not null && step is not null)
            {
                string? name = PromptName("Nouvelle opération");
                if (name is not null)
                {
                    string key = NewKey("operation");
                    Stage(catalog => ProjectJourneyTemplateEditor.AddOperation(
                        catalog, template.Key, step.Key, key, name),
                        "Opération ajoutée", template.Key, step.Key, key);
                }
            }

            return Task.CompletedTask;
        }, "Ajouter une opération");

    /// <summary>Renames an operation definition.</summary>
    private async void RenameOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            ProjectJourneyStepTemplate? step = SelectedStep();
            ProjectJourneyOperationTemplate? operation = SelectedOperation();
            if (template is not null && step is not null && operation is not null)
            {
                string? name = PromptName("Renommer l'opération", operation.Name);
                if (name is not null)
                {
                    Stage(catalog => ProjectJourneyTemplateEditor.RenameOperation(
                        catalog, template.Key, step.Key, operation.Key, name),
                        "Opération renommée", template.Key, step.Key, operation.Key);
                }
            }

            return Task.CompletedTask;
        }, "Renommer une opération");

    /// <summary>
    /// Changes only the optional prompt identifier, never launching any model.
    /// Use a single dash to clear an existing prompt link.
    /// </summary>
    private async void PromptOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            ProjectJourneyStepTemplate? step = SelectedStep();
            ProjectJourneyOperationTemplate? operation = SelectedOperation();
            if (template is not null && step is not null && operation is not null)
            {
                TextPromptDialog dialog = new(
                    "Référence de prompt",
                    "Clé ASCII du prompt, ou « - » pour aucune référence :",
                    operation.PromptKey ?? "-")
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true)
                {
                    string? key = dialog.Value == "-" ? null : dialog.Value;
                    Stage(catalog => ProjectJourneyTemplateEditor.SetPromptKey(
                        catalog, template.Key, step.Key, operation.Key, key),
                        "Référence de prompt modifiée",
                        template.Key, step.Key, operation.Key);
                }
            }

            return Task.CompletedTask;
        }, "Modifier un prompt");

    /// <summary>Moves an operation up.</summary>
    private async void UpOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveOperationAsync(-1), "Monter une opération");

    /// <summary>Moves an operation down.</summary>
    private async void DownOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => MoveOperationAsync(1), "Descendre une opération");

    /// <summary>Moves an operation definition without replacing its key.</summary>
    /// <param name="direction">-1 or +1.</param>
    /// <returns>Completed edit.</returns>
    private Task MoveOperationAsync(int direction)
    {
        ProjectJourneyTemplate? template = SelectedTemplate();
        ProjectJourneyStepTemplate? step = SelectedStep();
        ProjectJourneyOperationTemplate? operation = SelectedOperation();
        if (template is not null && step is not null && operation is not null)
        {
            Stage(catalog => ProjectJourneyTemplateEditor.MoveOperation(
                catalog, template.Key, step.Key, operation.Key, direction),
                "Opération déplacée", template.Key, step.Key, operation.Key);
        }

        return Task.CompletedTask;
    }

    /// <summary>Removes an operation proposal.</summary>
    private async void RemoveOperation_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() =>
        {
            ProjectJourneyTemplate? template = SelectedTemplate();
            ProjectJourneyStepTemplate? step = SelectedStep();
            ProjectJourneyOperationTemplate? operation = SelectedOperation();
            if (template is not null && step is not null && operation is not null &&
                MessageBox.Show(this, "Retirer « " + operation.Name +
                    " » de ce modèle ?", "Retirer une opération",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
                MessageBoxResult.Yes)
            {
                Stage(catalog => ProjectJourneyTemplateEditor.RemoveOperation(
                    catalog, template.Key, step.Key, operation.Key),
                    "Opération retirée", template.Key, step.Key);
            }

            return Task.CompletedTask;
        }, "Retirer une opération");

    /// <summary>Reloads only after asking about unsaved staged edits.</summary>
    private async void Reload_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(() => ReloadAsync(true), "Recharger");

    /// <summary>Saves all reviewed model changes with one guarded commit.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            if (!_dirty || _catalog is null || _readOnly)
            {
                return;
            }

            if (MessageBox.Show(this,
                "Enregistrer ce catalogue pour les futurs Parcours ?\n\n" +
                _catalog.Templates.Count + " modèle(s), " +
                _catalog.Templates.Sum(item => item.Steps.Count) +
                " étape(s).\n\nLes Parcours existants et leurs preuves ne " +
                "seront jamais modifiés.",
                "Enregistrer les modèles", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _revision = await _service.SaveTemplatesAsync(_catalog, _revision);
                _dirty = false;
                Changed = true;
                Render(SelectedTemplate()?.Key, SelectedStep()?.Key,
                    SelectedOperation()?.Key);
                StatusText.Text = "Modèles enregistrés · nouvelle révision protégée.";
            }
            catch (Exception exception) when (exception is IOException or
                InvalidDataException or InvalidOperationException or
                UnauthorizedAccessException)
            {
                MessageBox.Show(this,
                    "Le catalogue a changé depuis son ouverture ou l'écriture " +
                    "est impossible. Aucun remplacement automatique.\n\n" +
                    exception.Message + "\n\nRechargez avant de modifier à nouveau.",
                    "Conflit de modèles", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusText.Text = "Sauvegarde refusée · modifications conservées en mémoire.";
            }
        }, "Enregistrer les modèles");

    /// <summary>Closes only if no unsaved edits are silently lost.</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Asks before discarding pending in-memory edits on close.</summary>
    private void Dialog_Closing(object? sender, CancelEventArgs e)
    {
        if (_dirty && MessageBox.Show(this,
            "Des modifications de modèles ne sont pas enregistrées.\n\n" +
            "Fermer et les abandonner ?", "Modèles de Parcours",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private sealed record ComplexityChoice(ProjectComplexity Value, string Label);

    private sealed record TemplateRow(
        ProjectJourneyTemplate Template, string Name, string Detail);

    private sealed record StepRow(
        ProjectJourneyStepTemplate Step, string Name, string Key);

    private sealed record OperationRow(
        ProjectJourneyOperationTemplate Operation, string Name, string PromptLabel);
}
