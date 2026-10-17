namespace Nodalis.Core.Settings;

public sealed record EditorPreferences
{
    public bool WordWrap { get; init; } = true;

    public bool ShowLineNumbers { get; init; }

    public bool LivePreview { get; init; } = true;

    public int FontSize { get; init; } = 14;

    public int AutosaveDelayMilliseconds { get; init; } = 750;

    public EditorSplitMode SplitMode { get; init; } = EditorSplitMode.None;

    public double SplitRatio { get; init; } = 0.5;

    public Guid? SecondaryDocumentTabId { get; init; }
}
