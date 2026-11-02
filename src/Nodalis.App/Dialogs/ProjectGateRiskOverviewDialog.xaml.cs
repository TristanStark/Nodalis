using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// In-memory filtered project-wide Gate/reservation/RIS dashboard. Source I/O
/// happens once in the parent on explicit user command, never during painting.
/// </summary>
public partial class ProjectGateRiskOverviewDialog : Window
{
    private readonly ProjectGateRiskOverview _overview;
    private bool _painting;

    /// <summary>Shows one immutable joined snapshot without source writes.</summary>
    /// <param name="overview">Source-validated project-wide projection.</param>
    public ProjectGateRiskOverviewDialog(ProjectGateRiskOverview overview)
    {
        _overview = overview ??
            throw new ArgumentNullException(nameof(overview));
        InitializeComponent();
        OpenCountText.Text = "Ouvertes : " + overview.OpenCount;
        LiftedCountText.Text = "Levées : " + overview.LiftedCount;
        WithoutRiskCountText.Text =
            "Ouvertes sans RIS : " + overview.OpenWithoutRiskCount;
        OrphanCountText.Text =
            "Ouvertes avec RIS orphelin : " + overview.OpenOrphanCount;
        BlockedCountText.Text =
            "Ouvertes avec mesure bloquée : " + overview.OpenBlockedCount;
        UnassessedCountText.Text =
            "Ouvertes non évaluées : " + overview.OpenUnassessedCount;
        FilterCombo.SelectedIndex = 0;
        RefreshRows();
    }

    /// <summary>Selected immutable Gate GUID for existing history navigation.</summary>
    public Guid? SelectedGateId { get; private set; }

    /// <summary>Original frozen Gate decision identity.</summary>
    public Guid? SelectedDecisionId { get; private set; }

    /// <summary>Original reservation GUID under that decision.</summary>
    public Guid? SelectedReservationId { get; private set; }

    /// <summary>Source-verified Markdown RIS path, never synthesized from GUID.</summary>
    public string? SelectedRiskPath { get; private set; }

