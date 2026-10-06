using System.Windows;
using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Provides an editable Markdown review step before generated tests can be inserted into a project.
/// </summary>
public partial class AiTestReviewDialog : Window
{
    /// <summary>
    /// Initializes the generated-test review dialog.
    /// </summary>
    /// <param name="markdown">The raw local-model Markdown proposal.</param>
    /// <param name="options">The selected generation options.</param>
    /// <param name="sourceLabels">The exact source labels used for generation.</param>
    public AiTestReviewDialog(
            string markdown,
            AiTestGenerationOptions options,
            IReadOnlyList<string> sourceLabels)
    {
        ArgumentNullException.ThrowIfNull(
            markdown);
        ArgumentNullException.ThrowIfNull(
            options);
        ArgumentNullException.ThrowIfNull(
            sourceLabels);

        InitializeComponent();

        MarkdownTextBox.Text =
            markdown;
        OptionsText.Text =
            "Type : " +
            options.TypeLabel +
            " · Niveau : " +
            options.LevelLabel;
        SourcesText.Text =
            sourceLabels.Count ==
                    0
                ? "Sources : aucune"
                : "Sources : " +
                  string.Join(
                      " · ",
                      sourceLabels);
    }

    /// <summary>Gets the edited Markdown explicitly approved for insertion.</summary>
    public string? ApprovedMarkdown { get; private set; }

    /// <summary>
    /// Requires non-empty Markdown before authorizing creation in the Tests section.
    /// </summary>
    /// <param name="sender">The insertion button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Insert_Click(
            object sender,
            RoutedEventArgs e)
    {
        string markdown =
            MarkdownTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                markdown))
        {
            MessageBox.Show(
                this,
                "Le Markdown à insérer ne peut pas être vide.",
                "Prévisualisation des tests",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ApprovedMarkdown =
            markdown;
        DialogResult =
            true;
    }
}
