using Nodalis.Core.Projects;

namespace Nodalis.Core.Abstractions;

public interface IProjectCreator
{
    Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default);
}
