using System.Text;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Persistence;

public sealed class FileSystemWorkspaceStore : IWorkspaceStore
{
    public FileSystemWorkspaceStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    private string ManifestPath => Path.Combine(
        RootPath,
        WorkspaceLayout.WorkspaceManifestFileName);

    public async Task<WorkspaceManifest> InitializeAsync(
        string workspaceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceName);

        Directory.CreateDirectory(RootPath);

        if (File.Exists(ManifestPath))
        {
            throw new InvalidOperationException(
                $"A Nodalis workspace already exists at '{RootPath}'.");
        }

        Directory.CreateDirectory(Path.Combine(
            RootPath,
            WorkspaceLayout.ApplicationsDirectoryName));
        Directory.CreateDirectory(Path.Combine(
            RootPath,
            WorkspaceLayout.AttachmentsDirectoryName));
        Directory.CreateDirectory(Path.Combine(
            RootPath,
            WorkspaceLayout.TemplatesDirectoryName));

        var templateStore = new FileSystemTemplateStore(RootPath);
        await templateStore.InitializeDefaultsAsync(cancellationToken);

        var manifest = new WorkspaceManifest
        {
            Id = Guid.NewGuid(),
            Name = workspaceName.Trim()
        };

        await AtomicJsonFile.WriteAsync(ManifestPath, manifest, cancellationToken);

        await CreateTextFileIfMissingAsync(
            Path.Combine(RootPath, WorkspaceLayout.GlobalQuickNotesFileName),
            "# Notes rapides\n\n",
            cancellationToken);

        await CreateTextFileIfMissingAsync(
            Path.Combine(RootPath, WorkspaceLayout.GlobalGlossaryFileName),
            "# Glossaire général\n\n",
            cancellationToken);

        return manifest;
    }

    public async Task<WorkspaceManifest> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ManifestPath))
        {
            throw new FileNotFoundException(
                "The selected directory is not a Nodalis workspace.",
                ManifestPath);
        }

        var manifest = await AtomicJsonFile.ReadAsync<WorkspaceManifest>(
            ManifestPath,
            cancellationToken);

        if (manifest.SchemaVersion > WorkspaceManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Workspace schema {manifest.SchemaVersion} is newer than this version of Nodalis supports.");
        }

        return manifest;
    }

    public Task SaveAsync(
        WorkspaceManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SchemaVersion != WorkspaceManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Cannot save workspace schema {manifest.SchemaVersion}; supported schema is {WorkspaceManifest.CurrentSchemaVersion}.");
        }

        return AtomicJsonFile.WriteAsync(ManifestPath, manifest, cancellationToken);
    }

    private static async Task CreateTextFileIfMissingAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }
}
