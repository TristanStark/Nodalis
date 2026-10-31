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
        SummaryText.Text = draft.IncludedSources + " document(s) textuel(s) inclus · " +
            draft.Warnings.Count + " avertissement(s). Vérifiez les sources " +
            "et relisez le contenu avant de le copier dans votre outil.";
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

    /// <summary>Closes the preview without persisting its text.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
