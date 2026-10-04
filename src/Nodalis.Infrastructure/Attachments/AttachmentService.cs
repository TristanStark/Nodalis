using Nodalis.Core.Attachments;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Attachments;

public sealed class AttachmentService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;

    public AttachmentService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(
            _workspaceRoot);
    }

    public async Task<AttachmentReference> CopyIntoWorkspaceAsync(
        string sourcePath,
        string ownerDocumentPath,
        CancellationToken cancellationToken = default)
    {
        var source = ValidateExistingFile(
            sourcePath);

        var owner = Path.GetFullPath(
            ownerDocumentPath);

        if (!File.Exists(owner))
        {
            throw new FileNotFoundException(
                "The owner Markdown document does not exist.",
                owner);
        }

        var ownerTarget = await _linkIndex.FindByPathAsync(
            owner,
            cancellationToken);

        if (ownerTarget is null)
        {
            await _linkIndex.RefreshAsync(
                cancellationToken);

            ownerTarget = await _linkIndex.FindByPathAsync(
                owner,
                cancellationToken);
        }

        if (ownerTarget is null)
        {
            throw new InvalidDataException(
                "The owner document could not be indexed.");
        }

        var directory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.AttachmentsDirectoryName,
            ownerTarget.Id.ToString("D"));

        Directory.CreateDirectory(
            directory);

        var destination = WindowsPathRules.GetUniqueFilePath(
            directory,
            Path.GetFileName(source));

        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true))
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await input.CopyToAsync(
                    output,
                    cancellationToken);

                await output.FlushAsync(
                    cancellationToken);
            }

            File.Move(
                temporary,
                destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return CreateReference(
            AttachmentStorageMode.CopiedIntoWorkspace,
            destination,
            owner);
    }

    public AttachmentReference CreateExternalReference(
        string sourcePath,
        string ownerDocumentPath)
    {
        var source = ValidateExistingFile(
            sourcePath);

        return CreateReference(
            AttachmentStorageMode.ExternalReference,
            source,
            Path.GetFullPath(ownerDocumentPath),
            forceAbsolute: true);
    }

    public AttachmentReference Resolve(
        string markdownTarget,
        string ownerDocumentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDocumentPath);

        var ownerDirectory = Path.GetDirectoryName(
            Path.GetFullPath(ownerDocumentPath))
            ?? _workspaceRoot;

        string fullPath;

        if (Uri.TryCreate(
                markdownTarget,
                UriKind.Absolute,
                out var uri))
        {
            if (!uri.IsFile)
            {
                return new AttachmentReference
                {
                    StorageMode = AttachmentStorageMode.ExternalReference,
                    DisplayName = markdownTarget,
                    FullPath = markdownTarget,
                    MarkdownTarget = markdownTarget,
                    Exists = false
                };
            }

            fullPath = uri.LocalPath;
        }
        else if (Path.IsPathRooted(markdownTarget))
        {
            fullPath = markdownTarget;
        }
        else
        {
            fullPath = Path.Combine(
                ownerDirectory,
                markdownTarget.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        fullPath = Path.GetFullPath(
            fullPath);

        var isInsideWorkspace = IsInsideOrEqual(
            fullPath,
            _workspaceRoot);

        return new AttachmentReference
        {
            StorageMode = isInsideWorkspace
                ? AttachmentStorageMode.CopiedIntoWorkspace
                : AttachmentStorageMode.ExternalReference,
            DisplayName = Path.GetFileName(fullPath),
            FullPath = fullPath,
            MarkdownTarget = markdownTarget,
            Exists = File.Exists(fullPath)
        };
    }

    private AttachmentReference CreateReference(
        AttachmentStorageMode mode,
        string fullPath,
        string ownerDocumentPath,
        bool forceAbsolute = false)
    {
        var path = Path.GetFullPath(
            fullPath);

        var ownerDirectory = Path.GetDirectoryName(
            ownerDocumentPath)
            ?? _workspaceRoot;

        var markdownTarget = forceAbsolute
            ? path.Replace(
                Path.DirectorySeparatorChar,
                '/')
            : Path.GetRelativePath(
                    ownerDirectory,
                    path)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');

        return new AttachmentReference
        {
            StorageMode = mode,
            DisplayName = Path.GetFileName(path),
            FullPath = path,
            MarkdownTarget = markdownTarget,
            Exists = File.Exists(path)
        };
    }

    private static string ValidateExistingFile(
        string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(
            sourcePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The selected attachment does not exist.",
                fullPath);
        }

        return fullPath;
    }

    private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullCandidate,
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
