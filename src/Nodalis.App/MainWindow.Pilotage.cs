using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>
/// Centralizes existing Nodalis 2.0 project tools in the Pilotage menu.
/// Stages and operations are read from the selected project's real journey,
/// not from a stale list of hardcoded workflow names.
/// </summary>
public partial class MainWindow
{
    private int _pilotageMenuRevision;

    private enum PilotageJourneyMode
    {
        Steps,
        Prepare,
        Assign
    }

    private enum PilotageTargetMode
    {
        Focus,
        Prepare,
        Assign
    }

    private sealed record PilotageJourneyTarget(
        Guid ProjectId, string ProjectPath, Guid StepId,
        Guid? OperationId, PilotageTargetMode Mode);

    /// <summary>
    /// An immutable snapshot of one stage for deferred WPF menu materialization.
    /// Stable IDs are rechecked against the project before any action runs.
    /// </summary>
    private sealed record PilotageOpenJourneyTarget(
        Guid ProjectId, string ProjectPath);

    private sealed record PilotageStageMenuContext(
        Guid ProjectId, string ProjectPath,
        ProjectJourneyPilotageStage Stage, PilotageJourneyMode Mode)
    {
        /// <summary>
        /// Operation controls were built for this one opening of the parent
        /// menu. A fresh project menu creates a new context object.
        /// </summary>
        public bool Expanded { get; set; }
    }

