namespace Nodalis.Core.Importing;

public sealed record DocxImportPlan
{
    public required string TargetProjectDisplayName { get; init; }

    public required string TargetProjectRelativePath { get; init; }

    public bool CreatesProject { get; init; }

    public List<DocxImportPlannedChange> Changes { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}
