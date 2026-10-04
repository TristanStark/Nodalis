using System.Windows;
using Nodalis.Core.Meetings;

namespace Nodalis.App.Dialogs;

public partial class MeetingDialog : Window
{
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

    private void Create_Click(
        object sender,
        RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();

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
