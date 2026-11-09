using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;
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
        ExportSystemButton.IsEnabled = SystemTab.IsEnabled;
        SummaryText.Text = draft.PromptCount +
            " prompt(s) préparé(s) · " + draft.IncludedSources +
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
        IReadOnlyList<string> pending = ProjectJourneyPromptVariables
            .FindPlaceholders(DraftText.Text);
        if (pending.Count > 0 &&
            MessageBox.Show(this,
                "Variables non renseignées : " +
                string.Join(", ", pending) +
                "\n\nLe brouillon est modifiable : renseignez ou " +
                "supprimez chaque variable. Copier quand même ?",
                "Prompt incomplet · confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

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

    /// <summary>Explicitly exports the edited user message, never an AI call.</summary>
    /// <param name="sender">User-export button.</param>
    /// <param name="e">Click event.</param>
    private async void ExportUser_Click(object sender, RoutedEventArgs e)
    {
        await ExportAsync(DraftText.Text, "prompt-utilisateur.md",
            containsWorkspaceData: true);
    }

    /// <summary>Explicitly exports ONLY the separate system instructions.</summary>
    /// <param name="sender">System-export button.</param>
    /// <param name="e">Click event.</param>
    private async void ExportSystem_Click(object sender, RoutedEventArgs e)
    {
        if (SystemTab.IsEnabled)
        {
            await ExportAsync(SystemText.Text, "frt100-system.md",
                containsWorkspaceData: false);
        }
    }

    /// <summary>
    /// Writes an explicitly chosen UTF-8 file after disclosure and approval.
    /// The two messages are never concatenated or sent over the network.
    /// </summary>
    /// <param name="content">Reviewed content, current in-memory revision.</param>
    /// <param name="suggestedName">Default filename, not a workspace path.</param>
    /// <param name="containsWorkspaceData">Whether sources could be included.</param>
    /// <returns>Completed explicit export or cancellation.</returns>
    private async Task ExportAsync(string content, string suggestedName,
        bool containsWorkspaceData)
    {
        if (containsWorkspaceData)
        {
            IReadOnlyList<string> pending =
                ProjectJourneyPromptVariables.FindPlaceholders(content);
            string warning = "Le prompt utilisateur peut contenir des données " +
                "confidentielles de votre workspace. Vérifiez son contenu " +
                "et choisissez vous-même un emplacement sûr.";
            if (pending.Count > 0)
            {
                warning += "\n\nVariables non renseignées : " +
                    string.Join(", ", pending) + ".";
            }

            if (MessageBox.Show(this, warning + "\n\nContinuer l'export ?",
                    "Exporter un prompt · contrôle humain",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }
        }

        SaveFileDialog picker = new()
        {
            Title = "Exporter un message IA préparé (sans envoi)",
            FileName = suggestedName,
            Filter = "Markdown (*.md)|*.md|Texte (*.txt)|*.txt",
            AddExtension = true,
            DefaultExt = ".md",
            OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(picker.FileName, content,
                new UTF8Encoding(false));
            StatusText.Text = "Message exporté au chemin choisi. " +
                "Aucune requête IA n'a été envoyée.";
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Export impossible",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Closes the preview without persisting its text.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
