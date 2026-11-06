using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// On-demand, project-scoped human association/dissociation of an existing
/// reservation and an existing stable task or milestone. No target is edited.
/// </summary>
public partial class ProjectDeploymentReservationRelationDialog : Window
{
    private readonly string _projectDirectory;
    private readonly Guid _reservationId;
    private readonly bool _readOnly;
    private readonly ProjectDeploymentReservationService _reservations;
    private readonly ProjectDeploymentReservationTargetService _targets;
    private ProjectDeploymentReservationSnapshot _snapshot;
    private bool _busy;

    /// <summary>Opens the historical relation editor without scanning sources.</summary>
    /// <param name="workspaceRoot">Current Nodalis workspace.</param>
    /// <param name="projectDirectory">Owner project.</param>
    /// <param name="snapshot">Verified follow-up history and SHA.</param>
    /// <param name="reservationId">Human-selected stable reservation.</param>
    /// <param name="readOnly">Archived project or read-only mode.</param>
    public ProjectDeploymentReservationRelationDialog(
        string workspaceRoot, string projectDirectory,
        ProjectDeploymentReservationSnapshot snapshot,
        Guid reservationId, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProjectDeploymentReservationCodec.Validate(snapshot.Ledger);
        ProjectDeploymentReservation item = snapshot.Ledger.Items.Single(
            candidate => candidate.Id == reservationId);
        _projectDirectory = projectDirectory;
        _snapshot = snapshot;
        _reservationId = reservationId;
        _readOnly = readOnly;
        _reservations = new ProjectDeploymentReservationService(workspaceRoot);
        _targets = new ProjectDeploymentReservationTargetService(workspaceRoot);
        InitializeComponent();
        HeadingText.Text = item.Reference + " · " + item.Title;
        ActionCombo.ItemsSource = new[]
        {
            new ActionOption(ProjectDeploymentReservationRelationAction.Link,
                "Associer un élément existant"),
            new ActionOption(ProjectDeploymentReservationRelationAction.Unlink,
                "Dissocier un lien historique")
        };
        KindCombo.ItemsSource = new[]
        {
            new KindOption(ProjectDeploymentReservationRelationKind.Task,
                "Tâche avec ID stable"),
            new KindOption(ProjectDeploymentReservationRelationKind.Milestone,
                "Jalon avec ID enregistré")
        };
        ActionCombo.SelectedIndex = 0;
        KindCombo.SelectedIndex = 0;
        PaintHistory();
        UpdateChoices();
    }

    /// <summary>True only after a human-confirmed append-only publication.</summary>
    public bool Changed { get; private set; }

    /// <summary>Shows audit events without task/milestone filesystem reads.</summary>
    private void PaintHistory()
    {
        IReadOnlyList<ProjectDeploymentReservationActiveLink> active =
            ProjectDeploymentReservationRelations.Active(
                _snapshot.Ledger, _reservationId);
        List<string> lines =
        [
            "LIENS ACTIFS · " + active.Count,
            string.Empty
        ];
        foreach (ProjectDeploymentReservationActiveLink item in active)
        {
            lines.Add(item.Kind + " · " + item.TargetId.ToString("D"));
            lines.Add("  " + item.SourceRelativePath);
        }

        lines.Add(string.Empty);
        lines.Add("HISTORIQUE DES RELATIONS");
        foreach (ProjectDeploymentReservationRelationEvent item in
            _snapshot.Ledger.Relations?.Where(eventItem =>
                eventItem.ReservationId == _reservationId) ?? [])
        {
            lines.Add(string.Empty);
            lines.Add(item.RecordedUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm") + " · " + item.Action +
                " · " + item.Kind);
            lines.Add("  " + item.TargetId.ToString("D"));
            lines.Add("  " + item.SourceRelativePath);
            lines.Add("  " + item.Actor + " · " + item.Rationale);
        }

        HistoryBox.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Refreshes only in-memory options; linking never auto-scans.</summary>
    private void UpdateChoices()
    {
        if (ActionCombo is null || KindCombo is null || TargetCombo is null ||
            LoadTargetsButton is null || SaveButton is null)
        {
            return;
        }

        bool canEdit = !_readOnly && !_busy;
        SaveButton.IsEnabled = canEdit;
        if (KindCombo.SelectedItem is not KindOption kind ||
            ActionCombo.SelectedItem is not ActionOption action)
        {
            LoadTargetsButton.IsEnabled = false;
            return;
        }

        bool unlink =
            action.Action == ProjectDeploymentReservationRelationAction.Unlink;
        LoadTargetsButton.IsEnabled = canEdit && !unlink;
        if (unlink)
        {
            IReadOnlyList<LinkOption> available =
                ProjectDeploymentReservationRelations.Active(
                    _snapshot.Ledger, _reservationId)
                .Where(item => item.Kind == kind.Kind)
                .Select(item => new LinkOption(
                    item.TargetId, item.SourceRelativePath,
                    item.TargetId.ToString("D") + " · " +
                    item.SourceRelativePath)).ToArray();
            TargetCombo.ItemsSource = available;
            TargetCombo.SelectedIndex = available.Count > 0 ? 0 : -1;
            CandidatesText.Text = available.Count +
                " lien(s) actuellement actifs. Une source supprimée reste " +
                "dissociable sans recréation.";
        }
        else
        {
            TargetCombo.ItemsSource = Array.Empty<LinkOption>();
            CandidatesText.Text =
                "Cliquez sur « Charger les éléments du projet » pour " +
                "vérifier les identifiants et exclure les doublons.";
        }
    }

    /// <summary>
    /// Expensive source lookup happens only on this explicit button, on a
    /// worker thread. The WPF list never causes workspace re-indexation.
    /// </summary>
    /// <param name="sender">Load candidates button.</param>
    /// <param name="e">WPF routed event.</param>
    private async void LoadTargets_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || ActionCombo.SelectedItem is not ActionOption action ||
                action.Action != ProjectDeploymentReservationRelationAction.Link ||
                KindCombo.SelectedItem is not KindOption kind)
            {
                return;
            }

