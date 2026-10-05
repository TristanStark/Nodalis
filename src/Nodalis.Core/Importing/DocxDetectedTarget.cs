namespace Nodalis.Core.Importing;

public sealed record DocxDetectedTarget
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required string QualifiedName { get; init; }

    public required string RelativePath { get; init; }

    public int Confidence { get; init; }

    public List<string> Evidence { get; init; } = [];
}
