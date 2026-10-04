namespace Nodalis.Core.Meetings;

public sealed record MeetingCreationResult
{
    public required string FilePath { get; init; }

    public required string ScopeName { get; init; }

    public required string ScopeKind { get; init; }

    public required Guid DocumentId { get; init; }
}
