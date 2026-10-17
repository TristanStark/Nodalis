namespace Nodalis.Core.Settings;

public sealed record BackupPreferences
{
    public bool AutomaticEnabled { get; init; }

    public string? DestinationDirectory { get; init; }

    public int IntervalMinutes { get; init; } = 60;

    public int RetentionCount { get; init; } = 10;
}
