using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Project-local, human-only reservation follow-up. The source MEP is never
/// edited, even when the tracked reserve is reopened after business closure.
/// </summary>
public partial class ProjectDeploymentReservationDialog : Window
{
    private readonly ProjectDeploymentReservationService _service;
    private readonly ProjectGateRegistryService _gates;
    private readonly ProjectDeploymentEntry _mep;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private ProjectDeploymentReservationSnapshot? _snapshot;
    private bool _busy;

    /// <summary>Opens only the obligations for one explicitly selected MEP.</summary>
    /// <param name="workspaceRoot">Current Nodalis workspace.</param>
    /// <param name="projectDirectory">Canonical owning project.</param>
    /// <param name="mep">Previously source-verified selected MEP.</param>
    /// <param name="readOnly">Project archive or read-only mode.</param>
    public ProjectDeploymentReservationDialog(string workspaceRoot,
        string projectDirectory, ProjectDeploymentEntry mep, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(mep);
        _service = new ProjectDeploymentReservationService(workspaceRoot);
        _gates = new ProjectGateRegistryService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _mep = mep;
        _readOnly = readOnly;
        InitializeComponent();
        OperationText.Text = mep.Operation.Reference + " · " +
            mep.Operation.Title + " · décisions historiques inchangées";
        OriginCombo.ItemsSource = ProjectDeploymentReservationOrigins
            .List(mep.Operation).Select(item => new OriginOption(item,
                item.Kind + " · " + item.Description.Replace('\n', ' ')
                    .Replace('\r', ' ').Truncate(100))).ToArray();
        OriginCombo.SelectedIndex = OriginCombo.Items.Count > 0 ? 0 : -1;
        Loaded += async (_, _) => await GuardAsync(
            () => ReloadAsync(null), "Charger les réserves d'exploitation");
        UpdateControls();
    }

    /// <summary>Indicates creation or appended events for worktree refresh.</summary>
    public bool Changed { get; private set; }

    /// <summary>Currently selected persisted item.</summary>
    /// <returns>One source-grounded row or null.</returns>
    private ReservationRow? CurrentRow() =>
        ReservationsList.SelectedItem as ReservationRow;

    /// <summary>
    /// Reads the one project-local register off the UI thread, never scans
    /// the workspace nor mutates a closed MEP.
    /// </summary>
    /// <param name="selectedId">Reservation GUID to restore on refresh.</param>
    /// <returns>Completed refresh.</returns>
    private async Task ReloadAsync(Guid? selectedId)
    {
        ProjectDeploymentReservationSnapshot snapshot = await Task.Run(() =>
            _service.ReadAsync(_projectDirectory));
        _snapshot = snapshot;
        ReservationRow[] rows = snapshot.Ledger.Items
            .Where(item => item.OperationId == _mep.Operation.Id)
            .Select(item =>
            {
                ProjectDeploymentReservationState state =
                    ProjectDeploymentReservationCodec.GetState(
                        snapshot.Ledger, item.Id);
                return new ReservationRow(item, state,
                    item.Reference + " · " + item.Title,
                    "État : " + StatusLabel(state.Status) + " · " +
                    state.Owner + (item.DueDate is DateOnly due
                        ? " · cible : " + due.ToString("dd/MM/yyyy")
                        : ""));
            }).ToArray();
        ReservationsList.ItemsSource = rows;
        ReservationsList.SelectedItem = rows.FirstOrDefault(item =>
            item.Item.Id == selectedId) ?? rows.FirstOrDefault();
        StatusText.Text = (_readOnly ? "Archives · consultation seule · " : "") +
            rows.Length + " réserve(s) de cette MEP, " +
            rows.Count(row => row.State.Status !=
                ProjectDeploymentReservationStatus.Resolved) +
            " à suivre · aucune décision MEP modifiée.";
        PaintDetails();
        UpdateControls();
    }

