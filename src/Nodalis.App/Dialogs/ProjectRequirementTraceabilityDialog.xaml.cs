using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only, project-confined EXI/CA/test-link matrix. All disk and index
/// work happens before the window is created; filtering is in-memory only.
/// </summary>
public partial class ProjectRequirementTraceabilityDialog : Window
{
    private readonly ProjectRequirementTraceabilityOverview _overview;
    private readonly ProjectRequirementCampaignOverview _campaigns;
    private readonly Dictionary<ProjectRequirementTraceabilityRow,
        ProjectRequirementCampaignCriterionHistory> _campaignRows;
    private bool _painting;
    private string _criterionDetails = string.Empty;

    /// <summary>Creates a passive matrix from checked project snapshots.</summary>
    /// <param name="overview">Verified current EXI/CA/test links.</param>
    /// <param name="campaigns">Historical REC passages, never an EXI verdict.</param>
    public ProjectRequirementTraceabilityDialog(
        ProjectRequirementTraceabilityOverview overview,
        ProjectRequirementCampaignOverview campaigns)
    {
        _overview = overview ??
            throw new ArgumentNullException(nameof(overview));
        _campaigns = campaigns ??
            throw new ArgumentNullException(nameof(campaigns));
        _campaignRows = new(ReferenceEqualityComparer.Instance);
        foreach (ProjectRequirementCampaignCriterionHistory history
            in campaigns.Rows)
        {
            _campaignRows.Add(history.Criterion, history);
        }
        InitializeComponent();

        TotalText.Text = overview.Rows.Count + " critère(s)";
        LinkedText.Text = overview.LinkedCount + " avec test lié";
        UnresolvedText.Text = overview.UnresolvedCount +
            " identité(s) à examiner";
        DiagnosticsCountText.Text =
            (overview.Diagnostics.Count + campaigns.Diagnostics.Count) +
            " diagnostic(s) de source";
        HistoricalText.Text = campaigns.PassageTestCount +
            " sélection(s) de test dans REC · " +
            campaigns.DeclaredResultCount + " verdict(s) humain(s)";
        FilterCombo.SelectedIndex = 0;
        RefreshRows();
    }

    /// <summary>Existing workspace-relative EXI file selected for navigation.</summary>
    public string? SelectedRequirementPath { get; private set; }

    /// <summary>
    /// Exact stable test block selected for re-resolution in the main window.
    /// No stored line number is blindly trusted after the dialog closes.
    /// </summary>
    public Guid? SelectedTestBlockId { get; private set; }

    /// <summary>Filters a loaded snapshot without scanning files or indexes.</summary>
    private void RefreshRows()
    {
        _painting = true;
        try
        {
            string? previous = (CriteriaList.SelectedItem as DisplayRow)?
                .Identity;
            string query = SearchBox.Text.Trim();
            int filter = FilterCombo.SelectedIndex;
            DisplayRow[] candidates = _overview.Rows.Where(row =>
                filter switch
                {
                    1 => row.Coverage == ProjectRequirementCoverage.LinkedTest,
                    2 => row.Coverage == ProjectRequirementCoverage.Uncovered,
                    3 => row.Coverage is ProjectRequirementCoverage.Unidentified or
                        ProjectRequirementCoverage.StaleSource,
                    _ => true
                }).Where(row => query.Length == 0 ||
                row.Requirement.Card.Reference.Contains(query,
                    StringComparison.CurrentCultureIgnoreCase) ||
                row.Requirement.Card.Title.Contains(query,
                    StringComparison.CurrentCultureIgnoreCase) ||
                row.Criterion.Reference.Contains(query,
                    StringComparison.CurrentCultureIgnoreCase) ||
                row.Criterion.Title.Contains(query,
                    StringComparison.CurrentCultureIgnoreCase))
                .Select(row => new DisplayRow(
                    row,
                    row.Requirement.Card.Reference + " / " +
                        row.Criterion.Reference,
                    row.Requirement.Card.Reference + " · " +
                        row.Criterion.Reference + " — " + row.Criterion.Title,
                    DescribeCoverage(row.Coverage) + " · " +
                        row.Tests.Count + " test(s) lié(s) · " +
                        (_campaignRows.TryGetValue(row,
                            out ProjectRequirementCampaignCriterionHistory? history)
                            ? history.Tests.Sum(test => test.Occurrences.Count)
                            : 0) + " passage(s) REC"))
                .ToArray();

            CriteriaList.ItemsSource = candidates;
            CriteriaList.SelectedItem = candidates.FirstOrDefault(row =>
                row.Identity == previous) ?? candidates.FirstOrDefault();
            StatusText.Text = candidates.Length + "/" +
                _overview.Rows.Count + " critère(s) affiché(s) · lecture seule";
            RenderSelection();
        }
        finally
        {
            _painting = false;
        }
    }

