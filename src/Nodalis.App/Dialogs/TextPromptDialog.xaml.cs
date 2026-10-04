using System.Windows;

namespace Nodalis.App.Dialogs;

public partial class TextPromptDialog : Window
{
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
