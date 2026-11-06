using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only MEP history viewer and deliberate one-file creation dialog.
/// Never starts a deployment or silently converts a gate into an approval.
/// </summary>
public partial class ProjectDeploymentRegistryDialog : Window
{
    private readonly ProjectDeploymentService _service;
    private readonly ProjectGateRegistryService _gates;
    private readonly ProjectDeploymentPreflightService _preflight;
    private readonly ProjectTestCampaignService _campaigns;
    private readonly ProjectGateIncidentCatalogService _incidents;
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private ProjectDeploymentCatalog? _catalog;
    private bool _busy;

    /// <summary>Creates a project-scoped deployment viewer.</summary>
    /// <param name="workspaceRoot">Current workspace.</param>
    /// <param name="projectDirectory">Explicit owner project.</param>
    /// <param name="readOnly">Archive or read-only workspace.</param>
    public ProjectDeploymentRegistryDialog(string workspaceRoot,
        string projectDirectory, bool readOnly)
    {
        _service = new ProjectDeploymentService(workspaceRoot);
        _gates = new ProjectGateRegistryService(workspaceRoot);
        _preflight = new ProjectDeploymentPreflightService(workspaceRoot);
        _campaigns = new ProjectTestCampaignService(workspaceRoot);
        _incidents = new ProjectGateIncidentCatalogService(workspaceRoot);
        _workspaceRoot = workspaceRoot;
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
        InitializeComponent();
        Loaded += async (_, _) => await GuardAsync(
            () => ReloadAsync(null), "Charger les opérations MEP");
    }

    /// <summary>True only if a user explicitly created a new source file.</summary>
    public bool Changed { get; private set; }

    /// <summary>Exact verified source selected to open in Nodalis.</summary>
    public string? SelectedSourcePath { get; private set; }

    /// <summary>Current stable MEP selection from the in-memory listing.</summary>
    /// <returns>Selected row or null.</returns>
    private MepRow? CurrentRow() => OperationsList.SelectedItem as MepRow;

    /// <summary>
    /// Reloads the bounded folder outside the WPF dispatcher and retains the
    /// same MEP GUID when possible; selecting/scrolling performs no disk I/O.
    /// </summary>
    /// <param name="selectedId">Optional ID to restore.</param>
    /// <returns>Completed refresh.</returns>
    private async Task ReloadAsync(Guid? selectedId)
    {
        ProjectDeploymentCatalog catalog = await Task.Run(() =>
            _service.ListAsync(_projectDirectory));
        _catalog = catalog;
        MepRow[] rows = catalog.Operations.Select(item =>
            new MepRow(item,
                item.Operation.Reference + " — " + item.Operation.Title,
                item.Operation.Attempts.Count + " tentative(s) · " +
                item.Operation.Events.Count + " événement(s)"))
            .ToArray();
        OperationsList.ItemsSource = rows;
        OperationsList.SelectedItem = rows.FirstOrDefault(item =>
            item.Entry.Operation.Id == selectedId) ?? rows.FirstOrDefault();
        DiagnosticsText.Text = catalog.Diagnostics.Count == 0
            ? "Aucun document ignoré."
            : catalog.Diagnostics.Count + " source(s) ignorée(s) : " +
              string.Join(" · ", catalog.Diagnostics.Take(3));
        StatusText.Text = _readOnly
            ? "Projet archivé / lecture seule · aucune création ni édition."
            : rows.Length + " opération(s) · référence suivante : " +
              (catalog.NextReference ?? "plus de références libres");
        PaintDetails();
        UpdateButtons();
    }

