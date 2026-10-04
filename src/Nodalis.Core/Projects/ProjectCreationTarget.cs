namespace Nodalis.Core.Projects;

public sealed record ProjectCreationTarget
{
    public required Guid ApplicationId { get; init; }

    public required string ApplicationName { get; init; }

    public Guid? ModuleId { get; init; }

    public string? ModuleName { get; init; }

    public Guid? ParentProjectId { get; init; }

    public string? ParentProjectName { get; init; }

    public required string ParentDirectory { get; init; }

    public required string DisplayName { get; init; }
}
