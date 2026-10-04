using System.Windows;
using Nodalis.Core.Notes;

namespace Nodalis.App.Dialogs;

public partial class QuickNoteDialog : Window
{
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
