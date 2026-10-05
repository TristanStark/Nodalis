namespace Nodalis.Core.Meetings;

public sealed record MeetingDraft
{
    public required string Title { get; init; }

    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public string Participants { get; init; } = string.Empty;

    public string Context { get; init; } = string.Empty;

    public string Agenda { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string Decisions { get; init; } = string.Empty;

    public string Actions { get; init; } = string.Empty;

    public string AiTranscript { get; init; } = string.Empty;

    public string AiSummary { get; init; } = string.Empty;

    public string OutlookContent { get; init; } = string.Empty;
}
