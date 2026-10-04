using Nodalis.Core.Domain;

namespace Nodalis.Core.Abstractions;

public interface IWorkspaceStore
{
    string RootPath { get; }

    /// <summary>
    /// Performs the <c>InitializeAsync</c> operation.
    /// </summary>
    /// <param name="workspaceName">The <c>workspaceName</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<WorkspaceManifest> InitializeAsync(
            string workspaceName,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the <c>LoadAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<WorkspaceManifest> LoadAsync(
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the <c>SaveAsync</c> operation.
    /// </summary>
    /// <param name="manifest">The <c>manifest</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task SaveAsync(
            WorkspaceManifest manifest,
            CancellationToken cancellationToken = default);
}
