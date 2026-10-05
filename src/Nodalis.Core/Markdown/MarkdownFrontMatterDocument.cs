namespace Nodalis.Core.Markdown;

public sealed record MarkdownFrontMatterDocument
{
    public Dictionary<string, string> Properties { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public required string Body { get; init; }

    public bool HasFrontMatter { get; init; }

    public required string NewLine { get; init; }
}
