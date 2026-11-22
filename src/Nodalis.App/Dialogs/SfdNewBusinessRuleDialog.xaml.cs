using System.Windows;
using Nodalis.Core.Sfd;

namespace Nodalis.App.Dialogs;

/// <summary>Explicit human authoring of one canonical RM definition.</summary>
public partial class SfdNewBusinessRuleDialog : Window
{
    private sealed record CertaintyChoice(SfdAssertionCertainty Value, string Label);

    /// <summary>Creates the isolated RM creation dialog.</summary>
    public SfdNewBusinessRuleDialog()
    {
        InitializeComponent();
        CertaintyComboBox.ItemsSource = new[]
        {
            new CertaintyChoice(SfdAssertionCertainty.ToConfirm, "À confirmer"),
            new CertaintyChoice(SfdAssertionCertainty.Inferred, "Déduit"),
            new CertaintyChoice(SfdAssertionCertainty.Confirmed, "Confirmé")
        };
        CertaintyComboBox.SelectedIndex = 0;
        Loaded += (_, _) => RuleTextBox.Focus();
    }

    /// <summary>One authored atomic business statement.</summary>
    public string Statement => RuleTextBox.Text.Trim();

    /// <summary>Optional business provenance, required to mark a rule confirmed.</summary>
    public string Source => SourceTextBox.Text.Trim();

    /// <summary>Human-chosen certainty, independent of document approval.</summary>
    public SfdAssertionCertainty Certainty =>
        ((CertaintyChoice)CertaintyComboBox.SelectedItem).Value;

    /// <summary>Validates the human-selected certainty and source before submitting.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">Click event.</param>
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Statement) ||
            (Certainty == SfdAssertionCertainty.Confirmed &&
                string.IsNullOrWhiteSpace(Source)))
        {
            MessageBox.Show(this,
                "Indiquez une règle. Une règle confirmée exige une source applicable.",
                "Nouvelle règle SFD", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
