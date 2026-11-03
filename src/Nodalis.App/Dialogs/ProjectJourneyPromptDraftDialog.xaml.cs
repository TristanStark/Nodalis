using System.Runtime.InteropServices;
using System.Windows;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>Displays a locally built prompt draft without executing any AI.</summary>
public partial class ProjectJourneyPromptDraftDialog : Window
{
    /// <summary>Creates a read-only, copy-only draft preview.</summary>
    /// <param name="draft">Bounded reviewed prompt text and diagnostics.</param>
    public ProjectJourneyPromptDraftDialog(ProjectJourneyPromptDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        InitializeComponent();
        DraftText.Text = draft.Content;
        SystemText.Text = draft.SystemPrompt ??
            "Aucun prompt système associé. Aucun modèle FRT-100 n'est imposé.";
        SystemTab.IsEnabled = !string.IsNullOrWhiteSpace(draft.SystemPrompt);
        CopySystemButton.IsEnabled = SystemTab.IsEnabled;
        SummaryText.Text = draft.IncludedSources +
            " document(s) textuel(s) inclus · " +
            draft.Warnings.Count + " avertissement(s). " +
            (SystemTab.IsEnabled
                ? "Deux canaux DISTINCTS : configurez le système FRT-100 " +
                    "dans l'outil externe, puis collez le prompt utilisateur."
                : "Aucun système FRT-100 actif : relisez les sources avant copie.");
    }

    /// <summary>Copies only upon explicit user interaction.</summary>
    /// <param name="sender">Copy button.</param>
    /// <param name="e">Click event.</param>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DraftText.Text);
            StatusText.Text = "Brouillon copié. Rien n'a été envoyé ni enregistré.";
        }
        catch (ExternalException exception)
        {
            MessageBox.Show(this,
                "Le presse-papiers est indisponible : " + exception.Message,
                "Copie impossible", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copies the separate FRT system reference only on human request.
    /// Copying does NOT configure any external model automatically.
    /// </summary>
    /// <param name="sender">System-copy button.</param>
    /// <param name="e">Click event.</param>
    private void CopySystem_Click(object sender, RoutedEventArgs e)
    {
        if (!SystemTab.IsEnabled)
        {
            return;
        }

        try
        {
            Clipboard.SetText(SystemText.Text);
            StatusText.Text =
                "Référentiel système copié : configurez-le vous-même dans le LLM.";
        }
        catch (ExternalException exception)
        {
            MessageBox.Show(this,
                "Le presse-papiers est indisponible : " + exception.Message,
                "Copie impossible", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>Closes the preview without persisting its text.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
