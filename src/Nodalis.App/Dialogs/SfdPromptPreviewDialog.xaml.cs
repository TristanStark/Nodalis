using System.Windows;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit copy-only preview. Never sends prompts to external models or
/// writes source excerpts or prompt text to the workspace.
/// </summary>
public partial class SfdPromptPreviewDialog : Window
{
    private readonly SfdPreparedPrompt _draft;

    /// <summary>Shows the complete SFD schema and selected evidence to review.</summary>
    /// <param name="draft">Bounded, in-memory prepared SFD prompt.</param>
    public SfdPromptPreviewDialog(SfdPreparedPrompt draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _draft = draft;
        InitializeComponent();
        TitleText.Text = draft.Title;
        PromptTextBox.Text = draft.Content;
        WarningsText.Text = "Vérifiez la confidentialité avant de copier " +
            draft.IncludedSources + " source(s). " +
            (draft.Warnings.Count == 0
                ? "Aucun avertissement."
                : string.Join(" | ", draft.Warnings.Take(8)));
    }

    /// <summary>Copies the reviewed text only after a direct user click.</summary>
    /// <param name="sender">Copy button.</param>
    /// <param name="e">Click event.</param>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_draft.Content);
            StatusText.Text = "Prompt copié après confirmation.";
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            MessageBox.Show(this, exception.Message, "Presse-papiers indisponible",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
