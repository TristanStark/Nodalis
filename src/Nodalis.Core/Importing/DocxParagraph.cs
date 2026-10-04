namespace Nodalis.Core.Importing;

public sealed record DocxParagraph
{
    public string Text { get; init; } = string.Empty;

    public string? StyleId { get; init; }

    public string? StyleName { get; init; }

    public int? HeadingLevel { get; init; }

    public bool IsListItem { get; init; }

    public int? NumberingId { get; init; }

    public int? ListLevel { get; init; }

    public List<DocxHyperlink> Hyperlinks { get; init; } = [];
}