    /// <summary>Paints only loaded data, no disk I/O on selection changes.</summary>
    private void PaintDetails()
    {
        if (CurrentRow() is not ReservationRow selected ||
            _snapshot is null)
        {
            DetailsBox.Text = "Sélectionnez une réserve suivie ou créez-en une " +
                "à partir d'une décision MEP avec réserves.";
            return;
        }

        ProjectDeploymentReservation item = selected.Item;
        List<string> lines =
        [
            item.Reference + " — " + item.Title,
            "ID : " + item.Id.ToString("D"),
            "MEP : " + _mep.Operation.Reference,
            "Source : " + item.OriginKind + " / " +
                item.OriginId.ToString("D"),
            "SHA de la MEP source : " + item.OriginSha256,
            "Réserve historique : " + item.OriginSummary,
            "Description : " + item.Description,
            "Création : " + item.CreatedUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm") +
                " · par " + item.CreatedBy,
            "Responsable initial : " + item.InitialOwner,
            "Responsable courant : " + selected.State.Owner,
            "État : " + StatusLabel(selected.State.Status),
            "Échéance : " + (item.DueDate?.ToString("dd/MM/yyyy") ??
                "aucune"),
            string.Empty,
            "HISTORIQUE DES DÉCISIONS HUMAINES"
        ];
        foreach (ProjectDeploymentReservationEvent action in
            _snapshot.Ledger.Events.Where(action =>
                action.ReservationId == item.Id))
        {
            lines.Add(action.RecordedUtc.ToLocalTime().ToString(
                "dd/MM/yyyy HH:mm") + " · " + ActionLabel(action.Action));
            lines.Add("  " + action.Actor + " · " + action.Rationale);
            if (action.NewOwner is not null)
            {
                lines.Add("  Nouveau responsable : " + action.NewOwner);
            }

            if (action.Evidence is not null)
            {
                lines.Add("  Preuve : " + action.Evidence.RelativePath);
            }
        }

        DetailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Allows only the transitions consistent with selected history.</summary>
    private void UpdateControls()
    {
        ReservationRow? selected = CurrentRow();
        bool editable = !_busy && !_readOnly && _snapshot is not null;
        CreateButton.IsEnabled = editable &&
            OriginCombo.SelectedItem is OriginOption &&
            ProjectDeploymentReservationService.NextReference(
                _snapshot!.Ledger) is not null;
        RecordButton.IsEnabled = editable && selected is not null;
        List<ActionOption> actions = [];
        if (selected is not null)
        {
            if (selected.State.Status ==
                ProjectDeploymentReservationStatus.Resolved)
            {
                actions.Add(new ActionOption(
                    ProjectDeploymentReservationAction.Reopen,
                    "Réouvrir la réserve"));
            }
            else
            {
                if (selected.State.Status ==
                    ProjectDeploymentReservationStatus.Open)
                {
                    actions.Add(new ActionOption(
                        ProjectDeploymentReservationAction.TakeInCharge,
                        "Prendre en charge"));
                }

                actions.Add(new ActionOption(
                    ProjectDeploymentReservationAction.ProgressNote,
                    "Consigner une progression"));
                actions.Add(new ActionOption(
                    ProjectDeploymentReservationAction.Reassign,
                    "Changer le responsable"));
                actions.Add(new ActionOption(
                    ProjectDeploymentReservationAction.Resolve,
                    "Déclarer la résolution"));
            }
        }

        ProjectDeploymentReservationAction? previous =
            (ActionCombo.SelectedItem as ActionOption)?.Action;
        ActionCombo.ItemsSource = actions;
        ActionCombo.SelectedItem = actions.FirstOrDefault(item =>
            item.Action == previous) ?? actions.FirstOrDefault();
        UpdateActionFields();
    }

    /// <summary>Enables owner only for an explicitly requested reassignment.</summary>
    private void UpdateActionFields()
    {
        if (NewOwnerBox is null || NewOwnerLabel is null)
        {
            return;
        }

        bool reassignment = ActionCombo.SelectedItem is ActionOption choice &&
            choice.Action == ProjectDeploymentReservationAction.Reassign;
        NewOwnerBox.IsEnabled = reassignment;
        NewOwnerLabel.IsEnabled = reassignment;
    }

    /// <summary>Wraps all user actions with WPF error reporting and busy state.</summary>
    /// <param name="action">One user-requested operation.</param>
    /// <param name="context">Diagnostic context.</param>
    /// <returns>Completed guarded operation.</returns>
    private async Task GuardAsync(Func<Task> action, string context)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateControls();
        try
        {
            await UiActionGuard.RunAsync(this,
                "Réserves exploitation · " + context, action);
        }
        finally
        {
            _busy = false;
            UpdateControls();
        }
    }

