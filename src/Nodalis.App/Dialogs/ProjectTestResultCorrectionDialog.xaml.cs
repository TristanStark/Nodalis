using System.Windows;
using Nodalis.Core.Tests;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit human administrative correction, distinct from a test execution.
/// </summary>
public partial class ProjectTestResultCorrectionDialog : Window
{
    private readonly ProjectTestOutcome _original;

    /// <summary>Displays immutable test context and initial human verdict.</summary>
    /// <param name="testLabel">Frozen human test label.</param>
    /// <param name="passageNumber">Historic frozen passage number.</param>
    /// <param name="original">Original declaration that remains in history.</param>
    public ProjectTestResultCorrectionDialog(
        string testLabel, int passageNumber, ProjectTestOutcome original)
    {
        _original = original;
        InitializeComponent();
        ContextText.Text = "Passage " + passageNumber + " · " + testLabel +
            "\nVerdict initial : " + original +
            " (conservé dans le passage)";
        OutcomeCombo.SelectedIndex = (int)original;
        Loaded += (_, _) => ActorBox.Focus();
    }

    /// <summary>Explicit corrected declaration.</summary>
    public ProjectTestOutcome SelectedOutcome { get; private set; }

    /// <summary>Human actor responsible for the correction.</summary>
    public string SelectedActor { get; private set; } = string.Empty;

    /// <summary>Required explanation, not a substitute for retesting.</summary>
    public string SelectedReason { get; private set; } = string.Empty;

    /// <summary>Checks human data and requires an actual administrative change.</summary>
    /// <param name="sender">Confirmation button.</param>
    /// <param name="e">Click.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string actor = ActorBox.Text.Trim();
        string reason = ReasonBox.Text.Trim();
        if (OutcomeCombo.SelectedIndex is < 0 or > 3 ||
            OutcomeCombo.SelectedIndex == (int)_original ||
            !SingleLine(actor, 160) || !SingleLine(reason, 2000))
        {
            MessageBox.Show(this,
                "Choisissez une déclaration différente et indiquez " +
                "l'auteur et le motif sur une ligne.",
                "Rectification incomplète",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedOutcome = (ProjectTestOutcome)OutcomeCombo.SelectedIndex;
        SelectedActor = actor;
        SelectedReason = reason;
        DialogResult = true;
    }

    /// <summary>Checks bounded single-line human text.</summary>
    /// <param name="value">Actor or reason.</param>
    /// <param name="maximum">Maximum characters.</param>
    /// <returns>Whether this field can be recorded.</returns>
    private static bool SingleLine(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
}