    /// <summary>Paints one already loaded event history without rescanning.</summary>
    private void PaintDetails()
    {
        if (CurrentRow() is not MepRow row)
        {
            DetailsBox.Text = "Aucune opération MEP sélectionnée.";
            return;
        }

        ProjectDeploymentOperation operation = row.Entry.Operation;
        ProjectDeploymentClosure? closure = operation.Closure;
        List<ProjectDeploymentHandoverReview> handovers =
            operation.HandoverReviews ?? [];
        List<string> lines =
        [
            operation.Reference + " — " + operation.Title,
            "Identité : " + operation.Id.ToString("D"),
            "Création : " + operation.CreatedUtc.ToLocalTime().ToString(
                "dd/MM/yyyy HH:mm"),
            "Source : " + row.Entry.RelativePath,
            "Révision SHA-256 : " + row.Entry.SourceSha256,
            string.Empty,
            "Les tentatives sont des déclarations humaines, pas des GO automatiques.",
            "Bilan : " + (closure?.Outcome.ToString() ?? "non enregistré")
        ];
        if (closure is not null)
        {
            lines.Add("Clôture : " +
                closure.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                " · responsable : " + closure.Actor);
            if (closure.EffectiveDeploymentUtc is DateTimeOffset effective)
            {
                lines.Add("Déploiement effectif : " +
                    effective.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            }

            lines.Add("Motif : " + closure.Rationale);
            if (!string.IsNullOrWhiteSpace(closure.Reservations))
            {
                lines.Add("Réserves : " + closure.Reservations);
            }
        }

        List<ProjectDeploymentPostCheck> postChecks = operation.PostChecks ?? [];
        lines.Add(string.Empty);
        lines.Add("CONTRÔLES POST-MEP · " + postChecks.Count + " revue(s)");
        foreach (ProjectDeploymentPostCheck check in postChecks)
        {
            lines.Add(check.RecordedUtc.ToLocalTime().ToString(
                "dd/MM/yyyy HH:mm") + " · " + check.Verdict);
            lines.Add("  REC GUID : " + check.CampaignId.ToString("D") +
                " · passage : " + check.PassageId.ToString("D"));
            lines.Add("  " + check.Passed + " conforme(s), " +
                check.Failed + " non conforme(s), " +
                check.Blocked + " bloqué(s), " +
                (check.FrozenTests - check.Passed - check.Failed -
                 check.Blocked - check.NotApplicable) +
                " non exécuté(s) · SHA REC figé");
            lines.Add("  Auteur : " + check.Actor +
                " · justification : " + check.Rationale);
            if (!string.IsNullOrWhiteSpace(check.Reservations))
            {
                lines.Add("  Réserves : " + check.Reservations);
            }

            foreach (string incidentId in check.IncidentIds)
            {
                lines.Add("  Incident lié : " + incidentId);
            }
        }

        lines.Add(string.Empty);
        lines.Add("PASSATION EXPLOITATION · " + handovers.Count + " revue(s)");
        foreach (ProjectDeploymentHandoverReview review in handovers)
        {
            lines.Add(review.RecordedUtc.ToLocalTime().ToString(
                "dd/MM/yyyy HH:mm") + " · " + review.Outcome);
            lines.Add("  Responsable : " + review.Actor +
                " · destinataire : " + review.Recipient);
            lines.Add("  Justification : " + review.Rationale);
            if (!string.IsNullOrWhiteSpace(review.Reservations))
            {
                lines.Add("  Réserves : " + review.Reservations);
            }

            foreach (ProjectDeploymentHandoverCheck check in review.Checks)
            {
                lines.Add("  " + check.Area + " : " + check.Status +
                    " · " + check.Note);
                if (check.Evidence is not null)
                {
                    lines.Add("    Preuve : " + check.Evidence.RelativePath);
                }
            }
        }

        foreach (ProjectDeploymentAttempt attempt in operation.Attempts)
        {
            lines.Add(string.Empty);
            lines.Add("TENTATIVE " + attempt.Number + " · " +
                attempt.StartedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            lines.Add("Avant : " + attempt.ObservedVersionBefore +
                " · cible : " + attempt.TargetVersion);
            lines.Add("Environnement : " + attempt.Environment);
            lines.Add("Périmètre : " + attempt.Scope);
            lines.Add("Auteur : " + attempt.StartedBy);
            lines.Add("GO relu : " + attempt.GateDecisionId.ToString("D") +
                " · " + attempt.AuthorizationReviewedBy);
            lines.Add("Procédure : " + attempt.Procedure.RelativePath);
            lines.Add("Rollback : " + attempt.RollbackProcedure.RelativePath);
            ProjectDeploymentAttemptEvent[] history = operation.Events
                .Where(item => item.AttemptId == attempt.Id).ToArray();
            if (history.Length == 0)
            {
                lines.Add("  Aucun événement encore historisé.");
            }

            foreach (ProjectDeploymentAttemptEvent action in history)
            {
                string observation = action.ObservedVersionAfter is null
                    ? string.Empty
                    : " · version constatée : " + action.ObservedVersionAfter;
                lines.Add("  " + action.RecordedUtc.ToLocalTime().ToString(
                    "dd/MM/yyyy HH:mm") + " · " + action.Kind +
                    (action.RollbackOutcome is null
                        ? string.Empty : " · rollback " + action.RollbackOutcome) +
                    observation);
                lines.Add("    " + action.Actor + " : " + action.Rationale);
            }
        }

        DetailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Enables only meaningful controls on the current snapshot.</summary>
    private void UpdateButtons()
    {
        CreateButton.IsEnabled = !_busy && !_readOnly &&
            _catalog?.NextReference is not null;
        OpenButton.IsEnabled = !_busy && CurrentRow() is not null;
        PreflightButton.IsEnabled = !_busy &&
            CurrentRow()?.Entry.Operation.Attempts.Count > 0;
        ProjectDeploymentOperation? selected = CurrentRow()?.Entry.Operation;
        bool hasAttempt = selected?.Attempts.Count > 0;
        ProjectDeploymentAttemptEvent? last = selected?.Events.LastOrDefault();
        bool finished = last is not null &&
            ProjectDeploymentMarkdownCodec.IsFinish(last.Kind);
        bool open = selected?.Closure is null;
        AttemptButton.IsEnabled = !_busy && !_readOnly && open &&
            selected is not null && (!hasAttempt || finished);
        EventButton.IsEnabled = !_busy && !_readOnly && open &&
            selected is not null && hasAttempt && !finished;
        CloseOperationButton.IsEnabled = !_busy && !_readOnly && open &&
            selected is not null && (!hasAttempt || finished);
        HandoverButton.IsEnabled = !_busy && !_readOnly && open &&
            selected is not null && hasAttempt && finished;
        PostCheckButton.IsEnabled = !_busy && !_readOnly && open &&
            selected is not null && hasAttempt && finished;
        ReservationButton.IsEnabled = !_busy && selected is not null;
        RefreshButton.IsEnabled = !_busy;
    }

    /// <summary>Runs one WPF action inside the normal error guard.</summary>
    /// <param name="action">Async operation.</param>
    /// <param name="context">Human-readable operation label.</param>
    /// <returns>Completed UI action.</returns>
    private async Task GuardAsync(Func<Task> action, string context)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await UiActionGuard.RunAsync(this, "MEP · " + context, action);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Shows selected operation's existing Markdown in the editor.</summary>
    /// <param name="sender">Open source button.</param>
    /// <param name="e">WPF click event.</param>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || CurrentRow() is not MepRow row)
        {
            return;
        }

        SelectedSourcePath = row.Entry.RelativePath;
        DialogResult = true;
    }

    /// <summary>Opens a human-approved creation with no implicit attempts.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Create_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || _catalog?.NextReference is not string reference)
            {
                return;
            }

            TextPromptDialog prompt = new(
                "Créer une opération MEP", "Intitulé de la mise en production :")
            {
                Owner = this
            };
            if (prompt.ShowDialog() != true)
            {
                return;
            }

            string title = prompt.Value;
            if (MessageBox.Show(this,
                "Créer " + reference + " — " + title + " ?\n\n" +
                "Une seule fiche Markdown sera créée dans " +
                "Exploitation/Déploiement/Opérations. " +
                "Aucune tentative ni autorisation ne sera créée et " +
                "aucun déploiement ne sera exécuté.",
                "Nouvelle MEP · confirmer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectDeploymentEntry saved =
                await _service.CreateAsync(_projectDirectory, title, reference);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
        }, "Créer une opération MEP");

    /// <summary>
    /// Starts a human-described attempt after explicit gate selection and
    /// current-file SHA capture; a historical GO never approves a new MEP.
    /// </summary>
    /// <param name="sender">New attempt button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Attempt_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || CurrentRow() is not MepRow selected)
            {
                return;
            }

            ProjectDeploymentOperation operation = selected.Entry.Operation;
            if (operation.Attempts.Count > 0)
            {
                ProjectDeploymentAttemptState state =
                    ProjectDeploymentMarkdownCodec.GetAttemptState(operation,
                        operation.Attempts[^1].Id);
                if (!state.IsFinished)
                {
                    throw new InvalidOperationException(
                        "Terminez explicitement la tentative en cours.");
                }
            }

            ProjectGateRegistrySnapshot gates = await _gates.ReadAsync(
                _projectDirectory);
            ProjectGateDecision[] eligible = gates.Registry.Decisions
                .GroupBy(item => item.GateId)
                .Select(group => group.Last())
                .Where(item => item.Verdict is ProjectGateVerdict.Go or
                    ProjectGateVerdict.GoWithReservations)
                .ToArray();
            if (eligible.Length == 0)
            {
                MessageBox.Show(this,
                    "Aucun GO humain actuellement admissible dans ce projet. " +
                    "Consignez d'abord la décision depuis le registre Gates.",
                    "MEP · décision requise", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ProjectDeploymentAttemptDialog dialog = new(
                _workspaceRoot, eligible)
            { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ProjectGateEvidenceSnapshot procedure =
                await _gates.CaptureEvidenceAsync(dialog.ProcedurePath);
            ProjectGateEvidenceSnapshot rollback =
                await _gates.CaptureEvidenceAsync(dialog.RollbackPath);
            ProjectDeploymentAttempt attempt = dialog.BuildAttempt(
                operation.Attempts.Count + 1, procedure, rollback);
            ProjectDeploymentOperation proposed =
                ProjectDeploymentEditor.StartAttempt(operation, attempt);
            ProjectDeploymentEntry saved = await _service.SaveAsync(
                _projectDirectory, selected.Entry, proposed);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
            StatusText.Text = "Tentative consignée sous contrôle SHA. " +
                "Aucune mise en production n'a été exécutée.";
        }, "Nouvelle tentative");

    /// <summary>
    /// Appends a separate human event to the latest unfinished attempt,
    /// including rollback requests, execution and observed outcomes.
    /// </summary>
    /// <param name="sender">Log event button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Event_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || CurrentRow() is not MepRow selected ||
                selected.Entry.Operation.Attempts.Count == 0)
            {
                return;
            }

            ProjectDeploymentOperation operation = selected.Entry.Operation;
            ProjectDeploymentAttempt attempt = operation.Attempts[^1];
            ProjectDeploymentAttemptState state =
                ProjectDeploymentMarkdownCodec.GetAttemptState(
                    operation, attempt.Id);
            if (state.IsFinished)
            {
                throw new InvalidOperationException(
                    "Cette tentative est déjà terminée.");
            }

            ProjectDeploymentEventDialog dialog = new() { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ProjectDeploymentAttemptEvent action =
                dialog.BuildEvent(attempt.Id);
            ProjectDeploymentOperation proposed =
                ProjectDeploymentEditor.AppendEvent(operation, action);
            ProjectDeploymentEntry saved = await _service.SaveAsync(
                _projectDirectory, selected.Entry, proposed);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
            StatusText.Text = "Événement historique ajouté. " +
                "Aucun GO, jalon ni déploiement n'a été modifié.";
        }, "Consigner un événement");

    /// <summary>
    /// Rechecks a recorded attempt against current project-local GO proofs,
    /// procedures, risks and incidents without writing any source file.
    /// </summary>
    /// <param name="sender">Human preflight request.</param>
    /// <param name="e">WPF routed click event.</param>
    private async void Preflight_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (CurrentRow() is not MepRow selected ||
                selected.Entry.Operation.Attempts.Count == 0)
            {
                return;
            }

            Guid id = selected.Entry.Operation.Attempts[^1].Id;
            ProjectDeploymentPreflightReport report = await Task.Run(() =>
                _preflight.ReviewAsync(_projectDirectory, selected.Entry, id));
            ProjectDeploymentPreflightDialog dialog = new(report)
            {
                Owner = this
            };
            dialog.ShowDialog();
            StatusText.Text = "Contrôle pré-MEP consultatif · aucune " +
                "autorisation ni événement enregistré.";
        }, "Contrôles pré-MEP");

    /// <summary>
    /// Consigns the single human business outcome separately from attempts,
    /// without auto-closing gates, milestones, incidents or linked tasks.
    /// </summary>
    /// <param name="sender">Explicit MEP closure button.</param>
    /// <param name="e">WPF click event.</param>
    private async void CloseOperation_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || CurrentRow() is not MepRow selected ||
                selected.Entry.Operation.Closure is not null)
            {
                return;
            }

            ProjectDeploymentOperation operation = selected.Entry.Operation;
            if (operation.Attempts.Count > 0)
            {
                ProjectDeploymentAttemptState state =
                    ProjectDeploymentMarkdownCodec.GetAttemptState(
                        operation, operation.Attempts[^1].Id);
                if (!state.IsFinished)
                {
                    throw new InvalidDataException(
                        "Terminez la dernière tentative avant le bilan.");
                }
            }

            ProjectDeploymentClosureDialog dialog = new(operation)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true ||
                dialog.ConfirmedClosure is not ProjectDeploymentClosure closure)
            {
                return;
            }

            ProjectDeploymentOperation proposed =
                ProjectDeploymentEditor.Close(operation, closure);
            ProjectDeploymentEntry saved = await _service.SaveAsync(
                _projectDirectory, selected.Entry, proposed);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
            StatusText.Text = "Bilan MEP figé · historique conservé. " +
                "Aucun autre statut métier modifié.";
        }, "Clôturer une opération MEP");

    /// <summary>
    /// Explicitly links an exact REC campaign passage and selected canonical
    /// maintenance incidents to the latest completed deployment attempt.
    /// </summary>
    /// <param name="sender">Post-deployment review button.</param>
    /// <param name="e">WPF routed click.</param>
    private async void PostCheck_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || CurrentRow() is not MepRow selected ||
                selected.Entry.Operation.Closure is not null ||
                selected.Entry.Operation.Attempts.Count == 0)
            {
                return;
            }

            ProjectDeploymentOperation operation = selected.Entry.Operation;
            ProjectDeploymentAttemptState attemptState =
                ProjectDeploymentMarkdownCodec.GetAttemptState(
                    operation, operation.Attempts[^1].Id);
            if (!attemptState.IsFinished)
            {
                throw new InvalidDataException(
                    "Terminez la tentative avant la revue post-déploiement.");
            }

            ProjectTestCampaignCatalog campaigns = await Task.Run(() =>
                _campaigns.ListAsync(_projectDirectory));
            ProjectGateIncidentCatalog incidents = await Task.Run(() =>
                _incidents.ListAsync(_projectDirectory));
            ProjectDeploymentPostCheckDialog dialog = new(
                operation, campaigns, incidents)
            { Owner = this };
            if (dialog.EligiblePassages == 0)
            {
                MessageBox.Show(this,
                    "Aucun passage REC du projet ne correspond à la version " +
                    "et à l'environnement de cette tentative. " +
                    "Enregistrez d'abord un passage dans Tests et recette.",
                    "Contrôles post-MEP · passage requis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (dialog.ShowDialog() != true ||
                dialog.ConfirmedCheck is not ProjectDeploymentPostCheck check)
            {
                return;
            }

            ProjectDeploymentOperation proposed =
                ProjectDeploymentEditor.RecordPostCheck(operation, check);
            ProjectDeploymentEntry saved = await _service.SaveAsync(
                _projectDirectory, selected.Entry, proposed);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
            StatusText.Text = "Passage REC et éventuels incidents liés. " +
                "Les résultats et statuts canoniques restent inchangés.";
        }, "Contrôle post-déploiement");

    /// <summary>
    /// Records one explicit post-deployment stabilization and operational
    /// handover review, protecting older decisions with SHA-CAS.
    /// </summary>
    /// <param name="sender">Handover button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Handover_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || CurrentRow() is not MepRow selected ||
                selected.Entry.Operation.Closure is not null ||
                selected.Entry.Operation.Attempts.Count == 0)
            {
                return;
            }

            ProjectDeploymentOperation operation = selected.Entry.Operation;
            ProjectDeploymentAttemptState state =
                ProjectDeploymentMarkdownCodec.GetAttemptState(
                    operation, operation.Attempts[^1].Id);
            if (!state.IsFinished)
            {
                throw new InvalidDataException(
                    "Terminez la tentative avant la passation exploitation.");
            }

            ProjectDeploymentHandoverDialog dialog = new(operation)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            Dictionary<ProjectDeploymentHandoverArea,
                ProjectGateEvidenceSnapshot?> evidence = [];
            foreach (ProjectDeploymentHandoverDialog.ControlRow row in dialog.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.EvidencePath))
                {
                    evidence[row.Area] = null;
                    continue;
                }

                evidence[row.Area] = await _gates.CaptureEvidenceAsync(
                    row.EvidencePath.Trim());
            }

            ProjectDeploymentHandoverReview review = dialog.BuildReview(evidence);
            ProjectDeploymentOperation proposed =
                ProjectDeploymentEditor.ReviewHandover(operation, review);
            ProjectDeploymentEntry saved = await _service.SaveAsync(
                _projectDirectory, selected.Entry, proposed);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
            StatusText.Text = "Passation enregistrée dans l'historique. " +
                "Les réserves restent visibles · aucun autre statut modifié.";
        }, "Passation exploitation");

    /// <summary>
    /// Opens the independent human follow-up registry, available even after
    /// business closure. A completed MEP remains fully immutable.
    /// </summary>
    /// <param name="sender">Reservation tracking button.</param>
    /// <param name="e">WPF routed click.</param>
    private async void Reservation_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (CurrentRow() is not MepRow selected)
            {
                return;
            }

            ProjectDeploymentReservationDialog dialog = new(
                _workspaceRoot, _projectDirectory, selected.Entry, _readOnly)
            {
                Owner = this
            };
            dialog.ShowDialog();
            if (dialog.Changed)
            {
                Changed = true;
                StatusText.Text = "Réserves suivies indépendamment de la MEP. " +
                    "Aucune tentative, décision ou anomalie modifiée.";
            }

            await Task.CompletedTask;
        }, "Suivi des réserves exploitation");

    /// <summary>Reloads the canonical folder only on explicit user request.</summary>
    /// <param name="sender">Refresh button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReloadAsync(CurrentRow()?.Entry.Operation.Id),
            "Actualiser les fiches MEP");

    /// <summary>Closes the read-only viewer without creating anything.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Updates details from the already materialized local catalog.</summary>
    /// <param name="sender">Operations list.</param>
    /// <param name="e">WPF selection event.</param>
    private void OperationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        PaintDetails();
        UpdateButtons();
    }

    /// <summary>Display-only row grounded in one source-verified MEP.</summary>
    /// <param name="Entry">Canonical operation.</param>
    /// <param name="Name">Readable reference and title.</param>
    /// <param name="Summary">History counts.</param>
    private sealed record MepRow(
        ProjectDeploymentEntry Entry, string Name, string Summary);
}
