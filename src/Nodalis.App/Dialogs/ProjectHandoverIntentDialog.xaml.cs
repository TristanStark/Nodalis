using System.IO;
using System.Windows;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>Selectable source object for the explicit project handover form.</summary>
/// <param name="Id">Existing verified object identity.</param>
/// <param name="Label">Human display text, distinct from technical identity.</param>
public sealed record ProjectHandoverItemChoice(Guid Id, string Label);

/// <summary>Selectable existing destination project.</summary>
/// <param name="Id">Persisted target project GUID.</param>
/// <param name="Directory">Target project physical path, verified again by preflight.</param>
/// <param name="Label">Human display text.</param>
public sealed record ProjectHandoverTargetChoice(
    Guid Id, string Directory, string Label);

/// <summary>Human request used for a second, revision-checked preflight.</summary>
/// <param name="ItemId">Selected existing source object.</param>
/// <param name="TargetDirectory">Selected existing target project.</param>
/// <param name="Method">Requested A/B intention strategy.</param>
/// <param name="Actor">Human reviewer.</param>
/// <param name="Rationale">Required follow-up reasoning.</param>
public sealed record ProjectHandoverRequest(
    Guid ItemId, string TargetDirectory, ProjectHandoverMethod Method,
    string Actor, string Rationale);

/// <summary>
/// Lightweight, scrollable review of pending A/B intentions; no physical
/// transfer button is exposed until an independently validated executor exists.
/// </summary>
public partial class ProjectHandoverIntentDialog : Window
{
    /// <summary>Shows the latest project-local inventory and intention history.</summary>
    /// <param name="audit">Selected project's fresh read-only audit.</param>
    /// <param name="targets">Existing navigation project candidates.</param>
    /// <param name="snapshot">Canonical intention history.</param>
    /// <param name="transactions">Durable execution and recovery journals.</param>
    /// <param name="readOnly">Archived or read-only workspace flag.</param>
    public ProjectHandoverIntentDialog(
        ProjectClosureAuditSnapshot? audit,
        IReadOnlyList<ProjectHandoverTargetChoice> targets,
        ProjectHandoverIntentSnapshot snapshot,
        IReadOnlyList<ProjectHandoverTaskJournal> transactions, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProjectHandoverIntentCodec.Validate(snapshot.Ledger);
        InitializeComponent();

        ProjectHandoverItemChoice[] items = audit?.Items
            .Where(item => item.RelativePath is not null)
            .Select(item => new ProjectHandoverItemChoice(
                item.Id, item.Reference + " · " + item.Title))
            .ToArray() ?? [];
        ItemCombo.ItemsSource = items;
        TargetCombo.ItemsSource = targets;
        MethodCombo.ItemsSource = new[]
        {
            new MethodChoice(ProjectHandoverMethod.MoveOriginal,
                "A · déplacer l'original en conservant son GUID"),
            new MethodChoice(ProjectHandoverMethod.LinkedSuccessor,
                "B · créer un successeur lié avec nouveau GUID")
        };
        if (items.Length > 0)
        {
            ItemCombo.SelectedIndex = 0;
        }

        if (targets.Count > 0)
        {
            TargetCombo.SelectedIndex = 0;
        }

        MethodCombo.SelectedIndex = 0;
        ProjectHandoverIntent[] history = snapshot.Ledger.Intents.ToArray();
        HistoryText.Text = history.Length == 0
            ? "Aucune passation préparée."
            : history.Length + " intention(s) conservée(s) sans exécution.";
        PreviousText.Text = history.Length == 0
            ? "Les futures préparations seront listées ici."
            : string.Join("\n", history.TakeLast(20).Select(item =>
                item.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                " · " + item.SourceReference + " · " + item.Method +
                " · " + item.TargetProjectRelativePath +
                " · " + item.Actor));
        WarningText.Text = readOnly
            ? "Lecture seule : les intentions enregistrées restent consultables."
            : "Choisissez l'objet source, le destinataire et A ou B. " +
              "Une prévisualisation vérifiera les collisions et les " +
              "changements d'état avant d'autoriser l'enregistrement.";
        ProjectHandoverIntent[] executable = history.Where(intent =>
            intent.SourceObjectKind == "Tâches ouvertes" &&
            intent.SourceRelativePath.EndsWith("/Tâches.md",
                StringComparison.OrdinalIgnoreCase) &&
            !transactions.Any(transaction =>
                transaction.IntentId == intent.Id)).ToArray();
        ProjectHandoverExecutionChoice[] executionChoices = executable
            .Select(intent => new ProjectHandoverExecutionChoice(
                intent.Id, intent.SourceReference + " · " + intent.Method +
                " · " + intent.TargetProjectRelativePath)).ToArray();
        ExecutionCombo.ItemsSource = executionChoices;
        if (executionChoices.Length > 0)
        {
            ExecutionCombo.SelectedIndex = 0;
        }

        ProjectHandoverTaskJournal[] pending = transactions.Where(item =>
            item.Phase is not ("completed" or "rolled-back")).ToArray();
        ProjectHandoverTaskJournal[] completed = transactions.Where(item =>
            item.SourceProjectId == snapshot.Ledger.ProjectId &&
            item.Phase == "completed").ToArray();
        TransactionText.Text = transactions.Count == 0
            ? "Aucune exécution transactionnelle dans ce workspace."
            : string.Join("\n", transactions.Take(30).Select(item =>
                item.TransactionId.ToString("D") + " · " + item.Method +
                " · " + item.Phase + " · " +
                item.SourceProjectRelativePath + " vers " +
                item.TargetProjectRelativePath));
        RollbackCombo.ItemsSource = completed.Select(item =>
            new ProjectHandoverExecutionChoice(item.TransactionId,
                item.TaskId.ToString("D") + " · " + item.Method +
                " · " + item.CreatedUtc.ToLocalTime().ToString("dd/MM HH:mm")))
            .ToArray();
        if (completed.Length > 0)
        {
            RollbackCombo.SelectedIndex = 0;
        }

        ExecutionCombo.IsEnabled = !readOnly && pending.Length == 0;
        ExecuteButton.IsEnabled = !readOnly && pending.Length == 0 &&
            executionChoices.Length > 0;
        RecoveryButton.IsEnabled = !readOnly && pending.Length > 0;
        RollbackCombo.IsEnabled = !readOnly && pending.Length == 0 &&
            completed.Length > 0;
        RollbackButton.IsEnabled = RollbackCombo.IsEnabled;
        if (readOnly)
        {
            ExecuteButton.Visibility = Visibility.Collapsed;
            RecoveryButton.Visibility = Visibility.Collapsed;
            RollbackButton.Visibility = Visibility.Collapsed;
        }

        bool allowEdit = !readOnly && items.Length > 0 && targets.Count > 0;
        ItemCombo.IsEnabled = allowEdit;
        TargetCombo.IsEnabled = allowEdit;
        MethodCombo.IsEnabled = allowEdit;
        ActorBox.IsEnabled = allowEdit;
        RationaleBox.IsEnabled = allowEdit;
        PrepareButton.Visibility = allowEdit
            ? Visibility.Visible : Visibility.Collapsed;
        if (!readOnly && !allowEdit)
        {
            WarningText.Text = "Aucun objet à reprendre ou aucun autre " +
                "projet actif accessible. Rien ne sera créé.";
        }
    }

