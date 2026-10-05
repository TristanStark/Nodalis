namespace Nodalis.Core.Decisions;

public sealed record DecisionCreationResult
{
    public required string FilePath { get; init; }

    public required string ScopeName { get; init; }

    public required string ScopeKind { get; init; }
}
