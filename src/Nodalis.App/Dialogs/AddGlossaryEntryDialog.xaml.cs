using System.Windows;
using Nodalis.Core.Glossary;

namespace Nodalis.App.Dialogs;

public partial class AddGlossaryEntryDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="AddGlossaryEntryDialog"/>.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="term">The <c>term</c> value.</param>
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

    /// <summary>
    /// Performs the <c>Add_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void Add_Click(
            object sender,
            RoutedEventArgs e)
    {
        string term = TermTextBox.Text.Trim();
        string definition = DefinitionTextBox.Text.Trim();

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

    /// <summary>
    /// Performs the <c>ParseValues</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> ParseValues(string value) =>
            value
                .Split(
                    [';', ','],
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    /// <summary>
    /// Performs the <c>ParseLinks</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
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