    /// <summary>Explains the selected criterion without assuming execution.</summary>
    private void RenderSelection()
    {
        if (CriteriaList.SelectedItem is not DisplayRow selected)
        {
            TestsList.ItemsSource = null;
            _criterionDetails = _overview.Rows.Count == 0
                ? "Aucun critère identifié dans les exigences de ce projet.\n" +
                  "Les anciens fichiers ne sont pas migrés automatiquement."
                : "Aucun critère ne correspond au filtre.";
            DetailsBox.Text = _criterionDetails;
            OpenRequirementButton.IsEnabled = false;
            OpenTestButton.IsEnabled = false;
            return;
        }

        ProjectRequirementTraceabilityRow row = selected.Row;
        TestsList.ItemsSource = row.Tests;
        TestsList.SelectedIndex = row.Tests.Count == 0 ? -1 : 0;
        List<string> details =
        [
            "Exigence : " + row.Requirement.Card.Reference +
                " — " + row.Requirement.Card.Title,
            "Fiche : " + row.Requirement.RelativePath,
            "Critère : " + row.Criterion.Reference +
                " — " + row.Criterion.Title,
            "GUID : " + (row.Criterion.Id?.ToString("D") ?? "non attribué"),
            "Ligne : " + row.Criterion.HeadingLine,
            "Couverture de liens : " + DescribeCoverage(row.Coverage),
            "Tests identifiés liés : " + row.Tests.Count,
            "",
            "VALIDATION MÉTIER : " + row.Requirement.Card.Validation,
            "RÉALISATION : " + row.Requirement.Card.Implementation,
            "VÉRIFICATION : " + row.Requirement.Card.Verification,
            "EXÉCUTION : déclarations historiques REC disponibles ci-dessous.",
            "CONFORMITÉ : non déductible d'une relation ou d'un résultat.",
            "PREUVES : uniquement des notes déclaratives non vérifiées.",
            "SOURCE ACTUELLE : empreinte du test non vérifiée par cette vue."
        ];
        if (row.Diagnostic is not null)
        {
            details.Add("");
            details.Add("ATTENTION : " + row.Diagnostic);
        }

        if (_overview.Diagnostics.Count != 0)
        {
            details.Add("");
            details.Add("DIAGNOSTICS DE SOURCE (" +
                _overview.Diagnostics.Count + ") :");
            details.AddRange(_overview.Diagnostics.Take(12));
            if (_overview.Diagnostics.Count > 12)
            {
                details.Add("... et " +
                    (_overview.Diagnostics.Count - 12) +
                    " autre(s) diagnostic(s).");
            }
        }

        if (_campaigns.Diagnostics.Count != 0)
        {
            details.Add("");
            details.Add("DIAGNOSTICS DES CAMPAGNES REC (" +
                _campaigns.Diagnostics.Count + ") :");
            details.AddRange(_campaigns.Diagnostics.Take(12));
            if (_campaigns.Diagnostics.Count > 12)
            {
                details.Add("... et " +
                    (_campaigns.Diagnostics.Count - 12) +
                    " autre(s) diagnostic(s).");
            }
        }

        _criterionDetails = string.Join(Environment.NewLine, details);
        RenderTestHistory();
        OpenRequirementButton.IsEnabled = true;
        OpenTestButton.IsEnabled =
            TestsList.SelectedItem is ProjectRequirementTestSource;
    }

