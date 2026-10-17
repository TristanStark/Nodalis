using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Maintenance;

namespace Nodalis.App.Dialogs;

/// <summary>Read-only incident comparison with explicit, reversible review decisions.</summary>
public partial class MaintenanceRecurrenceReviewDialog : Window
{
    /// <summary>Initializes an explanation-first review of candidate pairs.</summary>
    /// <param name="suggestions">Deterministic suggestions, ordered by score.</param>
    public MaintenanceRecurrenceReviewDialog(
        IReadOnlyList<MaintenanceRecurrenceSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        InitializeComponent();
        SuggestionsList.ItemsSource = suggestions;
        if (suggestions.Count > 0)
        {
            SuggestionsList.SelectedIndex = 0;
        }

        Loaded += (_, _) => SuggestionsList.Focus();
    }

    /// <summary>Pair selected by a user for link/ignore.</summary>
    public MaintenanceRecurrenceSuggestion? SelectedSuggestion { get; private set; }

    /// <summary>Human decision to persist (no write is made by this dialog).</summary>
    public MaintenanceRecurrenceDecision? SelectedDecision { get; private set; }

    /// <summary>Renders explanations and raw field values side by side.</summary>
    /// <param name="sender">Suggestion list.</param>
    /// <param name="e">WPF selection change.</param>
    private void SuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionsList.SelectedItem is not MaintenanceRecurrenceSuggestion candidate)
        {
            return;
        }

        ExplanationText.Text = candidate.Explanation + " · score " + candidate.Score +
            "/100 (seuil " + MaintenanceRecurrenceMatcher.MinimumScore + ")";
        RenderIncident(candidate.First, LeftSummaryText, LeftDateText,
            LeftReferenceText, LeftSymptomsText, LeftTagsText, LeftSourceText);
        RenderIncident(candidate.Second, RightSummaryText, RightDateText,
            RightReferenceText, RightSymptomsText, RightTagsText, RightSourceText);
    }

    /// <summary>Displays one incident from its source snapshot without changing Markdown.</summary>
    /// <param name="incident">History item.</param>
    /// <param name="summary">Summary target.</param>
    /// <param name="date">Date target.</param>
    /// <param name="reference">Reference target.</param>
    /// <param name="symptoms">Symptoms target.</param>
    /// <param name="tags">Tags target.</param>
    /// <param name="source">Source target.</param>
    private static void RenderIncident(MaintenanceHistoryItem incident,
        TextBlock summary, TextBlock date, TextBlock reference, TextBlock symptoms,
        TextBlock tags, TextBlock source)
    {
        summary.Text = incident.Summary;
        date.Text = "Date : " + incident.DateLabel;
        reference.Text = "Référence : " + (string.IsNullOrWhiteSpace(incident.Reference)
            ? "non renseignée" : incident.Reference);
        symptoms.Text = "Symptômes : " + (string.IsNullOrWhiteSpace(incident.Symptoms)
            ? "non renseignés" : incident.Symptoms);
        tags.Text = "Tags : " + (string.IsNullOrWhiteSpace(incident.Tags)
            ? "aucun" : incident.Tags);
        source.Text = incident.SourceLabel + " · " + incident.Id;
    }

    /// <summary>Confirms the explicit link action without modifying either incident here.</summary>
    /// <param name="sender">Link button.</param>
    /// <param name="e">WPF click.</param>
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        Accept(MaintenanceRecurrenceDecision.Link);
    }

    /// <summary>Confirms the explicit ignore action without modifying either incident here.</summary>
    /// <param name="sender">Ignore button.</param>
    /// <param name="e">WPF click.</param>
    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        Accept(MaintenanceRecurrenceDecision.Ignore);
    }

    /// <summary>Returns a user-confirmed action to the owning history window.</summary>
    /// <param name="decision">Choice to save in Markdown.</param>
    private void Accept(MaintenanceRecurrenceDecision decision)
    {
        if (SuggestionsList.SelectedItem is not MaintenanceRecurrenceSuggestion suggestion)
        {
            return;
        }

        string verb = decision == MaintenanceRecurrenceDecision.Link
            ? "Enregistrer un lien de récurrence entre ces deux incidents ?" :
              "Ne plus suggérer cette paire d'incidents ?";
        if (MessageBox.Show(this,
                verb + "\n\nCette action ne fusionne, ne ferme et ne supprime aucun incident.",
                "Confirmer le rapprochement",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        SelectedSuggestion = suggestion;
        SelectedDecision = decision;
        DialogResult = true;
    }
}
