using System.Windows;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays local-AI analysis as read-only proposals without mutating workspace content.
/// </summary>
public partial class AiProposalResultDialog : Window
{
    /// <summary>
    /// Initializes one proposal-only result dialog.
    /// </summary>
    /// <param name="content">The local model output.</param>
    /// <param name="contextLabels">The source labels supplied to the model.</param>
    public AiProposalResultDialog(
            string content,
            IReadOnlyList<string> contextLabels)
    {
        ArgumentNullException.ThrowIfNull(
            content);
        ArgumentNullException.ThrowIfNull(
            contextLabels);

        InitializeComponent();

        ResultTextBox.Text =
            content;
        ContextText.Text =
            contextLabels.Count ==
                    0
                ? "Contexte : aucun"
                : "Contexte analysé : " +
                  string.Join(
                      " · ",
                      contextLabels);
    }

    /// <summary>
    /// Copies the displayed proposal text to the clipboard without modifying the workspace.
    /// </summary>
    /// <param name="sender">The copy button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Copy_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(
                ResultTextBox.Text))
        {
            Clipboard.SetText(
                ResultTextBox.Text);
        }
    }
}
