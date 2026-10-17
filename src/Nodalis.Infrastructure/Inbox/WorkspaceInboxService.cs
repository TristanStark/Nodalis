using System.Text.Json;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Inbox;

/// <summary>
/// Stores explicit captures as individually recoverable Markdown/JSON directories.
/// No Inbox directory or workspace migration is created until the first capture.
/// </summary>
public sealed class WorkspaceInboxService
{
    private const string MetadataFileName = "item.json";
    private const string MarkdownFileName = "capture.md";
    private static readonly TimeSpan MaximumDuplicateWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan DefaultDuplicateWindow = TimeSpan.FromDays(7);

    private readonly string _workspaceRoot;
    private readonly string _inboxRoot;
    private readonly string _itemsRoot;
    private readonly string _stagingRoot;
    private readonly string _quarantineRoot;
    private readonly string _trashRoot;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    /// <summary>Creates an Inbox store bound to a single existing workspace.</summary>
    /// <param name="workspaceRoot">Root containing the Nodalis workspace manifest.</param>
    public WorkspaceInboxService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _inboxRoot = Path.Combine(_workspaceRoot, WorkspaceLayout.InboxDirectoryName);
        _itemsRoot = Path.Combine(_inboxRoot, "Items");
        _stagingRoot = Path.Combine(_inboxRoot, ".staging");
        _quarantineRoot = Path.Combine(_inboxRoot, "Quarantaine");
        _trashRoot = Path.Combine(_inboxRoot, "Corbeille");
    }

    /// <summary>
    /// Commits a new capture using a complete staging directory and an atomic directory rename.
    /// A crash before the rename leaves recoverable staging data, not a partially visible item.
    /// </summary>
    /// <param name="request">Content and explicitly requested attachments.</param>
    /// <param name="cancellationToken">Cancellation before publication leaves staging for recovery.</param>
    /// <returns>The published capture with stable identifier.</returns>
    public async Task<InboxEntry> CaptureAsync(
            InboxCaptureRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ContentMarkdown);
        ArgumentNullException.ThrowIfNull(request.AttachmentSourcePaths);

        if (!Enum.IsDefined(request.Kind) ||
            request.AttachmentSourcePaths.Count > InboxAttachmentRules.MaximumFileCount)
        {
            throw new ArgumentException("Invalid capture type or too many attachments.", nameof(request));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureWorkspace();
            EnsureInboxDirectories();

            Guid id = Guid.NewGuid();
            string stagedPath = Path.Combine(_stagingRoot, id.ToString("N"));
            string publishedPath = Path.Combine(_itemsRoot, id.ToString("N"));
            Directory.CreateDirectory(stagedPath);

            List<InboxAttachment> attachments = new List<InboxAttachment>();
            foreach (string sourcePath in request.AttachmentSourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attachments.Add(await CopyAttachmentAsync(
                    sourcePath, stagedPath, cancellationToken));
            }

            InboxEntry entry = new InboxEntry
            {
                Id = id,
                CreatedUtc = DateTimeOffset.UtcNow,
                Kind = request.Kind,
                Origin = request.Origin,
                Attachments = attachments,
                ContentMarkdown = request.ContentMarkdown,
                FingerprintSha256 = await ComputeStoredFingerprintAsync(
                    request.Kind, request.ContentMarkdown, stagedPath,
                    attachments, cancellationToken)
            };

            await AtomicFileWriter.WriteAllTextAsync(
                Path.Combine(stagedPath, MarkdownFileName),
                entry.ContentMarkdown,
                cancellationToken);
            await AtomicJsonFile.WriteAsync(
                Path.Combine(stagedPath, MetadataFileName),
                entry,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Directory.Move(stagedPath, publishedPath);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Finds recent probable duplicates by content, kind and attached file
    /// SHA-256. Never blocks an explicit Save and never persists preview data.
    /// Includes recoverable trash and supports pre-hash legacy captures.
    /// </summary>
    /// <param name="request">User-confirmed contents to compare.</param>
    /// <param name="window">Optional window, 7 days by default and 30 maximum.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Recent matching IDs and states, newest first.</returns>
    public async Task<IReadOnlyList<InboxDuplicateCandidate>> FindProbableDuplicatesAsync(
        InboxCaptureRequest request,
        TimeSpan? window = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ContentMarkdown);
        ArgumentNullException.ThrowIfNull(request.AttachmentSourcePaths);
        TimeSpan horizon = window ?? DefaultDuplicateWindow;
        if (horizon <= TimeSpan.Zero || horizon > MaximumDuplicateWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(window),
                "La fenêtre de doublons doit être comprise entre 0 et 30 jours.");
        }

        if (!Enum.IsDefined(request.Kind) ||
            request.AttachmentSourcePaths.Count > InboxAttachmentRules.MaximumFileCount)
        {
            throw new ArgumentException("Capture Inbox non prise en charge.", nameof(request));
        }

        string fingerprint = await InboxDuplicateFingerprint.ComputeAsync(
            request.Kind, request.ContentMarkdown,
            request.AttachmentSourcePaths.Select(path =>
                (Path.GetFileName(path), path)).ToArray(), cancellationToken);

        InboxReadResult active = await ReadAllAsync(cancellationToken);
        InboxReadResult trash = await ReadTrashAsync(cancellationToken);
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - horizon;
        List<InboxDuplicateCandidate> matches = new List<InboxDuplicateCandidate>();
        foreach ((InboxEntry entry, bool inTrash) in
            active.Entries.Select(entry => (entry, false))
                .Concat(trash.Entries.Select(entry => (entry, true))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Kind != request.Kind || entry.CreatedUtc < cutoff)
            {
                continue;
            }

            string? previous = entry.FingerprintSha256;
            if (previous is null)
            {
                string folder = Path.Combine(
                    inTrash ? _trashRoot : _itemsRoot, entry.Id.ToString("N"));
                try
                {
                    previous = await ComputeStoredFingerprintAsync(
                        entry.Kind, entry.ContentMarkdown, folder,
                        entry.Attachments, cancellationToken);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or
                        UnauthorizedAccessException)
                {
                    // Preserve older incomplete captures; missing attachments
                    // cannot prove a match and are warned about by ReadAllAsync.
                    continue;
                }
            }

            if (string.Equals(previous, fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new InboxDuplicateCandidate
                {
                    Id = entry.Id,
                    CreatedUtc = entry.CreatedUtc,
                    State = entry.State,
                    InTrash = inTrash
                });
            }
        }

        return matches.OrderByDescending(candidate => candidate.CreatedUtc).ToArray();
    }

    /// <summary>Hashes published or staged copies by original name and bytes.</summary>
    /// <param name="kind">Capture kind.</param>
    /// <param name="markdown">Explicit content.</param>
    /// <param name="itemDirectory">Folder containing attachments.</param>
    /// <param name="attachments">Validated attachment metadata.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Canonical content digest.</returns>
    private static Task<string> ComputeStoredFingerprintAsync(
        InboxCaptureKind kind, string markdown, string itemDirectory,
        IReadOnlyList<InboxAttachment> attachments, CancellationToken token) =>
        InboxDuplicateFingerprint.ComputeAsync(kind, markdown,
            attachments.Select(attachment =>
                (attachment.OriginalFileName,
                    Path.Combine(itemDirectory,
                        attachment.RelativePath.Replace('/',
                            Path.DirectorySeparatorChar)))).ToArray(), token);

    /// <summary>
    /// Loads captures, promotes complete interrupted writes and quarantines invalid entries.
    /// Missing attachments generate visible warnings without discarding readable text.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sorted captures and recovery diagnostics.</returns>
    public async Task<InboxReadResult> ReadAllAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<InboxEntry> entries = new List<InboxEntry>();
            List<InboxWarning> warnings = new List<InboxWarning>();
            if (!Directory.Exists(_inboxRoot))
            {
                return new InboxReadResult { Entries = entries, Warnings = warnings };
            }

            CheckManagedDirectory(_inboxRoot);
            if (Directory.Exists(_itemsRoot))
            {
                CheckManagedDirectory(_itemsRoot);
            }

            await RecoverStagingAsync(warnings, cancellationToken);

            if (Directory.Exists(_itemsRoot))
            {
                foreach (string directory in Directory.EnumerateDirectories(_itemsRoot))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        entries.Add(await ReadEntryAsync(directory, warnings, cancellationToken));
                    }
                    catch (Exception exception) when (IsDamagedEntry(exception))
                    {
                        await QuarantineAsync(directory, exception.Message, warnings, cancellationToken);
                    }
                }
            }

            return new InboxReadResult
            {
                Entries = entries.OrderByDescending(entry => entry.CreatedUtc)
                    .ThenBy(entry => entry.Id).ToArray(),
                Warnings = warnings
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lists recoverable items without modifying or silently dropping damaged trash.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trashed entries and any diagnostics.</returns>
    public async Task<InboxReadResult> ReadTrashAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<InboxEntry> entries = new List<InboxEntry>();
            List<InboxWarning> warnings = new List<InboxWarning>();
            if (!Directory.Exists(_trashRoot))
            {
                return new InboxReadResult { Entries = entries, Warnings = warnings };
            }

            CheckManagedDirectory(_inboxRoot);
            CheckManagedDirectory(_trashRoot);
            foreach (string directory in Directory.EnumerateDirectories(_trashRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    entries.Add(await ReadEntryAsync(directory, warnings, cancellationToken));
                }
                catch (Exception exception) when (IsDamagedEntry(exception))
                {
                    warnings.Add(new InboxWarning
                    {
                        Item = Path.GetFileName(directory),
                        Message = "Capture en corbeille illisible : " + exception.Message
                    });
                }
            }

            return new InboxReadResult
            {
                Entries = entries.OrderByDescending(entry => entry.CreatedUtc).ToArray(),
                Warnings = warnings
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Atomically updates lifecycle state and optional conversion provenance.</summary>
    /// <param name="id">Stable capture ID.</param>
    /// <param name="state">New lifecycle state.</param>
    /// <param name="convertedTargetRelativePath">Optional workspace-relative target document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated entry.</returns>
    public async Task<InboxEntry> UpdateStateAsync(
            Guid id,
            InboxItemState state,
            string? convertedTargetRelativePath = null,
            CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (convertedTargetRelativePath is not null)
        {
            ValidateWorkspaceRelativePath(convertedTargetRelativePath);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            CheckManagedDirectory(_inboxRoot);
            CheckManagedDirectory(_itemsRoot);
            string directory = GetItemDirectory(_itemsRoot, id);
            InboxEntry existing = await ReadEntryAsync(
                directory, new List<InboxWarning>(), cancellationToken);
            InboxEntry updated = existing with
            {
                State = state,
                ConvertedTargetRelativePath =
                    convertedTargetRelativePath ?? existing.ConvertedTargetRelativePath
            };
            await AtomicJsonFile.WriteAsync(
                Path.Combine(directory, MetadataFileName), updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Moves one capture and its attachments to the recoverable Inbox trash.</summary>
    /// <param name="id">Capture ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the move.</returns>
    public async Task MoveToTrashAsync(
            Guid id,
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInboxDirectories();
            string source = GetItemDirectory(_itemsRoot, id);
            await ReadEntryAsync(source, new List<InboxWarning>(), cancellationToken);
            string destination = GetItemDirectory(_trashRoot, id);
            if (Directory.Exists(destination))
            {
                throw new IOException("An item with this ID is already in the Inbox trash.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(source, destination);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Restores a trashed capture without overwriting an existing item.</summary>
    /// <param name="id">Capture ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The restored capture.</returns>
    public async Task<InboxEntry> RestoreAsync(
            Guid id,
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CheckManagedDirectory(_inboxRoot);
            CheckManagedDirectory(_trashRoot);
            string source = GetItemDirectory(_trashRoot, id);
            InboxEntry entry = await ReadEntryAsync(
                source, new List<InboxWarning>(), cancellationToken);
            string destination = GetItemDirectory(_itemsRoot, id);
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new IOException("Restoration would overwrite an existing Inbox capture.");
            }

            Directory.CreateDirectory(_itemsRoot);
            CheckManagedDirectory(_itemsRoot);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(source, destination);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Copies one explicitly selected file into the unpublished capture directory.</summary>
    /// <param name="sourcePath">Source file selected by the user.</param>
    /// <param name="stagedPath">Unpublished destination directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Workspace-local reference to the copy.</returns>
    private static async Task<InboxAttachment> CopyAttachmentAsync(
            string sourcePath,
            string stagedPath,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        FileInfo source = new FileInfo(Path.GetFullPath(sourcePath));
        if (!source.Exists || (source.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Attachment does not exist or is a symbolic link.");
        }

        if (source.Length > InboxAttachmentRules.MaximumFileBytes)
        {
            throw new IOException("Attachment exceeds the 25 MiB limit.");
        }

        InboxAttachmentRules.ValidateFileName(source.Name);
        string extension = source.Extension;

        string relativePath = "attachments/" + Guid.NewGuid().ToString("N") + extension;
        string attachmentsDirectory = Path.Combine(stagedPath, "attachments");
        Directory.CreateDirectory(attachmentsDirectory);
        string destinationPath = Path.Combine(
            attachmentsDirectory, Path.GetFileName(relativePath));

        await using (FileStream input = new FileStream(
            source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous))
        await using (FileStream output = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            if (output.Length > InboxAttachmentRules.MaximumFileBytes)
            {
                throw new IOException("Attachment grew beyond the 25 MiB limit during import.");
            }
        }

        return new InboxAttachment
        {
            OriginalFileName = source.Name,
            RelativePath = relativePath,
            SizeBytes = new FileInfo(destinationPath).Length
        };
    }

    /// <summary>Recovers complete captures interrupted before publication.</summary>
    /// <param name="warnings">Diagnostics collected for the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing recovery.</returns>
    private async Task RecoverStagingAsync(
            List<InboxWarning> warnings,
            CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_stagingRoot))
        {
            return;
        }

        CheckManagedDirectory(_stagingRoot);
        Directory.CreateDirectory(_itemsRoot);
        foreach (string directory in Directory.EnumerateDirectories(_stagingRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                InboxEntry entry = await ReadEntryAsync(
                    directory, warnings, cancellationToken);
                string destination = GetItemDirectory(_itemsRoot, entry.Id);
                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    throw new IOException("Recovered capture ID already exists.");
                }

                Directory.Move(directory, destination);
                warnings.Add(new InboxWarning
                {
                    Item = entry.Id.ToString("N"),
                    Message = "Capture interrompue récupérée."
                });
            }
            catch (Exception exception) when (IsDamagedEntry(exception))
            {
                await QuarantineAsync(directory, exception.Message, warnings, cancellationToken);
            }
        }
    }

    /// <summary>Reads one canonical directory and validates every stored reference.</summary>
    /// <param name="directory">Entry directory.</param>
    /// <param name="warnings">Non-fatal missing-attachment diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated capture.</returns>
    private async Task<InboxEntry> ReadEntryAsync(
            string directory,
            List<InboxWarning> warnings,
            CancellationToken cancellationToken)
    {
        CheckManagedDirectory(directory);
        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out Guid directoryId))
        {
            throw new InvalidDataException("Inbox item directory has no valid GUID.");
        }

        string metadataPath = Path.Combine(directory, MetadataFileName);
        string markdownPath = Path.Combine(directory, MarkdownFileName);
        CheckRegularFile(metadataPath);
        CheckRegularFile(markdownPath);

        InboxEntry entry = await AtomicJsonFile.ReadAsync<InboxEntry>(
            metadataPath, cancellationToken);
        if (entry.SchemaVersion != InboxEntry.CurrentSchemaVersion ||
            entry.Id == Guid.Empty || entry.Id != directoryId ||
            entry.CreatedUtc == default ||
            !Enum.IsDefined(entry.Kind) || !Enum.IsDefined(entry.State) ||
            entry.Attachments is null ||
            (entry.FingerprintSha256 is not null &&
             (entry.FingerprintSha256.Length != 64 ||
              !entry.FingerprintSha256.All(char.IsAsciiHexDigit))))
        {
            throw new InvalidDataException("Invalid Inbox schema, identity or state.");
        }

        if (entry.ConvertedTargetRelativePath is not null)
        {
            ValidateWorkspaceRelativePath(entry.ConvertedTargetRelativePath);
        }

        foreach (InboxAttachment attachment in entry.Attachments)
        {
            if (attachment is null ||
                string.IsNullOrWhiteSpace(attachment.OriginalFileName) ||
                attachment.SizeBytes < 0 ||
                !attachment.RelativePath.StartsWith("attachments/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid Inbox attachment metadata.");
            }

            string relativePath = ValidateRelativePath(attachment.RelativePath, directory);
            string fullPath = Path.Combine(directory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                warnings.Add(new InboxWarning
                {
                    Item = entry.Id.ToString("N"),
                    Message = "Pièce jointe manquante : " + attachment.OriginalFileName
                });
            }
            else
            {
                CheckManagedDirectory(Path.GetDirectoryName(fullPath)!);
                CheckRegularFile(fullPath);
            }
        }

        string markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
        return entry with { ContentMarkdown = markdown };
    }

    /// <summary>Quarantines an untrusted or incomplete directory without deleting its data.</summary>
    /// <param name="directory">Directory to isolate.</param>
    /// <param name="reason">Reason displayed to the user.</param>
    /// <param name="warnings">Collected diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the quarantine operation.</returns>
    private async Task QuarantineAsync(
            string directory,
            string reason,
            List<InboxWarning> warnings,
            CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_quarantineRoot);
        CheckManagedDirectory(_quarantineRoot);
        string name = Path.GetFileName(directory);
        string destination = Path.Combine(_quarantineRoot,
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") +
            "-" + Guid.NewGuid().ToString("N"));
        Directory.Move(directory, destination);
        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(destination, "diagnostic.txt"), reason, cancellationToken);
        warnings.Add(new InboxWarning
        {
            Item = name,
            Message = "Capture isolée dans Inbox/Quarantaine : " + reason
        });
    }

    /// <summary>Validates a workspace-relative conversion reference.</summary>
    /// <param name="relativePath">Potentially untrusted reference.</param>
    private void ValidateWorkspaceRelativePath(string relativePath)
    {
        ValidateRelativePath(relativePath, _workspaceRoot);
    }

    /// <summary>Rejects absolute, traversing, ambiguous and escaping relative paths.</summary>
    /// <param name="relativePath">Potentially untrusted relative path.</param>
    /// <param name="root">Directory that must contain the resolved reference.</param>
    /// <returns>The original safe relative path.</returns>
    private static string ValidateRelativePath(string relativePath, string root)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':') ||
            relativePath.Split(['/', '\\']).Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.')))
        {
            throw new InvalidDataException("Inbox reference contains an unsafe relative path.");
        }

        string full = Path.GetFullPath(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)));
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Inbox reference escapes the workspace.");
        }

        return relativePath;
    }

    /// <summary>Validates the exact managed directory and rejects directory junctions.</summary>
    /// <param name="directory">Directory to inspect.</param>
    private static void CheckManagedDirectory(string directory)
    {
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Inbox directory is missing or redirected.");
        }
    }

    /// <summary>Rejects missing files and symbolic links.</summary>
    /// <param name="path">Expected regular file.</param>
    private static void CheckRegularFile(string path)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Inbox content is missing or redirected.");
        }
    }

    /// <summary>Returns a GUID-only path under a managed collection.</summary>
    /// <param name="root">Collection root.</param>
    /// <param name="id">Capture ID.</param>
    /// <returns>Directory path for the entry.</returns>
    private static string GetItemDirectory(string root, Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A capture ID must not be empty.", nameof(id));
        }

        return Path.Combine(root, id.ToString("N"));
    }

    /// <summary>Checks that the selected directory is an existing Nodalis workspace.</summary>
    private void EnsureWorkspace()
    {
        if (!File.Exists(Path.Combine(
            _workspaceRoot, WorkspaceLayout.WorkspaceManifestFileName)))
        {
            throw new InvalidOperationException("Inbox captures require an existing workspace.");
        }
    }

    /// <summary>Creates the Inbox collection lazily and rejects redirected folders.</summary>
    private void EnsureInboxDirectories()
    {
        Directory.CreateDirectory(_inboxRoot);
        CheckManagedDirectory(_inboxRoot);
        Directory.CreateDirectory(_itemsRoot);
        CheckManagedDirectory(_itemsRoot);
        Directory.CreateDirectory(_stagingRoot);
        CheckManagedDirectory(_stagingRoot);
        Directory.CreateDirectory(_trashRoot);
        CheckManagedDirectory(_trashRoot);
    }

    /// <summary>Determines which read failures indicate a corrupt entry, not cancellation.</summary>
    /// <param name="exception">Observed read or validation exception.</param>
    /// <returns>Whether the entry should be isolated rather than silently skipped.</returns>
    private static bool IsDamagedEntry(Exception exception)
    {
        return exception is JsonException or InvalidDataException or IOException or
            UnauthorizedAccessException;
    }
}
