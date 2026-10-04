namespace Nodalis.Core.Markdown;

public sealed record MarkdownBlock
{
    public required MarkdownBlockKind Kind { get; init; }

    public string Text { get; init; } = string.Empty;

    public int Level { get; init; }

    public bool? IsChecked { get; init; }

    public string? Language { get; init; }

    public List<List<string>> TableRows { get; init; } = [];
}
