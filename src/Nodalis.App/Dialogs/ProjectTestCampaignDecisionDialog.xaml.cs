using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tests;

namespace Nodalis.App.Dialogs;

/// <summary>
/// One human-reviewed REC decision. Warnings are never a hidden blocker;
/// verdict, reserves, author and rationale are explicit fields.
/// </summary>
public partial class ProjectTestCampaignDecisionDialog : Window
{
    private readonly ProjectTestCampaignDecisionKind _kind;

    /// <summary>Creates a reviewed decision form from the current snapshot.</summary>
    /// <param name="kind">Requested explicit REC transition.</param>
    /// <param name="campaignLabel">Human-readable campaign, not source ID.</param>
    /// <param name="warnings">Read-only nonblocking qualification warnings.</param>
    public ProjectTestCampaignDecisionDialog(
        ProjectTestCampaignDecisionKind kind,
        string campaignLabel, IReadOnlyList<string> warnings)
    {
        _kind = kind;
        InitializeComponent();
        HeadingText.Text = kind switch
        {
            ProjectTestCampaignDecisionKind.EnterQualification =>
                "Soumettre la campagne à qualification",
            ProjectTestCampaignDecisionKind.Close =>
                "Clôturer la campagne avec décision humaine",
            ProjectTestCampaignDecisionKind.Reopen =>
                "Réouvrir la campagne",
            ProjectTestCampaignDecisionKind.Cancel =>
                "Annuler la campagne",
            _ => "Décision de campagne"
        };
        ContextText.Text = campaignLabel +
            " · La décision et sa justification seront historisées sans " +
            "modifier les résultats des passages.";
        WarningsBox.Text = warnings.Count == 0
            ? "Aucun avertissement calculé."
            : string.Join(Environment.NewLine,
                warnings.Select(item => "• " + item));
        bool closing = kind == ProjectTestCampaignDecisionKind.Close;
        VerdictGroup.Visibility = closing
            ? Visibility.Visible : Visibility.Collapsed;
        ReservationsGroup.Visibility = closing
            ? Visibility.Visible : Visibility.Collapsed;
        VerdictCombo.SelectedIndex = 0;
        Loaded += (_, _) => ActorBox.Focus();
    }

    /// <summary>Human actor.</summary>
    public string SelectedActor { get; private set; } = string.Empty;

    /// <summary>Required justification.</summary>
    public string SelectedRationale { get; private set; } = string.Empty;

    /// <summary>Explicit closure verdict; null for all other events.</summary>
    public ProjectTestCampaignVerdict? SelectedVerdict { get; private set; }

    /// <summary>Written, non-inferred human reservations.</summary>
    public IReadOnlyList<string> SelectedReservations { get; private set; } = [];

    /// <summary>Prevents accidental writing of irrelevant reservations.</summary>
    /// <param name="sender">Verdict selection.</param>
    /// <param name="e">Selection event.</param>
    private void VerdictCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (ReservationsGroup is not null)
        {
            ReservationsBox.IsEnabled = VerdictCombo.SelectedIndex != 0;
        }
    }

    /// <summary>Accepts only a consistent reviewed human decision.</summary>
    /// <param name="sender">Confirm button.</param>
    /// <param name="e">Click event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string actor = ActorBox.Text.Trim();
        string rationale = RationaleBox.Text.Trim();
        if (!SingleLine(actor, 160) || !SingleLine(rationale, 2000))
        {
            MessageBox.Show(this,
                "Indiquez le décideur et une justification sur une seule ligne.",
                "Décision REC incomplète",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectTestCampaignVerdict? verdict = null;
        string[] reservations = [];
        if (_kind == ProjectTestCampaignDecisionKind.Close)
        {
            if (VerdictCombo.SelectedIndex is < 0 or > 2)
            {
                MessageBox.Show(this, "Choisissez un verdict explicite.",
                    "Clôture incomplète", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            verdict = (ProjectTestCampaignVerdict)VerdictCombo.SelectedIndex;
            reservations = ReservationsBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0).ToArray();
            if (reservations.Length > 50 ||
                reservations.Any(item => !SingleLine(item, 500)) ||
                verdict == ProjectTestCampaignVerdict.ValidatedWithReservations
                    && reservations.Length == 0 ||
                verdict == ProjectTestCampaignVerdict.Validated
                    && reservations.Length > 0)
            {
                MessageBox.Show(this,
                    "Pour une clôture avec réserves, renseignez au moins " +
                    "une réserve (maximum 50, 500 caractères par ligne). " +
                    "Une validation sans réserves ne peut en conserver.",
                    "Réserves incohérentes",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        SelectedActor = actor;
        SelectedRationale = rationale;
        SelectedVerdict = verdict;
        SelectedReservations = reservations;
        DialogResult = true;
    }

    /// <summary>Checks one nonempty human one-line field.</summary>
    /// <param name="value">Human entry.</param>
    /// <param name="max">Allowed length.</param>
    /// <returns>Whether it is valid.</returns>
    private static bool SingleLine(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && !value.Any(char.IsControl);
}
