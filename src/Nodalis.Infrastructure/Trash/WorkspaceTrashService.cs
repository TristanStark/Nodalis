using System.Text.Json;
using Nodalis.Core.Trash;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Trash;

public sealed class WorkspaceTrashService
{
    private readonly string _workspaceRoot;
    private readonly string _trashRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceTrashService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceTrashService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _trashRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.TrashDirectoryName);
    }

    /// <summary>
    /// Moves a supported workspace item into the Nodalis trash while keeping its payload intact.
    /// </summary>
    /// <param name="path">The file or directory to move.</param>
    /// <param name="kind">The logical item kind.</param>
    /// <param name="itemId">The stable item identifier when one is available.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted trash entry.</returns>
    public async Task<TrashEntry> MoveToTrashAsync(
            string path,
            TrashItemKind kind,
            Guid? itemId = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = GetValidatedWorkspaceItemPath(path);
        bool isDirectory = Directory.Exists(fullPath);
        bool isFile = File.Exists(fullPath);

        if (!isDirectory && !isFile)
        {
            throw new FileNotFoundException(
                "The item to move to the trash no longer exists.",
                fullPath);
        }

        DateTimeOffset deletedUtc = DateTimeOffset.UtcNow;
        Guid entryId = Guid.NewGuid();
        string entryDirectory = Path.Combine(
            _trashRoot,
            $"{deletedUtc:yyyyMMdd-HHmmssfff}-{entryId:N}");
        string payloadName = Path.GetFileName(fullPath);
        string payloadPath = Path.Combine(
            entryDirectory,
            payloadName);

        TrashEntry entry = new TrashEntry
        {
            EntryId = entryId,
            Kind = kind,
            DisplayName = kind == TrashItemKind.Document
                ? Path.GetFileNameWithoutExtension(fullPath)
                : payloadName,
            OriginalRelativePath = Path.GetRelativePath(
                _workspaceRoot,
                fullPath),
            PayloadName = payloadName,
            DeletedUtc = deletedUtc,
            ItemId = itemId
        };

        Directory.CreateDirectory(entryDirectory);

        try
        {
            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    entryDirectory,
                    WorkspaceLayout.TrashManifestFileName),
                entry,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (isDirectory)
            {
                Directory.Move(
                    fullPath,
                    payloadPath);
            }
            else
            {
                File.Move(
                    fullPath,
                    payloadPath);
            }

            return entry;
        }
        catch
        {
            if ((Directory.Exists(fullPath) || File.Exists(fullPath)) &&
                Directory.Exists(entryDirectory))
            {
                Directory.Delete(
                    entryDirectory,
                    recursive: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Lists valid trash entries ordered from newest to oldest.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current trash entries.</returns>
    public async Task<IReadOnlyList<TrashEntry>> ListAsync(
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_trashRoot))
        {
            return Array.Empty<TrashEntry>();
        }

        List<TrashEntry> entries = new List<TrashEntry>();

        foreach (string entryDirectory in Directory
                     .EnumerateDirectories(_trashRoot)
                     .OrderBy(
                         directory => directory,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string manifestPath = Path.Combine(
                entryDirectory,
                WorkspaceLayout.TrashManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            TrashEntry entry = await AtomicJsonFile.ReadAsync<TrashEntry>(
                manifestPath,
                cancellationToken);

            string payloadPath = GetPayloadPath(
                entryDirectory,
                entry);

            if (File.Exists(payloadPath) ||
                Directory.Exists(payloadPath))
            {
                entries.Add(entry);
            }
        }

        return entries
            .OrderByDescending(item => item.DeletedUtc)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Restores one trash entry to its original location.
    /// </summary>
    /// <param name="entryId">The trash entry identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The restored absolute path.</returns>
    public async Task<string> RestoreAsync(
            Guid entryId,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LocatedTrashEntry located = await FindEntryAsync(
            entryId,
            cancellationToken);

        string destinationPath = GetRestoreDestination(
            located.Entry);

        if (File.Exists(destinationPath) ||
            Directory.Exists(destinationPath))
        {
            throw new TrashRestoreCollisionException(
                destinationPath);
        }

        string? parentDirectory = Path.GetDirectoryName(
            destinationPath);

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidDataException(
                "The trash entry has no valid restore parent.");
        }

        if (!Directory.Exists(parentDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Restore parent '{parentDirectory}' does not exist. Restore its parent item first.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(located.PayloadPath))
        {
            Directory.Move(
                located.PayloadPath,
                destinationPath);
        }
        else if (File.Exists(located.PayloadPath))
        {
            File.Move(
                located.PayloadPath,
                destinationPath);
        }
        else
        {
            throw new FileNotFoundException(
                "The trash payload is missing.",
                located.PayloadPath);
        }

        if (Directory.Exists(located.EntryDirectory))
        {
            Directory.Delete(
                located.EntryDirectory,
                recursive: true);
        }

        return destinationPath;
    }

    /// <summary>
    /// Permanently deletes every item currently stored in the Nodalis trash.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public Task EmptyAsync(
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(_trashRoot))
        {
            Directory.Delete(
                _trashRoot,
                recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds and validates a persisted trash entry.
    /// </summary>
    /// <param name="entryId">The trash entry identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The located entry and payload.</returns>
    private async Task<LocatedTrashEntry> FindEntryAsync(
            Guid entryId,
            CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_trashRoot))
        {
            throw new FileNotFoundException(
                "The Nodalis trash is empty.");
        }

        foreach (string entryDirectory in Directory.EnumerateDirectories(
                     _trashRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string manifestPath = Path.Combine(
                entryDirectory,
                WorkspaceLayout.TrashManifestFileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            TrashEntry entry;

            try
            {
                entry = await AtomicJsonFile.ReadAsync<TrashEntry>(
                    manifestPath,
                    cancellationToken);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry.EntryId != entryId)
            {
                continue;
            }

            string payloadPath = GetPayloadPath(
                entryDirectory,
                entry);

            return new LocatedTrashEntry(
                entry,
                entryDirectory,
                payloadPath);
        }

        throw new FileNotFoundException(
            $"Trash entry '{entryId}' was not found.");
    }

    /// <summary>
    /// Gets and validates the payload path for an entry.
    /// </summary>
    /// <param name="entryDirectory">The physical trash entry directory.</param>
    /// <param name="entry">The persisted entry.</param>
    /// <returns>The absolute payload path.</returns>
    private static string GetPayloadPath(
            string entryDirectory,
            TrashEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.PayloadName) ||
            !string.Equals(
                Path.GetFileName(entry.PayloadName),
                entry.PayloadName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The trash payload name is invalid.");
        }

        string payloadPath = Path.GetFullPath(
            Path.Combine(
                entryDirectory,
                entry.PayloadName));

        if (!IsSameOrChildPath(
                entryDirectory,
                payloadPath) ||
            string.Equals(
                Path.GetFullPath(entryDirectory),
                payloadPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The trash payload path escapes its entry directory.");
        }

        return payloadPath;
    }

    /// <summary>
    /// Resolves and validates the original restore destination.
    /// </summary>
    /// <param name="entry">The persisted trash entry.</param>
    /// <returns>The absolute destination path.</returns>
    private string GetRestoreDestination(
            TrashEntry entry)
    {
        if (string.IsNullOrWhiteSpace(
                entry.OriginalRelativePath) ||
            Path.IsPathRooted(
                entry.OriginalRelativePath))
        {
            throw new InvalidDataException(
                "The trash entry has an invalid original path.");
        }

        string destinationPath = Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                entry.OriginalRelativePath));

        if (!IsSameOrChildPath(
                _workspaceRoot,
                destinationPath) ||
            string.Equals(
                _workspaceRoot,
                destinationPath,
                StringComparison.OrdinalIgnoreCase) ||
            IsSameOrChildPath(
                _trashRoot,
                destinationPath))
        {
            throw new InvalidDataException(
                "The trash entry restore path escapes the workspace.");
        }

        return destinationPath;
    }

    /// <summary>
    /// Validates that a deletion target belongs to the workspace and is not already in the trash.
    /// </summary>
    /// <param name="path">The requested target path.</param>
    /// <returns>The validated absolute path.</returns>
    private string GetValidatedWorkspaceItemPath(
            string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!IsSameOrChildPath(
                _workspaceRoot,
                fullPath) ||
            string.Equals(
                _workspaceRoot,
                fullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only items inside the workspace can be moved to the trash.");
        }

        if (IsSameOrChildPath(
                _trashRoot,
                fullPath))
        {
            throw new InvalidOperationException(
                "An item already in the trash cannot be trashed again.");
        }

        return fullPath;
    }

    /// <summary>
    /// Determines whether a path is equal to or nested below another path.
    /// </summary>
    /// <param name="parentPath">The parent path.</param>
    /// <param name="candidatePath">The candidate path.</param>
    /// <returns><see langword="true"/> when the candidate is inside the parent.</returns>
    private static bool IsSameOrChildPath(
            string parentPath,
            string candidatePath)
    {
        string parent = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(parentPath));
        string candidate = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidatePath));

        if (string.Equals(
                parent,
                candidate,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LocatedTrashEntry(
        TrashEntry Entry,
        string EntryDirectory,
        string PayloadPath);
}
