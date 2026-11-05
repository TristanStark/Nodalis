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
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    /// <summary>Displays a project-local anomaly list with optional human audit.</summary>
    /// <param name="overview">Unmodified canonical source projection.</param>
    /// <param name="workspaceRoot">Workspace root for revision-checked audit.</param>
    /// <param name="projectDirectory">Current project folder.</param>
    /// <param name="readOnly">Archive or workspace read-only state.</param>
    public ProjectRecAnomaliesDialog(ProjectRecAnomalyOverview overview,
        string workspaceRoot, string projectDirectory, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(overview);
        _overview = overview;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _readOnly = readOnly;
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

    /// <summary>Whether any deliberate human decision was durably saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Shows the separate incident-centric assessment
    /// precomputed from the same loaded REC and maintenance snapshot.
    /// </summary>
    /// <param name="sender">Human consultative navigation button.</param>
    /// <param name="e">Button click event.</param>
    private void Followup_Click(object sender, RoutedEventArgs e)
    {
        ProjectRecIncidentFollowupDialog dialog = new(
            _overview.IncidentFollowups, _workspaceRoot,
            _projectDirectory, _readOnly)
        { Owner = this };
        dialog.ShowDialog();
        Changed |= dialog.Changed;
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
