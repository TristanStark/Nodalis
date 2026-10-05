using Nodalis.Core.Projects;

namespace Nodalis.Core.Abstractions;

public interface IProjectCreator
{
    /// <summary>
    /// Performs the <c>CreateAsync</c> operation.
    /// </summary>
    /// <param name="request">The <c>request</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<ProjectCreationResult> CreateAsync(
            ProjectCreationRequest request,
            CancellationToken cancellationToken = default);
}
