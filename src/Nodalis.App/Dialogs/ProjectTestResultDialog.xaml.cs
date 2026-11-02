using System.Windows;
using Nodalis.Core.Tests;

namespace Nodalis.App.Dialogs;

/// <summary>Records one explicit outcome, never an automatically inferred pass.</summary>
public partial class ProjectTestResultDialog : Window
{
    /// <summary>Initializes a human-only result editor.</summary>
    /// <param name="testLabel">Current frozen test name.</param>
    /// <param name="passageNumber">Frozen passage number.</param>
    public ProjectTestResultDialog(string testLabel, int passageNumber)
    {
        InitializeComponent();
        ContextText.Text = "Passage " + passageNumber +
            " · " + testLabel;
        OutcomeCombo.SelectedIndex = 0;
        Loaded += (_, _) => ActorBox.Focus();
    }

    /// <summary>Human-selected outcome.</summary>
    public ProjectTestOutcome SelectedOutcome { get; private set; }

    /// <summary>Human executor.</summary>
    public string SelectedActor { get; private set; } = string.Empty;

    /// <summary>Required written rationale.</summary>
    public string SelectedRationale { get; private set; } = string.Empty;

    /// <summary>Optional note, not a checked proof.</summary>
    public string? SelectedEvidence { get; private set; }

    /// <summary>Verifies local inputs before the second approval in the manager.</summary>
    /// <param name="sender">Confirmation button.</param>
    /// <param name="e">Button click event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        string actor = ActorBox.Text.Trim();
        string rationale = RationaleBox.Text.Trim();
        string evidence = EvidenceBox.Text.Trim();
        if (OutcomeCombo.SelectedIndex is < 0 or > 3 ||
            !SingleLine(actor, 160) ||
            !SingleLine(rationale, 2000) ||
            evidence.Length > 4000 || evidence.Any(char.IsControl))
        {
            MessageBox.Show(this,
                "Choisissez un résultat et indiquez un exécutant ainsi " +
                "qu'une justification sur une seule ligne.",
                "Résultat incomplet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedOutcome = (ProjectTestOutcome)OutcomeCombo.SelectedIndex;
        SelectedActor = actor;
        SelectedRationale = rationale;
        SelectedEvidence = evidence.Length == 0 ? null : evidence;
        DialogResult = true;
    }

    /// <summary>Rejects empty or multiline human fields.</summary>
    /// <param name="value">User input.</param>
    /// <param name="max">Maximum field length.</param>
    /// <returns>True for a valid human declaration.</returns>
    private static bool SingleLine(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && !value.Any(char.IsControl);
}
