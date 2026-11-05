using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tests;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only virtualized presentation of calculated REC/maintenance anomalies.
/// </summary>
public partial class ProjectRecAnomaliesDialog : Window
{
    private readonly ProjectRecAnomalyOverview _overview;
    /// <summary>Displays a previously calculated, project-scoped overview.</summary>
    /// <param name="overview">Unmodified canonical source projection.</param>
    public ProjectRecAnomaliesDialog(ProjectRecAnomalyOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        _overview = overview;
        InitializeComponent();
        FollowupButton.IsEnabled = overview.IncidentFollowups.Count > 0;
        SummaryText.Text = overview.Rows.Count + " résultat(s) non conforme(s)" +
            " ou bloqué(s) historisé(s) · " + overview.UnlinkedCount +
            " sans incident actif · " + overview.MissingSourceCount +
            " avec source INC absente/ambiguë.";
        DiagnosticsBox.Text = overview.Diagnostics.Count == 0
            ? "Aucun avertissement de lecture."
            : string.Join(Environment.NewLine, overview.Diagnostics);
        AnomaliesList.ItemsSource = overview.Rows;
        AnomaliesList.SelectedIndex = overview.Rows.Count > 0 ? 0 : -1;
        if (overview.Rows.Count == 0)
        {
            DetailsBox.Text = "Aucun résultat REC historique Non conforme " +
                "ou Bloqué dans les campagnes lisibles du projet. Cela ne " +
                "prouve pas que les tests sont conformes ou exécutés.";
        }
    }

    /// <summary>
    /// Shows the separate incident-centric, strictly read-only assessment
    /// precomputed from the same loaded REC and maintenance snapshot.
    /// </summary>
    /// <param name="sender">Human consultative navigation button.</param>
    /// <param name="e">Button click event.</param>
    private void Followup_Click(object sender, RoutedEventArgs e)
    {
        ProjectRecIncidentFollowupDialog dialog = new(
            _overview.IncidentFollowups)
        { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>Shows original event, source links and missing information.</summary>
    /// <param name="sender">Historical results selection.</param>
    /// <param name="e">Changed selection.</param>
    private void Anomalies_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (AnomaliesList.SelectedItem is not ProjectRecAnomalyRow row)
        {
            return;
        }

        List<string> lines =
        [
            "Campagne : " + row.CampaignReference,
            "Passage : " + row.PassageNumber +
                " · " + row.PassageId.ToString("D"),
            "Test : " + row.TestReference +
                " · " + row.TestId.ToString("D"),
            "Environnement : " + row.Environment,
            "Version : " + row.ApplicationVersion,
            "Résultat enregistré le : " + row.RecordedUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm"),
            "VERDICT INITIAL : " + row.OriginalOutcome,
            "Rectification administrative : " +
                (row.RectifiedOutcome?.ToString() ?? "aucune"),
            "Preuve déclarée (NON vérifiée) : " +
                (row.DeclaredEvidence ?? "aucune"),
            "",
            "INCIDENTS ACTIFS : " + row.LinkedIncidentIds.Count,
            row.IncidentDescriptions.Count == 0
                ? "Aucun incident actuellement associé."
                : string.Join(Environment.NewLine, row.IncidentDescriptions),
            "",
            "Sources INC absentes/ambiguës : " +
                row.MissingIncidentIds.Count,
            "Interventions liées (déclarations de maintenance) : " +
                row.InterventionCount,
            "",
            "IMPORTANT : ni intervention terminée ni ancien résultat " +
                "Conforme ne vaut résolution vérifiée.",
            "Une nouvelle exécution et une décision humaine sont requises."
        ];
        DetailsBox.Text = string.Join(Environment.NewLine, lines);
    }
}
