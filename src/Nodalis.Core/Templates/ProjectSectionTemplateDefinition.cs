namespace Nodalis.Core.Templates;

public sealed record ProjectSectionTemplateDefinition
{
    public required string Name { get; init; }

    public int Order { get; init; }

    public bool IsSingleton { get; init; }

    public string? TemplateKey { get; init; }
}
