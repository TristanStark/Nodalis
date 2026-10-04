using System.Windows;
using Nodalis.Core.Templates;

namespace Nodalis.App.Dialogs;

public partial class NewNoteDialog : Window
{
    public NewNoteDialog(
        IReadOnlyCollection<MarkdownTemplateDefinition> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        InitializeComponent();

        var choices = new List<TemplateChoice>
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

    private void Create_Click(
        object sender,
        RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();

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
