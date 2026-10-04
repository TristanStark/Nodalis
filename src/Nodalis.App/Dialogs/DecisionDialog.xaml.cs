using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Decisions;

namespace Nodalis.App.Dialogs;

public partial class DecisionDialog : Window
{
    public DecisionDialog(
        string scopeKind,
        string scopeName,
        string? sourceDisplayName = null,
        IReadOnlyList<string>? sourceCandidates = null)
    {
        InitializeComponent();

        ScopeText.Text =
            $"{scopeKind} · {scopeName}";
        SourceText.Text =
            string.IsNullOrWhiteSpace(sourceDisplayName)
                ? "Aucune"
                : sourceDisplayName;
        DatePicker.SelectedDate = DateTime.Today;
        StatusTextBox.Text = "Actée";

        var candidates = sourceCandidates?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? [];

        if (candidates.Length > 0)
        {
            SourceSuggestionPanel.Visibility = Visibility.Visible;
            SourceDecisionComboBox.ItemsSource = candidates;
            SourceDecisionComboBox.SelectedIndex = 0;
        }

        Loaded += (_, _) =>
        {
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        };
    }

    public DecisionDraft? Draft { get; private set; }

    private void SourceDecisionComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SourceDecisionComboBox.SelectedItem is not string candidate)
        {
            return;
        }

        DecisionTextBox.Text = candidate;

        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            TitleTextBox.Text = candidate;
        }
    }

    private void Create_Click(
        object sender,
        RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        var decision = DecisionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(decision))
        {
            MessageBox.Show(
                this,
                "Le titre et le texte de la décision sont requis.",
                "Decision Record",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Draft = new DecisionDraft
        {
            Title = title,
            Date = DatePicker.SelectedDate is DateTime date
                ? DateOnly.FromDateTime(date)
                : DateOnly.FromDateTime(DateTime.Today),
            Decision = decision,
            Context = ContextTextBox.Text.Trim(),
            Justification = JustificationTextBox.Text.Trim(),
            Impacts = ImpactsTextBox.Text.Trim(),
            Status = StatusTextBox.Text.Trim(),
            Links = LinksTextBox.Text.Trim()
        };

        DialogResult = true;
    }
}
