using System.Windows;
using Nodalis.Core.Notes;

namespace Nodalis.App.Dialogs;

public partial class QuickNoteDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="QuickNoteDialog"/>.
    /// </summary>
    /// <param name="scopes">The <c>scopes</c> value.</param>
    public QuickNoteDialog(
            IReadOnlyList<QuickNoteScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        InitializeComponent();

        ScopeComboBox.ItemsSource = scopes;

        if (scopes.Count > 0)
        {
            ScopeComboBox.SelectedIndex = 0;
        }

        Loaded += (_, _) => NoteTextBox.Focus();
    }

    public QuickNoteScope SelectedScope =>
        (QuickNoteScope)ScopeComboBox.SelectedItem;

    public string NoteText => NoteTextBox.Text.Trim();

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
        if (ScopeComboBox.SelectedItem is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NoteTextBox.Text))
        {
            MessageBox.Show(
                this,
                "Saisissez une note.",
                "Note rapide",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
