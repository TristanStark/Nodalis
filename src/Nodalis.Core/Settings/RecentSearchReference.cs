namespace Nodalis.Core.Settings;

public sealed record RecentSearchReference
{
    public required string Query { get; init; }

    public string? ContextRelativePath { get; init; }

    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;
}