    /// <summary>Filters an already built set; no directory or Worktree scans.</summary>
    private void RefreshRows()
    {
        bool old = _painting;
        _painting = true;
        try
        {
            Guid? selectedId =
                (ReservationsList.SelectedItem as ProjectGateRiskOverviewRow)?
                    .Reservation.ReservationId;
            int filter = FilterCombo.SelectedIndex;
            string term = SearchBox.Text.Trim();
            ProjectGateRiskOverviewRow[] rows = _overview.Rows.Where(row =>
                filter switch
                {
                    1 => row.Reservation.IsOpen,
                    2 => !row.Reservation.IsOpen,
                    3 => row.Reservation.IsOpen && row.Risks.Count == 0,
                    4 => row.Reservation.IsOpen && row.OrphanCount > 0,
                    5 => row.Reservation.IsOpen && row.HasBlockedMeasures,
                    6 => row.Reservation.IsOpen && row.HasUnassessedRisks,
                    7 => row.Reservation.IsOpen && row.HasUnfinishedMeasures,
                    _ => true
                }).Where(row => term.Length == 0 ||
                    row.Reservation.Description.Contains(term,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    row.Reservation.GateName.Contains(term,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    (row.Reservation.Owner?.Contains(term,
                        StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                    row.Risks.Any(risk =>
                        risk.Label.Contains(term,
                            StringComparison.CurrentCultureIgnoreCase)))
                .ToArray();
            ReservationsList.ItemsSource = rows;
            ReservationsList.SelectedItem = rows.FirstOrDefault(item =>
                item.Reservation.ReservationId == selectedId) ??
                rows.FirstOrDefault();
            ResultsText.Text = rows.Length + "/" + _overview.Rows.Count +
                " réserve(s) · " + _overview.Diagnostics.Count +
                " diagnostic(s) de source · lecture seule";
            RenderSelection();
        }
        finally
        {
            _painting = old;
        }
    }

    /// <summary>Displays a reservation and all of its current RIS references.</summary>
    private void RenderSelection()
    {
        if (ReservationsList.SelectedItem is not ProjectGateRiskOverviewRow row)
        {
            LinkedCountText.Text = "Aucune réserve sélectionnée.";
            RisksList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            OpenGateButton.IsEnabled = false;
            OpenRiskButton.IsEnabled = false;
            return;
        }

        LinkedCountText.Text = row.Risks.Count + " RIS lié(s) · " +
            row.OrphanCount + " source(s) absente(s)/ambiguë(s) · " +
            row.HistoricalUnlinks + " événement(s) de dissociation";
        RisksList.ItemsSource = row.Risks;
        RisksList.SelectedIndex = row.Risks.Count == 0 ? -1 : 0;
        ProjectGateReservationOverviewRow origin = row.Reservation;
        DetailsBox.Text = string.Join(Environment.NewLine,
        [
            "Gate : " + origin.GateName,
            "Gate GUID : " + origin.GateId.ToString("D"),
            "Décision GUID : " + origin.DecisionId.ToString("D"),
            "Verdict figé : " + origin.HistoricalVerdict,
            "Réserve GUID : " + origin.ReservationId.ToString("D"),
            "État actuel : " + (origin.IsOpen ? "OUVERTE" : "LEVÉE"),
            "Échéance : " +
                (origin.DueDate?.ToString("dd/MM/yyyy") ?? "non fixée"),
            "Responsable : " + (origin.Owner ?? "non désigné"),
            "Événements de levée/réouverture : " + origin.EventCount,
            "Liens RIS actifs : " + row.Risks.Count,
            "RIS non résolus : " + row.OrphanCount,
            "Liens RIS dissociés dans l'historique : " +
                row.HistoricalUnlinks,
            "Mesures bloquées (RIS résolus) : " +
                row.Risks.Sum(risk => risk.BlockedMeasures),
            "Les références de tâches ne prouvent aucune complétion."
        ]);
        OpenGateButton.IsEnabled = true;
        UpdateRiskButton();
    }

    /// <summary>Shows navigation only for a physically resolved RIS path.</summary>
    private void UpdateRiskButton()
    {
        if (OpenRiskButton is not null)
        {
            OpenRiskButton.IsEnabled =
                RisksList.SelectedItem is ProjectGateRiskOverviewRiskRow
                { RelativePath: not null };
        }
    }

    /// <summary>Updates only the local search results.</summary>
    /// <param name="sender">Search input.</param>
    /// <param name="e">WPF change event.</param>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_painting && ReservationsList is not null)
        {
            RefreshRows();
        }
    }

    /// <summary>Applies the selected in-memory source/state filter.</summary>
    /// <param name="sender">Filter combo.</param>
    /// <param name="e">WPF selection event.</param>
    private void FilterCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting && ReservationsList is not null)
        {
            RefreshRows();
        }
    }

    /// <summary>Renders one selected reservation without I/O.</summary>
    /// <param name="sender">Reservation selection.</param>
    /// <param name="e">WPF selection event.</param>
    private void ReservationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderSelection();
        }
    }

    /// <summary>Updates the risk navigation control on row selection.</summary>
    /// <param name="sender">Linked RIS list.</param>
    /// <param name="e">WPF event.</param>
    private void RisksList_SelectionChanged(
        object sender, SelectionChangedEventArgs e) =>
        UpdateRiskButton();

    /// <summary>Double-click on a resolved risk opens its source.</summary>
    /// <param name="sender">RIS list.</param>
    /// <param name="e">Mouse event.</param>
    private void RisksList_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) =>
        OpenSelectedRisk();

    /// <summary>Returns frozen origin IDs to the normal Gate editor.</summary>
    /// <param name="sender">Navigate Gate button.</param>
    /// <param name="e">WPF click.</param>
    private void OpenGate_Click(object sender, RoutedEventArgs e)
    {
        if (ReservationsList.SelectedItem is not ProjectGateRiskOverviewRow row)
        {
            return;
        }

        SelectedGateId = row.Reservation.GateId;
        SelectedDecisionId = row.Reservation.DecisionId;
        SelectedReservationId = row.Reservation.ReservationId;
        DialogResult = true;
    }

    /// <summary>Opens only a canonical source path from the loaded catalog.</summary>
    /// <param name="sender">Navigate risk button.</param>
    /// <param name="e">WPF click.</param>
    private void OpenRisk_Click(object sender, RoutedEventArgs e) =>
        OpenSelectedRisk();

    /// <summary>Returns source for safe resolution in the existing editor.</summary>
    private void OpenSelectedRisk()
    {
        if (RisksList.SelectedItem is ProjectGateRiskOverviewRiskRow
            { RelativePath: string relativePath })
        {
            SelectedRiskPath = relativePath;
            DialogResult = true;
        }
    }

    /// <summary>Closes without selecting or writing to a source.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF routed event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
