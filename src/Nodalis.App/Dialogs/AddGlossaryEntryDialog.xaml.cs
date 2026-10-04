using System.Windows;
using Nodalis.Core.Glossary;

namespace Nodalis.App.Dialogs;

public partial class AddGlossaryEntryDialog : Window
{
    public AddGlossaryEntryDialog(
        GlossaryScope scope,
        string term)
    {
        ArgumentNullException.ThrowIfNull(scope);

        Scope = scope;

        InitializeComponent();

        ScopeText.Text = scope.DisplayName;
        TermTextBox.Text = term.Trim();

        Loaded += (_, _) =>
        {
            DefinitionTextBox.Focus();
        };
    }

    public GlossaryScope Scope { get; }

    public GlossaryEntryDraft Draft { get; private set; } =
        new()
        {
            Term = string.Empty,
            Definition = string.Empty
        };

    private void Add_Click(
        object sender,
        RoutedEventArgs e)
    {
        var term = TermTextBox.Text.Trim();
        var definition = DefinitionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(term) ||
            string.IsNullOrWhiteSpace(definition))
        {
            MessageBox.Show(
                this,
                "Le terme et la définition sont obligatoires.",
                "Ajouter au glossaire",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Draft = new GlossaryEntryDraft
        {
            Term = term,
            Definition = definition,
            Synonyms = ParseValues(
                SynonymsTextBox.Text),
            Acronyms = ParseValues(
                AcronymsTextBox.Text),
            Links = ParseLinks(
                LinksTextBox.Text)
        };

        DialogResult = true;
    }

    private static List<string> ParseValues(string value) =>
        value
            .Split(
                [';', ','],
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static List<string> ParseLinks(string value) =>
        value
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n')
            .Split(
                ['\n', ';'],
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
