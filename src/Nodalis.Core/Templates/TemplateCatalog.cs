namespace Nodalis.Core.Templates;

public sealed record TemplateCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<MarkdownTemplateDefinition> Templates { get; init; } = [];
}
