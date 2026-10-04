using System.Windows;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

public partial class MoveModuleDialog : Window
{
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