    /// <summary>Creates one explicit obligation from an actual MEP reservation.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">WPF event.</param>
    private async void Create_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || _snapshot is null ||
                OriginCombo.SelectedItem is not OriginOption origin ||
                ProjectDeploymentReservationService.NextReference(
                    _snapshot.Ledger) is not string reference)
            {
                return;
            }

            string title = TitleBox.Text.Trim();
            string description = DescriptionBox.Text.Trim();
            string owner = OwnerBox.Text.Trim();
            string actor = CreatorBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(owner) ||
                string.IsNullOrWhiteSpace(actor))
            {
                throw new InvalidDataException(
                    "Intitulé, action à reprendre, responsable et auteur requis.");
            }

            ProjectDeploymentReservation item = new()
            {
                Id = Guid.NewGuid(),
                Reference = reference,
                OperationId = _mep.Operation.Id,
                OriginKind = origin.Source.Kind,
                OriginId = origin.Source.Id,
                OriginSha256 = _mep.SourceSha256,
                OriginSummary = origin.Source.Description,
                Title = title,
                Description = description,
                CreatedBy = actor,
                InitialOwner = owner,
                CreatedUtc = DateTimeOffset.UtcNow,
                DueDate = DueDatePicker.SelectedDate is DateTime date
                    ? DateOnly.FromDateTime(date) : null
            };
            ProjectDeploymentReservationCodec.Add(_snapshot.Ledger, item);
            if (MessageBox.Show(this,
                "Créer " + reference + " — " + title + " ?\n\n" +
                "Responsable : " + owner + "\n" +
                "Origine : " + _mep.Operation.Reference + " / " +
                origin.Source.Kind + "\n\n" +
                "Cette fiche est distincte de la MEP clôturée. " +
                "Aucun incident, tâche ou GO ne sera modifié.",
                "Reprise explicite d'une réserve",
                MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectDeploymentReservationSnapshot saved =
                await _service.AddAsync(_projectDirectory,
                    _snapshot, _mep, item);
            _snapshot = saved;
            Changed = true;
            await ReloadAsync(item.Id);
            ModeTabs.SelectedIndex = 0;
            TitleBox.Clear();
            DescriptionBox.Clear();
            StatusText.Text = reference +
                " créée dans le suivi exploitation, sans modifier la MEP.";
        }, "Créer une réserve");

    /// <summary>Appends one auditable human action to the selected reserve.</summary>
    /// <param name="sender">Record button.</param>
    /// <param name="e">WPF event.</param>
    private async void Record_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || _snapshot is null ||
                CurrentRow() is not ReservationRow selected ||
                ActionCombo.SelectedItem is not ActionOption choice)
            {
                return;
            }

            string actor = ActionActorBox.Text.Trim();
            string reason = ActionReasonBox.Text.Trim();
            ProjectGateEvidenceSnapshot? evidence = null;
            if (!string.IsNullOrWhiteSpace(EvidenceBox.Text))
            {
                evidence = await _gates.CaptureEvidenceAsync(
                    EvidenceBox.Text.Trim());
            }

            ProjectDeploymentReservationEvent action = new()
            {
                Id = Guid.NewGuid(),
                ReservationId = selected.Item.Id,
                Action = choice.Action,
                Actor = actor,
                Rationale = reason,
                RecordedUtc = DateTimeOffset.UtcNow,
                NewOwner = choice.Action ==
                    ProjectDeploymentReservationAction.Reassign
                    ? NewOwnerBox.Text.Trim() : null,
                Evidence = evidence
            };
            ProjectDeploymentReservationCodec.Append(
                _snapshot.Ledger, action);
            if (MessageBox.Show(this,
                "Consigner " + ActionLabel(choice.Action) + " sur " +
                selected.Item.Reference + " ?\n\n" +
                reason + "\n\n" +
                "Seul le registre de suivi sera modifié. " +
                "Une résolution n'efface pas les réserves du bilan MEP " +
                "et ne ferme aucun incident automatiquement.",
                "Confirmer l'action exploitation",
                MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectDeploymentReservationSnapshot saved =
                await _service.RecordAsync(_projectDirectory,
                    _snapshot, action);
            _snapshot = saved;
            Changed = true;
            await ReloadAsync(selected.Item.Id);
            ActionReasonBox.Clear();
            EvidenceBox.Clear();
            NewOwnerBox.Clear();
            StatusText.Text = "Action historisée sur " +
                selected.Item.Reference + " · aucune MEP ni anomalie modifiée.";
        }, "Consigner une action");

    /// <summary>Updates the selected human obligation without scanning files.</summary>
    /// <param name="sender">ListBox.</param>
    /// <param name="e">Selection event.</param>
    private void ReservationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        PaintDetails();
        UpdateControls();
    }

    /// <summary>Shows the immutable source note for the chosen origin.</summary>
    /// <param name="sender">Origin combobox.</param>
    /// <param name="e">WPF event.</param>
    private void OriginCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (OriginSummaryText is null)
        {
            return;
        }

        if (OriginCombo.SelectedItem is OriginOption choice)
        {
            OriginSummaryText.Text = "Texte historique : " +
                choice.Source.Description;
            DescriptionBox.Text = choice.Source.Description;
        }
        else
        {
            OriginSummaryText.Text =
                "Aucune réserve explicite disponible dans la MEP.";
        }

        UpdateControls();
    }

    /// <summary>Updates the active reassignment field.</summary>
    /// <param name="sender">Action combobox.</param>
    /// <param name="e">WPF event.</param>
    private void ActionCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e) => UpdateActionFields();

    /// <summary>Reloads the isolated follow-up file on human request.</summary>
    /// <param name="sender">Refresh button.</param>
    /// <param name="e">WPF event.</param>
    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReloadAsync(CurrentRow()?.Item.Id),
            "Actualiser les réserves");

    /// <summary>Closes the modal without modifying the source MEP.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Readable current resolution state.</summary>
    /// <param name="status">Derived state.</param>
    /// <returns>French label.</returns>
    private static string StatusLabel(ProjectDeploymentReservationStatus status) =>
        status switch
        {
            ProjectDeploymentReservationStatus.Open => "Ouverte",
            ProjectDeploymentReservationStatus.InProgress => "En cours",
            ProjectDeploymentReservationStatus.Resolved => "Résolue",
            _ => "Inconnue"
        };

    /// <summary>Readable explicit human action.</summary>
    /// <param name="action">Persisted event kind.</param>
    /// <returns>French label.</returns>
    private static string ActionLabel(ProjectDeploymentReservationAction action) =>
        action switch
        {
            ProjectDeploymentReservationAction.TakeInCharge =>
                "Prise en charge",
            ProjectDeploymentReservationAction.ProgressNote =>
                "Progression",
            ProjectDeploymentReservationAction.Reassign =>
                "Réattribution",
            ProjectDeploymentReservationAction.Resolve =>
                "Résolution",
            ProjectDeploymentReservationAction.Reopen =>
                "Réouverture",
            _ => "Inconnue"
        };

    /// <summary>Selected human obligation and its projection.</summary>
    /// <param name="Item">Immutable origin.</param>
    /// <param name="State">Calculated current human state.</param>
    /// <param name="Label">UI row title.</param>
    /// <param name="Summary">UI row owner and due date.</param>
    private sealed record ReservationRow(
        ProjectDeploymentReservation Item,
        ProjectDeploymentReservationState State, string Label, string Summary);

    /// <summary>One eligible historical origin with exact source text.</summary>
    /// <param name="Source">Human MEP decision source.</param>
    /// <param name="Label">Human-readable option.</param>
    private sealed record OriginOption(
        ProjectDeploymentReservationSource Source, string Label);

    /// <summary>One permitted state transition offered to the reviewer.</summary>
    /// <param name="Action">Actual append-only event.</param>
    /// <param name="Label">Readable action.</param>
    private sealed record ActionOption(
        ProjectDeploymentReservationAction Action, string Label);
}

/// <summary>Pure small label helper, not part of the storage model.</summary>
internal static class ReservationLabelExtensions
{
    /// <summary>Truncates a long menu label without touching the source data.</summary>
    /// <param name="value">Full input label.</param>
    /// <param name="max">Desired maximum.</param>
    /// <returns>Display-only text.</returns>
    public static string Truncate(this string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
