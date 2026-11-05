using System.Windows;
using System.Windows.Controls;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human selection of an existing canonical incident; the picker never
/// creates, links or closes any maintenance or REC record.
/// </summary>
public partial class ProjectIncidentRetestSelectionDialog : Window
{
    /// <summary>Lists project-local incident candidates without mutation.</summary>
    /// <param name="catalog">Verified maintenance incident catalogue.</param>
    public ProjectIncidentRetestSelectionDialog(ProjectGateIncidentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        InitializeComponent();
        SummaryText.Text = catalog.Candidates.Count +
            " incident(s) identifié(s) · " + catalog.ExcludedCount +
            " incident(s) incomplet(s) ou ambigu(s). Choisissez un " +
            "incident pour inspecter ses associations REC historiques.";
        IncidentsList.ItemsSource = catalog.Candidates;
        ConfirmButton.IsEnabled = false;
    }

    /// <summary>Exact human-selected maintenance incident GUID code.</summary>
    public string? SelectedIncidentId { get; private set; }

    /// <summary>Allows proceeding only with a real listed candidate.</summary>
    /// <param name="sender">Incident list.</param>
    /// <param name="e">Current list selection.</param>
    private void Incidents_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (ConfirmButton is not null)
        {
            ConfirmButton.IsEnabled =
                IncidentsList.SelectedItem is ProjectGateIncidentCandidate;
        }
    }

    /// <summary>Returns the exact chosen code, never a fuzzy title.</summary>
    /// <param name="sender">Human confirmation button.</param>
    /// <param name="e">Click.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (IncidentsList.SelectedItem is ProjectGateIncidentCandidate incident)
        {
            SelectedIncidentId = incident.Id;
            DialogResult = true;
        }
    }
}
