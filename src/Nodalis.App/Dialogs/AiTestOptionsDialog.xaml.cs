using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Collects the explicit test family and scope before local generation.
/// </summary>
public partial class AiTestOptionsDialog : Window
{
    /// <summary>
    /// Initializes the test-generation options dialog.
    /// </summary>
    public AiTestOptionsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Gets the selected options after the dialog succeeds.</summary>
    public AiTestGenerationOptions? Options { get; private set; }

    /// <summary>
    /// Validates the selected enum-backed choices and returns them to the generation workflow.
    /// </summary>
    /// <param name="sender">The continue button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Continue_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (TypeComboBox.SelectedItem is not
                ComboBoxItem typeItem ||
            LevelComboBox.SelectedItem is not
                ComboBoxItem levelItem ||
            typeItem.Tag is not
                string typeValue ||
            levelItem.Tag is not
                string levelValue ||
            !Enum.TryParse(
                typeValue,
                ignoreCase:
                    true,
                out AiTestType type) ||
            !Enum.TryParse(
                levelValue,
                ignoreCase:
                    true,
                out AiTestLevel level))
        {
            MessageBox.Show(
                this,
                "Sélectionnez un type et un niveau de test valides.",
                "Paramètres de génération",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Options =
            new AiTestGenerationOptions
            {
                Type =
                    type,
                Level =
                    level
            };

        DialogResult =
            true;
    }
}
