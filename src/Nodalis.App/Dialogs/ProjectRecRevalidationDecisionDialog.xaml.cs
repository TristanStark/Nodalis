using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-only, source-scoped incident revalidation form. The actual
/// maintenance write always goes through revision-checked infrastructure.
/// </summary>
public partial class ProjectRecRevalidationDecisionDialog : Window
{
    private sealed record DecisionChoice(
        ProjectRecRevalidationDisposition Value, string Label);

    private readonly ProjectRecRevalidationPreview _review;

    /// <summary>Populates an explicit decision form from a verified snapshot.</summary>
    /// <param name="review">Source revisions and current revalidation advice.</param>
    public ProjectRecRevalidationDecisionDialog(
        ProjectRecRevalidationPreview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        _review = review;
        InitializeComponent();
        DispositionBox.ItemsSource = new[]
        {
            new DecisionChoice(ProjectRecRevalidationDisposition.Deferred,
                "Reporter — tests ou preuves supplémentaires nécessaires"),
            new DecisionChoice(ProjectRecRevalidationDisposition.Rejected,
                "Rejeter — réexécution non concluante"),
            new DecisionChoice(ProjectRecRevalidationDisposition.Confirmed,
                "Confirmer — revalidation humaine du périmètre précis")
        };
        DispositionBox.SelectedIndex = 0;
        ContextText.Text = review.Incident.IncidentId + " · " +
            review.Incident.AffectedTestCount +
            " test(s) concernés, " + review.Incident.LaterPassedCount +
            " avec nouveau Passed dans le même environnement." +
            (review.Latest is ProjectRecRevalidationEvent previous
                ? "\nDernière décision : " + previous.Disposition +
                    ", " + previous.RecordedUtc.ToLocalTime()
                        .ToString("dd/MM/yyyy HH:mm") +
                    (review.LatestUsesCurrentRec
                        ? " · même photographie REC"
                        : " · sources REC modifiées depuis")
                : "\nAucune décision humaine encore enregistrée.");

        ProjectRecRetestObservation? first = review.Incident.Tests.FirstOrDefault();
        if (first is not null)
        {
            string? commonEnvironment = first.LaterEnvironment ??
                first.FailureEnvironment;
            string? commonVersion = first.LaterVersion ??
                first.FailureVersion;
            if (review.Incident.Tests.All(item =>
                string.Equals(item.LaterEnvironment ?? item.FailureEnvironment,
                    commonEnvironment, StringComparison.OrdinalIgnoreCase)))
            {
                EnvironmentBox.Text = commonEnvironment;
            }

            if (review.Incident.Tests.All(item =>
                string.Equals(item.LaterVersion ?? item.FailureVersion,
                    commonVersion, StringComparison.OrdinalIgnoreCase)))
            {
                VersionBox.Text = commonVersion;
            }
        }

        LimitText.Text = review.Incident.ReviewMayBeProposed
            ? "Examen humain envisageable : cela n'est ni une preuve " +
                "de résolution ni un changement de statut. Pour confirmer, " +
                "tous les nouveaux Passed doivent couvrir exactement " +
                "l'environnement et la version saisis. Les trois " +
                "attestations et une référence de preuve sont obligatoires."
            : "Revalidation pas encore démontrée : une confirmation " +
                "est bloquée, mais le report ou le rejet humain peuvent " +
                "être historisés avec justification et périmètre.";
        ActorBox.Focus();
    }

    /// <summary>Disposition explicitement choisie par le responsable.</summary>
    public ProjectRecRevalidationDisposition Disposition { get; private set; }

    /// <summary>Human audit actor.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human written decision rationale.</summary>
    public string? Reason { get; private set; }

    /// <summary>Evidence reference claimed to have been reviewed.</summary>
    public string? Evidence { get; private set; }

    /// <summary>Explicit target execution environment.</summary>
    public string? TargetEnvironment { get; private set; }

    /// <summary>Explicit target application version.</summary>
    public string? TargetVersion { get; private set; }

    /// <summary>Confirmation that current TEST definitions were reviewed.</summary>
    public bool SourcesChecked { get; private set; }

    /// <summary>Confirmation that external evidence was inspected.</summary>
    public bool EvidenceChecked { get; private set; }

    /// <summary>Confirmation of scope, version and regressions.</summary>
    public bool ScopeChecked { get; private set; }

    /// <summary>
    /// Explains which attestations become mandatory for Confirmed.
    /// </summary>
    /// <param name="sender">Human disposition selection.</param>
    /// <param name="e">Selection changed.</param>
    private void DispositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LimitText is null ||
            DispositionBox.SelectedItem is not DecisionChoice choice)
        {
            return;
        }

        if (choice.Value == ProjectRecRevalidationDisposition.Confirmed &&
            !_review.Incident.ReviewMayBeProposed)
        {
            LimitText.Text = "Confirmation interdite : certains TEST " +
                "n'ont pas de nouveau Passed comparable ou la source " +
                "incident est indisponible. Choisissez Reporter ou Rejeter.";
        }
    }

    /// <summary>Validates only human form fields before final commit preview.</summary>
    /// <param name="sender">Review form confirmation button.</param>
    /// <param name="e">Click event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (DispositionBox.SelectedItem is not DecisionChoice choice)
        {
            return;
        }

        string actor = ActorBox.Text.Trim();
        string reason = ReasonBox.Text.Trim();
        string evidence = EvidenceBox.Text.Trim();
        string environment = EnvironmentBox.Text.Trim();
        string version = VersionBox.Text.Trim();
        bool confirmed =
            choice.Value == ProjectRecRevalidationDisposition.Confirmed;
        if (!ValidLine(actor, 160) ||
            !ValidLine(reason, 2000) || reason.Length < 10 ||
            evidence.Length > 2000 || evidence.Any(char.IsControl) ||
            !ValidLine(environment, 160) ||
            !ValidLine(version, 160) ||
            confirmed &&
                (!_review.Incident.ReviewMayBeProposed ||
                 evidence.Length < 10 ||
                 SourcesCheck.IsChecked != true ||
                 EvidenceCheck.IsChecked != true ||
                 ScopeCheck.IsChecked != true))
        {
            MessageBox.Show(this,
                "Auteur, justification (10 caractères minimum), version et " +
                "environnement sont requis sur une ligne. Pour confirmer, " +
                "un nouveau Passed comparable pour chaque test, une preuve " +
                "référencée et les trois attestations sont obligatoires.",
                "Revalidation REC · contrôle humain",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Disposition = choice.Value;
        Actor = actor;
        Reason = reason;
        Evidence = evidence;
        TargetEnvironment = environment;
        TargetVersion = version;
        SourcesChecked = SourcesCheck.IsChecked == true;
        EvidenceChecked = EvidenceCheck.IsChecked == true;
        ScopeChecked = ScopeCheck.IsChecked == true;
        DialogResult = true;
    }

    /// <summary>Rejects empty, multiline or overly long audit values.</summary>
    /// <param name="value">User-entered human field.</param>
    /// <param name="length">Maximum allowed characters.</param>
    /// <returns>Whether safe to include in an audit record.</returns>
    private static bool ValidLine(string value, int length) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= length && !value.Any(char.IsControl);
}
