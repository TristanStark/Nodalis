using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Available actions on a real, stable journey stage or operation.
/// Only the caller reopens the latest project snapshot before executing.
/// </summary>
public enum ProjectJourneyQuickAction
{
    Open,
    Prepare,
    Assign
}

/// <summary>
/// A single human-selected action, using source GUIDs rather than titles.
/// </summary>
/// <param name="StepId">Stable owning stage.</param>
/// <param name="OperationId">Optional operation identity.</param>
/// <param name="Action">Exact desired action.</param>
public sealed record ProjectJourneyQuickSelection(
    Guid StepId, Guid? OperationId, ProjectJourneyQuickAction Action);

/// <summary>
/// Searchable and virtualized, read-only index of every stage and operation.
/// It has no filesystem operations, no prompt execution and no write action.
/// </summary>
public partial class ProjectJourneyQuickAccessDialog : Window
{
    private const int MaximumVisibleRows = 250;
    private readonly DispatcherTimer _filterTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(160)
    };
    private int _searchRevision;
    private bool _searchPending;

    private readonly IReadOnlyList<ProjectJourneyPilotageSearchRow> _rows;
    private readonly bool _readOnly;

    /// <summary>Creates only a human search view over verified project IDs.</summary>
    /// <param name="rows">Complete off-thread project-local search index.</param>
    /// <param name="readOnly">Whether this project's writes are disabled.</param>
    public ProjectJourneyQuickAccessDialog(
        IReadOnlyList<ProjectJourneyPilotageSearchRow> rows, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _readOnly = readOnly;
        _rows = rows;
        InitializeComponent();
        _filterTimer.Tick += FilterTimer_Tick;
        Closed += (_, _) =>
        {
            _searchRevision++;
            _filterTimer.Stop();
        };
        RefreshResults();
        SearchBox.Focus();
    }

    /// <summary>Confirmed selection, or null when the user closes/cancels.</summary>
    public ProjectJourneyQuickSelection? Selection { get; private set; }

    /// <summary>Updates the virtualized subset without changing the project.</summary>
    /// <param name="sender">Search input.</param>
    /// <param name="e">Text edit event.</param>
    private void SearchBox_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        // One new revision per keystroke. Discard old background results and
        // avoid saturating WPF while searching a very large journey.
        _searchRevision++;
        _searchPending = true;
        _filterTimer.Stop();
        _filterTimer.Start();
        if (CountText is not null)
        {
            CountText.Text = "Recherche en cours…";
        }

        UpdateActions();
    }

    /// <summary>
    /// Debounces human input, searches immutable IDs away from the WPF
    /// dispatcher and publishes only the most recent query results.
    /// </summary>
    /// <param name="sender">WPF background-priority timer.</param>
    /// <param name="e">Timer tick event.</param>
    private async void FilterTimer_Tick(object? sender, EventArgs e)
    {
        _filterTimer.Stop();
        if (!IsLoaded)
        {
            return;
        }

        int revision = _searchRevision;
        string query = SearchBox.Text;
        ProjectJourneyPilotageSearchResult results = await Task.Run(() =>
            ProjectJourneyPilotageSearch.Find(
                _rows, query, MaximumVisibleRows));
        if (revision != _searchRevision || !IsLoaded)
        {
            return;
        }

        ShowResults(results);
    }

    /// <summary>Updates availability for stage-only and read-only rows.</summary>
    /// <param name="sender">Selected result list.</param>
    /// <param name="e">WPF selection event.</param>
    private void Results_SelectionChanged(
        object sender, SelectionChangedEventArgs e) =>
        UpdateActions();

    /// <summary>Controls use the same selected row after search or clicking.</summary>
    private void UpdateActions()
    {
        if (ResultsList is null || OpenButton is null ||
            PrepareButton is null || AssignButton is null)
        {
            return;
        }

        ProjectJourneyPilotageSearchRow? row =
            ResultsList.SelectedItem as ProjectJourneyPilotageSearchRow;
        OpenButton.IsEnabled = !_searchPending && row is not null;
        PrepareButton.IsEnabled = !_searchPending &&
            row?.OperationId is not null;
        AssignButton.IsEnabled = !_searchPending && !_readOnly &&
            row?.OperationId is not null;
    }

    /// <summary>Double-click navigates, never silently copies a prompt.</summary>
    /// <param name="sender">Results list.</param>
    /// <param name="e">Mouse double-click event.</param>
    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ResultsList, source)
                is ListBoxItem row && row.IsSelected)
        {
            Finish(ProjectJourneyQuickAction.Open);
        }
    }

    /// <summary>Opens the chosen stage/operation via the caller.</summary>
    /// <param name="sender">Human action button.</param>
    /// <param name="e">Button event.</param>
    private void Open_Click(object sender, RoutedEventArgs e) =>
        Finish(ProjectJourneyQuickAction.Open);

    /// <summary>Requests an explicitly reviewed and manually copied draft.</summary>
    /// <param name="sender">Human action button.</param>
    /// <param name="e">Button event.</param>
    private void Prepare_Click(object sender, RoutedEventArgs e) =>
        Finish(ProjectJourneyQuickAction.Prepare);

    /// <summary>Requests the existing human prompt association picker.</summary>
    /// <param name="sender">Human action button.</param>
    /// <param name="e">Button event.</param>
    private void Assign_Click(object sender, RoutedEventArgs e) =>
        Finish(ProjectJourneyQuickAction.Assign);

    /// <summary>Returns only the verified GUIDs and chosen action to Pilotage.</summary>
    /// <param name="action">Chosen human interaction.</param>
    private void Finish(ProjectJourneyQuickAction action)
    {
        if (_searchPending ||
            ResultsList.SelectedItem is not ProjectJourneyPilotageSearchRow row ||
            action != ProjectJourneyQuickAction.Open &&
                row.OperationId is null ||
            action == ProjectJourneyQuickAction.Assign && _readOnly)
        {
            return;
        }

        Selection = new ProjectJourneyQuickSelection(
            row.StepId, row.OperationId, action);
        DialogResult = true;
    }

    /// <summary>
    /// Searches the complete dataset but realizes only a bounded result set,
    /// even with thousands of operations. Accent-insensitive French matching
    /// also handles project-local arbitrary step names.
    /// </summary>
    private void RefreshResults()
    {
        if (ResultsList is null || CountText is null ||
            SearchBox is null)
        {
            return;
        }

        ProjectJourneyPilotageSearchResult result =
            ProjectJourneyPilotageSearch.Find(
                _rows, SearchBox.Text, MaximumVisibleRows);
        ShowResults(result);
    }

    /// <summary>Rebinds a bounded result only on the dispatcher thread.</summary>
    /// <param name="result">Pure immutable search response.</param>
    private void ShowResults(ProjectJourneyPilotageSearchResult result)
    {
        _searchPending = false;
        ResultsList.ItemsSource = result.Rows;
        CountText.Text = result.Rows.Count +
            " résultat(s) affiché(s)" +
            (result.HasMore
                ? " · affinez la recherche pour les suivants"
                : " · parcours du projet uniquement") +
            " · " + result.TotalIndexed + " éléments indexés.";
        ResultsList.SelectedIndex =
            ResultsList.Items.Count > 0 ? 0 : -1;
        UpdateActions();
    }

}