            IReadOnlyList<ProjectDeploymentReservationTarget> candidates =
                await Task.Run(() => _targets.ListAsync(
                    _projectDirectory, kind.Kind));
            HashSet<Guid> linked = ProjectDeploymentReservationRelations.Active(
                _snapshot.Ledger, _reservationId)
                .Where(item => item.Kind == kind.Kind)
                .Select(item => item.TargetId).ToHashSet();
            LinkOption[] choices = candidates
                .Where(item => !linked.Contains(item.Id))
                .Select(item => new LinkOption(item.Id,
                    item.SourceRelativePath, item.Label + " · " +
                    item.SourceRelativePath)).ToArray();
            TargetCombo.ItemsSource = choices;
            TargetCombo.SelectedIndex = choices.Length > 0 ? 0 : -1;
            CandidatesText.Text = choices.Length + " cible(s) uniques et " +
                "persistantes du projet, non déjà associées. " +
                "Les sources seront revérifiées à la sauvegarde.";
        }, "Chercher les tâches ou jalons existants");

    /// <summary>Shows only valid actions/candidates for selected type.</summary>
    /// <param name="sender">Action or type selector.</param>
    /// <param name="e">Selection change event.</param>
    private void Choice_SelectionChanged(
        object sender, SelectionChangedEventArgs e) => UpdateChoices();

    /// <summary>Appends one human-confirmed link/unlink, never a side effect.</summary>
    /// <param name="sender">Confirm button.</param>
    /// <param name="e">WPF routed event.</param>
    private async void Save_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || ActionCombo.SelectedItem is not ActionOption action ||
                KindCombo.SelectedItem is not KindOption kind ||
                TargetCombo.SelectedItem is not LinkOption target)
            {
                throw new InvalidDataException(
                    "Sélectionnez une action, un type et une cible valide.");
            }

            ProjectDeploymentReservationRelationEvent relation = new()
            {
                Id = Guid.NewGuid(),
                ReservationId = _reservationId,
                Kind = kind.Kind,
                TargetId = target.Id,
                SourceRelativePath = target.SourceRelativePath,
                Action = action.Action,
                Actor = ActorBox.Text.Trim(),
                Rationale = ReasonBox.Text.Trim(),
                RecordedUtc = DateTimeOffset.UtcNow
            };
            ProjectDeploymentReservationRelations.Append(
                _snapshot.Ledger, relation);
            if (MessageBox.Show(this,
                (action.Action ==
                    ProjectDeploymentReservationRelationAction.Link
                    ? "Associer" : "Dissocier") + " ce " +
                (kind.Kind == ProjectDeploymentReservationRelationKind.Task
                    ? "lien vers une tâche" : "lien vers un jalon") + " ?\n\n" +
                target.Label + "\n\n" +
                relation.Rationale + "\n\n" +
                "La tâche, le jalon, la MEP et la réserve restent " +
                "inchangés ; seul l'historique des liens sera complété.",
                "Confirmer le lien de la réserve",
                MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            _snapshot = await _reservations.RecordRelationAsync(
                _projectDirectory, _snapshot, relation);
            Changed = true;
            DialogResult = true;
        }, "Consigner une relation");

    /// <summary>Reports WPF errors and guards against repeated submissions.</summary>
    /// <param name="action">One explicit action.</param>
    /// <param name="context">Readable error context.</param>
    /// <returns>Completed UI action.</returns>
    private async Task GuardAsync(Func<Task> action, string context)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SaveButton.IsEnabled = false;
        LoadTargetsButton.IsEnabled = false;
        try
        {
            await UiActionGuard.RunAsync(this,
                "Liens des réserves · " + context, action);
        }
        finally
        {
            _busy = false;
            if (!Changed)
            {
                SaveButton.IsEnabled = !_readOnly;
                LoadTargetsButton.IsEnabled = !_readOnly &&
                    ActionCombo.SelectedItem is ActionOption choice &&
                    choice.Action ==
                        ProjectDeploymentReservationRelationAction.Link;
            }
        }
    }

    /// <summary>Closes without writing any relation.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Selection option for an already-persisted task or milestone.</summary>
    /// <param name="Id">Stable GUID.</param>
    /// <param name="SourceRelativePath">Current or historical source hint.</param>
    /// <param name="Label">Readable option.</param>
    private sealed record LinkOption(
        Guid Id, string SourceRelativePath, string Label);

    /// <summary>Action available to the human reviewer.</summary>
    /// <param name="Action">Append-only link or unlink.</param>
    /// <param name="Label">Human label.</param>
    private sealed record ActionOption(
        ProjectDeploymentReservationRelationAction Action, string Label);

    /// <summary>Target type for explicit read-only lookup.</summary>
    /// <param name="Kind">Task or milestone category.</param>
    /// <param name="Label">Human label.</param>
    private sealed record KindOption(
        ProjectDeploymentReservationRelationKind Kind, string Label);
}
