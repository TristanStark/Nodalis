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

    public IReadOnlyList<Guid> DependencyIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyList<string> DependencyNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DependencyWarnings { get; init; } = Array.Empty<string>();

    public string DependencySummary =>
        DependencyNames.Count == 0
            ? "Aucun prérequis"
            : "Prérequis : " + string.Join(", ", DependencyNames);

    public string DependencyWarningSummary =>
        DependencyWarnings.Count == 0
            ? string.Empty
            : "⚠ " + string.Join(" · ", DependencyWarnings);
}
