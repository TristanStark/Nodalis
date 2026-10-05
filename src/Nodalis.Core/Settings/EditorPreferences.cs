namespace Nodalis.Core.Settings;

public sealed record EditorPreferences
{
    public bool WordWrap { get; init; } = true;

    public bool ShowLineNumbers { get; init; }

    public bool LivePreview { get; init; } = true;

    public int FontSize { get; init; } = 14;

    public int AutosaveDelayMilliseconds { get; init; } = 750;
}
