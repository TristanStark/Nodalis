namespace Nodalis.Core.Milestones;

public sealed record MilestoneDraft
{
    public required string Name { get; init; }

    public DateOnly? TargetDate { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? Link { get; init; }
}
