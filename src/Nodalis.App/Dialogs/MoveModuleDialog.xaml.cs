using System.Windows;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

public partial class MoveModuleDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="MoveModuleDialog"/>.
    /// </summary>
    /// <param name="targets">The <c>targets</c> value.</param>
    public MoveModuleDialog(
            IReadOnlyList<ProjectCreationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        InitializeComponent();

        TargetComboBox.ItemsSource = targets;

        if (targets.Count > 0)
        {
            TargetComboBox.SelectedIndex = 0;
        }
    }

    public ProjectCreationTarget SelectedTarget =>
        (ProjectCreationTarget)TargetComboBox.SelectedItem;

    /// <summary>
    /// Performs the <c>Move_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Move_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (TargetComboBox.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez une destination.",
                "Déplacer le module",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
