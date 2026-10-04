namespace Nodalis.Core.Links;

public sealed record BacklinkEntry
{
    public required LinkTargetEntry Source { get; init; }

    public required int LineNumber { get; init; }

    public required string Excerpt { get; init; }

    public required string RawTarget { get; init; }
}
