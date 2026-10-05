using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

public sealed record ProjectCreationRequest
{
    public required string Name { get; init; }

    public required ProjectComplexity Complexity { get; init; }

    public required ProjectCreationTarget Target { get; init; }

    public List<string> BusinessLinks { get; init; } = [];
}
