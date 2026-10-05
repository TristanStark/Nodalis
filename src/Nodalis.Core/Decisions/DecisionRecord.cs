namespace Nodalis.Core.Decisions;

public sealed record DecisionRecord
{
    public required string SourceRelativePath { get; init; }

    public required string Title { get; init; }

    public DateOnly? Date { get; init; }

    public string Status { get; init; } = string.Empty;

    public required string ScopeName { get; init; }

    public required string ScopeKind { get; init; }

    public string Decision { get; init; } = string.Empty;

    public string Context { get; init; } = string.Empty;

    public string Justification { get; init; } = string.Empty;

    public string Impacts { get; init; } = string.Empty;

    public string SourceReference { get; init; } = string.Empty;

    public string Links { get; init; } = string.Empty;

    public DecisionLifecycleState LifecycleState { get; init; } = DecisionLifecycleState.Other;

    public string SupersedesReference { get; init; } = string.Empty;

    public string SupersededByReference { get; init; } = string.Empty;

    public string LifecycleWarning { get; init; } = string.Empty;

    public bool HasLifecycleWarning =>
        !string.IsNullOrWhiteSpace(
            LifecycleWarning);
}