    /// <summary>
    /// Searches the real journey in a virtualized dialog rather than
    /// requiring a long hierarchical menu on large customized projects.
    /// </summary>
    /// <returns>Selected stable project operation through existing actions.</returns>
    private async Task OpenPilotageQuickAccessAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Recherche dans le parcours");
            return;
        }

        Guid projectId = project.Id;
        string directory = project.FullPath;
        ProjectJourneySnapshot? snapshot = await Task.Run(() =>
            new ProjectJourneyService(_root.FullPath)
                .ReadAsync(directory));
        NavigationNodeViewModel? current = PilotageProject();
        if (current is null || current.Id != projectId ||
            !string.Equals(current.FullPath, directory,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Projet modifié pendant la recherche Pilotage.";
            return;
        }

        if (snapshot is null)
        {
            MessageBox.Show(this,
                "Ce projet n'a pas encore de parcours initialisé. " +
                "Ouvrez « Parcours projet » pour examiner puis " +
                "confirmer une initialisation, sans création automatique.",
                "Rechercher une étape · Pilotage 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (snapshot.Document.ProjectId != projectId)
        {
            throw new InvalidDataException(
                "Le parcours ne correspond pas au projet sélectionné.");
        }

        IReadOnlyList<ProjectJourneyPilotageSearchRow> index =
            await Task.Run(() => ProjectJourneyPilotageSearch.Index(
                ProjectJourneyPilotageProjection.Build(snapshot.Document)));
        NavigationNodeViewModel? stillSelected = PilotageProject();
        if (stillSelected?.Id != projectId ||
            !string.Equals(stillSelected.FullPath, directory,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Projet modifié pendant l'indexation Pilotage.";
            return;
        }

        ProjectJourneyQuickAccessDialog dialog = new(
            index, PilotageReadOnly(project))
        { Owner = this };
        if (dialog.ShowDialog() != true ||
            dialog.Selection is not ProjectJourneyQuickSelection choice)
        {
            return;
        }

        PilotageTargetMode mode = choice.Action switch
        {
            ProjectJourneyQuickAction.Open => PilotageTargetMode.Focus,
            ProjectJourneyQuickAction.Prepare => PilotageTargetMode.Prepare,
            ProjectJourneyQuickAction.Assign => PilotageTargetMode.Assign,
            _ => throw new InvalidDataException(
                "Action de recherche Pilotage inconnue.")
        };
        await OpenPilotageTargetAsync(new PilotageJourneyTarget(
            projectId, directory, choice.StepId, choice.OperationId,
            mode));
    }

    /// <summary>Opens searchable direct navigation and prompt preparation.</summary>
    /// <param name="sender">Pilotage search menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageQuickAccess_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Recherche dans le parcours 2.0",
            OpenPilotageQuickAccessAsync);

    /// <summary>Opens the actual Pilotage project, not an old document selection.</summary>
    /// <param name="sender">Main Pilotage journey command.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageOpenCurrentJourney_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Parcours du projet actif", () =>
        {
            NavigationNodeViewModel? project = PilotageProject();
            if (project is null)
            {
                ShowPilotageProjectHint("Parcours projet");
                return Task.CompletedTask;
            }

            return OpenProjectJourneyAsync(project);
        });

    /// <summary>
    /// Opens the exact menu-selected project when its journey does not yet
    /// exist, without using another active Worktree document as a fallback.
    /// </summary>
    /// <param name="sender">Human initialization entry.</param>
    /// <param name="e">Menu click.</param>
    private async void PilotageOpenJourney_Click(
        object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem
            {
                Tag: PilotageOpenJourneyTarget target
            })
        {
            return;
        }

        await RunUiActionAsync("Ouvrir un parcours non initialisé", () =>
        {
            NavigationNodeViewModel? project = PilotageProject();
            if (project is null || project.Id != target.ProjectId ||
                !string.Equals(project.FullPath, target.ProjectPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text =
                    "Projet modifié : rouvrez Pilotage avant initialisation.";
                return Task.CompletedTask;
            }

            return OpenProjectJourneyAsync(project);
        });
    }

    /// <summary>Lists actual project stages when the submenu opens.</summary>
    /// <param name="sender">Journey stages submenu.</param>
    /// <param name="e">WPF submenu open event.</param>
    private async void PilotageJourneySteps_SubmenuOpened(
        object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, PilotageJourneyStepsMenu) &&
            ReferenceEquals(e.OriginalSource, PilotageJourneyStepsMenu))
        {
            await PopulatePilotageJourneyAsync(PilotageJourneyStepsMenu,
                PilotageJourneyMode.Steps);
        }
    }

    /// <summary>Lists all actual project operations for copy-ready prompts.</summary>
    /// <param name="sender">Prompt submenu.</param>
    /// <param name="e">WPF submenu open event.</param>
    private async void PilotagePrompts_SubmenuOpened(
        object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, PilotagePromptsMenu) &&
            ReferenceEquals(e.OriginalSource, PilotagePromptsMenu))
        {
            await PopulatePilotageJourneyAsync(PilotagePromptsMenu,
                PilotageJourneyMode.Prepare);
        }
    }

    /// <summary>Lists actual operations for navigating to their prompt picker.</summary>
    /// <param name="sender">Prompt assignment submenu.</param>
    /// <param name="e">WPF submenu open event.</param>
    private async void PilotagePromptAssignments_SubmenuOpened(
        object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, PilotagePromptAssignmentsMenu) &&
            ReferenceEquals(e.OriginalSource, PilotagePromptAssignmentsMenu))
        {
            await PopulatePilotageJourneyAsync(
                PilotagePromptAssignmentsMenu, PilotageJourneyMode.Assign);
        }
    }

    /// <summary>
    /// Loads the current project-only journey asynchronously. It never
    /// initializes a missing journey or modifies any project document.
    /// </summary>
    /// <param name="menu">Submenu that requested the stage list.</param>
    /// <param name="mode">Stage navigation, draft preparation or assignment.</param>
    /// <returns>Menu items for the exact current project or safe diagnostics.</returns>
    private async Task PopulatePilotageJourneyAsync(
        MenuItem menu, PilotageJourneyMode mode)
    {
        int generation = ++_pilotageMenuRevision;
        menu.Items.Clear();
        menu.Items.Add(PilotageNotice("Lecture du parcours…"));
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            menu.Items.Clear();
            menu.Items.Add(PilotageNotice("Sélectionnez d'abord un projet."));
            return;
        }

        string directory = project.FullPath;
        Guid projectId = project.Id;
        try
        {
            ProjectJourneyService service = new(_root.FullPath);
            ProjectJourneySnapshot? snapshot = await Task.Run(() =>
                service.ReadAsync(directory));
            ProjectJourneyTemplate? proposed = snapshot is null &&
                mode == PilotageJourneyMode.Steps
                    ? await Task.Run(() =>
                        service.PreviewDefaultAsync(directory))
                    : null;
            NavigationNodeViewModel? selected = PilotageProject();
            if (generation != _pilotageMenuRevision || !menu.IsSubmenuOpen ||
                selected is null || selected.Id != projectId ||
                !string.Equals(selected.FullPath, directory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            menu.Items.Clear();
            if (snapshot is null)
            {
                menu.Items.Add(PilotageNotice(
                    "Parcours non initialisé : aucun document créé."));
                if (proposed is not null)
                {
                    MenuItem template = new()
                    {
                        Header = "Étapes proposées · modèle non activé",
                        ToolTip = "Aperçu en lecture seule, aucun parcours créé"
                    };
                    int previewNumber = 0;
                    foreach (ProjectJourneyStepTemplate stage in proposed.Steps
                        .OrderBy(item => item.Order))
                    {
                        MenuItem proposedStep = new()
                        {
                            Header = PilotageMenuHeader(
                                (++previewNumber) + ". " + stage.Name)
                        };
                        foreach (ProjectJourneyOperationTemplate operation in
                            stage.Operations)
                        {
                            proposedStep.Items.Add(PilotageNotice(
                                operation.Name + " · opération proposée"));
                        }

                        template.Items.Add(proposedStep);
                    }

                    menu.Items.Add(template);
                }

                MenuItem open = new()
                {
                    Header = "Ouvrir / initialiser le parcours…",
                    Tag = new PilotageOpenJourneyTarget(projectId, directory)
                };
                open.Click += PilotageOpenJourney_Click;
                menu.Items.Add(open);
                return;
            }

            if (snapshot.Document.ProjectId != projectId)
            {
                menu.Items.Add(PilotageNotice(
                    "Identité du parcours différente du projet."));
                return;
            }

            ProjectJourneyPilotageStage[] steps =
                ProjectJourneyPilotageProjection.Build(snapshot.Document)
                    .ToArray();
            if (steps.Length == 0)
            {
                menu.Items.Add(PilotageNotice(
                    "Aucune étape : éditez le parcours pour en créer."));
                return;
            }

            int stageNumber = 0;
            foreach (ProjectJourneyPilotageStage step in steps)
            {
                // Human stage numbering is always 1..N, independently of
                // the technical 0/10/20 values stored by the journey editor.
                string stepLabel = (++stageNumber) + ". " + step.Name;
                if (mode == PilotageJourneyMode.Steps)
                {
                    stepLabel += " · " + PilotageStepStatus(step.Status);
                }

                MenuItem stage = new()
                {
                    Header = PilotageMenuHeader(stepLabel),
                    Tag = new PilotageStageMenuContext(
                        projectId, directory, step, mode)
                };
                // Do not eagerly construct every operation MenuItem while
                // hovering over Pilotage: large projects used to allocate
                // thousands of WPF controls and could freeze the dispatcher.
                stage.Items.Add(PilotageNotice(
                    "Développer pour afficher les opérations…"));
                stage.SubmenuOpened += PilotageStage_SubmenuOpened;
                menu.Items.Add(stage);
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            if (generation == _pilotageMenuRevision && menu.IsSubmenuOpen)
            {
                menu.Items.Clear();
                menu.Items.Add(PilotageNotice(
                    "Parcours inaccessible : " + exception.GetType().Name));
            }
        }
    }

    /// <summary>
    /// Materializes only the chosen stage's operations when that stage is
    /// expanded. No disk read or automatic change occurs on hover.
    /// </summary>
    /// <param name="sender">Current stage menu.</param>
    /// <param name="e">Routed submenu event.</param>
    private void PilotageStage_SubmenuOpened(
        object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem stage ||
            !ReferenceEquals(e.OriginalSource, stage) ||
            stage.Tag is not PilotageStageMenuContext context)
        {
            return;
        }

        NavigationNodeViewModel? project = PilotageProject();
        if (project is null || project.Id != context.ProjectId ||
            !string.Equals(project.FullPath, context.ProjectPath,
                StringComparison.OrdinalIgnoreCase))
        {
            stage.Items.Clear();
            stage.Items.Add(PilotageNotice(
                "Projet modifié : rouvrez Pilotage."));
            return;
        }

        // Avoid rebuilding hundreds of WPF controls each time the pointer
        // briefly re-enters an already expanded stage submenu. The parent
        // menu always provides a fresh context on its next opening.
        if (context.Expanded)
        {
            return;
        }

        stage.Items.Clear();
        if (context.Mode == PilotageJourneyMode.Steps)
        {
            stage.Items.Add(PilotageTargetItem(
                "Ouvrir cette étape", context.ProjectId,
                context.ProjectPath, context.Stage.Id, null,
                PilotageTargetMode.Focus));
            if (context.Stage.Operations.Count > 0)
            {
                stage.Items.Add(new Separator());
            }
        }

        if (context.Stage.Operations.Count == 0 &&
            context.Mode != PilotageJourneyMode.Steps)
        {
            stage.Items.Add(PilotageNotice(
                "Aucune opération dans cette étape."));
        }

        foreach (ProjectJourneyPilotageOperation operation in
            context.Stage.Operations)
        {
            if (context.Mode == PilotageJourneyMode.Steps)
            {
                MenuItem nested = new()
                {
                    Header = PilotageMenuHeader(operation.Name),
                    ToolTip = operation.PromptKey is null
                        ? "Aucun prompt associé : brouillon générique disponible."
                        : "Prompt associé : " + operation.PromptKey
                };
                nested.Items.Add(PilotageTargetItem(
                    "Afficher l'opération", context.ProjectId,
                    context.ProjectPath, context.Stage.Id,
                    operation.Id, PilotageTargetMode.Focus));
                nested.Items.Add(PilotageTargetItem(
                    "Préparer / relire / copier le prompt…",
                    context.ProjectId, context.ProjectPath,
                    context.Stage.Id, operation.Id,
                    PilotageTargetMode.Prepare));
                MenuItem assign = PilotageTargetItem(
                    "Associer / changer le prompt…", context.ProjectId,
                    context.ProjectPath, context.Stage.Id,
                    operation.Id, PilotageTargetMode.Assign);
                assign.IsEnabled = !PilotageReadOnly(project);
                nested.Items.Add(assign);
                stage.Items.Add(nested);
                continue;
            }

            string operationLabel = operation.Name +
                (operation.PromptKey is null
                    ? " · prompt non associé" : "");
            MenuItem item = PilotageTargetItem(
                operationLabel, context.ProjectId, context.ProjectPath,
                context.Stage.Id, operation.Id,
                context.Mode == PilotageJourneyMode.Prepare
                    ? PilotageTargetMode.Prepare
                    : PilotageTargetMode.Assign);
            if (context.Mode == PilotageJourneyMode.Assign)
            {
                item.IsEnabled = !PilotageReadOnly(project);
            }

            stage.Items.Add(item);
        }

        // Only consider this stage materialized once all controls exist.
        // An interrupted construction can be retried on the next opening.
        context.Expanded = true;
    }

    /// <summary>Uses the same readable French stage statuses as the journey.</summary>
    /// <param name="status">Canonical stage state.</param>
    /// <returns>Stable human-facing label, not a task status.</returns>
    private static string PilotageStepStatus(ProjectJourneyStepStatus status) =>
        status switch
        {
            ProjectJourneyStepStatus.Todo => "À faire",
            ProjectJourneyStepStatus.InProgress => "En cours",
            ProjectJourneyStepStatus.Completed => "Terminée",
            ProjectJourneyStepStatus.NotApplicable => "Non applicable",
            _ => "Inconnu"
        };

    /// <summary>Builds one disabled, noninteractive menu diagnostic.</summary>
    /// <param name="message">Safe user-visible status.</param>
    /// <returns>Disabled WPF menu item.</returns>
    private static MenuItem PilotageNotice(string message) =>
        new() { Header = PilotageMenuHeader(message), IsEnabled = false };

    /// <summary>
    /// Escapes WPF access-key underscores in user-customized stage/operation
    /// labels. The visible title must not lose characters or define
    /// accidental keyboard shortcuts.
    /// </summary>
    /// <param name="title">Human label, including custom project names.</param>
    /// <returns>WPF-safe literal header text.</returns>
    private static string PilotageMenuHeader(string title) =>
        title.Replace("_", "__", StringComparison.Ordinal);

    /// <summary>Creates one strongly identified project-local shortcut.</summary>
    /// <param name="label">Menu action text.</param>
    /// <param name="projectId">Stable selected project GUID.</param>
    /// <param name="projectPath">Exact selected project directory.</param>
    /// <param name="stepId">Stable stage GUID.</param>
    /// <param name="operationId">Optional operation GUID.</param>
    /// <param name="mode">Navigation, prompt draft or prompt assignment.</param>
    /// <returns>One action wired to existing project views.</returns>
    private MenuItem PilotageTargetItem(string label, Guid projectId,
        string projectPath, Guid stepId, Guid? operationId,
        PilotageTargetMode mode)
    {
        MenuItem item = new()
        {
            Header = PilotageMenuHeader(label),
            Tag = new PilotageJourneyTarget(
                projectId, projectPath, stepId, operationId, mode)
        };
        item.Click += PilotageJourneyTarget_Click;
        return item;
    }

    /// <summary>Revalidates current project before opening a live operation.</summary>
    /// <param name="sender">Action containing stable project/step IDs.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageJourneyTarget_Click(
        object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem
            {
                Tag: PilotageJourneyTarget target
            })
        {
            await RunUiActionAsync("Pilotage 2.0 · étape du projet",
                () => OpenPilotageTargetAsync(target));
        }
    }

    /// <summary>
    /// Opens the correct project scope and never reuses an operation ID from a
    /// different selection or a journey changed since the menu was built.
    /// </summary>
    /// <param name="target">Stable identity captured in the menu item.</param>
    /// <returns>Reviewed journey view or copy-ready prompt dialog.</returns>
    private async Task OpenPilotageTargetAsync(PilotageJourneyTarget target)
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null || project.Id != target.ProjectId ||
            !string.Equals(project.FullPath, target.ProjectPath,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Projet modifié depuis l'ouverture de Pilotage : " +
                "sélectionnez à nouveau l'étape.";
            return;
        }

        if (target.Mode == PilotageTargetMode.Prepare)
        {
            await PreparePilotagePromptAsync(target);
            return;
        }

        if (target.Mode == PilotageTargetMode.Assign &&
            PilotageReadOnly(project))
        {
            StatusText.Text =
                "Projet archivé ou workspace en lecture seule : " +
                "l'association de prompts est indisponible.";
            return;
        }

        // Recheck the stage and operation against a fresh project snapshot,
        // never use a stale label or copy of the original object.
        ProjectJourneySnapshot? current = await Task.Run(() =>
            new ProjectJourneyService(_root.FullPath)
                .ReadAsync(target.ProjectPath));
        ProjectJourneyStep? stage = current?.Document.Steps
            .SingleOrDefault(item => item.Id == target.StepId);
        if (current?.Document.ProjectId != target.ProjectId ||
            stage is null ||
            target.OperationId is Guid opId &&
                !stage.Operations.Any(item => item.Id == opId))
        {
            StatusText.Text = "Étape du parcours modifiée : " +
                "rouvrez la liste Pilotage.";
            return;
        }

        if (PilotageProject()?.Id != target.ProjectId)
        {
            StatusText.Text = "Sélection du projet modifiée avant navigation.";
            return;
        }

        ProjectJourneyTabEntry? tab = _projectJourneyTabs.FirstOrDefault(
            item => item.ProjectId == target.ProjectId &&
                string.Equals(item.ProjectDirectory, target.ProjectPath,
                    StringComparison.OrdinalIgnoreCase));
        if (tab is null)
        {
            await OpenProjectJourneyAsync(project);
            tab = _projectJourneyTabs.FirstOrDefault(item =>
                item.ProjectId == target.ProjectId &&
                string.Equals(item.ProjectDirectory, target.ProjectPath,
                    StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // An already open tab is the source of truth, even when the
            // worktree selection still points at a different project.
            ActivateProjectJourneyTab(tab);
        }

        if (tab is null)
        {
            return;
        }

        tab.View.FocusFromPilotage(target.StepId, target.OperationId,
            assignPrompt: target.Mode == PilotageTargetMode.Assign);
        StatusText.Text = target.Mode == PilotageTargetMode.Assign
            ? "Sélecteur du prompt de l'opération ouvert après " +
                "validation du parcours ; confirmation humaine requise."
            : "Parcours ouvert sur l'étape / opération choisie.";
    }

    /// <summary>
    /// Uses the same prompt preparation and human copy dialog as the journey.
    /// It never sends data to an AI and does not mutate a project file.
    /// </summary>
    /// <param name="target">Exact project/step/operation identity.</param>
    /// <returns>Human-reviewed draft and optional explicit clipboard copy.</returns>
    private async Task PreparePilotagePromptAsync(PilotageJourneyTarget target)
    {
        if (target.OperationId is not Guid operationId)
        {
            throw new InvalidDataException(
                "Sélectionnez une opération réelle pour préparer son prompt.");
        }

        ProjectJourneySnapshot? snapshot = await Task.Run(() =>
            new ProjectJourneyService(_root.FullPath)
                .ReadAsync(target.ProjectPath));
        ProjectJourneyStep? stage = snapshot?.Document.Steps
            .SingleOrDefault(item => item.Id == target.StepId);
        ProjectJourneyOperation? operation = stage?.Operations
            .SingleOrDefault(item => item.Id == operationId);
        if (snapshot?.Document.ProjectId != target.ProjectId ||
            stage is null || operation is null ||
            PilotageProject()?.Id != target.ProjectId)
        {
            StatusText.Text = "La sélection ou le parcours a changé : " +
                "relancez la préparation depuis Pilotage.";
            return;
        }

        string promptRoot = Path.Combine(AppContext.BaseDirectory, "Prompts");
        ProjectJourneyPromptPreparationService preparer = new(
            _root.FullPath, promptRoot);
        ProjectJourneyPromptDraft draft;
        string? templatePath = operation.PromptKey is string key
            ? Path.Combine(promptRoot, key + ".md") : null;
        if (templatePath is not null && File.Exists(templatePath))
        {
            string original = await new FileSystemAiPromptStore(promptRoot)
                .LoadAsync(operation.PromptKey!);
            IReadOnlyList<string> variables =
                ProjectJourneyPromptVariables.FindPlaceholders(original);
            if (variables.Count > 0)
            {
                ProjectJourneyPromptBindingsDialog picker = new(
                    _root.FullPath, variables, operation.SourcePaths)
                {
                    Owner = this
                };
                if (picker.ShowDialog() != true)
                {
                    return;
                }

                draft = await Task.Run(() =>
                    preparer.PrepareWithBindingsAsync(
                        stage, operation, picker.Bindings));
            }
            else
            {
                draft = await Task.Run(() =>
                    preparer.PrepareAsync(stage, operation));
            }
        }
        else
        {
            draft = await Task.Run(() =>
                preparer.PrepareAsync(stage, operation));
        }

        // A long variable-binding dialog or source preparation can span
        // external edits to the project journey. Never present a copy-ready
        // draft whose selected operation is no longer the reviewed version.
        ProjectJourneySnapshot? refreshed = await Task.Run(() =>
            new ProjectJourneyService(_root.FullPath)
                .ReadAsync(target.ProjectPath));
        NavigationNodeViewModel? selected = PilotageProject();
        if (refreshed?.Document.ProjectId != target.ProjectId ||
            !string.Equals(refreshed?.Sha256, snapshot!.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            selected?.Id != target.ProjectId ||
            !string.Equals(selected.FullPath, target.ProjectPath,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Prompt non présenté : parcours ou projet " +
                "modifié pendant sa préparation.";
            MessageBox.Show(this,
                "Le projet ou le parcours a changé depuis la sélection " +
                "de l'opération. Rouvrez Pilotage et préparez le prompt " +
                "avec les sources à jour.",
                "Pilotage 2.0 · préparation devenue obsolète",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectJourneyPromptDraftDialog review = new(draft)
        {
            Owner = this
        };
        review.ShowDialog();
        StatusText.Text = "Prompt préparé pour relecture / copie manuelle " +
            "· aucune IA exécutée, aucune donnée projet modifiée.";
    }

    /// <summary>Gets the selected unique project, or nothing if ambiguous.</summary>
    /// <returns>Existing project navigation node or null.</returns>
    private NavigationNodeViewModel? PilotageProject()
    {
        NavigationNodeViewModel? project;
        if (_activeProjectJourneyTab is ProjectJourneyTabEntry active &&
            ProjectJourneyHost.Visibility == Visibility.Visible)
        {
            // The current journey surface wins over an old Worktree
            // selection: never display another project's prompts while
            // this tab is active.
            project = _root.DescendantsAndSelf().FirstOrDefault(node =>
                node.Kind == WorkspaceNodeKind.Project &&
                node.Id == active.ProjectId &&
                string.Equals(node.FullPath, active.ProjectDirectory,
                    StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            project = GetArchiveContextProject();
        }

        if (project is null || project.Id == Guid.Empty ||
            _root.DescendantsAndSelf().Count(item =>
                item.Kind == WorkspaceNodeKind.Project &&
                item.Id == project.Id) != 1)
        {
            return null;
        }

        return project;
    }

    /// <summary>Checks read-only workspace and archived project context.</summary>
    /// <param name="project">Uniquely selected project node.</param>
    /// <returns>Whether write actions must be disabled.</returns>
    private bool PilotageReadOnly(NavigationNodeViewModel project)
    {
        string relative = Path.GetRelativePath(
            _root.FullPath, project.FullPath);
        return _workspaceReadOnly || string.Equals(
            relative.Split(Path.DirectorySeparatorChar)[0],
            WorkspaceLayout.ArchivesDirectoryName,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shows the already available project-specific external AI review.</summary>
    /// <returns>Explicit user-review dialog or no action.</returns>
    private Task OpenPilotageExternalAiAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Réponse IA externe");
            return Task.CompletedTask;
        }

        ProjectAiResponsePreviewDialog dialog = new(
            _root.FullPath, project.FullPath, PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    /// <summary>Shows the existing, human-controlled risk registry.</summary>
    /// <returns>Opened project-local risk dialog and any chosen source.</returns>
    private async Task OpenPilotageRisksAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Risques du projet");
            return;
        }

        ProjectRiskRegistryDialog dialog = new(
            _root.FullPath, project.FullPath, PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await RefreshNavigationAsync(project.FullPath);
        }

        if (dialog.SelectedGateId is Guid gateId)
        {
            await OpenPilotageGatesAsync(gateId,
                dialog.SelectedGateDecisionId,
                dialog.SelectedGateReservationId);
        }
        else if (dialog.SelectedRiskPath is string risk)
        {
            await OpenProjectJourneySourceAsync(risk);
        }
        else if (dialog.SelectedLinkedTaskPath is string task)
        {
            await OpenProjectJourneySourceAsync(
                task, dialog.SelectedLinkedTaskLine);
        }
    }

    /// <summary>Opens existing project gates without linking them to an operation.</summary>
    /// <param name="gateId">Optional gate to highlight.</param>
    /// <param name="decisionId">Optional past gate verdict to show.</param>
    /// <param name="reservationId">Optional linked reservation to show.</param>
    /// <returns>Completed project-local gate registry dialog.</returns>
    private async Task OpenPilotageGatesAsync(Guid? gateId = null,
        Guid? decisionId = null, Guid? reservationId = null)
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Gates / Go-No-Go");
            return;
        }

        ProjectGateRegistryDialog dialog = new(
            _root.FullPath, project.FullPath, PilotageReadOnly(project),
            [], allowSelection: false, initialGateId: gateId,
            initialDecisionId: decisionId, initialReservationId: reservationId)
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await RefreshNavigationAsync(project.FullPath);
        }

        if (dialog.SelectedRiskPath is string risk)
        {
            await OpenProjectJourneySourceAsync(risk);
        }
    }

    /// <summary>Explains project selection without silently switching context.</summary>
    /// <param name="context">Requested tool's localized title.</param>
    private void ShowPilotageProjectHint(string context)
    {
        MessageBox.Show(this,
            "Sélectionnez un projet ou un document appartenant à un " +
            "projet dont l'identifiant est unique.",
            context + " · Pilotage 2.0", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>Opens existing journey template customization.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageJourneyTemplates_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Modèles de parcours", ManageJourneyTemplatesAsync);

    /// <summary>Opens human external AI response review, never local execution.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageExternalAi_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Réponse IA externe", OpenPilotageExternalAiAsync);

    /// <summary>Starts the existing EXI creation wizard.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageNewRequirement_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Créer une exigence", CreateProjectRequirementAsync);

    /// <summary>
    /// Opens the calculated anomaly and incident revalidation view directly,
    /// without making the user navigate via the REC campaign manager.
    /// </summary>
    /// <returns>Consultation or explicit, revision-checked human decisions.</returns>
    private async Task OpenPilotageRecAnomaliesAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Anomalies de recette");
            return;
        }

        ProjectRecAnomalyOverview overview = await Task.Run(() =>
            new ProjectRecAnomalyService(_root.FullPath)
                .ReadAsync(project.FullPath));
        NavigationNodeViewModel? current = PilotageProject();
        if (current?.Id != project.Id ||
            !string.Equals(current.FullPath, project.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Projet modifié pendant la lecture des anomalies REC.";
            return;
        }

        ProjectRecAnomaliesDialog dialog = new(
            overview, _root.FullPath, project.FullPath,
            PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await RefreshNavigationAsync(project.FullPath);
            StatusText.Text =
                "Décision humaine de revalidation enregistrée dans Maintenance.";
        }
        else
        {
            StatusText.Text =
                "Anomalies REC consultées · aucun incident modifié.";
        }
    }

    /// <summary>Opens anomalies and revalidation for the current project.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageRecAnomalies_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Anomalies REC et revalidation",
            OpenPilotageRecAnomaliesAsync);

    /// <summary>Opens the current EXI/CA/TEST traceability matrix.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageTraceability_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Traçabilité EXI/CA/TEST",
            ShowProjectRequirementTraceabilityAsync);

    /// <summary>Opens REC manager including anomalies and human revalidation.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageTestCampaigns_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Campagnes et résultats REC",
            OpenProjectTestCampaignsAsync);

    /// <summary>Opens stable Markdown heading identity assignment.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageAssignBlockIdentity_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Identifier un bloc TEST ou CA",
            AssignCurrentHeadingIdentityAsync);

    /// <summary>Opens the already verified GUID block navigation tool.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageGotoBlock_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Atteindre un bloc par ID",
            ShowBlockNavigationAsync);

    /// <summary>Opens the project risk registry from Pilotage.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageRisks_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Registre des risques", OpenPilotageRisksAsync);

    /// <summary>Opens the project gate and Go/No-Go registry.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageGates_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Gates et décisions Go/No-Go",
            () => OpenPilotageGatesAsync());

    /// <summary>Opens the already available project decisions list.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageDecisions_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Décisions du projet", ShowDecisionsAsync);

    /// <summary>Creates a human Decision Record from its existing workflow.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageNewDecision_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Nouvelle décision", CreateDecisionAsync);

    /// <summary>Previews an opt-in complex-structure migration.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageMigrationPreview_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Prévisualiser migration 2.0",
            ShowComplexMigrationPreviewAsync);

    /// <summary>Opens the existing approved complex migration workflow.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageMigrateComplex_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Migration Complexe 2.0",
            MigrateComplexProjectAsync);

    /// <summary>Opens the existing human-reviewed migration rollback.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageRollbackComplex_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Restaurer projet avant migration",
            RollbackComplexProjectMigrationAsync);

    /// <summary>Reuses the existing canonical incident creation workflow.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageNewIncident_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Nouvel incident", CreateMaintenanceIncidentAsync);

    /// <summary>Reuses the existing canonical intervention creation workflow.</summary>
    /// <param name="sender">Menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageNewIntervention_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Nouvelle intervention",
            CreateMaintenanceInterventionAsync);
}
