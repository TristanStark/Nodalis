using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit gate registry editor. Reads do not write; every definition edit
/// and human verdict is confirmed and atomically SHA-checked.
/// </summary>
public partial class ProjectGateRegistryDialog : Window
{
    private readonly ProjectGateRegistryService _service;
    private readonly string _serviceWorkspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private readonly bool _allowSelection;
    private readonly Guid? _initialGateId;
    private readonly List<string> _suggestedEvidence;
    private ProjectGateRegistrySnapshot? _snapshot;
    private bool _busy;
    private bool _painting;

    /// <summary>Creates one project-local gate registry dialog.</summary>
    /// <param name="workspaceRoot">Active workspace.</param>
    /// <param name="projectDirectory">Existing project directory.</param>
    /// <param name="readOnly">Whether the workspace is read-only.</param>
    /// <param name="suggestedEvidence">Existing operation source references.</param>
    /// <param name="allowSelection">May return a gate to link to the operation.</param>
    /// <param name="initialGateId">Existing gate to highlight in the registry.</param>
    public ProjectGateRegistryDialog(string workspaceRoot,
        string projectDirectory, bool readOnly,
        IReadOnlyList<string> suggestedEvidence, bool allowSelection,
        Guid? initialGateId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(suggestedEvidence);
        _service = new ProjectGateRegistryService(workspaceRoot);
        _serviceWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _readOnly = readOnly;
        _allowSelection = allowSelection;
        _initialGateId = initialGateId;
        _suggestedEvidence = suggestedEvidence
            .Where(ProjectJourneySourcePath.IsSafe)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        InitializeComponent();
        Loaded += async (_, _) => await GuardAsync(
            () => ReloadAsync(_initialGateId), "Charger le registre des gates");
    }

    /// <summary>Chosen gate ID when the user explicitly selects one.</summary>
    public Guid? SelectedGateId { get; private set; }

    /// <summary>Whether any registry change was accepted and persisted.</summary>
    public bool Changed { get; private set; }

    /// <summary>Returns the selected gate definition.</summary>
    private ProjectGateDefinition? CurrentGate() =>
        (GatesList.SelectedItem as GateRow)?.Gate;

    /// <summary>Displays a read-only, unmodified registry.</summary>
    /// <param name="selectedId">Optional gate to reselect.</param>
    /// <returns>Completed read.</returns>
    private async Task ReloadAsync(Guid? selectedId)
    {
        _snapshot = await _service.ReadAsync(_projectDirectory);
        Paint(selectedId);
        StatusText.Text = _readOnly
            ? "Consultation uniquement · workspace/projet en lecture seule."
            : _snapshot.Sha256 is null
                ? "Aucun registre encore enregistré · aucune écriture lors de la lecture."
                : "Registre chargé · décisions passées immuables, modifications confirmées.";
    }

