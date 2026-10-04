namespace Nodalis.Core.Links;

public sealed record LinkResolution
{
    public required LinkResolutionStatus Status { get; init; }

    public LinkTargetEntry? Target { get; init; }

    public List<LinkTargetEntry> Candidates { get; init; } = [];
}
