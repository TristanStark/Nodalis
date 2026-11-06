using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-only MEP outcome collection. Never changes attempt results or
/// authorizes a technical deployment; the Core model checks every invariant.
/// </summary>
public partial class ProjectDeploymentClosureDialog : Window
{
    private readonly ProjectDeploymentOperation _operation;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo(
        "fr-FR");

    /// <summary>Shows only outcomes consistent with the last actual attempt.</summary>
    /// <param name="operation">Current validated source, without closure.</param>
    public ProjectDeploymentClosureDialog(ProjectDeploymentOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ProjectDeploymentMarkdownCodec.Validate(operation);
        if (operation.Closure is not null)
        {
            throw new InvalidDataException("Cette MEP possède déjà un bilan.");
        }

        _operation = operation;
        InitializeComponent();
        ProjectDeploymentAttempt? lastAttempt = operation.Attempts.LastOrDefault();
        ProjectDeploymentAttemptEvent? lastEvent = operation.Events.LastOrDefault();
        List<OutcomeChoice> choices = [];
        if (lastAttempt is null)
        {
            choices.Add(new OutcomeChoice(ProjectDeploymentOutcome.Cancelled,
                "Annulée · jamais démarrée"));
        }
        else if (lastEvent?.Kind == ProjectDeploymentEventKind.FinishedSucceeded)
        {
            choices.Add(new OutcomeChoice(ProjectDeploymentOutcome.Succeeded,
                "Réussie"));
            choices.Add(new OutcomeChoice(
                ProjectDeploymentOutcome.SucceededWithReservations,
                "Réussie avec réserves"));
        }
        else if (lastEvent is not null &&
            ProjectDeploymentMarkdownCodec.IsFinish(lastEvent.Kind))
        {
            choices.Add(new OutcomeChoice(ProjectDeploymentOutcome.Failed,
                "Échouée"));
            choices.Add(new OutcomeChoice(ProjectDeploymentOutcome.Cancelled,
                "Annulée"));
        }
        else
        {
            throw new InvalidDataException(
                "Terminez explicitement la dernière tentative avant le bilan.");
        }

        HistoryText.Text = operation.Reference + " · " + operation.Title +
            "\n" + (lastAttempt is null
                ? "Aucune tentative enregistrée."
                : "Dernière tentative : " + lastAttempt.Number +
                  " · " + lastEvent?.Kind);
        OutcomeCombo.ItemsSource = choices;
        OutcomeCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Creates the human-confirmed closure with a separate date for actual
    /// deployment and the current recorded business decision timestamp.
    /// </summary>
    /// <returns>One immutable final business outcome, not a GO.</returns>
    public ProjectDeploymentClosure BuildClosure()
    {
        if (OutcomeCombo.SelectedItem is not OutcomeChoice choice)
        {
            throw new InvalidDataException("Verdict MEP manquant.");
        }

        bool success = choice.Kind is ProjectDeploymentOutcome.Succeeded or
            ProjectDeploymentOutcome.SucceededWithReservations;
        DateTimeOffset? deployed = null;
        if (success)
        {
            if (!DateTimeOffset.TryParseExact(EffectiveBox.Text.Trim(),
                "dd/MM/yyyy HH:mm", French, DateTimeStyles.AssumeLocal,
                out DateTimeOffset value))
            {
                throw new InvalidDataException(
                    "Date de déploiement attendue : jj/MM/aaaa HH:mm.");
            }

            deployed = value;
        }

        return new ProjectDeploymentClosure
        {
            Id = Guid.NewGuid(),
            AttemptId = _operation.Attempts.LastOrDefault()?.Id,
            Outcome = choice.Kind,
            RecordedUtc = DateTimeOffset.UtcNow,
            EffectiveDeploymentUtc = deployed,
            Actor = ActorBox.Text.Trim(),
            Rationale = ReasonBox.Text.Trim(),
            Reservations = choice.Kind ==
                ProjectDeploymentOutcome.SucceededWithReservations
                    ? ReservationsBox.Text.Trim() : null
        };
    }

    /// <summary>Shows effective date and reservations only for their verdicts.</summary>
    /// <param name="sender">Verdict dropdown.</param>
    /// <param name="e">WPF selection event.</param>
    private void OutcomeCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (OutcomeCombo.SelectedItem is not OutcomeChoice choice)
        {
            return;
        }

        bool success = choice.Kind is ProjectDeploymentOutcome.Succeeded or
            ProjectDeploymentOutcome.SucceededWithReservations;
        EffectiveLabel.IsEnabled = success;
        EffectiveBox.IsEnabled = success;
        bool reserved = choice.Kind ==
            ProjectDeploymentOutcome.SucceededWithReservations;
        ReservationLabel.IsEnabled = reserved;
        ReservationsBox.IsEnabled = reserved;
    }

    /// <summary>Validates explicit fields before final human confirmation.</summary>
    /// <param name="sender">Review button.</param>
    /// <param name="e">WPF click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (OutcomeCombo.SelectedItem is not OutcomeChoice choice ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            string.IsNullOrWhiteSpace(ReasonBox.Text) ||
            choice.Kind == ProjectDeploymentOutcome.SucceededWithReservations &&
                string.IsNullOrWhiteSpace(ReservationsBox.Text))
        {
            MessageBox.Show(this,
                "Renseignez le responsable, la justification et les réserves " +
                "pour le verdict « Réussie avec réserves ».",
                "Bilan MEP incomplet", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ProjectDeploymentClosure closure;
        try
        {
            closure = BuildClosure();
            ProjectDeploymentEditor.Close(_operation, closure);
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(this, error.Message, "Bilan MEP invalide",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this,
            "Clôturer définitivement " + _operation.Reference + " : " +
            choice.Label + " ?\n\n" +
            (closure.EffectiveDeploymentUtc is DateTimeOffset deployed
                ? "Date effective constatée : " +
                  deployed.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                  "\n" : "") +
            (_operation.HandoverReviews is null ||
                _operation.HandoverReviews.Count == 0
                ? "Aucune revue de passation exploitation enregistrée : " +
                  "vérifiez consciemment les réserves et responsabilités.\n\n"
                : "") +
            "La date de clôture est distincte. Après enregistrement, " +
            "plus aucune tentative, passation ni événement ne sera ajouté " +
            "à cette MEP. " +
            "Aucun jalon, GO ou incident ne sera modifié.",
            "Confirmer le bilan métier", MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ConfirmedClosure = closure;
            DialogResult = true;
        }
    }

    /// <summary>Reviewed closure accepted explicitly by the human.</summary>
    public ProjectDeploymentClosure? ConfirmedClosure { get; private set; }

    /// <summary>Readable verdict option consistent with prior events.</summary>
    /// <param name="Kind">Persisted business outcome.</param>
    /// <param name="Label">Human label.</param>
    private sealed record OutcomeChoice(ProjectDeploymentOutcome Kind,
        string Label);
}
