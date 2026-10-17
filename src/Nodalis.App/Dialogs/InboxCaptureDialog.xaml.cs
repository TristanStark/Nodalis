using System.Windows;

namespace Nodalis.App.Dialogs;

/// <summary>Minimal explicit text capture surface for the global Inbox shortcut.</summary>
public partial class InboxCaptureDialog : Window
{
    /// <summary>Initializes the accessible native-WPF capture dialog.</summary>
    public InboxCaptureDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CaptureTextBox.Focus();
    }

    public string CapturedMarkdown => CaptureTextBox.Text;

    /// <summary>Rejects empty captures before dismissing the modal dialog.</summary>
    /// <param name="sender">Clicked Save button.</param>
    /// <param name="e">Routed click event.</param>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CaptureTextBox.Text))
        {
            MessageBox.Show(this, "Saisissez du contenu à capturer.",
                "Inbox", MessageBoxButton.OK, MessageBoxImage.Information);
            CaptureTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
