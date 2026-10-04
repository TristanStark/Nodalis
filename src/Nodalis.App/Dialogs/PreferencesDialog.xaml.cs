using System.Windows;
using Nodalis.Core.Settings;

namespace Nodalis.App.Dialogs;

public partial class PreferencesDialog : Window
{
    private readonly EditorPreferences _initial;

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

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!int.TryParse(
                FontSizeTextBox.Text.Trim(),
                out var fontSize) ||
            fontSize is < 8 or > 48)
        {
            ShowValidation(
                "La taille de police doit être comprise entre 8 et 48.");
            FontSizeTextBox.Focus();
            return;
        }

        if (!int.TryParse(
                AutosaveDelayTextBox.Text.Trim(),
                out var autosaveDelay) ||
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
