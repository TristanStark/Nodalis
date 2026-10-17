using System.IO;
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
    /// <param name="ai">The local AI preferences.</param>
    public PreferencesDialog(
            EditorPreferences editor,
            bool contextPanelOpen,
            AiPreferences ai)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(ai);

        _initial = editor;

        InitializeComponent();

        FontSizeTextBox.Text = editor.FontSize.ToString();
        AutosaveDelayTextBox.Text =
            editor.AutosaveDelayMilliseconds.ToString();
        WordWrapCheckBox.IsChecked = editor.WordWrap;
        LivePreviewCheckBox.IsChecked = editor.LivePreview;
        ContextPanelCheckBox.IsChecked = contextPanelOpen;
        AiEnabledCheckBox.IsChecked = ai.IsEnabled;
        AiExecutableTextBox.Text = ai.ExecutablePath;
        AiArgumentsTextBox.Text = ai.Arguments;
        AiModelTextBox.Text = ai.ModelName;
        AiTimeoutTextBox.Text = ai.TimeoutSeconds.ToString();
    }

    public EditorPreferences? Editor { get; private set; }

    public bool ContextPanelOpen { get; private set; }

    public AiPreferences? Ai { get; private set; }

    /// <summary>
    /// Performs the <c>Save_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
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

        if (!int.TryParse(
                AiTimeoutTextBox.Text.Trim(),
                out int aiTimeout) ||
            aiTimeout is < 1 or > 3600)
        {
            ShowValidation(
                "Le timeout IA doit être compris entre 1 et 3600 secondes.");
            AiTimeoutTextBox.Focus();
            return;
        }

        bool aiEnabled =
            AiEnabledCheckBox.IsChecked == true;
        string aiExecutable =
            AiExecutableTextBox.Text.Trim();

        if (aiEnabled &&
            (string.IsNullOrWhiteSpace(
                 aiExecutable) ||
             !Path.IsPathFullyQualified(
                 aiExecutable) ||
             !File.Exists(
                 aiExecutable)))
        {
            ShowValidation(
                "Lorsque l'IA locale est activée, indiquez un chemin absolu vers un exécutable local existant.");
            AiExecutableTextBox.Focus();
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

        Ai = new AiPreferences
        {
            IsEnabled = aiEnabled,
            ExecutablePath = aiExecutable,
            Arguments = AiArgumentsTextBox.Text.Trim(),
            ModelName = AiModelTextBox.Text.Trim(),
            TimeoutSeconds = aiTimeout
        };

        DialogResult = true;
    }

    /// <summary>
    /// Performs the <c>ShowValidation</c> operation.
    /// </summary>
    /// <param name="message">The <c>message</c> value.</param>
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
