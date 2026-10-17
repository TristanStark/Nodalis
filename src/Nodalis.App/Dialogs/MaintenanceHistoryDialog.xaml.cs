using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Maintenance;

namespace Nodalis.App.Dialogs;

/// <summary>A preloaded project or application scope for the same read-only Markdown index.</summary>
public sealed record MaintenanceHistoryScope(
    string DisplayName, IReadOnlyList<MaintenanceHistoryItem> Entries);

/// <summary>Requests source navigation or editing for a selected history result.</summary>
public enum MaintenanceHistoryAction
{
    OpenSource,
    EditIntervention,
    LinkRecurrence,
    IgnoreRecurrence
}

/// <summary>
/// Searches a snapshot of maintenance Markdown with no persistence or I/O on every keystroke.
/// </summary>
public partial class MaintenanceHistoryDialog : Window
{
    private readonly IReadOnlyList<MaintenanceHistoryScope> _scopes;
    private readonly IReadOnlyList<string> _loadErrors;
    private bool _ready;
    private IReadOnlyList<MaintenanceHistoryItem> _filteredInterventions = [];

    /// <summary>Builds the contextual history browser from already indexed Markdown.</summary>
    /// <param name="scopes">Available project/application scopes.</param>
    /// <param name="loadErrors">Files that could not be read (must never be hidden silently).</param>
    /// <param name="showSuggestions">Current locally saved suggestion visibility preference.</param>
    public MaintenanceHistoryDialog(
        IReadOnlyList<MaintenanceHistoryScope> scopes,
        IReadOnlyList<string>? loadErrors = null,
        bool showSuggestions = true)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0)
        {
            throw new ArgumentException("Au moins un contexte de maintenance est requis.", nameof(scopes));
        }

        _scopes = scopes;
        _loadErrors = loadErrors ?? [];
        InitializeComponent();
        ShowRecurrencesCheckBox.IsChecked = showSuggestions;
        ReviewRecurrencesButton.Visibility = showSuggestions
            ? Visibility.Visible : Visibility.Collapsed;
        ScopeComboBox.ItemsSource = _scopes;
        ScopeComboBox.DisplayMemberPath = nameof(MaintenanceHistoryScope.DisplayName);
        ScopeComboBox.SelectedIndex = 0;

        IReadOnlyList<MaintenanceHistoryItem> all = _scopes.SelectMany(s => s.Entries).ToArray();
        StatusComboBox.ItemsSource = BuildOptions(all.Select(x => x.Status));
        CategoryComboBox.ItemsSource = BuildOptions(all.Select(x => x.Category));
        StatusComboBox.DisplayMemberPath = nameof(FilterOption.Label);
        CategoryComboBox.DisplayMemberPath = nameof(FilterOption.Label);
        StatusComboBox.SelectedIndex = 0;
        CategoryComboBox.SelectedIndex = 0;

        IncidentsList.SelectionChanged += (_, _) =>
        {
            if (IncidentsList.SelectedItem is not null)
            {
                InterventionsList.SelectedItem = null;
            }

            if (LinkedOnlyCheckBox.IsChecked == true)
            {
                UpdateRelatedInterventions();
            }
        };
        InterventionsList.SelectionChanged += (_, _) =>
        {
            if (InterventionsList.SelectedItem is not null &&
                LinkedOnlyCheckBox.IsChecked != true)
            {
                IncidentsList.SelectedItem = null;
            }
        };

        Loaded += (_, _) =>
        {
            _ready = true;
            ApplyFilters();
            SearchTextBox.Focus();
        };
    }

    private sealed record FilterOption(string Label, string Value);

    /// <summary>Selected source entry after the dialog closes successfully.</summary>
    public MaintenanceHistoryItem? SelectedEntry { get; private set; }

    /// <summary>Whether to edit the selected intervention or just navigate to its Markdown.</summary>
    public MaintenanceHistoryAction SelectedAction { get; private set; } =
        MaintenanceHistoryAction.OpenSource;

    /// <summary>Whether the user wants recurrence suggestions shown in maintenance.</summary>
    public bool ShowSuggestionsEnabled => ShowRecurrencesCheckBox.IsChecked == true;

    /// <summary>The exact pair selected for an explicit recurrence decision.</summary>
    public MaintenanceRecurrenceSuggestion? SelectedRecurrence { get; private set; }

    /// <summary>Prepares distinct filter dropdown values while keeping an all-items option.</summary>
    /// <param name="values">Values from the source Markdown.</param>
    /// <returns>Choices available in the current scope collection.</returns>
    private static IReadOnlyList<FilterOption> BuildOptions(IEnumerable<string> values)
    {
        List<FilterOption> options =
        [
            new FilterOption("(Tous)", string.Empty)
        ];
        options.AddRange(values
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new FilterOption(s, s)));
        return options;
    }

    /// <summary>Runs an in-memory search when a text input changes.</summary>
    /// <param name="sender">Changed text field.</param>
    /// <param name="e">Text change.</param>
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    /// <summary>Updates the visible lists after a dropdown change.</summary>
    /// <param name="sender">Changed selector.</param>
    /// <param name="e">Selection change.</param>
    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    /// <summary>Filters by both date boundaries without silently losing undated entries.</summary>
    /// <param name="sender">Date picker.</param>
    /// <param name="e">Date change.</param>
    private void DateChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    /// <summary>Refreshes the two virtualized lists from an already loaded snapshot.</summary>
    private void ApplyFilters()
    {
        if (!_ready || ScopeComboBox.SelectedItem is not MaintenanceHistoryScope scope)
        {
            return;
        }

        MaintenanceHistoryFilter filter = new MaintenanceHistoryFilter
        {
            Search = SearchTextBox.Text,
            From = FromPicker.SelectedDate is DateTime from
                ? DateOnly.FromDateTime(from) : null,
            To = ToPicker.SelectedDate is DateTime to
                ? DateOnly.FromDateTime(to) : null,
            Status = (StatusComboBox.SelectedItem as FilterOption)?.Value ?? string.Empty,
            Category = (CategoryComboBox.SelectedItem as FilterOption)?.Value ?? string.Empty,
            Tag = TagTextBox.Text
        };
        IReadOnlyList<MaintenanceHistoryItem> filtered =
            MaintenanceHistoryIndex.Search(scope.Entries, filter);
        MaintenanceHistoryItem[] incidents = filtered
            .Where(entry => entry.Kind == MaintenanceRecordKind.Incident).ToArray();
        MaintenanceHistoryItem[] interventions = filtered
            .Where(entry => entry.Kind == MaintenanceRecordKind.Intervention).ToArray();
        string? incidentId = (IncidentsList.SelectedItem as MaintenanceHistoryItem)?.Id;
        IncidentsList.ItemsSource = incidents;
        if (incidentId is not null)
        {
            IncidentsList.SelectedItem = incidents.FirstOrDefault(item =>
                string.Equals(item.Id, incidentId, StringComparison.OrdinalIgnoreCase));
        }

        _filteredInterventions = interventions;
        UpdateRelatedInterventions();
        IncidentTitleText.Text = "Incidents (" + incidents.Length + ")";
        int missingDates = filtered.Count(entry => !entry.Date.HasValue);
        int incomplete = filtered.Count(entry => entry.IsIncomplete);
        SummaryText.Text = $"{filtered.Count} résultat(s) sur {scope.Entries.Count} entrée(s) · " +
            $"{missingDates} sans date · {incomplete} incomplet(s)";
        WarningsText.Text = (missingDates > 0 && (filter.From.HasValue || filter.To.HasValue)
                ? "Les entrées sans date restent visibles même avec un filtre de dates. " : "") +
            (_loadErrors.Count > 0
                ? $"{_loadErrors.Count} fichier(s) inaccessible(s) : " +
                    string.Join(" ; ", _loadErrors.Take(3)) : "");
    }

    /// <summary>Updates related records when the user enables or disables incident context filtering.</summary>
    /// <param name="sender">Related-only checkbox.</param>
    /// <param name="e">Checkbox event.</param>
    private void RelatedFilterChanged(object sender, RoutedEventArgs e)
    {
        UpdateRelatedInterventions();
    }

    /// <summary>
    /// Narrows the intervention list to the selected incident without reloading files
    /// or discarding unrelated results from the in-memory projection.
    /// </summary>
    private void UpdateRelatedInterventions()
    {
        if (!_ready)
        {
            return;
        }

        MaintenanceHistoryItem? incident = IncidentsList.SelectedItem as MaintenanceHistoryItem;
        bool relatedOnly = LinkedOnlyCheckBox.IsChecked == true &&
            !string.IsNullOrWhiteSpace(incident?.Id);
        MaintenanceHistoryItem[] shown = relatedOnly
            ? _filteredInterventions.Where(item =>
                string.Equals(item.IncidentId, incident!.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.SourcePath, incident.SourcePath,
                    StringComparison.OrdinalIgnoreCase)).ToArray()
            : _filteredInterventions.ToArray();

        InterventionsList.ItemsSource = shown;
        InterventionTitleText.Text = "Interventions (" + shown.Length + ")" +
            (relatedOnly ? " · incident sélectionné" : "");
    }

    /// <summary>Lets users enable or disable only the recurrence feature.</summary>
    /// <param name="sender">Visibility checkbox.</param>
    /// <param name="e">Selection event.</param>
    private void ShowRecurrencesChanged(object sender, RoutedEventArgs e)
    {
        if (ReviewRecurrencesButton is null)
        {
            return;
        }

        ReviewRecurrencesButton.Visibility = ShowSuggestionsEnabled
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Launches a human review of explainable suggestions for the chosen scope.</summary>
    /// <param name="sender">Review button.</param>
    /// <param name="e">WPF click.</param>
    private void ReviewRecurrences_Click(object sender, RoutedEventArgs e)
    {
        if (!ShowSuggestionsEnabled ||
            ScopeComboBox.SelectedItem is not MaintenanceHistoryScope scope)
        {
            return;
        }

        IReadOnlyList<MaintenanceRecurrenceSuggestion> suggestions =
            MaintenanceRecurrenceMatcher.Find(scope.Entries);
        if (suggestions.Count == 0)
        {
            MessageBox.Show(this,
                "Aucune paire d'incidents n'atteint le seuil de suggestion (" +
                MaintenanceRecurrenceMatcher.MinimumScore +
                "/100). Les incidents ne sont jamais rapprochés sur le seul texte similaire.",
                "Suggestions d'incidents récurrents",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MaintenanceRecurrenceReviewDialog review =
            new MaintenanceRecurrenceReviewDialog(suggestions) { Owner = this };
        if (review.ShowDialog() != true || review.SelectedSuggestion is null ||
            review.SelectedDecision is null)
        {
            return;
        }

        SelectedRecurrence = review.SelectedSuggestion;
        SelectedAction = review.SelectedDecision == MaintenanceRecurrenceDecision.Link
            ? MaintenanceHistoryAction.LinkRecurrence
            : MaintenanceHistoryAction.IgnoreRecurrence;
        DialogResult = true;
    }

    /// <summary>Returns the selected entry from either list, never one from an old search.</summary>
    /// <returns>Chosen incident or intervention.</returns>
    private MaintenanceHistoryItem? CurrentSelection()
    {
        return InterventionsList.SelectedItem as MaintenanceHistoryItem ??
               IncidentsList.SelectedItem as MaintenanceHistoryItem;
    }

    /// <summary>Opens the selected source from the explicit button.</summary>
    /// <param name="sender">Button.</param>
    /// <param name="e">Click event.</param>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Accept(MaintenanceHistoryAction.OpenSource);
    }

    /// <summary>Opens an entry by double-clicking either list.</summary>
    /// <param name="sender">Source list.</param>
    /// <param name="e">Mouse input.</param>
    private void Open_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is MaintenanceHistoryItem)
        {
            Accept(MaintenanceHistoryAction.OpenSource);
        }
    }

    /// <summary>Requests form editing for a valid intervention or source editing otherwise.</summary>
    /// <param name="sender">Edit button.</param>
    /// <param name="e">Click event.</param>
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        MaintenanceHistoryItem? selected = CurrentSelection();
        if (selected is null)
        {
            MessageBox.Show(this, "Sélectionnez d'abord un élément.",
                "Historique", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Kind != MaintenanceRecordKind.Intervention ||
            selected.IsIncomplete || string.IsNullOrWhiteSpace(selected.Id))
        {
            MessageBox.Show(this,
                "Cet élément ne peut pas être modifié par le formulaire. " +
                "La source Markdown sera ouverte pour une correction directe.",
                "Historique", MessageBoxButton.OK, MessageBoxImage.Information);
            Accept(MaintenanceHistoryAction.OpenSource);
            return;
        }

        Accept(MaintenanceHistoryAction.EditIntervention);
    }

    /// <summary>Returns the selected source and action to the owning main window.</summary>
    /// <param name="action">Requested navigation or edit.</param>
    private void Accept(MaintenanceHistoryAction action)
    {
        MaintenanceHistoryItem? selected = CurrentSelection();
        if (selected is null)
        {
            return;
        }

        SelectedEntry = selected;
        SelectedAction = action;
        DialogResult = true;
    }
}
