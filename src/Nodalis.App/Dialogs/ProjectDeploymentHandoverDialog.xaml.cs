using System.IO;
using System.Windows;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-only stabilization/operational handover form. It collects seven
/// explicit checks but does not run any tests, deploy or write source files.
/// </summary>
public partial class ProjectDeploymentHandoverDialog : Window
{
    private readonly ProjectDeploymentOperation _operation;
    private readonly ProjectDeploymentAttempt _attempt;
    private readonly List<ControlRow> _rows = [];

    /// <summary>Shows the review of the latest finished MEP attempt.</summary>
    /// <param name="operation">Validated MEP, before final business closure.</param>
    public ProjectDeploymentHandoverDialog(ProjectDeploymentOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ProjectDeploymentMarkdownCodec.Validate(operation);
        if (operation.Closure is not null || operation.Attempts.Count == 0)
        {
            throw new InvalidDataException(
                "Passation impossible sans tentative ou après clôture MEP.");
        }

        _operation = operation;
        _attempt = operation.Attempts[^1];
        ProjectDeploymentAttemptState state =
            ProjectDeploymentMarkdownCodec.GetAttemptState(
                operation, _attempt.Id);
        if (!state.IsFinished)
        {
            throw new InvalidDataException(
                "Terminez la tentative avant la revue de stabilisation.");
        }

        InitializeComponent();
        bool succeeded = state.LastEventKind ==
            ProjectDeploymentEventKind.FinishedSucceeded;
        List<OutcomeChoice> choices = [];
        if (succeeded)
        {
            choices.Add(new OutcomeChoice(ProjectDeploymentHandoverOutcome.Accepted,
                "Acceptée"));
            choices.Add(new OutcomeChoice(
                ProjectDeploymentHandoverOutcome.AcceptedWithReservations,
                "Acceptée avec réserves"));
        }

        choices.Add(new OutcomeChoice(ProjectDeploymentHandoverOutcome.Refused,
            "Refusée"));
        choices.Add(new OutcomeChoice(ProjectDeploymentHandoverOutcome.Deferred,
            "Reportée"));
        OutcomeCombo.ItemsSource = choices;
        OutcomeCombo.SelectedIndex = succeeded ? 1 : choices.Count - 1;
        AttemptText.Text = operation.Reference + " · tentative " +
            _attempt.Number + " · " + state.LastEventKind +
            " · ancienne(s) revue(s) : " +
            (operation.HandoverReviews?.Count ?? 0);
        foreach (ProjectDeploymentHandoverArea area in
            ProjectDeploymentHandoverValidation.RequiredAreas)
        {
            _rows.Add(new ControlRow(area, AreaLabel(area)));
        }

        ChecksList.ItemsSource = _rows;
    }

    /// <summary>Human observations awaiting SHA capture in the caller.</summary>
    public IReadOnlyList<ControlRow> Rows => _rows;

    /// <summary>Creates the reviewed record with freshly captured evidence.</summary>
    /// <param name="evidence">Evidence snapshot by explicitly chosen area.</param>
    /// <returns>Immutable human review to append, never a deployment GO.</returns>
    public ProjectDeploymentHandoverReview BuildReview(
        IReadOnlyDictionary<ProjectDeploymentHandoverArea,
            ProjectGateEvidenceSnapshot?> evidence)
    {
        if (OutcomeCombo.SelectedItem is not OutcomeChoice outcome)
        {
            throw new InvalidDataException("Verdict de passation requis.");
        }

        return new ProjectDeploymentHandoverReview
        {
            Id = Guid.NewGuid(),
            AttemptId = _attempt.Id,
            RecordedUtc = DateTimeOffset.UtcNow,
            Actor = ActorBox.Text.Trim(),
            Recipient = RecipientBox.Text.Trim(),
            Outcome = outcome.Outcome,
            Rationale = RationaleBox.Text.Trim(),
            Reservations = string.IsNullOrWhiteSpace(ReservationsBox.Text)
                ? null : ReservationsBox.Text.Trim(),
            Checks = _rows.Select(row => new ProjectDeploymentHandoverCheck
            {
                Area = row.Area,
                Status = row.Status,
                Note = row.Note.Trim(),
                Evidence = evidence.TryGetValue(row.Area, out
                    ProjectGateEvidenceSnapshot? snapshot) ? snapshot : null
            }).ToList()
        };
    }

