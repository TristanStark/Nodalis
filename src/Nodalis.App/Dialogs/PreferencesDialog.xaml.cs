using System.Windows;
using Nodalis.Core.Settings;

namespace Nodalis.App.Dialogs;

public partial class PreferencesDialog : Window
{
    private readonly EditorPreferences _initial;

    /// <summary>
    /// Initializes a new instance of <see cref="PreferencesDialog"/>.
    /// </summary>
    /// <param name="editor">The <c>editor</c> value.</param>
    /// <param name="contextPanelOpen">The <c>contextPanelOpen</c> value.</param>
public PreferencesDialog(
        EditorPreferences editor,
        bool contextPanelOpen)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _initial = editor;

        InitializeComponent();

        FontSizeTextBox.Text = editor.FontSize.ToString();
        AutosaveDelayTextBox.Text =
            editor.AutosaveDelayMilliseconds.ToString();
        WordWrapCheckBox.IsChecked = editor.WordWrap;
        LivePreviewCheckBox.IsChecked = editor.LivePreview;
        ContextPanelCheckBox.IsChecked = contextPanelOpen;
    }

    public EditorPreferences? Editor { get; private set; }

    public bool ContextPanelOpen { get; private set; }

    /// <summary>
    /// Performs the <c>Save_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!int.TryParse(
                FontSizeTextBox.Text.Trim(),
                out int fontSize) ||
            fontSize is < 8 or > 48)
        {
            ShowValidation(
                "La taille de police doit être comprise entre 8 et 48.");
            FontSizeTextBox.Focus();
            return;
        }

        if (!int.TryParse(
                AutosaveDelayTextBox.Text.Trim(),
                out int autosaveDelay) ||
            autosaveDelay is < 100 or > 10_000)
        {
            ShowValidation(
                "Le délai d'autosave doit être compris entre 100 et 10000 ms.");
            AutosaveDelayTextBox.Focus();
            return;
        }

        Editor = _initial with
        {
            FontSize = fontSize,
            AutosaveDelayMilliseconds = autosaveDelay,
            WordWrap = WordWrapCheckBox.IsChecked == true,
            LivePreview = LivePreviewCheckBox.IsChecked == true
        };

        ContextPanelOpen =
            ContextPanelCheckBox.IsChecked == true;

        DialogResult = true;
    }

    /// <summary>
    /// Performs the <c>ShowValidation</c> operation.
    /// </summary>
    /// <param name="message">The <c>message</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void ShowValidation(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Préférences",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
