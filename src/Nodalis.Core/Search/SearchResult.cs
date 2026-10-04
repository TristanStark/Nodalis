namespace Nodalis.Core.Search;

public sealed record SearchResult
{
    public required SearchScopeKind Scope { get; init; }

    public required string FilePath { get; init; }

    public required string DisplayName { get; init; }

    public required int LineNumber { get; init; }

    public required string Excerpt { get; init; }
}
