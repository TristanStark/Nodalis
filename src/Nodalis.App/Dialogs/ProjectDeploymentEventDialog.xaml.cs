using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// A single human-authored MEP event; distinguishes request, execution and
/// observed rollback, with no automatic completion or implied success.
/// </summary>
public partial class ProjectDeploymentEventDialog : Window
{
    /// <summary>Creates an explicit attempt-event form.</summary>
    public ProjectDeploymentEventDialog()
    {
        InitializeComponent();
        KindCombo.ItemsSource = new[]
        {
            new KindChoice(ProjectDeploymentEventKind.ActionLogged,
                "Journaliser une action"),
            new KindChoice(ProjectDeploymentEventKind.RollbackRequested,
                "Demander un rollback"),
            new KindChoice(ProjectDeploymentEventKind.RollbackStarted,
                "Déclarer le démarrage du rollback"),
            new KindChoice(ProjectDeploymentEventKind.RollbackObserved,
                "Constater le résultat du rollback"),
            new KindChoice(ProjectDeploymentEventKind.FinishedSucceeded,
                "Terminer : déploiement réussi"),
            new KindChoice(ProjectDeploymentEventKind.FinishedFailed,
                "Terminer : déploiement échoué"),
            new KindChoice(ProjectDeploymentEventKind.FinishedInterrupted,
                "Terminer : interrompu")
        };
        RollbackCombo.ItemsSource = new[]
        {
            new OutcomeChoice(ProjectDeploymentRollbackOutcome.Succeeded,
                "Retour confirmé"),
            new OutcomeChoice(ProjectDeploymentRollbackOutcome.Partial,
                "Retour partiel"),
            new OutcomeChoice(ProjectDeploymentRollbackOutcome.Failed,
                "Retour échoué"),
            new OutcomeChoice(ProjectDeploymentRollbackOutcome.Interrupted,
                "Retour interrompu")
        };
        RollbackCombo.SelectedIndex = 0;
        KindCombo.SelectedIndex = 0;
    }

    /// <summary>Builds one new immutable event for the selected attempt.</summary>
    /// <param name="attemptId">Existing active attempt ID.</param>
    /// <returns>Human event, without any file write.</returns>
    public ProjectDeploymentAttemptEvent BuildEvent(Guid attemptId)
    {
        if (KindCombo.SelectedItem is not KindChoice choice)
        {
            throw new InvalidDataException("Type d'événement requis.");
        }

        ProjectDeploymentEventKind kind = choice.Kind;
        return new ProjectDeploymentAttemptEvent
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            Kind = kind,
            RecordedUtc = DateTimeOffset.UtcNow,
            Actor = ActorBox.Text.Trim(),
            Rationale = ReasonBox.Text.Trim(),
            ObservedVersionAfter = kind is
                ProjectDeploymentEventKind.RollbackObserved or
                ProjectDeploymentEventKind.FinishedSucceeded or
                ProjectDeploymentEventKind.FinishedFailed or
                ProjectDeploymentEventKind.FinishedInterrupted
                    ? string.IsNullOrWhiteSpace(ObservedVersionBox.Text)
                        ? null : ObservedVersionBox.Text.Trim()
                    : null,
            RollbackOutcome =
                kind == ProjectDeploymentEventKind.RollbackObserved &&
                RollbackCombo.SelectedItem is OutcomeChoice outcome
                    ? outcome.Outcome : null
        };
    }

    /// <summary>Enables only fields meaningful for the selected event kind.</summary>
    /// <param name="sender">Kind dropdown.</param>
    /// <param name="e">Selection change.</param>
    private void KindCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (KindCombo.SelectedItem is not KindChoice selected)
        {
            return;
        }

        ProjectDeploymentEventKind kind = selected.Kind;
        bool observed = kind is ProjectDeploymentEventKind.RollbackObserved or
            ProjectDeploymentEventKind.FinishedSucceeded or
            ProjectDeploymentEventKind.FinishedFailed or
            ProjectDeploymentEventKind.FinishedInterrupted;
        ObservedVersionBox.IsEnabled = observed;
        VersionLabel.IsEnabled = observed;
        bool rollback = kind == ProjectDeploymentEventKind.RollbackObserved;
        RollbackCombo.IsEnabled = rollback;
        RollbackLabel.IsEnabled = rollback;
    }

    /// <summary>Requires an explicit actor, explanation and observed result.</summary>
    /// <param name="sender">Approve button.</param>
    /// <param name="e">WPF click.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (KindCombo.SelectedItem is not KindChoice selected ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            string.IsNullOrWhiteSpace(ReasonBox.Text) ||
            selected.Kind is
                (ProjectDeploymentEventKind.RollbackObserved or
                 ProjectDeploymentEventKind.FinishedSucceeded) &&
                string.IsNullOrWhiteSpace(ObservedVersionBox.Text))
        {
            MessageBox.Show(this,
                "Renseignez un acteur, une justification et, pour une " +
                "réussite ou un rollback constaté, la version réellement observée.",
                "Événement MEP incomplet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
            "Consigner l'événement « " + selected.Label + " » ?\n\n" +
            "Cet événement sera figé dans l'historique. Une demande " +
            "de rollback ne prouve ni son exécution ni sa réussite. " +
            "Nodalis ne lancera aucune opération technique.",
            "Événement MEP · confirmer",
            MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes)
        {
            DialogResult = true;
        }
    }

    /// <summary>UI label for one explicit action category.</summary>
    /// <param name="Kind">Persisted event type.</param>
    /// <param name="Label">French display label.</param>
    private sealed record KindChoice(
        ProjectDeploymentEventKind Kind, string Label);

    /// <summary>UI label for the observed rollback outcome.</summary>
    /// <param name="Outcome">Observed result type.</param>
    /// <param name="Label">French display label.</param>
    private sealed record OutcomeChoice(
        ProjectDeploymentRollbackOutcome Outcome, string Label);
}
