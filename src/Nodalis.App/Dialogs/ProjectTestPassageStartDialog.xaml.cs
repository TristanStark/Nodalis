using System.Windows;
using System.Windows.Controls;

namespace Nodalis.App.Dialogs;

/// <summary>Read-only, index-derived selectable test definition.</summary>
/// <param name="Id">Globally unique block identity from the current index.</param>
/// <param name="Label">Current title, used for display only.</param>
/// <param name="RelativePath">Canonical workspace-relative source.</param>
public sealed record ProjectTestCandidate(
    Guid Id, string Label, string RelativePath);

/// <summary>One explicit multi-test selection and frozen passage context.</summary>
public partial class ProjectTestPassageStartDialog : Window
{
    private readonly IReadOnlyList<ProjectTestCandidate> _candidates;

    /// <summary>Initializes a multi-select test picker without file access.</summary>
    /// <param name="candidates">Unique project-local test block choices.</param>
    public ProjectTestPassageStartDialog(
        IReadOnlyList<ProjectTestCandidate> candidates)
    {
        _candidates = candidates ??
            throw new ArgumentNullException(nameof(candidates));
        InitializeComponent();
        TestsList.ItemsSource = _candidates;
        UpdateSelectionLabel();
        Loaded += (_, _) => EnvironmentBox.Focus();
    }

    /// <summary>Approved test GUIDs in displayed source order.</summary>
    public IReadOnlyList<Guid> SelectedTestIds { get; private set; } = [];

    /// <summary>Explicit frozen environment.</summary>
    public string SelectedEnvironment { get; private set; } = string.Empty;

    /// <summary>Explicit frozen application version.</summary>
    public string SelectedVersion { get; private set; } = string.Empty;

    /// <summary>Human authorizing the new passage.</summary>
    public string SelectedActor { get; private set; } = string.Empty;

    /// <summary>Selection display, without any index refresh.</summary>
    private void UpdateSelectionLabel()
    {
        SelectionText.Text = TestsList.SelectedItems.Count + "/" +
            _candidates.Count + " test(s) sélectionné(s)";
    }

    /// <summary>Selects all visible project test definitions.</summary>
    /// <param name="sender">Select all button.</param>
    /// <param name="e">Button click.</param>
    private void SelectAll_Click(object sender, RoutedEventArgs e) =>
        TestsList.SelectAll();

    /// <summary>Clears the current test selection.</summary>
    /// <param name="sender">Clear button.</param>
    /// <param name="e">Button click.</param>
    private void Clear_Click(object sender, RoutedEventArgs e) =>
        TestsList.UnselectAll();

    /// <summary>Updates the selected test count.</summary>
    /// <param name="sender">Tests list.</param>
    /// <param name="e">Selected items event.</param>
    private void TestsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionLabel();

    /// <summary>Validates selected test set and user-provided context.</summary>
    /// <param name="sender">Confirm button.</param>
    /// <param name="e">Button event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ProjectTestCandidate[] selected = TestsList.SelectedItems
            .OfType<ProjectTestCandidate>().ToArray();
        string environment = EnvironmentBox.Text.Trim();
        string version = VersionBox.Text.Trim();
        string actor = ActorBox.Text.Trim();
        if (selected.Length is < 1 or > 500 ||
            !SingleLine(environment, 160) ||
            !SingleLine(version, 160) ||
            !SingleLine(actor, 160))
        {
            MessageBox.Show(this,
                "Sélectionnez 1 à 500 tests, puis renseignez environnement, " +
                "version et responsable (sur une seule ligne).",
                "Passage incomplet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedTestIds = _candidates.Where(item =>
            selected.Contains(item)).Select(item => item.Id).ToArray();
        SelectedEnvironment = environment;
        SelectedVersion = version;
        SelectedActor = actor;
        DialogResult = true;
    }

    /// <summary>Validates a human one-line string.</summary>
    /// <param name="value">User data.</param>
    /// <param name="max">Maximum characters.</param>
    /// <returns>True for one readable value.</returns>
    private static bool SingleLine(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && !value.Any(char.IsControl);
}
