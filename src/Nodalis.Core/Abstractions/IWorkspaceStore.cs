using Nodalis.Core.Domain;

namespace Nodalis.Core.Abstractions;

public interface IWorkspaceStore
{
    string RootPath { get; }

    Task<WorkspaceManifest> InitializeAsync(
        string workspaceName,
        CancellationToken cancellationToken = default);

    Task<WorkspaceManifest> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        WorkspaceManifest manifest,
        CancellationToken cancellationToken = default);
}
