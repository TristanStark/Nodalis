using System.Windows;
using Nodalis.Core.Meetings;

namespace Nodalis.App.Dialogs;

public partial class MeetingDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="MeetingDialog"/>.
    /// </summary>
    /// <param name="scopeKind">The <c>scopeKind</c> value.</param>
    /// <param name="scopeName">The <c>scopeName</c> value.</param>
    public MeetingDialog(
            string scopeKind,
            string scopeName)
    {
        InitializeComponent();

        ScopeText.Text =
            $"{scopeKind} · {scopeName}";
        DatePicker.SelectedDate = DateTime.Today;

        Loaded += (_, _) =>
        {
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        };
    }

    public MeetingDraft? Draft { get; private set; }

    /// <summary>
    /// Performs the <c>Create_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Create_Click(
            object sender,
            RoutedEventArgs e)
    {
        string title = TitleTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show(
                this,
                "Le titre de la réunion est requis.",
                "Compte-rendu de réunion",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            TitleTextBox.Focus();
            return;
        }

        Draft = new MeetingDraft
        {
            Title = title,
            Date = DatePicker.SelectedDate is DateTime date
                ? DateOnly.FromDateTime(date)
                : DateOnly.FromDateTime(DateTime.Today),
            Participants = ParticipantsTextBox.Text,
            Context = ContextTextBox.Text,
            Agenda = AgendaTextBox.Text,
            Notes = NotesTextBox.Text,
            Decisions = DecisionsTextBox.Text,
            Actions = ActionsTextBox.Text,
            AiTranscript = TranscriptTextBox.Text,
            AiSummary = SummaryTextBox.Text,
            OutlookContent = OutlookTextBox.Text
        };

        DialogResult = true;
    }
}
