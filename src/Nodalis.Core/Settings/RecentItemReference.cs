namespace Nodalis.Core.Settings;

public sealed record RecentItemReference
{
    public required UserItemReference Item { get; init; }

    public DateTimeOffset LastOpenedUtc { get; init; } = DateTimeOffset.UtcNow;
}