    /// <summary>
    /// Requests explicit approval and prominently warns about every
    /// unverified or reserved condition; never silently marks it conformant.
    /// </summary>
    /// <param name="sender">Confirm button.</param>
    /// <param name="e">WPF routed click.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (OutcomeCombo.SelectedItem is not OutcomeChoice choice ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            string.IsNullOrWhiteSpace(RecipientBox.Text) ||
            string.IsNullOrWhiteSpace(RationaleBox.Text) ||
            _rows.Any(row => string.IsNullOrWhiteSpace(row.Note)) ||
            choice.Outcome ==
                ProjectDeploymentHandoverOutcome.AcceptedWithReservations &&
                string.IsNullOrWhiteSpace(ReservationsBox.Text))
        {
            MessageBox.Show(this,
                "Choisissez un verdict, renseignez l'auteur, le destinataire, " +
                "le motif et un constat pour chacun des sept domaines. " +
                "Décrivez les réserves si l'acceptation est sous réserves.",
                "Passation incomplète", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int notVerified = _rows.Count(row => row.Status ==
            ProjectDeploymentHandoverStatus.NotVerified);
        int reservations = _rows.Count(row => row.Status ==
            ProjectDeploymentHandoverStatus.Reservation);
        int missingProof = _rows.Count(row =>
            row.Status == ProjectDeploymentHandoverStatus.Confirmed &&
            string.IsNullOrWhiteSpace(row.EvidencePath));
        string warning = notVerified + " non vérifié(s), " +
            reservations + " réserve(s), " +
            missingProof + " conforme(s) sans preuve jointe.";
        if (MessageBox.Show(this,
            "Consigner la passation : " + choice.Label + " ?\n\n" +
            warning + "\n\nCes alertes restent non bloquantes, mais " +
            "l'acceptation et le transfert des responsabilités exigent " +
            "votre validation explicite. Les preuves choisies seront " +
            "recontrôlées lors de l'enregistrement.\n" +
            "Aucun serveur, jalon, incident ou Go/No-Go n'est modifié.",
            "Confirmer la passation exploitation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
            MessageBoxResult.Yes)
        {
            DialogResult = true;
        }
    }

    /// <summary>Human-readable area label used by the scrollable form.</summary>
    /// <param name="area">Stable control category.</param>
    /// <returns>French label.</returns>
    private static string AreaLabel(ProjectDeploymentHandoverArea area) =>
        area switch
        {
            ProjectDeploymentHandoverArea.Documentation => "Documentation",
            ProjectDeploymentHandoverArea.Monitoring => "Supervision et alertes",
            ProjectDeploymentHandoverArea.BackupsAndRecovery =>
                "Sauvegardes et reprise",
            ProjectDeploymentHandoverArea.Security => "Sécurité",
            ProjectDeploymentHandoverArea.SupportAndOwnership =>
                "Support et responsabilités",
            ProjectDeploymentHandoverArea.RisksAndReservations =>
                "Risques et réserves",
            ProjectDeploymentHandoverArea.PostDeploymentTests =>
                "Tests post-déploiement",
            _ => area.ToString()
        };

    /// <summary>One editable check bound to the visual template.</summary>
    public sealed class ControlRow
    {
        /// <summary>One stable checklist area.</summary>
        public ProjectDeploymentHandoverArea Area { get; }

        /// <summary>Visible control title.</summary>
        public string Title { get; }

        /// <summary>Actual user-selected control status.</summary>
        public ProjectDeploymentHandoverStatus Status { get; set; } =
            ProjectDeploymentHandoverStatus.NotVerified;

        /// <summary>Explicit human observation or justification.</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>Optional path selected manually inside the workspace.</summary>
        public string EvidencePath { get; set; } = string.Empty;

        /// <summary>Available control outcomes.</summary>
        public IReadOnlyList<StatusChoice> Statuses { get; } =
        [
            new(ProjectDeploymentHandoverStatus.NotVerified, "Non vérifié"),
            new(ProjectDeploymentHandoverStatus.Confirmed, "Confirmé"),
            new(ProjectDeploymentHandoverStatus.Reservation, "Réserve"),
            new(ProjectDeploymentHandoverStatus.NotApplicable, "Sans objet")
        ];

        /// <summary>Constructs an editable row for one mandatory area.</summary>
        /// <param name="area">Stable area.</param>
        /// <param name="title">Readable label.</param>
        public ControlRow(ProjectDeploymentHandoverArea area, string title)
        {
            Area = area;
            Title = title;
        }
    }

    /// <summary>One option in the review decision combobox.</summary>
    /// <param name="Outcome">Final decision.</param>
    /// <param name="Label">French label.</param>
    private sealed record OutcomeChoice(
        ProjectDeploymentHandoverOutcome Outcome, string Label);

    /// <summary>One human control status in the combobox.</summary>
    /// <param name="Status">Persisted status.</param>
    /// <param name="Label">French label.</param>
    public sealed record StatusChoice(
        ProjectDeploymentHandoverStatus Status, string Label);
}
