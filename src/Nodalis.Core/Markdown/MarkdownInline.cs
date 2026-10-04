namespace Nodalis.Core.Markdown;

public sealed record MarkdownInline
{
    public required MarkdownInlineKind Kind { get; init; }

    public required string Text { get; init; }

    public string? Target { get; init; }
}
