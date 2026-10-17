namespace Nodalis.Core.Trash;

public sealed record TrashEntry
{
    public required Guid EntryId { get; init; }

    public required TrashItemKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string OriginalRelativePath { get; init; }

    public required string PayloadName { get; init; }

    public required DateTimeOffset DeletedUtc { get; init; }

    public Guid? ItemId { get; init; }
}
