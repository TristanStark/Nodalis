namespace Nodalis.Core.Importing;

public sealed record DocxParagraph
{
    public string Text { get; init; } = string.Empty;

    public string? StyleId { get; init; }

    public string? StyleName { get; init; }

    public int? HeadingLevel { get; init; }

    /// <summary>A candidate heading level, never applied automatically.</summary>
    public int? SuggestedHeadingLevel { get; init; }

    /// <summary>Visible evidence for an optional heading suggestion.</summary>
    public string? HeadingSuggestionReason { get; init; }

    public bool IsListItem { get; init; }

    public int? NumberingId { get; init; }

    public int? ListLevel { get; init; }

    public List<DocxHyperlink> Hyperlinks { get; init; } = [];

    /// <summary>Images and unsupported objects in source reading order.</summary>
    public List<DocxInlineMedia> Media { get; init; } = [];
}
