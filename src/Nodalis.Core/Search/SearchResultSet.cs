namespace Nodalis.Core.Search;

public sealed record SearchResultSet
{
    public List<SearchResult> Project { get; init; } = [];

    public List<SearchResult> Application { get; init; } = [];

    public List<SearchResult> Global { get; init; } = [];

    public int TotalCount =>
        Project.Count +
        Application.Count +
        Global.Count;
}
