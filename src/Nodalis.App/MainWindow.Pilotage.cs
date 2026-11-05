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
                    foreach (ProjectJourneyStepTemplate stage in proposed.Steps
                        .OrderBy(item => item.Order))
                    {
                        MenuItem proposedStep = new()
                        {
                            Header = stage.Order + ". " + stage.Name
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
                    Header = "Ouvrir / initialiser le parcours…"
                };
                open.Click += OpenProjectJourney_Click;
                menu.Items.Add(open);
                return;
            }

            if (snapshot.Document.ProjectId != projectId)
            {
                menu.Items.Add(PilotageNotice(
                    "Identité du parcours différente du projet."));
                return;
            }

            ProjectJourneyStep[] steps = snapshot.Document.Steps
                .OrderBy(step => step.Order).ToArray();
            if (steps.Length == 0)
            {
                menu.Items.Add(PilotageNotice(
                    "Aucune étape : éditez le parcours pour en créer."));
                return;
            }

            foreach (ProjectJourneyStep step in steps)
            {
                string stepLabel = step.Order + ". " + step.Name;
                if (mode == PilotageJourneyMode.Steps)
                {
                    stepLabel += " · " + PilotageStepStatus(step.Status);
                }

                MenuItem stage = new() { Header = stepLabel };
                if (mode == PilotageJourneyMode.Steps)
                {
                    stage.Items.Add(PilotageTargetItem(
                        "Ouvrir cette étape", projectId, directory,
                        step.Id, null, PilotageTargetMode.Focus));
                    if (step.Operations.Count > 0)
                    {
                        stage.Items.Add(new Separator());
                    }

                    foreach (ProjectJourneyOperation operation in
                        step.Operations)
                    {
                        MenuItem nested = new()
                        {
                            Header = operation.Name,
                            ToolTip = operation.PromptKey is null
                                ? "Aucun prompt associé : brouillon générique disponible."
                                : "Prompt associé : " + operation.PromptKey
                        };
                        nested.Items.Add(PilotageTargetItem(
                            "Afficher l'opération", projectId, directory,
                            step.Id, operation.Id, PilotageTargetMode.Focus));
                        nested.Items.Add(PilotageTargetItem(
                            "Préparer / relire / copier le prompt…", projectId,
                            directory, step.Id, operation.Id,
                            PilotageTargetMode.Prepare));
                        MenuItem assign = PilotageTargetItem(
                            "Associer / changer le prompt dans le Parcours…",
                            projectId, directory, step.Id, operation.Id,
                            PilotageTargetMode.Assign);
                        assign.IsEnabled = !PilotageReadOnly(project);
                        nested.Items.Add(assign);
                        stage.Items.Add(nested);
                    }
                }
                else if (step.Operations.Count == 0)
                {
                    stage.Items.Add(PilotageNotice(
                        "Aucune opération dans cette étape."));
                }
                else
                {
                    foreach (ProjectJourneyOperation operation in
                        step.Operations)
                    {
                        string operationLabel = operation.Name +
                            (operation.PromptKey is null
                                ? " · prompt non associé" : "");
                        MenuItem item = PilotageTargetItem(operationLabel,
                            projectId, directory, step.Id, operation.Id,
                            mode == PilotageJourneyMode.Prepare
                                ? PilotageTargetMode.Prepare
                                : PilotageTargetMode.Assign);
                        if (mode == PilotageJourneyMode.Assign)
                        {
                            item.IsEnabled = !PilotageReadOnly(project);
                        }

                        stage.Items.Add(item);
                    }
                }

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
        new() { Header = message, IsEnabled = false };

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
            Header = label,
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

        await OpenProjectJourneyAsync();
        ProjectJourneyTabEntry? tab = _projectJourneyTabs.FirstOrDefault(
            item => item.ProjectId == target.ProjectId &&
                string.Equals(item.ProjectDirectory, target.ProjectPath,
                    StringComparison.OrdinalIgnoreCase));
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
        NavigationNodeViewModel? project = GetArchiveContextProject();
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
