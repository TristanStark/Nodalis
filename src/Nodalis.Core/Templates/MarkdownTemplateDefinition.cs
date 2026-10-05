namespace Nodalis.Core.Templates;

public sealed record MarkdownTemplateDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string FileName { get; init; }

    public string Category { get; init; } = "Général";

    public string DefaultFileName { get; init; } = "{{title}}.md";
}
