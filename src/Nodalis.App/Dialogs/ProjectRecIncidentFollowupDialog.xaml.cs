using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tests;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Incident-centric, read-only REC follow-up. All decisions and incident
/// state changes remain outside this report and require a human action.
/// </summary>
public partial class ProjectRecIncidentFollowupDialog : Window
{
    /// <summary>Displays calculated incident follow-up records in-memory.</summary>
    /// <param name="followups">Project-local canonical projections.</param>
    public ProjectRecIncidentFollowupDialog(
        IReadOnlyList<ProjectRecIncidentFollowup> followups)
    {
        ArgumentNullException.ThrowIfNull(followups);
        InitializeComponent();
        SummaryText.Text = followups.Count + " incident(s) associé(s) · " +
            followups.Count(item => item.ReviewMayBeProposed) +
            " proposition(s) d'examen humain (non approuvées).";
        IncidentsList.ItemsSource = followups;
        IncidentsList.SelectedIndex = followups.Count > 0 ? 0 : -1;
    }

    /// <summary>Renders last result, historic failure and technical context.</summary>
    /// <param name="sender">Incident selection list.</param>
    /// <param name="e">Selection changed.</param>
    private void Incidents_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (IncidentsList.SelectedItem is not ProjectRecIncidentFollowup row)
        {
            TestsText.Text = string.Empty;
            MaintenanceText.Text = string.Empty;
            return;
        }

        List<string> tests =
        [
            "INCIDENT : " + row.IncidentId,
            "Source maintenance : " +
                (row.MissingSource ? "ABSENTE OU AMBIGUË" : row.IncidentSummary),
            "",
            "TESTS DISTINCTS : " + row.AffectedTestCount,
            "Nouveaux Passed dans même environnement : " +
                row.LaterPassedCount,
            row.ReviewMayBeProposed
                ? "Proposition : examen humain possible, AUCUNE validation automatique."
                : "Proposition : revalidation encore non démontrée.",
            ""
        ];
        foreach (ProjectRecRetestObservation test in row.Tests)
        {
            tests.Add("TEST : " + test.Reference + " (" +
                test.TestId.ToString("D") + ")");
            tests.Add("Échec lié le " +
                test.FailureUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                " · " + test.FailureEnvironment + " / " +
                test.FailureVersion);
            tests.Add(test.Summary);
            tests.Add("Preuve de reprise (NON vérifiée) : " +
                (test.LaterEvidence ?? "aucune"));
            tests.Add("");
        }

        TestsText.Text = string.Join(Environment.NewLine, tests);
        List<string> maintenance =
        [
            "INTERVENTIONS TECHNIQUES ASSOCIÉES : " +
                row.InterventionSummaries.Count
        ];
        maintenance.AddRange(row.InterventionSummaries);
        if (row.InterventionSummaries.Count == 0)
        {
            maintenance.Add("Aucune intervention explicitement liée.");
        }

        maintenance.Add("");
        maintenance.Add("POINTS DE CONTRÔLE :");
        maintenance.AddRange(row.Warnings.Select(warning => "• " + warning));
        MaintenanceText.Text = string.Join(Environment.NewLine, maintenance);
    }
}
