using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

public sealed record ProjectCreationResult
{
    public required ProjectManifest Project { get; init; }

    public required string ProjectDirectory { get; init; }

    public required string OverviewFilePath { get; init; }

    public List<string> CreatedPaths { get; init; } = [];
}
