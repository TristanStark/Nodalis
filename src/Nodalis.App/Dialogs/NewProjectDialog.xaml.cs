using System.Windows;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Templates;

namespace Nodalis.App.Dialogs;

public partial class NewProjectDialog : Window
{
    public sealed record ComplexityChoice(
        ProjectComplexity Complexity,
        string DisplayName);

    public sealed record ProjectKindChoice(
        ProjectKind Kind,
        string DisplayName);

    /// <summary>
    /// Initializes a new instance of <see cref="NewProjectDialog"/>.
    /// </summary>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <param name="profiles">The <c>profiles</c> value.</param>
    public NewProjectDialog(
            IReadOnlyList<ProjectCreationTarget> targets,
            IReadOnlyList<ProjectProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(profiles);

        InitializeComponent();

        ComplexityComboBox.SelectionChanged += (_, _) =>
            UpdateComplexityLayout();
        ProjectKindComboBox.SelectionChanged += (_, _) =>
            UpdateComplexityLayout();

        ProjectKindComboBox.ItemsSource = new[]
        {
            new ProjectKindChoice(ProjectKind.Standard, "Projet classique"),
            new ProjectKindChoice(ProjectKind.WithSpecifications, "Projet avec spec (SFD)")
        };
        ProjectKindComboBox.SelectedIndex = 0;

        TargetComboBox.ItemsSource = targets;
        if (targets.Count > 0)
        {
            TargetComboBox.SelectedIndex = 0;
        }

        ComplexityComboBox.ItemsSource = profiles
            .Select(profile => new ComplexityChoice(
                profile.Complexity,
                profile.DisplayName))
            .ToArray();

        if (profiles.Count > 0)
        {
            ComplexityComboBox.SelectedIndex = 0;
        }

        UpdateComplexityLayout();

        Loaded += (_, _) =>
        {
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        };
    }

    public string ProjectName => ProjectNameTextBox.Text.Trim();

    public ProjectCreationTarget SelectedTarget =>
        (ProjectCreationTarget)TargetComboBox.SelectedItem;

    public ProjectKind SelectedKind =>
        ((ProjectKindChoice)ProjectKindComboBox.SelectedItem).Kind;

    public ProjectComplexity SelectedComplexity =>
        SelectedKind == ProjectKind.WithSpecifications
            ? ProjectComplexity.Simple
            : ((ComplexityChoice)ComplexityComboBox.SelectedItem).Complexity;

    public IReadOnlyList<string> BusinessLinks =>
        SelectedComplexity == ProjectComplexity.UltraSimple
            ? Array.Empty<string>()
            : BusinessLinksTextBox.Text
                .Split(
                    ["\r\n", "\n", "\r"],
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

    /// <summary>
    /// Hides irrelevant project metadata when creating a one-sheet
    /// ultra-simple project, while retaining the project target selection.
    /// </summary>
    private void UpdateComplexityLayout()
    {
        if (ComplexityComboBox.SelectedItem is not ComplexityChoice selected)
        {
            return;
        }

        bool isSfd = SelectedKind == ProjectKind.WithSpecifications;
        bool ultraSimple = !isSfd && selected.Complexity == ProjectComplexity.UltraSimple;
        ComplexityLabel.Visibility = isSfd ? Visibility.Collapsed : Visibility.Visible;
        ComplexityComboBox.Visibility = isSfd ? Visibility.Collapsed : Visibility.Visible;
        ProjectNameLabel.Text = ultraSimple ? "Référence / nom" : "Nom";
        BusinessLinksPanel.Visibility = ultraSimple
            ? Visibility.Collapsed
            : Visibility.Visible;
        // Do not reserve grid space for hidden business links.
        BusinessLinksRow.Height = ultraSimple
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        // Keep the dialog footer fully visible in the compact layout.
        MinHeight = ultraSimple ? 360 : 480;
        Height = ultraSimple ? 400 : 560;
    }

    /// <summary>
    /// Performs the <c>Create_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Create_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
        {
            MessageBox.Show(
                this,
                "Le nom du projet est obligatoire.",
                "Nouveau projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ProjectNameTextBox.Focus();
            return;
        }

        if (TargetComboBox.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez une application, un module ou un projet parent.",
                "Nouveau projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (ComplexityComboBox.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez un niveau de projet.",
                "Nouveau projet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (SelectedKind == ProjectKind.WithSpecifications)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Créer un Projet avec spec ?\n\n" +
                "Nodalis préparera les dossiers SPECS FONCTIONNELLES, SPECS TECHNIQUES, " +
                "DOCUMENTATION, QUALIFICATION et MANAGEMENT, ainsi que les documents " +
                "de pilotage.\n\n" +
                "Aucun processus métier, aucune activité, exigence ou fonctionnalité " +
                "ne sera généré automatiquement.",
                "Aperçu de création — Projet SFD",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        DialogResult = true;
    }
}
