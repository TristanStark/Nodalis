using Nodalis.Core.Attachments;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Attachments;

public sealed class AttachmentService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;

    /// <summary>
    /// Initializes a new instance of <see cref="AttachmentService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
public AttachmentService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(
            _workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>CopyIntoWorkspaceAsync</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="ownerDocumentPath">The <c>ownerDocumentPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<AttachmentReference> CopyIntoWorkspaceAsync(
        string sourcePath,
        string ownerDocumentPath,
        CancellationToken cancellationToken = default)
    {
        string source = ValidateExistingFile(
            sourcePath);

        string owner = Path.GetFullPath(
            ownerDocumentPath);

        if (!File.Exists(owner))
        {
            throw new FileNotFoundException(
                "The owner Markdown document does not exist.",
                owner);
        }

        global::Nodalis.Core.Links.LinkTargetEntry? ownerTarget = await _linkIndex.FindByPathAsync(
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

        string directory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.AttachmentsDirectoryName,
            ownerTarget.Id.ToString("D"));

        Directory.CreateDirectory(
            directory);

        string destination = WindowsPathRules.GetUniqueFilePath(
            directory,
            Path.GetFileName(source));

        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (global::System.IO.FileStream input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true))
            await using (global::System.IO.FileStream output = new FileStream(
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

    /// <summary>
    /// Performs the <c>CreateExternalReference</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="ownerDocumentPath">The <c>ownerDocumentPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
public AttachmentReference CreateExternalReference(
        string sourcePath,
        string ownerDocumentPath)
    {
        string source = ValidateExistingFile(
            sourcePath);

        return CreateReference(
            AttachmentStorageMode.ExternalReference,
            source,
            Path.GetFullPath(ownerDocumentPath),
            forceAbsolute: true);
    }

    /// <summary>
    /// Performs the <c>Resolve</c> operation.
    /// </summary>
    /// <param name="markdownTarget">The <c>markdownTarget</c> value.</param>
    /// <param name="ownerDocumentPath">The <c>ownerDocumentPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
public AttachmentReference Resolve(
        string markdownTarget,
        string ownerDocumentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDocumentPath);

        string ownerDirectory = Path.GetDirectoryName(
            Path.GetFullPath(ownerDocumentPath))
            ?? _workspaceRoot;

        string fullPath;

        if (Uri.TryCreate(
                markdownTarget,
                UriKind.Absolute,
                out global::System.Uri? uri))
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

        bool isInsideWorkspace = IsInsideOrEqual(
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

    /// <summary>
    /// Performs the <c>CreateReference</c> operation.
    /// </summary>
    /// <param name="mode">The <c>mode</c> value.</param>
    /// <param name="fullPath">The <c>fullPath</c> value.</param>
    /// <param name="ownerDocumentPath">The <c>ownerDocumentPath</c> value.</param>
    /// <param name="forceAbsolute">The <c>forceAbsolute</c> value.</param>
    /// <returns>The result of the operation.</returns>
private AttachmentReference CreateReference(
        AttachmentStorageMode mode,
        string fullPath,
        string ownerDocumentPath,
        bool forceAbsolute = false)
    {
        string path = Path.GetFullPath(
            fullPath);

        string ownerDirectory = Path.GetDirectoryName(
            ownerDocumentPath)
            ?? _workspaceRoot;

        string markdownTarget = forceAbsolute
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

    /// <summary>
    /// Performs the <c>ValidateExistingFile</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ValidateExistingFile(
        string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullPath = Path.GetFullPath(
            sourcePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The selected attachment does not exist.",
                fullPath);
        }

        return fullPath;
    }

    /// <summary>
    /// Performs the <c>IsInsideOrEqual</c> operation.
    /// </summary>
    /// <param name="candidate">The <c>candidate</c> value.</param>
    /// <param name="root">The <c>root</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        string fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string fullRoot = Path.GetFullPath(root)
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
