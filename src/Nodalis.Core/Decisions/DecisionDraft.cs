namespace Nodalis.Core.Decisions;

public sealed record DecisionDraft
{
    public required string Title { get; init; }

    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public required string Decision { get; init; }

    public string Context { get; init; } = string.Empty;

    public string Justification { get; init; } = string.Empty;

    public string Impacts { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Links { get; init; } = string.Empty;
}
