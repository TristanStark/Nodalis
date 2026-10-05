namespace Nodalis.App.Dashboard;

public sealed record DashboardItemViewModel
{
    public required Guid TargetId { get; init; }

    public required string DisplayName { get; init; }

    public required string KindLabel { get; init; }

    public string? Context { get; init; }

    public DateTimeOffset? LastOpenedUtc { get; init; }
}
