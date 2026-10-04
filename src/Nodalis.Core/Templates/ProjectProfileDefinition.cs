using Nodalis.Core.Domain;

namespace Nodalis.Core.Templates;

public sealed record ProjectProfileDefinition
{
    public required ProjectComplexity Complexity { get; init; }

    public required string DisplayName { get; init; }

    public List<ProjectSectionTemplateDefinition> Sections { get; init; } = [];
}
