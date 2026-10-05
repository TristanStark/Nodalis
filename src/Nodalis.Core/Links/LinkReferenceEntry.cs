namespace Nodalis.Core.Links;

public sealed record LinkReferenceEntry
{
    public required Guid SourceId { get; init; }

    public Guid? TargetId { get; init; }

    public required string RawTarget { get; init; }

    public required int LineNumber { get; init; }

    public required string Excerpt { get; init; }
}