    /// <summary>Refreshes the selection without touching underlying files.</summary>
    /// <param name="selectedId">Optional gate to retain.</param>
    private void Paint(Guid? selectedId, Guid? selectedDecisionId = null)
    {
        _painting = true;
        try
        {
            ProjectGateRegistry? registry = _snapshot?.Registry;
            GateRow[] rows = registry?.Gates.Select(gate =>
            {
                ProjectGateDecision? latest = registry.Decisions.LastOrDefault(
                    item => item.GateId == gate.Id);
                return new GateRow(gate, gate.Name,
                    latest is null
                        ? "Aucune décision (pas de GO implicite)"
                        : "Dernier verdict historique : " + VerdictName(latest.Verdict) +
                          " · " + latest.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            }).ToArray() ?? [];
            GatesList.ItemsSource = rows;
            GatesList.SelectedItem = rows.FirstOrDefault(item =>
                item.Gate.Id == selectedId) ?? rows.FirstOrDefault();
            PaintSelection(selectedDecisionId);
        }
        finally
        {
            _painting = false;
        }

        UpdateButtons();
    }

    /// <summary>Refreshes criteria and immutable history for one gate.</summary>
    private void PaintSelection(Guid? selectedDecisionId = null)
    {
        bool wasPainting = _painting;
        _painting = true;
        try
        {
            ProjectGateDefinition? gate = CurrentGate();
            GateNameText.Text = gate?.Name ?? "Sélectionnez un point de contrôle";
            CriteriaList.ItemsSource = gate?.Criteria.Select(item =>
                new CriterionDisplay(item.Name +
                    (item.Optional ? " · facultatif" : " · obligatoire"))).ToArray();
            ProjectGateDecision[] decisions = _snapshot?.Registry.Decisions
                .Where(item => item.GateId == gate?.Id).Reverse().ToArray() ?? [];
            HistoryRow[] history = decisions.Select(item => new HistoryRow(item,
                VerdictName(item.Verdict) + " · " +
                item.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                item.Actor + " · " + item.Scope)).ToArray();
            HistoryList.ItemsSource = history;
            HistoryList.SelectedItem = history.FirstOrDefault(item =>
                item.Decision.Id == selectedDecisionId) ?? history.FirstOrDefault();
            PaintDecisionDetail();
        }
        finally
        {
            _painting = wasPainting;
        }

        UpdateButtons();
    }

    /// <summary>
    /// Shows the frozen original verdict and the separately appended human
    /// reservation history. Neither view modifies the original record.
    /// </summary>
    private void PaintDecisionDetail()
    {
        bool wasPainting = _painting;
        _painting = true;
        try
        {
            if (HistoryList.SelectedItem is not HistoryRow row ||
                _snapshot is null)
            {
                DecisionDetailsBox.Text = "Aucune décision enregistrée.";
                ReservationsList.ItemsSource = null;
                return;
            }

            ProjectGateDecision decision = row.Decision;
            ProjectGateRegistry registry = _snapshot.Registry;
            List<string> lines =
            [
                "Verdict INITIAL : " + VerdictName(decision.Verdict),
                "Auteur : " + decision.Actor,
                "Périmètre : " + decision.Scope,
                "Version : " + (decision.Version ?? "non précisée"),
                "Environnement : " + (decision.Environment ?? "non précisé"),
                "Justification : " + decision.Rationale,
                "Critères à la date de décision :"
            ];
            lines.AddRange(decision.Assessments.Select(item =>
                "  - " + item.Name + " : " + item.Status + " " + item.Comment));
            lines.Add("Preuves initiales figées (SHA-256) :");
            lines.AddRange(decision.Evidence.Select(item =>
                "  - " + item.RelativePath + " [" + item.Sha256 + "]"));
            lines.Add("Réserves initialement enregistrées :");
            lines.AddRange(decision.Reservations.Select(item =>
                "  - " + item.Id + " · " + item.Description));

            ProjectGateReservationEvent[] events = registry.ReservationEvents
                .Where(item => item.DecisionId == decision.Id).ToArray();
            lines.Add("Historique séparé des actions humaines sur les réserves :");
            foreach (ProjectGateReservationEvent item in events)
            {
                lines.Add("  - " + item.RecordedUtc.ToLocalTime()
                    .ToString("dd/MM/yyyy HH:mm") + " · " + item.Kind +
                    " · " + item.Actor + " · " + item.Rationale);
                lines.AddRange(item.Evidence.Select(proof =>
                    "      preuve : " + proof.RelativePath + " [" +
                    proof.Sha256 + "]"));
            }

            DecisionDetailsBox.Text = string.Join(Environment.NewLine, lines);
            Guid? selectedReservation = (ReservationsList.SelectedItem
                as ReservationRow)?.Reservation.Id;
            ReservationRow[] rows = decision.Reservations.Select(reservation =>
            {
                ProjectGateReservationEvent? last = events.LastOrDefault(item =>
                    item.ReservationId == reservation.Id);
                bool open = last is null ||
                    last.Kind == ProjectGateReservationEventKind.Reopened;
                return new ReservationRow(decision.Id, reservation, open,
                    (open ? "Ouverte · " : "Levée · ") + reservation.Description,
                    last is null
                        ? "Initiale · aucun événement de qualification"
                        : "Dernière action : " + last.RecordedUtc.ToLocalTime()
                            .ToString("dd/MM/yyyy HH:mm") + " · " + last.Actor);
            }).ToArray();
            ReservationsList.ItemsSource = rows;
            ReservationsList.SelectedItem = rows.FirstOrDefault(item =>
                item.Reservation.Id == selectedReservation) ??
                rows.FirstOrDefault();
        }
        finally
        {
            _painting = wasPainting;
        }

        if (!_painting)
        {
            UpdateButtons();
        }
    }

    /// <summary>Enables only contextually meaningful commands.</summary>
    private void UpdateButtons()
    {
        bool canEdit = !_busy && !_readOnly && _snapshot is not null;
        bool selected = !_busy && CurrentGate() is not null;
        AddGateButton.IsEnabled = canEdit;
        AddDefaultsButton.IsEnabled = canEdit;
        AddCriterionButton.IsEnabled = canEdit && selected;
        AddProofButton.IsEnabled = canEdit && selected;
        RecordDecisionButton.IsEnabled = canEdit && selected;
        ChooseGateButton.IsEnabled = selected && _allowSelection && !_readOnly;
        LiftReservationButton.IsEnabled = canEdit &&
            ReservationsList.SelectedItem is ReservationRow open && open.IsOpen;
        ReopenReservationButton.IsEnabled = canEdit &&
            ReservationsList.SelectedItem is ReservationRow closed && !closed.IsOpen;
    }

    /// <summary>Runs all async WPF actions inside the existing error boundary.</summary>
    /// <param name="action">One guarded action.</param>
    /// <param name="context">User-facing error context.</param>
    /// <returns>Completed action.</returns>
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
            await UiActionGuard.RunAsync(this, "Gates · " + context, action);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Saves one confirmed change, requiring the exact previously read SHA.
    /// No automatic merge, retry or silent overwrite on conflict.
    /// </summary>
    /// <param name="proposed">User-reviewed new registry.</param>
    /// <param name="description">Approval preview.</param>
    /// <param name="selectedId">Gate to reselect.</param>
    /// <returns>Completed save.</returns>
    private async Task SaveApprovedAsync(ProjectGateRegistry proposed,
        string description, Guid? selectedId,
        Guid? selectedDecisionId = null)
    {
        if (_readOnly || _snapshot is null)
        {
            return;
        }

        if (MessageBox.Show(this,
            description +
            "\n\nSeul le registre .project-gates.json sera modifié. " +
            "Aucun jalon, document, décision ancienne ni Parcours ne sera modifié.\n\n" +
            "Confirmer ?",
            "Registre des gates · confirmer",
            MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _snapshot = await _service.SaveAsync(_projectDirectory,
                proposed, _snapshot.Sha256);
            Changed = true;
            Paint(selectedId, selectedDecisionId);
            StatusText.Text = "Registre enregistré sous contrôle SHA-256.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or InvalidOperationException or
            UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message +
                "\n\nLe registre n'a pas été écrasé. Cliquez Recharger " +
                "pour comparer avec la version actuelle.",
                "Conflit de registre", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText.Text = "Écriture refusée · rechargement explicite requis.";
        }
    }

    /// <summary>Displays a one-field text input with no implicit write.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="prompt">Label for input.</param>
    /// <returns>Text entered or null when canceled.</returns>
    private string? Prompt(string title, string prompt)
    {
        TextPromptDialog dialog = new(title, prompt) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    /// <summary>Creates an individually named gate on confirmation.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">Click event.</param>
    private async void AddGate_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_snapshot is null)
            {
                return;
            }

            string? name = Prompt("Nouveau point de contrôle", "Nom du gate :");
            if (name is null)
            {
                return;
            }

            ProjectGateRegistry proposed =
                ProjectGateEditor.CreateGate(_snapshot.Registry, name);
            await SaveApprovedAsync(proposed,
                "Créer le point de contrôle « " + name + " » (sans décision) ?",
                proposed.Gates.Last().Id);
        }, "Créer un gate");

