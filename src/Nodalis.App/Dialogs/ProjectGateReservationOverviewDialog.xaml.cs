using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Project-wide, read-only reservation dashboard. Filtering and selecting
/// reservations uses an already loaded registry; it never scans the worktree,
/// writes a gate or changes the current human approval.
/// </summary>
public partial class ProjectGateReservationOverviewDialog : Window
{
    private readonly ProjectGateReservationOverview _overview;
    private bool _painting;

    /// <summary>Creates a passive dashboard from one canonical snapshot.</summary>
    /// <param name="overview">Validated project-wide reservation projection.</param>
    public ProjectGateReservationOverviewDialog(
        ProjectGateReservationOverview overview)
    {
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        InitializeComponent();
        OpenCountText.Text = "Ouvertes : " + overview.OpenCount;
        LiftedCountText.Text = "Levées : " + overview.LiftedCount;
        OverdueCountText.Text = "En retard : " + overview.OverdueCount;
        OwnerCountText.Text = "Sans responsable : " + overview.MissingOwnerCount;
        FilterCombo.SelectedIndex = 0;
        RefreshRows();
    }

    /// <summary>The selected canonical gate after navigation confirmation.</summary>
    public Guid? SelectedGateId { get; private set; }

    /// <summary>The historical decision owning the selected reservation.</summary>
    public Guid? SelectedDecisionId { get; private set; }

    /// <summary>The stable reservation to highlight in its original dossier.</summary>
    public Guid? SelectedReservationId { get; private set; }

    /// <summary>Rebuilds a small in-memory filtered list, with no filesystem IO.</summary>
    private void RefreshRows()
    {
        bool previous = _painting;
        _painting = true;
        try
        {
            Guid? selectedId =
                (ReservationsList.SelectedItem as ReservationDisplayRow)?.Row.ReservationId;
            string query = SearchBox.Text.Trim();
            int filter = FilterCombo.SelectedIndex;
            ProjectGateReservationOverviewRow[] rows = _overview.Rows
                .Where(item => filter switch
                {
                    1 => item.IsOpen,
                    2 => !item.IsOpen,
                    3 => item.IsOverdue,
                    4 => item.IsOpen && string.IsNullOrWhiteSpace(item.Owner),
                    _ => true
                })
                .Where(item => query.Length == 0 ||
                    item.Description.Contains(query,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    item.GateName.Contains(query,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Owner?.Contains(query,
                        StringComparison.CurrentCultureIgnoreCase) ?? false))
                .ToArray();

            ReservationDisplayRow[] displayed = rows.Select(item =>
                new ReservationDisplayRow(
                    item,
                    (item.IsOpen ? "OUVERTE · " : "LEVÉE · ") + item.Description,
                    item.GateName + " · " +
                    (item.Owner is null ? "Responsable non indiqué" :
                        item.Owner) +
                    (item.DueDate is DateOnly date
                        ? " · échéance " + date.ToString("dd/MM/yyyy") : "") +
                    (item.IsOverdue ? " · EN RETARD" : ""))).ToArray();
            ReservationsList.ItemsSource = displayed;
            ReservationsList.SelectedItem = displayed.FirstOrDefault(item =>
                item.Row.ReservationId == selectedId) ?? displayed.FirstOrDefault();
            ResultsText.Text = displayed.Length + " réserve(s) affichée(s) sur " +
                _overview.Rows.Count + " · lecture seule";
            RenderDetails();
        }
        finally
        {
            _painting = previous;
        }
    }

    /// <summary>Shows the historical decision, last event and due date.</summary>
    private void RenderDetails()
    {
        if (ReservationsList.SelectedItem is not ReservationDisplayRow selected)
        {
            DetailsBox.Text = "Aucune réserve correspondant aux filtres.";
            NavigateButton.IsEnabled = false;
            return;
        }

        ProjectGateReservationOverviewRow row = selected.Row;
        List<string> lines =
        [
            "Gate : " + row.GateName,
            "Identifiant du gate : " + row.GateId.ToString("D"),
            "Décision : " + row.DecisionId.ToString("D"),
            "Verdict historique : " + row.HistoricalVerdict,
            "Date décision : " + row.DecisionRecordedUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm"),
            "Réserve : " + row.ReservationId.ToString("D"),
            "Statut ACTUEL : " + (row.IsOpen ? "OUVERTE" : "LEVÉE"),
            "Description : " + row.Description,
            "Responsable : " + (row.Owner ?? "non précisé"),
            "Échéance : " +
                (row.DueDate?.ToString("dd/MM/yyyy") ?? "non précisée"),
            "Événements humains : " + row.EventCount
        ];
        if (row.LatestEvent is ProjectGateReservationEvent change)
        {
            lines.Add("Dernier événement : " + change.Kind + " · " +
                change.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            lines.Add("Auteur : " + change.Actor);
            lines.Add("Motif : " + change.Rationale);
            lines.Add("Preuves figées : " + change.Evidence.Count);
        }

        DetailsBox.Text = string.Join(Environment.NewLine, lines);
        NavigateButton.IsEnabled = true;
    }

    /// <summary>Updates the list when a filter changes.</summary>
    /// <param name="sender">Filter.</param>
    /// <param name="e">Selection changed.</param>
    private void FilterCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting && ReservationsList is not null)
        {
            RefreshRows();
        }
    }

    /// <summary>Updates the list when text changes.</summary>
    /// <param name="sender">Search input.</param>
    /// <param name="e">Text changed.</param>
    private void SearchBox_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        if (!_painting && ReservationsList is not null)
        {
            RefreshRows();
        }
    }

    /// <summary>Renders a selection without rebuilding the catalog.</summary>
    /// <param name="sender">Reservation list.</param>
    /// <param name="e">Selection changed.</param>
    private void ReservationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderDetails();
        }
    }

    /// <summary>Opens one explicitly selected historical dossier.</summary>
    /// <param name="sender">Reservation list.</param>
    /// <param name="e">Double click.</param>
    private void ReservationsList_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) => Navigate();

    /// <summary>Requests navigation without changing reservation state.</summary>
    /// <param name="sender">Navigation button.</param>
    /// <param name="e">Click event.</param>
    private void NavigateButton_Click(object sender, RoutedEventArgs e) =>
        Navigate();

    /// <summary>Returns the exact gate, decision and reservation identities.</summary>
    private void Navigate()
    {
        if (ReservationsList.SelectedItem is not ReservationDisplayRow selected)
        {
            return;
        }

        SelectedGateId = selected.Row.GateId;
        SelectedDecisionId = selected.Row.DecisionId;
        SelectedReservationId = selected.Row.ReservationId;
        DialogResult = true;
    }

    /// <summary>Dismisses the read-only overview.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record ReservationDisplayRow(
        ProjectGateReservationOverviewRow Row, string Label, string Detail);
}
