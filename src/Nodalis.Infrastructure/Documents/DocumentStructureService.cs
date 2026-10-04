using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Documents;

public sealed class DocumentStructureService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;

    public DocumentStructureService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
    }

    public async Task<string> RenameAsync(
        string documentPath,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var source = Path.GetFullPath(documentPath);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "The Markdown document to rename does not exist.",
                source);
        }

        if (!string.Equals(
                Path.GetExtension(source),
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only Markdown documents can be renamed by this service.");
        }

        var directory = Path.GetDirectoryName(source)
            ?? throw new InvalidOperationException(
                "Cannot determine the document directory.");

        var oldDisplayName = Path.GetFileNameWithoutExtension(source);
        var requestedName = newName.Trim();

        if (requestedName.EndsWith(
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            requestedName = Path.GetFileNameWithoutExtension(
                requestedName);
        }

        var safeFileName =
            WindowsPathRules.SanitizeSegment(requestedName) +
            ".md";

        var directDestination = Path.Combine(
            directory,
            safeFileName);

        if (string.Equals(
                source,
                directDestination,
                StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        await _linkIndex.RefreshAsync(
            cancellationToken);

        var destination = WindowsPathRules.GetUniqueFilePath(
            directory,
            safeFileName);

        File.Move(
            source,
            destination);

        try
        {
            await _linkIndex.RegisterRenameAsync(
                source,
                destination,
                oldDisplayName,
                cancellationToken);
        }
        catch
        {
            if (File.Exists(destination) &&
                !File.Exists(source))
            {
                File.Move(
                    destination,
                    source);
            }

            throw;
        }

        return destination;
    }
}
