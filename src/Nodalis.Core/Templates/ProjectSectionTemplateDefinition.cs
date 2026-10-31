namespace Nodalis.Core.Templates;

public sealed record ProjectSectionTemplateDefinition
{
    public required string Name { get; init; }

    public int Order { get; init; }

    public bool IsSingleton { get; init; }

    public string? TemplateKey { get; init; }

    /// <summary>Optional project-relative folder used only when creating new projects.</summary>
    public string? RelativeDirectory { get; init; }
}
