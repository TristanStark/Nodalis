using System.Windows;
using Nodalis.Core.Decisions;

namespace Nodalis.App.Dialogs;

public partial class DecisionReplacementDialog : Window
{
    /// <summary>
    /// Initializes a new replacement picker for one Decision Record.
    /// </summary>
    /// <param name="previous">The decision that will become superseded.</param>
    /// <param name="candidates">Possible replacement decisions from the same scope.</param>
    public DecisionReplacementDialog(
            DecisionRecord previous,
            IReadOnlyList<DecisionRecord> candidates)
    {
        ArgumentNullException.ThrowIfNull(
            previous);
        ArgumentNullException.ThrowIfNull(
            candidates);

        InitializeComponent();

        PreviousDecisionText.Text =
            previous.Title;
        ReplacementComboBox.ItemsSource =
            candidates;

        if (candidates.Count > 0)
        {
            ReplacementComboBox.SelectedIndex =
                0;
        }
    }

    /// <summary>
    /// Gets the replacement selected by the user.
    /// </summary>
    public DecisionRecord? SelectedReplacement { get; private set; }

    /// <summary>
    /// Accepts the currently selected replacement.
    /// </summary>
    /// <param name="sender">The confirmation button.</param>
    /// <param name="e">The routed event.</param>
    private void Confirm_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (ReplacementComboBox.SelectedItem is not DecisionRecord replacement)
        {
            MessageBox.Show(
                this,
                "Sélectionnez une décision remplaçante.",
                "Remplacer une décision",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedReplacement =
            replacement;
        DialogResult =
            true;
    }
}
