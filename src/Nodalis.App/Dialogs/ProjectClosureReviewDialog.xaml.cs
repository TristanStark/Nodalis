using System.Windows;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>Explicit, human-reviewed closure report request.</summary>
/// <param name="Outcome">Human verdict, never an archive command.</param>
/// <param name="Actor">Named human reviewer.</param>
/// <param name="Rationale">Required review and passation rationale.</param>
public sealed record ProjectClosureReviewInput(
    ProjectClosureReviewOutcome Outcome, string Actor, string Rationale);

/// <summary>Scrollable, dark-theme-friendly and non-blocking closure review.</summary>
public partial class ProjectClosureReviewDialog : Window
{
    /// <summary>Constructs a review dialog over one verified source snapshot.</summary>
    /// <param name="audit">Current audit, or null for archived read-only history.</param>
    /// <param name="snapshot">Read-only canonical closure history.</param>
    /// <param name="readOnly">Whether writing must be disabled.</param>
    public ProjectClosureReviewDialog(ProjectClosureAuditSnapshot? audit,
        ProjectClosureReportSnapshot snapshot, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProjectClosureReportCodec.Validate(snapshot.Report);
        if (!readOnly && audit is null)
        {
            throw new InvalidDataException(
                "Une revue modifiable nécessite un pré-audit.");
        }

        InitializeComponent();
        ProjectClosureReview[] reviews = snapshot.Report.Reviews.ToArray();
        HistoryText.Text = reviews.Length == 0
            ? "Aucune revue enregistrée dans ce projet."
            : reviews.Length + " revue(s) historisée(s) :\n" +
              string.Join("\n", reviews.TakeLast(4).Select(item =>
                  "n°" + item.Number + " · " +
                  item.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                  " · " + item.Outcome + " · " + item.Actor));
        AuditBox.Text = audit?.FormatPreview(25) ??
            "Projet archivé : historique des décisions en lecture seule. " +
            "Le pré-audit des sources ne s'exécute pas automatiquement en archive.";
        WarningText.Text = readOnly
            ? "Lecture seule : aucune nouvelle décision ni transfert possible."
            : "Le pré-audit est partiel (tâches, jalons, incidents et campagnes " +
              "encore à vérifier). Même une revue « prêt à archiver » ne " +
              "déplace pas le projet.";
        OutcomeCombo.ItemsSource = new[]
        {
            new OutcomeChoice(ProjectClosureReviewOutcome.ReviewRecorded,
                "Revue enregistrée · actions à poursuivre"),
            new OutcomeChoice(ProjectClosureReviewOutcome.ReadyForArchive,
                "Prêt à archiver · décision humaine, non exécutoire"),
            new OutcomeChoice(ProjectClosureReviewOutcome.Deferred,
                "Clôture reportée")
        };
        OutcomeCombo.SelectedIndex = 0;
        OutcomeCombo.IsEnabled = !readOnly;
        ActorBox.IsEnabled = !readOnly;
        ReasonBox.IsEnabled = !readOnly;
        SaveButton.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Human-validated input available only after confirmation.</summary>
    public ProjectClosureReviewInput? ApprovedInput { get; private set; }

    /// <summary>Checks required fields and requests explicit human approval.</summary>
    /// <param name="sender">Submit button.</param>
    /// <param name="e">WPF click event.</param>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (OutcomeCombo.SelectedItem is not OutcomeChoice choice ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            string.IsNullOrWhiteSpace(ReasonBox.Text))
        {
            MessageBox.Show(this,
                "Indiquez une décision, un auteur et une justification explicite.",
                "Bilan incomplet", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string actor = ActorBox.Text.Trim();
        string rationale = ReasonBox.Text.Trim();
        if (actor.Length > 160 || actor.Any(char.IsControl) ||
            rationale.Length > 4000 || rationale.Contains('\0'))
        {
            MessageBox.Show(this,
                "Auteur ou justification invalide : corrigez les champs.",
                "Bilan incomplet", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this,
            "Consigner cette revue immuable : " + choice.Label + " ?\n\n" +
            "Les éléments du pré-audit seront figés dans le Markdown. " +
            "Aucun objet métier, transfert ni archivage ne sera déclenché.",
            "Confirmer la revue de clôture", MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ApprovedInput = new ProjectClosureReviewInput(
                choice.Outcome, actor, rationale);
            DialogResult = true;
        }
    }

    /// <summary>One human-readable, selectable review outcome.</summary>
    /// <param name="Outcome">Persisted outcome.</param>
    /// <param name="Label">Visible French label.</param>
    private sealed record OutcomeChoice(
        ProjectClosureReviewOutcome Outcome, string Label);
}
