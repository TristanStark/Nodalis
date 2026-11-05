using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Incident-centric, read-only REC follow-up. All decisions and incident
/// state changes remain outside this report and require a human action.
/// </summary>
public partial class ProjectRecIncidentFollowupDialog : Window
{
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private bool _saving;

    /// <summary>Displays project-local incident follow-ups with safe audit access.</summary>
    /// <param name="followups">Canonical, nonauthoritative projections.</param>
    /// <param name="workspaceRoot">Current workspace root.</param>
    /// <param name="projectDirectory">Owning project directory.</param>
    /// <param name="readOnly">Archived or read-only workspace state.</param>
    public ProjectRecIncidentFollowupDialog(
        IReadOnlyList<ProjectRecIncidentFollowup> followups,
        string workspaceRoot, string projectDirectory, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(followups);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _readOnly = readOnly;
        InitializeComponent();
        DecisionButton.IsEnabled = false;
        SummaryText.Text = followups.Count + " incident(s) associé(s) · " +
            followups.Count(item => item.ReviewMayBeProposed) +
            " proposition(s) d'examen humain (non approuvées).";
        IncidentsList.ItemsSource = followups;
        IncidentsList.SelectedIndex = followups.Count > 0 ? 0 : -1;
    }

    /// <summary>Whether an explicit human decision was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>
    /// Asks for explicit review, a scoped version and written attestations
    /// before a revision-checked, append-only human decision is saved.
    /// </summary>
    /// <param name="sender">Human revalidation action button.</param>
    /// <param name="e">Click event.</param>
    private async void Decision_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _saving ||
            IncidentsList.SelectedItem is not ProjectRecIncidentFollowup selected ||
            selected.MissingSource)
        {
            return;
        }

        _saving = true;
        DecisionButton.IsEnabled = false;
        try
        {
            ProjectRecRevalidationService service = new(_workspaceRoot);
            ProjectRecRevalidationPreview review = await Task.Run(() =>
                service.InspectAsync(_projectDirectory, selected.IncidentId));
            if (IncidentsList.SelectedItem is not ProjectRecIncidentFollowup now ||
                !string.Equals(now.IncidentId, selected.IncidentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ProjectRecRevalidationDecisionDialog form = new(review)
            {
                Owner = this
            };
            if (form.ShowDialog() != true ||
                form.Actor is not string actor ||
                form.Reason is not string reason ||
                form.Evidence is not string evidence ||
                form.TargetEnvironment is not string environment ||
                form.TargetVersion is not string version)
            {
                return;
            }

            if (MessageBox.Show(this,
                "CONFIRMER L'ENREGISTREMENT DE LA DÉCISION HUMAINE ?\n\n" +
                "Incident : " + selected.IncidentId + "\n" +
                "Décision : " + form.Disposition + "\n" +
                "Responsable : " + actor + "\n" +
                "Environnement : " + environment + "\n" +
                "Version : " + version + "\n" +
                "Motif : " + reason + "\n" +
                "Référence de preuve : " +
                    (evidence.Length > 0 ? evidence : "(aucune)") + "\n" +
                "Sources contrôlées : " + form.SourcesChecked + "\n" +
                "Preuves examinées : " + form.EvidenceChecked + "\n" +
                "Périmètre contrôlé : " + form.ScopeChecked + "\n\n" +
                "La fiche de maintenance doit être enregistrée dans " +
                "l'éditeur avant cette opération.\n" +
                "Un audit de décision sera AJOUTÉ, sans effacer les précédents. " +
                "Le statut INC et les verdicts REC restent inchangés.",
                "Revalidation REC · confirmation finale",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            ProjectRecRevalidationEvent saved = await Task.Run(() =>
                service.RecordAsync(_projectDirectory, review,
                    form.Disposition, actor, reason, evidence, environment,
                    version, form.SourcesChecked, form.EvidenceChecked,
                    form.ScopeChecked));
            Changed = true;
            WorkspaceLinkIndexService.NotifyWorkspaceChanged(_workspaceRoot);
            MaintenanceText.Text += "\n\nDÉCISION HUMAINE ENREGISTRÉE : " +
                saved.Disposition + "\n" +
                saved.Id.ToString("D") + " · " +
                saved.RecordedUtc.ToLocalTime()
                    .ToString("dd/MM/yyyy HH:mm") + "\n" +
                "Version : " + saved.TargetVersion +
                " / " + saved.TargetEnvironment +
                "\nATTENTION : cette décision ne clôture aucun incident.";
            MessageBox.Show(this,
                "Décision humaine " + saved.Disposition +
                " journalisée dans la Fiche de Maintenance.md.\n\n" +
                "Aucun incident n'a changé d'état automatiquement. " +
                "Actualisez les vues REC et maintenance pour voir " +
                "la nouvelle révision.",
                "Revalidation REC · audit sauvegardé",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            OperationCanceledException)
        {
            MessageBox.Show(this,
                "Décision non enregistrée. Actualisez les sources et " +
                "vérifiez la fiche de maintenance.\n\n" +
                exception.Message,
                "Revalidation REC · opération refusée",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _saving = false;
            DecisionButton.IsEnabled = !_readOnly &&
                IncidentsList.SelectedItem is ProjectRecIncidentFollowup item &&
                !item.MissingSource;
        }
    }

    /// <summary>Renders last result, historic failure and technical context.</summary>
    /// <param name="sender">Incident selection list.</param>
    /// <param name="e">Selection changed.</param>
    private void Incidents_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (IncidentsList.SelectedItem is not ProjectRecIncidentFollowup row)
        {
            DecisionButton.IsEnabled = false;
            TestsText.Text = string.Empty;
            MaintenanceText.Text = string.Empty;
            return;
        }

        DecisionButton.IsEnabled = !_readOnly && !_saving &&
            !row.MissingSource;
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
            "Dernière décision humaine : " +
                (row.LatestHumanDecision is ProjectRecRevalidationEvent last
                    ? last.Disposition + " · " +
                        last.RecordedUtc.ToLocalTime()
                            .ToString("dd/MM/yyyy HH:mm") +
                        " · " + last.Actor
                    : "aucune décision historisée"),
            row.LatestHumanDecision is not null
                ? (row.LatestDecisionUsesCurrentRec
                    ? "Décision liée à la révision REC actuellement relue."
                    : "ATTENTION : décision portant sur une ancienne photographie REC.")
                : "Une proposition d'examen n'est pas une décision.",
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
