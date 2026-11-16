using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

public sealed record ProjectCreationRequest
{
    public required string Name { get; init; }

    public required ProjectComplexity Complexity { get; init; }

    /// <summary>Project family, separate from its legacy complexity.</summary>
    public ProjectKind Kind { get; init; } = ProjectKind.Standard;

    public required ProjectCreationTarget Target { get; init; }

    public List<string> BusinessLinks { get; init; } = [];
}
