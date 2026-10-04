namespace Nodalis.Core.Settings;

public sealed record UserItemReference
{
    public required string Kind { get; init; }

    public required string Key { get; init; }

    public string? DisplayName { get; init; }
}
