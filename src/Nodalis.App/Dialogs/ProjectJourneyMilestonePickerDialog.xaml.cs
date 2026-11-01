using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Selects one existing, unambiguous, persisted milestone identity without
/// changing its status, source document or project dependencies.
/// </summary>
public partial class ProjectJourneyMilestonePickerDialog : Window
{
    /// <summary>Initializes the read-only candidate picker.</summary>
    /// <param name="candidates">Verified, selectable project milestones.</param>
    /// <param name="legacyCount">Existing rows without stable persisted IDs.</param>
    /// <param name="ambiguousCount">Duplicate IDs excluded from selection.</param>
    public ProjectJourneyMilestonePickerDialog(
        IReadOnlyList<ProjectJourneyMilestoneCandidate> candidates,
        int legacyCount, int ambiguousCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        InitializeComponent();
        MilestonesList.ItemsSource = candidates;
        DiagnosticsText.Text = candidates.Count + " jalon(s) avec GUID enregistré" +
            " · " + legacyCount + " ligne(s) anciennes sans ID stable" +
            " · " + ambiguousCount + " GUID dupliqué(s). " +
            "Les lignes anciennes et ambiguës ne peuvent pas être liées " +
            "sans correction dans le registre de jalons.";
    }

    /// <summary>The chosen existing milestone, or null if canceled.</summary>
    public ProjectJourneyMilestoneCandidate? SelectedMilestone { get; private set; }

    /// <summary>Enables confirmation only when a candidate is selected.</summary>
    /// <param name="sender">Milestone list.</param>
    /// <param name="e">Selection event.</param>
    private void MilestonesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        ChooseButton.IsEnabled =
            MilestonesList.SelectedItem is ProjectJourneyMilestoneCandidate;
    }

    /// <summary>Uses double-click as an explicit choice of one candidate.</summary>
    /// <param name="sender">Milestone list.</param>
    /// <param name="e">Mouse event.</param>
    private void MilestonesList_MouseDoubleClick(
        object sender, MouseButtonEventArgs e) => Choose();

    /// <summary>Confirms selection without changing milestone sources.</summary>
    /// <param name="sender">Choose button.</param>
    /// <param name="e">Click event.</param>
    private void Choose_Click(object sender, RoutedEventArgs e) => Choose();

    /// <summary>Ends the picker with an explicitly selected existing identity.</summary>
    private void Choose()
    {
        if (MilestonesList.SelectedItem is not ProjectJourneyMilestoneCandidate milestone)
        {
            return;
        }

        SelectedMilestone = milestone;
        DialogResult = true;
    }

    /// <summary>Leaves the journey unchanged.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">Click event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
