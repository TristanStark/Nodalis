namespace Nodalis.Core.Notes;

public sealed record DailyNoteReference
{
    public required string QualifiedName { get; init; }

    public required string DisplayName { get; init; }

    public required string RelativePath { get; init; }

    public required string Kind { get; init; }
}
