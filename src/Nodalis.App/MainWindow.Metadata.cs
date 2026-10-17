using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Markdown;

namespace Nodalis.App;

/// <summary>
/// Adds portable Markdown document-property editing to the main window.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] PreferredMetadataPropertyOrder =
    [
        "status",
        "owner",
        "version",
        "environment",
        "type",
        "tags"
    ];

    /// <summary>
    /// Opens the front matter property editor for the active document.
    /// </summary>
    /// <param name="sender">The button that initiated the action.</param>
    /// <param name="e">The routed event.</param>
    private void EditDocumentProperties_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (_activeDocumentTab is null ||
            _activeDocumentTab.IsMissing)
        {
            return;
        }

        string currentMarkdown =
            MarkdownEditorTextBox.Text;

        MarkdownFrontMatterDocument current =
            MarkdownFrontMatterParser.Parse(
                currentMarkdown);

        global::Nodalis.App.Dialogs.DocumentPropertiesDialog dialog =
            new DocumentPropertiesDialog(
                current.Properties)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true ||
            dialog.ResultProperties is null)
        {
            return;
        }

        int currentPrefixLength =
            current.HasFrontMatter
                ? currentMarkdown.Length -
                  current.Body.Length
                : 0;

        int caretInBody =
            Math.Max(
                0,
                MarkdownEditorTextBox.CaretIndex -
                currentPrefixLength);

        string updatedMarkdown =
            MarkdownFrontMatterParser.Apply(
                currentMarkdown,
                dialog.ResultProperties);

        if (string.Equals(
                currentMarkdown,
                updatedMarkdown,
                StringComparison.Ordinal))
        {
            RefreshDocumentPropertiesContext(
                updatedMarkdown);
            return;
        }

        MarkdownEditorTextBox.Text =
            updatedMarkdown;

        MarkdownFrontMatterDocument updated =
            MarkdownFrontMatterParser.Parse(
                updatedMarkdown);

        int updatedPrefixLength =
            updated.HasFrontMatter
                ? updatedMarkdown.Length -
                  updated.Body.Length
                : 0;

        MarkdownEditorTextBox.CaretIndex =
            Math.Min(
                updatedMarkdown.Length,
                updatedPrefixLength +
                caretInBody);

        RefreshDocumentPropertiesContext(
            updatedMarkdown);

        StatusText.Text =
            dialog.ResultProperties.Count == 0
                ? "Propriétés du document supprimées"
                : $"Propriétés du document mises à jour · {dialog.ResultProperties.Count}";
    }

    /// <summary>
    /// Refreshes the property summary shown in the context panel.
    /// </summary>
    /// <param name="markdown">The active Markdown source.</param>
    private void RefreshDocumentPropertiesContext(
            string markdown)
    {
        MarkdownFrontMatterDocument metadata =
            MarkdownFrontMatterParser.Parse(
                markdown);

        KeyValuePair<string, string>[] properties =
            metadata.Properties
                .OrderBy(property =>
                    GetMetadataPropertyOrder(
                        property.Key))
                .ThenBy(
                    property => property.Key,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        DocumentPropertiesList.ItemsSource =
            properties;

        bool hasProperties =
            properties.Length > 0;

        DocumentPropertiesList.Visibility =
            hasProperties
                ? Visibility.Visible
                : Visibility.Collapsed;

        DocumentPropertiesEmptyText.Visibility =
            hasProperties
                ? Visibility.Collapsed
                : Visibility.Visible;

        EditDocumentPropertiesButton.IsEnabled =
            _activeDocumentTab is not null &&
            !_activeDocumentTab.IsMissing;

        DocumentPropertiesHost.Visibility =
            Visibility.Visible;
    }

    /// <summary>
    /// Hides document-specific metadata when the current context is not a document.
    /// </summary>
    private void ClearDocumentPropertiesContext()
    {
        DocumentPropertiesList.ItemsSource =
            null;
        DocumentPropertiesHost.Visibility =
            Visibility.Collapsed;
        EditDocumentPropertiesButton.IsEnabled =
            false;
    }

    /// <summary>
    /// Returns the stable display order for a metadata key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>The standard-property order or a custom-property fallback value.</returns>
    private static int GetMetadataPropertyOrder(
            string key)
    {
        for (int index = 0;
             index < PreferredMetadataPropertyOrder.Length;
             index++)
        {
            if (string.Equals(
                    PreferredMetadataPropertyOrder[index],
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return PreferredMetadataPropertyOrder.Length;
    }
}