    /// <summary>Proposes but never silently activates the five built-in gates.</summary>
    /// <param name="sender">Standard models button.</param>
    /// <param name="e">Click event.</param>
    private async void AddDefaults_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_snapshot is null)
            {
                return;
            }

            ProjectGateRegistry proposed =
                ProjectGateDefaults.ProposeMissing(_snapshot.Registry);
            int count = proposed.Gates.Count - _snapshot.Registry.Gates.Count;
            if (count == 0)
            {
                StatusText.Text = "Les cinq propositions existent déjà.";
                return;
            }

            await SaveApprovedAsync(proposed,
                "Ajouter " + count + " gate(s) standard(s) avec leurs critères : " +
                "cadrage, développement, recette, GO/NO-GO production, clôture ?",
                CurrentGate()?.Id);
        }, "Proposer les gates standards");

    /// <summary>Adds one required or optional criterion for future decisions.</summary>
    /// <param name="sender">Add criterion button.</param>
    /// <param name="e">Click event.</param>
    private async void AddCriterion_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            ProjectGateDefinition? gate = CurrentGate();
            if (gate is null || _snapshot is null)
            {
                return;
            }

            string? name = Prompt("Ajouter un critère", "Critère à qualifier :");
            if (name is null)
            {
                return;
            }

            MessageBoxResult optional = MessageBox.Show(this,
                "Ce critère est-il facultatif ?\n\n" +
                "Oui : facultatif. Non : obligatoire. Annuler : abandon.",
                "Critère du gate",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (optional == MessageBoxResult.Cancel)
            {
                return;
            }

            ProjectGateRegistry proposed = ProjectGateEditor.AddCriterion(
                _snapshot.Registry, gate.Id, name,
                optional == MessageBoxResult.Yes);
            await SaveApprovedAsync(proposed,
                "Ajouter le critère « " + name + " » au gate « " +
                gate.Name + " » ?\nLes décisions précédentes restent intactes.",
                gate.Id);
        }, "Ajouter un critère");

    /// <summary>Offers an existing workspace file as optional decision evidence.</summary>
    /// <param name="sender">Add proof button.</param>
    /// <param name="e">Click event.</param>
    private async void AddProof_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() =>
        {
            OpenFileDialog dialog = new()
            {
                Title = "Sélectionner une preuve existante du workspace",
                InitialDirectory = _projectDirectory,
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return Task.CompletedTask;
            }

            string reference = _sources.GetReferenceForExistingFile(dialog.FileName);
            if (!_suggestedEvidence.Contains(reference,
                StringComparer.OrdinalIgnoreCase))
            {
                _suggestedEvidence.Add(reference);
            }

            StatusText.Text = "Preuve proposée pour la prochaine revue : " + reference +
                " · aucun fichier modifié.";
            return Task.CompletedTask;
        }, "Sélectionner une preuve");

    /// <summary>
    /// Captures human-reviewed criteria and proof byte fingerprints, then
    /// appends a frozen decision without reinterpreting previous verdicts.
    /// </summary>
    /// <param name="sender">Record decision button.</param>
    /// <param name="e">Click event.</param>
    private async void RecordDecision_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            ProjectGateDefinition? gate = CurrentGate();
            if (gate is null || _snapshot is null)
            {
                return;
            }

            ProjectGateReviewDialog dialog = new(gate, _suggestedEvidence)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.Review is null)
            {
                return;
            }

            List<ProjectGateEvidenceSnapshot> evidence = [];
            foreach (string source in dialog.SelectedSourcePaths)
            {
                evidence.Add(await _service.CaptureEvidenceAsync(source));
            }

            ProjectGateReview reviewed = dialog.Review with { Evidence = evidence };
            ProjectGateRegistry proposed = ProjectGateEditor.AppendDecision(
                _snapshot.Registry, reviewed);
            await SaveApprovedAsync(proposed,
                "Historiser une NOUVELLE décision humaine\n\nGate : " +
                gate.Name + "\nVerdict : " +
                VerdictName(reviewed.Verdict) +
                "\nAuteur : " + reviewed.Actor +
                "\nPérimètre : " + reviewed.Scope +
                "\nPreuves figées : " + evidence.Count +
                "\nRéserves : " + reviewed.Reservations.Count +
                "\n\nLa décision passée ne sera jamais réécrite.",
                gate.Id);
        }, "Consigner une décision humaine");

    /// <summary>Starts a reviewed lift of the selected reservation.</summary>
    /// <param name="sender">Lift button.</param>
    /// <param name="e">Click event.</param>
    private async void LiftReservation_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => QualifyReservationAsync(
            ProjectGateReservationEventKind.Lifted),
            "Lever une réserve après qualification humaine");

    /// <summary>Starts a reviewed reopening of the selected reservation.</summary>
    /// <param name="sender">Reopen button.</param>
    /// <param name="e">Click event.</param>
    private async void ReopenReservation_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => QualifyReservationAsync(
            ProjectGateReservationEventKind.Reopened),
            "Réouvrir une réserve");

    /// <summary>
    /// Collects explicit human sign-off then fingerprints local proof and
    /// appends a SHA-guarded event, without touching the original verdict.
    /// </summary>
    /// <param name="kind">Lift or reopen requested by the user.</param>
    /// <returns>Completed explicit review and conditional persistence.</returns>
    private async Task QualifyReservationAsync(ProjectGateReservationEventKind kind)
    {
        if (_readOnly || _snapshot is null ||
            HistoryList.SelectedItem is not HistoryRow historical ||
            ReservationsList.SelectedItem is not ReservationRow selected ||
            historical.Decision.Id != selected.DecisionId)
        {
            return;
        }

        ProjectGateReservationState current =
            ProjectGateEditor.GetReservationState(_snapshot.Registry,
                selected.DecisionId, selected.Reservation.Id);
        if (current.IsOpen !=
            (kind == ProjectGateReservationEventKind.Lifted))
        {
            throw new InvalidOperationException(
                "L'état de la réserve a changé : rechargez le registre.");
        }

        ProjectGateReservationActionDialog dialog = new(
            _serviceWorkspaceRoot, _projectDirectory, selected.Reservation, kind)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true ||
            dialog.Actor is null || dialog.Rationale is null)
        {
            return;
        }

        List<ProjectGateEvidenceSnapshot> proofs = [];
        if (dialog.EvidencePath is string path)
        {
            proofs.Add(await _service.CaptureEvidenceAsync(path));
        }

        ProjectGateRegistry proposed = ProjectGateEditor.AppendReservationEvent(
            _snapshot.Registry, selected.DecisionId, selected.Reservation.Id,
            kind, dialog.Actor, dialog.Rationale, proofs);
        string label = kind == ProjectGateReservationEventKind.Lifted
            ? "LEVER" : "RÉOUVRIR";
        await SaveApprovedAsync(proposed,
            label + " la réserve de la décision historique « " +
            historical.Decision.GateName + " » ?\n" +
            "Réserve : " + selected.Reservation.Description + "\n" +
            "Auteur : " + dialog.Actor + "\n" +
            "Motif : " + dialog.Rationale + "\n" +
            "Preuves SHA-256 : " + proofs.Count +
            "\n\nLe verdict initial restera " +
            VerdictName(historical.Decision.Verdict) +
            ". Aucune décision, tâche ni aucun jalon n'est modifié.",
            historical.Decision.GateId, historical.Decision.Id);
    }

    /// <summary>Updates action buttons after choosing a reservation.</summary>
    /// <param name="sender">Reservation list.</param>
    /// <param name="e">Selection change.</param>
    private void ReservationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            UpdateButtons();
        }
    }

    /// <summary>Returns an existing gate for relation linking, without editing it.</summary>
    /// <param name="sender">Link button.</param>
    /// <param name="e">Click event.</param>
    private void ChooseGate_Click(object sender, RoutedEventArgs e)
    {
        ProjectGateDefinition? gate = CurrentGate();
        if (!_allowSelection || _readOnly || gate is null)
        {
            return;
        }

        SelectedGateId = gate.Id;
        DialogResult = true;
    }

    /// <summary>Responds to gate selection changes.</summary>
    /// <param name="sender">Gate list.</param>
    /// <param name="e">Selection event.</param>
    private void GatesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            PaintSelection();
        }
    }

    /// <summary>Shows the exact frozen decision selected in history.</summary>
    /// <param name="sender">Decision history list.</param>
    /// <param name="e">Selection event.</param>
    private void HistoryList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            PaintDecisionDetail();
        }
    }

    /// <summary>
    /// Opens an on-demand project-wide projection. The register is re-read
    /// only once for this user command; typing and filtering stay in memory.
    /// Navigation returns stable IDs to this existing historical view.
    /// </summary>
    /// <param name="sender">Overview button.</param>
    /// <param name="e">Click event.</param>
    private async void OverviewReservations_Click(
        object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            Guid? previousGate = CurrentGate()?.Id;
            _snapshot = await _service.ReadAsync(_projectDirectory);
            Paint(previousGate);
            ProjectGateReservationOverview overview =
                ProjectGateReservationOverviewBuilder.Build(
                    _snapshot.Registry, DateOnly.FromDateTime(DateTime.Today));
            ProjectGateReservationOverviewDialog dialog = new(overview)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true ||
                dialog.SelectedGateId is not Guid gateId ||
                dialog.SelectedDecisionId is not Guid decisionId ||
                dialog.SelectedReservationId is not Guid reservationId)
            {
                return;
            }

            // The overview never mutates the registry; the destination
            // is the original decision, not a synthetic reservation object.
            Paint(gateId, decisionId);
            ReservationRow? selected = ReservationsList.Items
                .OfType<ReservationRow>().FirstOrDefault(item =>
                    item.DecisionId == decisionId &&
                    item.Reservation.Id == reservationId);
            if (selected is not null)
            {
                ReservationsList.SelectedItem = selected;
                UpdateButtons();
            }

            StatusText.Text = "Réserve retrouvée dans sa décision d'origine · " +
                "aucun verdict ni document modifié.";
        }, "Consulter la vue transversale des réserves");

    /// <summary>Re-reads the register only on an explicit user action.</summary>
    /// <param name="sender">Reload button.</param>
    /// <param name="e">Click event.</param>
    private async void Reload_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReloadAsync(CurrentGate()?.Id),
            "Recharger le registre");

    /// <summary>Closes the gate registry without changing other open tabs.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>French label for the historical human verdict.</summary>
    /// <param name="verdict">Stored verdict enum.</param>
    /// <returns>Human-readable status, without recomputation.</returns>
    private static string VerdictName(ProjectGateVerdict verdict) =>
        verdict switch
        {
            ProjectGateVerdict.Go => "GO",
            ProjectGateVerdict.GoWithReservations => "GO sous réserves",
            ProjectGateVerdict.NoGo => "NO-GO",
            _ => "Reporté"
        };

    /// <summary>Read-only state of an original reservation in a selected dossier.</summary>
    private sealed record ReservationRow(
        Guid DecisionId, ProjectGateReservation Reservation, bool IsOpen,
        string Label, string Detail);

    private sealed record GateRow(
        ProjectGateDefinition Gate, string Name, string Summary);

    private sealed record CriterionDisplay(string Name);

    private sealed record HistoryRow(
        ProjectGateDecision Decision, string Label, string Summary);
}
