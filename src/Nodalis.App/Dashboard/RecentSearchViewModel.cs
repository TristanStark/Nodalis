namespace Nodalis.App.Dashboard;

public sealed record RecentSearchViewModel
{
    public required string Query { get; init; }

    public string? ContextRelativePath { get; init; }

    public required string ContextLabel { get; init; }

    public DateTimeOffset LastUsedUtc { get; init; }
}
