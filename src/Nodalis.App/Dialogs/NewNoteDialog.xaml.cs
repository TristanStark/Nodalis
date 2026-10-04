using System.Windows;
using Nodalis.Core.Templates;

namespace Nodalis.App.Dialogs;

public partial class NewNoteDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="NewNoteDialog"/>.
    /// </summary>
    /// <param name="templates">The <c>templates</c> value.</param>
    public NewNoteDialog(
            IReadOnlyCollection<MarkdownTemplateDefinition> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        InitializeComponent();

        global::System.Collections.Generic.List<global::Nodalis.App.Dialogs.NewNoteDialog.TemplateChoice> choices = new List<TemplateChoice>
        {
            new(null, "Libre")
        };

        choices.AddRange(
            templates
                .OrderBy(
                    template => template.Category,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    template => template.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(template =>
                    new TemplateChoice(
                        template.Key,
                        $"{template.Category} · {template.DisplayName}")));

        TemplateComboBox.ItemsSource = choices;
        TemplateComboBox.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        };
    }

    public string NoteTitle { get; private set; } = string.Empty;

    public string? SelectedTemplateKey { get; private set; }

    /// <summary>
    /// Performs the <c>Create_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void Create_Click(
            object sender,
            RoutedEventArgs e)
    {
        string title = TitleTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show(
                this,
                "Donnez un titre à la note.",
                "Nouvelle note",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            TitleTextBox.Focus();
            return;
        }

        if (TemplateComboBox.SelectedItem is not TemplateChoice choice)
        {
            return;
        }

        NoteTitle = title;
        SelectedTemplateKey = choice.Key;
        DialogResult = true;
    }

    private sealed record TemplateChoice(
        string? Key,
        string DisplayName);
}
