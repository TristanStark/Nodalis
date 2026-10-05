namespace Nodalis.Core.Links;

public sealed record LinkTargetEntry
{
    public required Guid Id { get; init; }

    public required LinkTargetKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string QualifiedName { get; init; }

    public required string RelativePath { get; init; }

    public string? ScopeIdentity { get; init; }

    public string? LocalRelativePath { get; init; }

    public string? ContentHash { get; init; }

    public List<string> Aliases { get; init; } = [];
}
