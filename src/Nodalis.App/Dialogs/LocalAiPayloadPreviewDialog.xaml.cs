using System.Windows;
using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Shows the exact payload that will be written to the configured local AI process.
/// </summary>
public partial class LocalAiPayloadPreviewDialog : Window
{
    /// <summary>
    /// Initializes a new exact-payload approval dialog.
    /// </summary>
    /// <param name="preview">The exact local AI payload preview.</param>
    public LocalAiPayloadPreviewDialog(
            LocalAiPayloadPreview preview)
    {
        ArgumentNullException.ThrowIfNull(
            preview);

        InitializeComponent();

        PromptIdText.Text =
            "Prompt : " +
            preview.PromptId;
        ContextLabelsText.Text =
            preview.ContextLabels.Count ==
                    0
                ? "Contexte : aucun"
                : "Contexte : " +
                  string.Join(
                      " · ",
                      preview.ContextLabels);
        PayloadTextBox.Text =
            preview.Payload;
    }

    /// <summary>
    /// Explicitly approves sending the displayed payload to the configured local process.
    /// </summary>
    /// <param name="sender">The approval button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Approve_Click(
            object sender,
            RoutedEventArgs e)
    {
        DialogResult =
            true;
    }
}
