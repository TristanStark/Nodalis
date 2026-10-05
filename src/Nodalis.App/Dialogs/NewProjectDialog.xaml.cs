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

        Loaded += (_, _) =>
        {
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        };
    }

    public string ProjectName => ProjectNameTextBox.Text.Trim();

    public ProjectCreationTarget SelectedTarget =>
        (ProjectCreationTarget)TargetComboBox.SelectedItem;

    public ProjectComplexity SelectedComplexity =>
        ((ComplexityChoice)ComplexityComboBox.SelectedItem).Complexity;

    public IReadOnlyList<string> BusinessLinks =>
        BusinessLinksTextBox.Text
            .Split(
                ["\r\n", "\n", "\r"],
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

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

        DialogResult = true;
    }
}