    /// <summary>
    /// Shows exact REC outcomes for the selected uniquely linked test block.
    /// Each passage stays separate; a previous Passed cannot qualify another
    /// environment, version or a newer passage without a human result.
    /// </summary>
    private void RenderTestHistory()
    {
        List<string> lines = [_criterionDetails];
        if (CriteriaList.SelectedItem is not DisplayRow selected ||
            TestsList.SelectedItem is not ProjectRequirementTestSource test ||
            !_campaignRows.TryGetValue(selected.Row,
                out ProjectRequirementCampaignCriterionHistory? row))
        {
            DetailsBox.Text = _criterionDetails;
            return;
        }

        ProjectRequirementCampaignTestHistory? history = row.Tests
            .FirstOrDefault(item => item.Test.BlockId == test.BlockId);
        lines.Add("");
        lines.Add("HISTORIQUE REC · " + test.HeadingTitle);
        if (history is null || history.Occurrences.Count == 0)
        {
            lines.Add("Aucun passage de campagne REC pour ce bloc.");
            lines.Add("Test lié ≠ test exécuté.");
        }
        else
        {
            lines.Add(history.Occurrences.Count +
                " sélection(s) figée(s), sans transfert de verdict.");
            lines.Add("Chaque résultat est propre à son passage, sa version" +
                " et son environnement.");
            foreach (ProjectRequirementCampaignOccurrence occurrence
                in history.Occurrences.Reverse().Take(20))
            {
                string outcome = occurrence.Outcome is ProjectTestOutcome declared
                    ? declared.ToString() + " (DÉCLARÉ)"
                    : "NON EXÉCUTÉ";
                lines.Add("");
                lines.Add(occurrence.CampaignReference + " · passage " +
                    occurrence.PassageNumber + " · " +
                    occurrence.StartedUtc.ToLocalTime().ToString(
                        "dd/MM/yyyy HH:mm"));
                lines.Add("Environnement : " + occurrence.Environment +
                    " · version : " + occurrence.ApplicationVersion);
                lines.Add("Résultat historique : " + outcome);
                lines.Add("SHA du test figé : " + occurrence.FrozenSha256);
                if (!occurrence.MatchesLinkedSource)
                {
                    lines.Add("SOURCE DÉPLACÉE : l'emplacement de définition" +
                        " a changé ; requalification nécessaire.");
                }
                else
                {
                    lines.Add("Source historique identifiée ; comparaison" +
                        " au contenu actuel NON effectuée ici.");
                }

                if (occurrence.Outcome.HasValue)
                {
                    lines.Add("Exécutant : " + occurrence.Actor);
                    lines.Add("Justification : " + occurrence.Rationale);
                    lines.Add("Preuve déclarée (NON VÉRIFIÉE) : " +
                        (occurrence.EvidenceNote ?? "aucune"));
                }
            }

            if (history.Occurrences.Count > 20)
            {
                lines.Add("");
                lines.Add("... et " + (history.Occurrences.Count - 20) +
                    " autre(s) passage(s) dans l'historique REC.");
            }
        }

        DetailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Readable link-only labels, never an execution verdict.</summary>
    /// <param name="coverage">Conservative identifier/link state.</param>
    /// <returns>French label.</returns>
    private static string DescribeCoverage(ProjectRequirementCoverage coverage) =>
        coverage switch
        {
            ProjectRequirementCoverage.LinkedTest => "Test lié · NON exécuté ici",
            ProjectRequirementCoverage.Uncovered => "Aucun test lié",
            ProjectRequirementCoverage.Unidentified => "Identité du critère à vérifier",
            ProjectRequirementCoverage.StaleSource => "Source ou index à actualiser",
            _ => "État inconnu"
        };

    /// <summary>Applies the local search query.</summary>
    /// <param name="sender">Search input.</param>
    /// <param name="e">Text change.</param>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_painting && CriteriaList is not null)
        {
            RefreshRows();
        }
    }

    /// <summary>Applies the selected local coverage filter.</summary>
    /// <param name="sender">Filter input.</param>
    /// <param name="e">Selection change.</param>
    private void FilterCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting && CriteriaList is not null)
        {
            RefreshRows();
        }
    }

    /// <summary>Refreshes selected details without any filesystem access.</summary>
    /// <param name="sender">Criterion list.</param>
    /// <param name="e">Selection change.</param>
    private void CriteriaList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderSelection();
        }
    }

    /// <summary>Updates only the test navigation button state.</summary>
    /// <param name="sender">Test list.</param>
    /// <param name="e">Selection change.</param>
    private void TestsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting && OpenTestButton is not null)
        {
            OpenTestButton.IsEnabled =
                TestsList.SelectedItem is ProjectRequirementTestSource;
            RenderTestHistory();
        }
    }

    /// <summary>Requests stable block navigation on double-click.</summary>
    /// <param name="sender">Test list.</param>
    /// <param name="e">Double-click.</param>
    private void TestsList_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) => NavigateTest();

    /// <summary>Requests the canonical EXI Markdown source.</summary>
    /// <param name="sender">Open requirement button.</param>
    /// <param name="e">Button event.</param>
    private void OpenRequirement_Click(object sender, RoutedEventArgs e)
    {
        if (CriteriaList.SelectedItem is not DisplayRow row)
        {
            return;
        }

        SelectedRequirementPath = row.Row.Requirement.RelativePath;
        DialogResult = true;
    }

    /// <summary>Requests test navigation without changing its state.</summary>
    /// <param name="sender">Open test button.</param>
    /// <param name="e">Button event.</param>
    private void OpenTest_Click(object sender, RoutedEventArgs e) =>
        NavigateTest();

    /// <summary>Returns the block GUID; caller revalidates it before opening.</summary>
    private void NavigateTest()
    {
        if (TestsList.SelectedItem is not ProjectRequirementTestSource test)
        {
            return;
        }

        SelectedTestBlockId = test.BlockId;
        DialogResult = true;
    }

    /// <summary>Closes without mutations or navigation.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Button event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record DisplayRow(
        ProjectRequirementTraceabilityRow Row,
        string Identity, string Title, string Summary);
}
