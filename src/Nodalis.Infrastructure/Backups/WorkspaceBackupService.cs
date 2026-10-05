using System.IO.Compression;
using System.Text.Json;
using Nodalis.Core.Backups;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Backups;

public sealed class WorkspaceBackupService
{
    private const string BackupFilePrefix = "Nodalis";
    private const string BackupManifestFileName = ".nodalis-backup.json";

    private readonly string _workspaceRoot;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceBackupService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceBackupService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
    }

    /// <summary>
    /// Creates a verified ZIP backup and applies retention for the current workspace.
    /// </summary>
    /// <param name="destinationDirectory">The backup directory, which must be outside the workspace.</param>
    /// <param name="retentionCount">The maximum number of backups to retain for this workspace.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The verified backup information.</returns>
    public async Task<WorkspaceBackupInfo> CreateBackupAsync(
            string destinationDirectory,
            int retentionCount,
            CancellationToken cancellationToken = default)
    {
        if (retentionCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionCount),
                "Backup retention must be between 1 and 100.");
        }

        string destination = ValidateBackupDestination(
            destinationDirectory);

        await _operationGate.WaitAsync(
            cancellationToken);

        try
        {
            global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore workspaceStore = new FileSystemWorkspaceStore(
                _workspaceRoot);
            global::Nodalis.Core.Domain.WorkspaceManifest workspace = await workspaceStore.LoadAsync(
                cancellationToken);

            Directory.CreateDirectory(destination);

            DateTimeOffset createdUtc = DateTimeOffset.UtcNow;
            string fileName =
                $"{BackupFilePrefix}-{workspace.Id:N}-{createdUtc:yyyyMMdd-HHmmssfff}.zip";
            string finalPath = Path.Combine(
                destination,
                fileName);
            string temporaryPath =
                finalPath + $".{Guid.NewGuid():N}.tmp";

            try
            {
                await Task.Run(
                    () => CreateArchive(
                        temporaryPath,
                        workspace,
                        createdUtc,
                        cancellationToken),
                    cancellationToken);

                global::Nodalis.Core.Backups.WorkspaceBackupInfo validation = await Task.Run(
                    () => ValidateArchiveInternal(
                        temporaryPath,
                        cancellationToken),
                    cancellationToken);

                if (validation.Status != BackupValidationStatus.Valid ||
                    validation.WorkspaceId != workspace.Id)
                {
                    throw new InvalidDataException(
                        validation.StatusMessage ??
                        "The generated backup failed validation.");
                }

                File.Move(
                    temporaryPath,
                    finalPath);

                await PruneAsync(
                    destination,
                    workspace.Id,
                    retentionCount,
                    cancellationToken);

                return validation with
                {
                    ArchivePath = finalPath,
                    SizeBytes = new FileInfo(finalPath).Length
                };
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Lists and validates backups belonging to the current workspace.
    /// </summary>
    /// <param name="destinationDirectory">The configured backup directory.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The backups ordered from newest to oldest.</returns>
    public async Task<IReadOnlyList<WorkspaceBackupInfo>> ListBackupsAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default)
    {
        string destination = ValidateBackupDestination(
            destinationDirectory);

        if (!Directory.Exists(destination))
        {
            return Array.Empty<WorkspaceBackupInfo>();
        }

        global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore workspaceStore = new FileSystemWorkspaceStore(
            _workspaceRoot);
        global::Nodalis.Core.Domain.WorkspaceManifest workspace = await workspaceStore.LoadAsync(
            cancellationToken);

        string[] archivePaths = Directory
            .EnumerateFiles(
                destination,
                GetBackupSearchPattern(workspace.Id),
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return await Task.Run(
            () =>
            {
                global::System.Collections.Generic.List<global::Nodalis.Core.Backups.WorkspaceBackupInfo> result = new List<WorkspaceBackupInfo>();

                foreach (string archivePath in archivePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Add(
                        ValidateArchiveInternal(
                            archivePath,
                            cancellationToken));
                }

                return (IReadOnlyList<WorkspaceBackupInfo>)result
                    .OrderByDescending(item => item.CreatedUtc)
                    .ThenByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            },
            cancellationToken);
    }

    /// <summary>
    /// Fully validates one ZIP backup, including every compressed entry.
    /// </summary>
    /// <param name="archivePath">The ZIP archive to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Validation information for the archive.</returns>
    public Task<WorkspaceBackupInfo> ValidateBackupAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        string fullPath = Path.GetFullPath(
            archivePath);

        return Task.Run(
            () => ValidateArchiveInternal(
                fullPath,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Restores a verified backup into a new or empty directory without overwriting an existing workspace.
    /// </summary>
    /// <param name="archivePath">The backup ZIP archive.</param>
    /// <param name="destinationDirectory">The new or empty restore directory.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The restored workspace directory.</returns>
    public async Task<string> RestoreBackupAsync(
            string archivePath,
            string destinationDirectory,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string archive = Path.GetFullPath(
            archivePath);
        string destination = ValidateRestoreDestination(
            destinationDirectory);

        await _operationGate.WaitAsync(
            cancellationToken);

        try
        {
            global::Nodalis.Core.Backups.WorkspaceBackupInfo validation = await Task.Run(
                () => ValidateArchiveInternal(
                    archive,
                    cancellationToken),
                cancellationToken);

            if (validation.Status != BackupValidationStatus.Valid ||
                validation.WorkspaceId is null)
            {
                throw new InvalidDataException(
                    validation.StatusMessage ??
                    "The selected archive is not a valid Nodalis backup.");
            }

            global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore currentStore = new FileSystemWorkspaceStore(
                _workspaceRoot);
            global::Nodalis.Core.Domain.WorkspaceManifest currentWorkspace = await currentStore.LoadAsync(
                cancellationToken);

            if (validation.WorkspaceId != currentWorkspace.Id)
            {
                throw new InvalidDataException(
                    "The selected backup belongs to a different Nodalis workspace.");
            }

            string? parent = Path.GetDirectoryName(
                destination);

            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new InvalidDataException(
                    "The restore destination has no valid parent directory.");
            }

            Directory.CreateDirectory(parent);

            string staging = Path.Combine(
                parent,
                $".nodalis-restore-{Guid.NewGuid():N}");

            try
            {
                await Task.Run(
                    () => ExtractArchive(
                        archive,
                        staging,
                        cancellationToken),
                    cancellationToken);

                global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore restoredStore = new FileSystemWorkspaceStore(
                    staging);
                global::Nodalis.Core.Domain.WorkspaceManifest restoredWorkspace = await restoredStore.LoadAsync(
                    cancellationToken);

                if (restoredWorkspace.Id != currentWorkspace.Id)
                {
                    throw new InvalidDataException(
                        "The restored workspace identity does not match the backup metadata.");
                }

                if (Directory.Exists(destination))
                {
                    if (Directory.EnumerateFileSystemEntries(destination).Any())
                    {
                        throw new BackupRestoreCollisionException(
                            destination);
                    }

                    Directory.Delete(destination);
                }

                Directory.Move(
                    staging,
                    destination);

                return destination;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(
                        staging,
                        recursive: true);
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Returns the timestamp of the newest backup file for automatic scheduling.
    /// </summary>
    /// <param name="destinationDirectory">The configured backup directory.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The newest backup timestamp, or <see langword="null"/> when no backup exists.</returns>
    public async Task<DateTimeOffset?> GetLatestBackupUtcAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default)
    {
        string destination = ValidateBackupDestination(
            destinationDirectory);

        if (!Directory.Exists(destination))
        {
            return null;
        }

        global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore workspaceStore = new FileSystemWorkspaceStore(
            _workspaceRoot);
        global::Nodalis.Core.Domain.WorkspaceManifest workspace = await workspaceStore.LoadAsync(
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        string? latest = Directory
            .EnumerateFiles(
                destination,
                GetBackupSearchPattern(workspace.Id),
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(
                path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();

        return latest is null
            ? null
            : new DateTimeOffset(
                File.GetLastWriteTimeUtc(latest),
                TimeSpan.Zero);
    }

    /// <summary>
    /// Determines whether an automatic backup is due for the supplied cadence.
    /// </summary>
    /// <param name="destinationDirectory">The configured backup directory.</param>
    /// <param name="intervalMinutes">The automatic backup cadence in minutes.</param>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when a backup should run now.</returns>
    public async Task<bool> IsBackupDueAsync(
            string destinationDirectory,
            int intervalMinutes,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
    {
        if (intervalMinutes is < 15 or > 10_080)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMinutes),
                "Automatic backup cadence must be between 15 minutes and 7 days.");
        }

        DateTimeOffset? latest = await GetLatestBackupUtcAsync(
            destinationDirectory,
            cancellationToken);

        return latest is null ||
               nowUtc - latest.Value >= TimeSpan.FromMinutes(
                   intervalMinutes);
    }

    /// <summary>
    /// Creates the ZIP archive in a worker thread.
    /// </summary>
    /// <param name="archivePath">The temporary archive path.</param>
    /// <param name="workspace">The workspace manifest.</param>
    /// <param name="createdUtc">The backup timestamp.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private void CreateArchive(
            string archivePath,
            WorkspaceManifest workspace,
            DateTimeOffset createdUtc,
            CancellationToken cancellationToken)
    {
        string[] files = Directory
            .EnumerateFiles(
                _workspaceRoot,
                "*",
                SearchOption.AllDirectories)
            .Where(file =>
                ShouldIncludeSourceFile(file))
            .OrderBy(
                file => Path.GetRelativePath(_workspaceRoot, file),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using global::System.IO.FileStream output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);

        using global::System.IO.Compression.ZipArchive archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: false);

        int fileCount = 0;

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = NormalizeArchivePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    file));

            global::System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(
                relativePath,
                CompressionLevel.Optimal);

            using global::System.IO.Stream target = entry.Open();
            using global::System.IO.FileStream source = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                options: FileOptions.SequentialScan);

            source.CopyTo(target);
            fileCount++;
        }

        global::Nodalis.Core.Backups.WorkspaceBackupManifest backupManifest = new WorkspaceBackupManifest
        {
            WorkspaceId = workspace.Id,
            WorkspaceName = workspace.Name,
            WorkspaceSchemaVersion = workspace.SchemaVersion,
            CreatedUtc = createdUtc,
            FileCount = fileCount
        };

        global::System.IO.Compression.ZipArchiveEntry manifestEntry = archive.CreateEntry(
            BackupManifestFileName,
            CompressionLevel.Optimal);

        using global::System.IO.Stream manifestStream = manifestEntry.Open();
        JsonSerializer.Serialize(
            manifestStream,
            backupManifest,
            JsonDefaults.Options);
    }

    /// <summary>
    /// Validates archive structure, metadata and compressed payload integrity.
    /// </summary>
    /// <param name="archivePath">The archive path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    private static WorkspaceBackupInfo ValidateArchiveInternal(
            string archivePath,
            CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(
            archivePath);
        long sizeBytes = File.Exists(fullPath)
            ? new FileInfo(fullPath).Length
            : 0;

        try
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "The backup archive does not exist.",
                    fullPath);
            }

            using global::System.IO.Compression.ZipArchive archive = ZipFile.OpenRead(
                fullPath);

            global::System.Collections.Generic.HashSet<string> names = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (global::System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string normalizedName = ValidateArchiveEntryName(
                    entry.FullName);

                if (!names.Add(normalizedName))
                {
                    throw new InvalidDataException(
                        $"The backup contains duplicate entry '{normalizedName}'.");
                }
            }

            global::System.IO.Compression.ZipArchiveEntry? metadataEntry = archive.Entries
                .SingleOrDefault(entry =>
                    string.Equals(
                        NormalizeArchivePath(entry.FullName),
                        BackupManifestFileName,
                        StringComparison.OrdinalIgnoreCase));

            global::System.IO.Compression.ZipArchiveEntry? workspaceEntry = archive.Entries
                .SingleOrDefault(entry =>
                    string.Equals(
                        NormalizeArchivePath(entry.FullName),
                        WorkspaceLayout.WorkspaceManifestFileName,
                        StringComparison.OrdinalIgnoreCase));

            if (metadataEntry is null ||
                workspaceEntry is null)
            {
                throw new InvalidDataException(
                    "The archive does not contain the required Nodalis backup and workspace manifests.");
            }

            WorkspaceBackupManifest? metadata;

            using (global::System.IO.Stream metadataStream = metadataEntry.Open())
            {
                metadata = JsonSerializer.Deserialize<WorkspaceBackupManifest>(
                    metadataStream,
                    JsonDefaults.Options);
            }

            if (metadata is null ||
                metadata.SchemaVersion != WorkspaceBackupManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "The backup manifest is missing or uses an unsupported schema.");
            }

            WorkspaceManifest? workspace;

            using (global::System.IO.Stream workspaceStream = workspaceEntry.Open())
            {
                workspace = JsonSerializer.Deserialize<WorkspaceManifest>(
                    workspaceStream,
                    JsonDefaults.Options);
            }

            if (workspace is null ||
                workspace.Id != metadata.WorkspaceId ||
                workspace.SchemaVersion != metadata.WorkspaceSchemaVersion ||
                workspace.SchemaVersion > WorkspaceManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "The workspace manifest does not match the backup metadata.");
            }

            int payloadFileCount = archive.Entries.Count(entry =>
                !string.Equals(
                    NormalizeArchivePath(entry.FullName),
                    BackupManifestFileName,
                    StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.EndsWith(
                    "/",
                    StringComparison.Ordinal));

            if (payloadFileCount != metadata.FileCount)
            {
                throw new InvalidDataException(
                    "The backup file count does not match its metadata.");
            }

            foreach (global::System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.FullName.EndsWith(
                        "/",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                using global::System.IO.Stream input = entry.Open();
                input.CopyTo(
                    Stream.Null);
            }

            return new WorkspaceBackupInfo
            {
                ArchivePath = fullPath,
                CreatedUtc = metadata.CreatedUtc,
                SizeBytes = sizeBytes,
                Status = BackupValidationStatus.Valid,
                StatusMessage = "Valide",
                WorkspaceId = metadata.WorkspaceId,
                WorkspaceName = metadata.WorkspaceName
            };
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            JsonException or
            UnauthorizedAccessException)
        {
            return new WorkspaceBackupInfo
            {
                ArchivePath = fullPath,
                CreatedUtc = File.Exists(fullPath)
                    ? File.GetLastWriteTimeUtc(fullPath)
                    : DateTimeOffset.MinValue,
                SizeBytes = sizeBytes,
                Status = BackupValidationStatus.Invalid,
                StatusMessage = exception.Message
            };
        }
    }

    /// <summary>
    /// Extracts a validated archive into a private staging directory.
    /// </summary>
    /// <param name="archivePath">The archive path.</param>
    /// <param name="stagingDirectory">The staging directory.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void ExtractArchive(
            string archivePath,
            string stagingDirectory,
            CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            stagingDirectory);

        string stagingRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(stagingDirectory));

        using global::System.IO.Compression.ZipArchive archive = ZipFile.OpenRead(
            archivePath);

        foreach (global::System.IO.Compression.ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedName = ValidateArchiveEntryName(
                entry.FullName);

            if (string.Equals(
                    normalizedName,
                    BackupManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destinationPath = Path.GetFullPath(
                Path.Combine(
                    stagingRoot,
                    normalizedName.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            if (!IsSameOrChildPath(
                    stagingRoot,
                    destinationPath) ||
                string.Equals(
                    stagingRoot,
                    destinationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' escapes the restore directory.");
            }

            if (entry.FullName.EndsWith(
                    "/",
                    StringComparison.Ordinal))
            {
                Directory.CreateDirectory(
                    destinationPath);
                continue;
            }

            string? parent = Path.GetDirectoryName(
                destinationPath);

            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' has no valid parent.");
            }

            Directory.CreateDirectory(
                parent);

            using global::System.IO.Stream source = entry.Open();
            using global::System.IO.FileStream target = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            source.CopyTo(
                target);
        }
    }

    /// <summary>
    /// Removes older backups after a successful new backup.
    /// </summary>
    /// <param name="destinationDirectory">The backup directory.</param>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="retentionCount">The number of backups to retain.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static Task PruneAsync(
            string destinationDirectory,
            Guid workspaceId,
            int retentionCount,
            CancellationToken cancellationToken)
    {
        string[] oldBackups = Directory
            .EnumerateFiles(
                destinationDirectory,
                GetBackupSearchPattern(workspaceId),
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .Skip(retentionCount)
            .ToArray();

        foreach (string oldBackup in oldBackups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(
                oldBackup);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates that the backup directory cannot recursively capture its own archives.
    /// </summary>
    /// <param name="destinationDirectory">The requested backup directory.</param>
    /// <returns>The normalized backup directory.</returns>
    private string ValidateBackupDestination(
            string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string destination = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(destinationDirectory));

        if (File.Exists(destination))
        {
            throw new IOException(
                "The backup destination is a file.");
        }

        if (IsSameOrChildPath(
                _workspaceRoot,
                destination))
        {
            throw new InvalidOperationException(
                "The backup destination must be outside the workspace.");
        }

        return destination;
    }

    /// <summary>
    /// Validates that restoration targets a separate new or empty directory.
    /// </summary>
    /// <param name="destinationDirectory">The requested restore directory.</param>
    /// <returns>The normalized restore directory.</returns>
    private string ValidateRestoreDestination(
            string destinationDirectory)
    {
        string destination = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(destinationDirectory));

        if (File.Exists(destination))
        {
            throw new BackupRestoreCollisionException(
                destination);
        }

        if (IsSameOrChildPath(
                _workspaceRoot,
                destination))
        {
            throw new InvalidOperationException(
                "A backup must be restored outside the currently open workspace.");
        }

        if (Directory.Exists(destination) &&
            Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new BackupRestoreCollisionException(
                destination);
        }

        return destination;
    }

    /// <summary>
    /// Decides whether a source file is stable workspace content suitable for backup.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <returns><see langword="true"/> when the file should be archived.</returns>
    private bool ShouldIncludeSourceFile(
            string filePath)
    {
        string relativePath = NormalizeArchivePath(
            Path.GetRelativePath(
                _workspaceRoot,
                filePath));

        string fileName = Path.GetFileName(
            filePath);

        if (string.Equals(
                relativePath,
                BackupManifestFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith(
                ".",
                StringComparison.Ordinal) &&
            fileName.EndsWith(
                ".tmp",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string stagingSegment =
            "/" + WorkspaceLayout.ImportStagingDirectoryName + "/";

        return !("/" + relativePath + "/").Contains(
            stagingSegment,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates one portable ZIP entry name against path traversal.
    /// </summary>
    /// <param name="entryName">The ZIP entry name.</param>
    /// <returns>The normalized forward-slash entry name.</returns>
    private static string ValidateArchiveEntryName(
            string entryName)
    {
        string normalized = NormalizeArchivePath(
            entryName);

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith(
                "/",
                StringComparison.Ordinal) ||
            normalized.Contains(
                ':',
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Archive entry '{entryName}' has an invalid path.");
        }

        string[] segments = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"Archive entry '{entryName}' contains path traversal.");
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes a ZIP path to the portable forward-slash form.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizeArchivePath(
            string path) =>
            path
                .Replace(
                    '\\',
                    '/')
                .TrimStart(
                    '/');

    /// <summary>
    /// Returns the archive search pattern for one workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <returns>The file search pattern.</returns>
    private static string GetBackupSearchPattern(
            Guid workspaceId) =>
            $"{BackupFilePrefix}-{workspaceId:N}-*.zip";

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

        return candidate.StartsWith(
            parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
