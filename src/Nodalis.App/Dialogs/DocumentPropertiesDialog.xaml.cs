using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Markdown;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Edits portable Markdown front matter properties for one document.
/// </summary>
public partial class DocumentPropertiesDialog : Window
{
    private static readonly HashSet<string> StandardKeys =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "status",
            "owner",
            "version",
            "environment",
            "type",
            "tags"
        };

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentPropertiesDialog"/>.
    /// </summary>
    /// <param name="properties">The current document properties.</param>
    public DocumentPropertiesDialog(
            IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(
            properties);

        InitializeComponent();

        CustomProperties =
            new ObservableCollection<DocumentPropertyEditorRow>();

        StatusTextBox.Text =
            GetPropertyValue(
                properties,
                "status");
        OwnerTextBox.Text =
            GetPropertyValue(
                properties,
                "owner");
        VersionTextBox.Text =
            GetPropertyValue(
                properties,
                "version");
        EnvironmentTextBox.Text =
            GetPropertyValue(
                properties,
                "environment");
        TypeTextBox.Text =
            GetPropertyValue(
                properties,
                "type");

        IReadOnlyList<string> tags =
            MarkdownFrontMatterParser.ParseTags(
                GetPropertyValue(
                    properties,
                    "tags"));

        TagsTextBox.Text =
            string.Join(
                ", ",
                tags);

        foreach (KeyValuePair<string, string> property in properties
                     .Where(property =>
                         !StandardKeys.Contains(
                             property.Key))
                     .OrderBy(
                         property => property.Key,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            CustomProperties.Add(
                new DocumentPropertyEditorRow
                {
                    Key =
                        property.Key,
                    Value =
                        property.Value
                });
        }

        DataContext =
            this;
    }

    /// <summary>
    /// Gets the editable custom property rows.
    /// </summary>
    public ObservableCollection<DocumentPropertyEditorRow> CustomProperties { get; }

    /// <summary>
    /// Gets the complete normalized property set after validation.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ResultProperties { get; private set; }

    /// <summary>
    /// Adds an empty custom-property row.
    /// </summary>
    /// <param name="sender">The button that initiated the action.</param>
    /// <param name="e">The routed event.</param>
    private void AddCustomProperty_Click(
            object sender,
            RoutedEventArgs e)
    {
        CustomProperties.Add(
            new DocumentPropertyEditorRow());
    }

    /// <summary>
    /// Removes the custom-property row associated with the clicked button.
    /// </summary>
    /// <param name="sender">The remove button.</param>
    /// <param name="e">The routed event.</param>
    private void RemoveCustomProperty_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not DocumentPropertyEditorRow row)
        {
            return;
        }

        CustomProperties.Remove(
            row);
    }

    /// <summary>
    /// Validates the editor and exposes the normalized property dictionary.
    /// </summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The routed event.</param>
    private void Save_Click(
            object sender,
            RoutedEventArgs e)
    {
        Dictionary<string, string> properties =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (!TryAddStandardProperty(
                properties,
                "status",
                StatusTextBox.Text,
                "Statut") ||
            !TryAddStandardProperty(
                properties,
                "owner",
                OwnerTextBox.Text,
                "Responsable") ||
            !TryAddStandardProperty(
                properties,
                "version",
                VersionTextBox.Text,
                "Version") ||
            !TryAddStandardProperty(
                properties,
                "environment",
                EnvironmentTextBox.Text,
                "Environnement") ||
            !TryAddStandardProperty(
                properties,
                "type",
                TypeTextBox.Text,
                "Type"))
        {
            return;
        }

        if (ContainsLineBreak(
                TagsTextBox.Text))
        {
            ShowValidationError(
                "Les tags doivent rester sur une seule ligne.");
            return;
        }

        IReadOnlyList<string> tags =
            MarkdownFrontMatterParser.ParseTags(
                TagsTextBox.Text);

        if (tags.Count > 0)
        {
            properties["tags"] =
                string.Join(
                    ", ",
                    tags);
        }

        foreach (DocumentPropertyEditorRow row in CustomProperties)
        {
            string key =
                row.Key.Trim();
            string value =
                row.Value.Trim();

            if (key.Length == 0 &&
                value.Length == 0)
            {
                continue;
            }

            if (!IsValidPropertyKey(
                    key))
            {
                ShowValidationError(
                    $"La clé « {key} » est invalide. Utilisez uniquement lettres, chiffres, tiret, underscore et point.");
                return;
            }

            if (StandardKeys.Contains(
                    key))
            {
                ShowValidationError(
                    $"« {key} » est une propriété standard. Utilisez le champ dédié.");
                return;
            }

            if (ContainsLineBreak(
                    value))
            {
                ShowValidationError(
                    $"La propriété « {key} » doit rester sur une seule ligne.");
                return;
            }

            if (properties.ContainsKey(
                    key))
            {
                ShowValidationError(
                    $"La propriété « {key} » est présente plusieurs fois.");
                return;
            }

            properties[key] =
                value;
        }

        ResultProperties =
            properties;
        DialogResult =
            true;
    }

    /// <summary>
    /// Adds a non-empty standard property after validating its single-line value.
    /// </summary>
    /// <param name="properties">The target dictionary.</param>
    /// <param name="key">The front matter key.</param>
    /// <param name="value">The edited value.</param>
    /// <param name="displayName">The localized field name.</param>
    /// <returns><see langword="true"/> when validation succeeds.</returns>
    private bool TryAddStandardProperty(
            IDictionary<string, string> properties,
            string key,
            string value,
            string displayName)
    {
        string normalized =
            value.Trim();

        if (normalized.Length == 0)
        {
            return true;
        }

        if (ContainsLineBreak(
                normalized))
        {
            ShowValidationError(
                $"{displayName} doit rester sur une seule ligne.");
            return false;
        }

        properties[key] =
            normalized;
        return true;
    }

    /// <summary>
    /// Reads one property without requiring it to exist.
    /// </summary>
    /// <param name="properties">The source dictionary.</param>
    /// <param name="key">The requested key.</param>
    /// <returns>The property value or an empty string.</returns>
    private static string GetPropertyValue(
            IReadOnlyDictionary<string, string> properties,
            string key) =>
            properties.TryGetValue(
                key,
                out string? value)
                ? value
                : string.Empty;

    /// <summary>
    /// Checks whether a property key belongs to the portable Nodalis subset.
    /// </summary>
    /// <param name="key">The candidate key.</param>
    /// <returns><see langword="true"/> when the key is valid.</returns>
    private static bool IsValidPropertyKey(
            string key) =>
            key.Length > 0 &&
            key.All(character =>
                char.IsLetterOrDigit(
                    character) ||
                character is
                    '-' or
                    '_' or
                    '.');

    /// <summary>
    /// Checks whether a value contains a line break unsupported by the compact front matter syntax.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when a line break is present.</returns>
    private static bool ContainsLineBreak(
            string value) =>
            value.Contains(
                '\r') ||
            value.Contains(
                '\n');

    /// <summary>
    /// Displays a validation error inside the dialog.
    /// </summary>
    /// <param name="message">The validation message.</param>
    private void ShowValidationError(
            string message)
    {
        MessageBox.Show(
            this,
            message,
            "Propriétés du document",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}

/// <summary>
/// Represents one editable custom Markdown property.
/// </summary>
public sealed class DocumentPropertyEditorRow
{
    /// <summary>
    /// Gets or sets the custom property key.
    /// </summary>
    public string Key { get; set; } =
        string.Empty;

    /// <summary>
    /// Gets or sets the custom property value.
    /// </summary>
    public string Value { get; set; } =
        string.Empty;
}
