namespace Nodalis.Core.Notes;

public sealed record QuickNotesSnapshot
{
    public required QuickNoteScope Scope { get; init; }

    public required string Content { get; init; }
}
