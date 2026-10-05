namespace Nodalis.Core.Notes;

public sealed record DailyNoteItem
{
    public required DateOnly Date { get; init; }

    public required string FullPath { get; init; }

    public required string RelativePath { get; init; }

    public bool IsToday { get; init; }

    public string DisplayDate =>
        Date.ToString("dddd dd MMMM yyyy");
}
