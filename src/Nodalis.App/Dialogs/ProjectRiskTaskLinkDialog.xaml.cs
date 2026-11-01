using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Risks;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human review of one durable relationship. Missing task sources are shown
/// as unresolved GUIDs and can be explicitly unlinked, never auto-healed.
/// </summary>
public partial class ProjectRiskTaskLinkDialog : Window
{
    private readonly ProjectRiskEntry _risk;
    private readonly IReadOnlyList<TaskItem> _tasks;

    /// <summary>One display-only eligible or previously linked target.</summary>
    /// <param name="Id">Permanent checkbox GUID.</param>
    /// <param name="Label">Human-readable source/diagnostic.</param>
    private sealed record TaskOption(Guid Id, string Label);

    /// <summary>Initializes a source-resolved human relation form.</summary>
    /// <param name="risk">Canonical risk with an existing treatment plan.</param>
    /// <param name="tasks">Unique persistent project tasks from the service.</param>
    public ProjectRiskTaskLinkDialog(
        ProjectRiskEntry risk, IReadOnlyList<TaskItem> tasks)
    {
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(tasks);
        if (risk.CurrentTreatment is null)
        {
            throw new InvalidOperationException(
                "Un plan de traitement est requis pour associer une tâche.");
        }

        _risk = risk;
        _tasks = tasks;
        InitializeComponent();
        HeadingText.Text = "Tâches et mesures · " + risk.Card.Reference;
        SummaryText.Text = risk.Card.Title + Environment.NewLine +
            "Version de traitement : " + risk.Treatments[^1].Id.ToString("D") +
            ". Les sources absentes peuvent être déliées, jamais " +
            "recréées automatiquement.";
        ActionCombo.ItemsSource = new[]
        {
            "Associer une tâche",
            "Dissocier une tâche"
        };
        MeasureCombo.ItemsSource = new[]
        {
            "Prévention",
            "Réduction / transfert",
            "Contingence"
        };
        ActionCombo.SelectedIndex = 0;
        MeasureCombo.SelectedIndex = 0;
        Loaded += (_, _) => TaskCombo.Focus();
    }

    /// <summary>Named treatment measure selected by the human.</summary>
    public ProjectRiskMeasureKind? Kind { get; private set; }

    /// <summary>Link or unlink decision.</summary>
    public ProjectRiskTaskLinkAction? Action { get; private set; }

    /// <summary>Only a truly persistent GUID can be chosen.</summary>
    public Guid? TaskId { get; private set; }

    /// <summary>Human author of the association.</summary>
    public string? Actor { get; private set; }

    /// <summary>Reason for this reversible link event.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Recomputes choices from in-memory verified task snapshots.</summary>
    /// <param name="sender">Choice change control.</param>
    /// <param name="e">WPF selection event.</param>
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TaskCombo is null || MeasureCombo.SelectedIndex is < 0 or > 2 ||
            ActionCombo.SelectedIndex is < 0 or > 1)
        {
            return;
        }

        ProjectRiskMeasureKind kind =
            (ProjectRiskMeasureKind)MeasureCombo.SelectedIndex;
        IReadOnlyList<Guid> linked = _risk.LinkedTaskIds(kind);
        TaskOption[] options;
        if (ActionCombo.SelectedIndex == 0)
        {
            options = _tasks.Where(task =>
                task.PersistentId is Guid id && !linked.Contains(id))
                .Select(task => new TaskOption(task.PersistentId!.Value,
                    task.Text + " · " + task.SourceRelativePath +
                    (task.IsCompleted ? " · checkbox cochée" : "")))
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        else
        {
            options = linked.Select(id =>
            {
                TaskItem? task = _tasks.FirstOrDefault(item =>
                    item.PersistentId == id);
                return new TaskOption(id, task is null
                    ? id.ToString("D") + " · source absente ou non résolue"
                    : task.Text + " · " + task.SourceRelativePath);
            }).ToArray();
        }

        TaskCombo.ItemsSource = options;
        TaskCombo.SelectedIndex = options.Length == 0 ? -1 : 0;
        CandidatesText.Text = options.Length == 0
            ? ActionCombo.SelectedIndex == 0
                ? "Aucune tâche éligible : attribuez un ID stable à une tâche " +
                  "du projet depuis la vue Tâches, ou vérifiez les doublons."
                : "Aucune tâche actuellement liée à cette mesure."
            : options.Length + " tâche(s) disponible(s) pour cette opération.";
    }

    /// <summary>Captures a proposal, without modifying any source.</summary>
    /// <param name="sender">User proposal button.</param>
    /// <param name="e">WPF event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (ActionCombo.SelectedIndex is < 0 or > 1 ||
            MeasureCombo.SelectedIndex is < 0 or > 2 ||
            TaskCombo.SelectedItem is not TaskOption option ||
            option.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            ActorBox.Text.Any(char.IsControl) ||
            RationaleBox.Text.Trim().Length < 10 ||
            RationaleBox.Text.Length > 4000 ||
            RationaleBox.Text.Any(character => char.IsControl(character) &&
                character is not ('\r' or '\n')))
        {
            MessageBox.Show(this,
                "Choisissez une opération, une mesure, une tâche possédant " +
                "un GUID stable, puis renseignez un décideur et un motif " +
                "d'au moins dix caractères.",
                "Relation tâche/mesure incomplète",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Kind = (ProjectRiskMeasureKind)MeasureCombo.SelectedIndex;
        Action = (ProjectRiskTaskLinkAction)ActionCombo.SelectedIndex;
        TaskId = option.Id;
        Actor = ActorBox.Text.Trim();
        Rationale = RationaleBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>Dismisses without creating a relationship.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">WPF event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
