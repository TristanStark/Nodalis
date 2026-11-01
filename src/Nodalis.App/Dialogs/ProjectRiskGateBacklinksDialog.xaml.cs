using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only RIS → frozen Gate reservation associations. Opening a Gate is
/// delegated to the existing project journey instead of inventing a document.
/// </summary>
public partial class ProjectRiskGateBacklinksDialog : Window
{
    /// <summary>Projects one immutable read snapshot in memory.</summary>
    /// <param name="risk">Canonical project RIS selected by the user.</param>
    /// <param name="catalog">Verified project-local Gate backlinks.</param>
    public ProjectRiskGateBacklinksDialog(
        ProjectRiskEntry risk, ProjectRiskGateBacklinkCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(risk);
        ArgumentNullException.ThrowIfNull(catalog);
        InitializeComponent();
        HeadingText.Text = "Réserves Gates · " + risk.Card.Reference;
        SummaryText.Text = risk.Card.Title + Environment.NewLine +
            "La relation, le verdict humain et la levée de la réserve " +
            "ont des états indépendants. Un lien supprimé reste dans " +
            "l'historique et n'est pas réactivé automatiquement.";
        BacklinksList.ItemsSource = catalog.Links;
        BacklinksList.SelectedIndex = catalog.Links.Count == 0 ? -1 : 0;
        DiagnosticsText.Text = catalog.Diagnostics.Count == 0
            ? "Aucun document diagnostiqué."
            : catalog.Diagnostics.Count + " avertissement(s) documentaire(s). " +
              string.Join(Environment.NewLine, catalog.Diagnostics.Take(3));
        StatusText.Text = catalog.Links.Count + " relation(s) à une réserve.";
        OpenGateButton.IsEnabled = catalog.RiskId is not null &&
            BacklinksList.SelectedItem is ProjectRiskGateBacklink;
    }

    /// <summary>Stable Gate selected for journey navigation.</summary>
    public Guid? SelectedGateId { get; private set; }

    /// <summary>Frozen originating decision selected by the user.</summary>
    public Guid? SelectedDecisionId { get; private set; }

    /// <summary>Specific historical reservation ID.</summary>
    public Guid? SelectedReservationId { get; private set; }

    /// <summary>Enables the destination command only for real backlinks.</summary>
    /// <param name="sender">Selection source.</param>
    /// <param name="e">WPF event.</param>
    private void Backlinks_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (OpenGateButton is not null)
        {
            OpenGateButton.IsEnabled =
                BacklinksList.SelectedItem is ProjectRiskGateBacklink;
        }
    }

    /// <summary>Returns IDs to the Gate registry without changing files.</summary>
    /// <param name="sender">Open Gate button.</param>
    /// <param name="e">WPF click.</param>
    private void OpenGate_Click(object sender, RoutedEventArgs e)
    {
        if (BacklinksList.SelectedItem is not ProjectRiskGateBacklink row)
        {
            return;
        }

        SelectedGateId = row.GateId;
        SelectedDecisionId = row.DecisionId;
        SelectedReservationId = row.ReservationId;
        DialogResult = true;
    }

    /// <summary>Opens a selected frozen decision by double-click.</summary>
    /// <param name="sender">List control.</param>
    /// <param name="e">WPF mouse event.</param>
    private void Backlinks_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) =>
        OpenGate_Click(sender, e);

    /// <summary>Closes without selecting or modifying anything.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