    /// <summary>Request returned only after validating all explicit inputs.</summary>
    public ProjectHandoverRequest? ApprovedRequest { get; private set; }

    /// <summary>Selected human intention for a separately approved task mutation.</summary>
    public Guid? RequestedExecutionIntentId { get; private set; }

    /// <summary>True only after the user clicked the explicit recovery action.</summary>
    public bool RequestedRecovery { get; private set; }

    /// <summary>Selected already-completed transaction for explicit rollback.</summary>
    public Guid? RequestedRollbackTransactionId { get; private set; }

    /// <summary>Validates selections before the external read-only preflight.</summary>
    /// <param name="sender">Prepare button.</param>
    /// <param name="e">WPF click event.</param>
    private void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (ItemCombo.SelectedItem is not ProjectHandoverItemChoice item ||
            TargetCombo.SelectedItem is not ProjectHandoverTargetChoice target ||
            MethodCombo.SelectedItem is not MethodChoice method ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            string.IsNullOrWhiteSpace(RationaleBox.Text))
        {
            MessageBox.Show(this,
                "Sélectionnez un objet, un projet, une stratégie et " +
                "renseignez un auteur ainsi qu'une justification.",
                "Préparation incomplète", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string actor = ActorBox.Text.Trim();
        string rationale = RationaleBox.Text.Trim();
        if (actor.Length > 160 || actor.Any(char.IsControl) ||
            rationale.Length > 4000 || rationale.Contains('\0'))
        {
            MessageBox.Show(this, "Auteur ou justification invalide.",
                "Préparation incomplète", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ApprovedRequest = new ProjectHandoverRequest(
            item.Id, target.Directory, method.Method, actor, rationale);
        DialogResult = true;
    }

    /// <summary>Requests an independently preflighted physical task transfer.</summary>
    /// <param name="sender">Execution button.</param>
    /// <param name="e">Click.</param>
    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (ExecutionCombo.SelectedItem is
            ProjectHandoverExecutionChoice choice)
        {
            RequestedExecutionIntentId = choice.Id;
            DialogResult = true;
        }
    }

    /// <summary>Requests explicit recovery of pending durable journals.</summary>
    /// <param name="sender">Recovery button.</param>
    /// <param name="e">Click.</param>
    private void Recovery_Click(object sender, RoutedEventArgs e)
    {
        RequestedRecovery = true;
        DialogResult = true;
    }

    /// <summary>Requests a separately approved two-document rollback.</summary>
    /// <param name="sender">Rollback button.</param>
    /// <param name="e">Click.</param>
    private void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (RollbackCombo.SelectedItem is
            ProjectHandoverExecutionChoice choice)
        {
            RequestedRollbackTransactionId = choice.Id;
            DialogResult = true;
        }
    }

    /// <summary>A verified plan or completed transaction selection.</summary>
    /// <param name="Id">Stable intention or transaction GUID.</param>
    /// <param name="Label">Localized display.</param>
    private sealed record ProjectHandoverExecutionChoice(Guid Id, string Label);

    /// <summary>Localized choice of a non-executing handover strategy.</summary>
    /// <param name="Method">Persisted strategy.</param>
    /// <param name="Label">Human label.</param>
    private sealed record MethodChoice(
        ProjectHandoverMethod Method, string Label);
}
