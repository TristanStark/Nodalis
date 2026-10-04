namespace Nodalis.Core.Milestones;

public sealed record MilestoneItem
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectName { get; init; }

    public required string SourceRelativePath { get; init; }

    public required int LineNumber { get; init; }

    public required string RawLine { get; init; }

    public required string Name { get; init; }

    public DateOnly? TargetDate { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? Link { get; init; }
}
