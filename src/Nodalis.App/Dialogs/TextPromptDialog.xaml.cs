using System.Windows;

namespace Nodalis.App.Dialogs;

public partial class TextPromptDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="TextPromptDialog"/>.
    /// </summary>
    /// <param name="title">The <c>title</c> value.</param>
    /// <param name="prompt">The <c>prompt</c> value.</param>
    /// <param name="initialValue">The <c>initialValue</c> value.</param>
    public TextPromptDialog(
            string title,
            string prompt,
            string initialValue = "")
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        ValueTextBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string Value => ValueTextBox.Text.Trim();

    /// <summary>
    /// Performs the <c>Ok_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void Ok_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueTextBox.Text))
        {
            MessageBox.Show(
                this,
                "Une valeur est requise.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
