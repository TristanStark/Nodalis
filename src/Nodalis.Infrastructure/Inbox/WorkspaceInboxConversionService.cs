using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Inbox;

/// <summary>
/// Converts Inbox text into native workspace Markdown. A durable conversion
/// intent plus an embedded origin marker makes retries safe across process
/// interruptions, including a crash after target writing but before state update.
/// </summary>
public sealed class WorkspaceInboxConversionService
{
    private const string IntentFileName = "conversion.json";
    private readonly string _root;
    private readonly WorkspaceInboxService _inbox;

    /// <summary>Initializes a conversion service for an existing workspace.</summary>
    /// <param name="workspaceRoot">Workspace root with manifest.</param>
    public WorkspaceInboxConversionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _inbox = new WorkspaceInboxService(_root);
    }

    /// <summary>
    /// Reads a durable interrupted-conversion intent without creating files.
    /// This lets the UI reopen the exact destination instead of asking users
    /// to remember a mode or path after a crash.
    /// </summary>
    /// <param name="entryId">Capture identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Original conversion choices, or null if not yet attempted.</returns>
    public async Task<InboxConversionRequest?> ReadPreparedConversionAsync(
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("ID de capture Inbox invalide.", nameof(entryId));
        }

        string directory = Path.Combine(_root, WorkspaceLayout.InboxDirectoryName,
            "Items", entryId.ToString("N"));
        string intentFile = Path.Combine(directory, IntentFileName);
        if (!File.Exists(intentFile))
        {
            return null;
        }

        CheckExistingDirectories(directory);
        ConversionIntent intent = await AtomicJsonFile.ReadAsync<ConversionIntent>(
            intentFile, cancellationToken);
        if (intent.EntryId != entryId)
        {
            throw new InvalidDataException(
                "L'intention de conversion ne correspond pas à cette capture.");
        }

        return new InboxConversionRequest
        {
            EntryId = entryId,
            Mode = intent.Mode,
            DestinationRelativeDirectory = intent.DestinationRelativeDirectory,
            NewDocumentName = intent.Mode == InboxConversionMode.NewDocument
                ? Path.GetFileName(intent.TargetRelativePath) : null,
            ExistingDocumentRelativePath =
                intent.Mode == InboxConversionMode.AppendDocument
                    ? intent.TargetRelativePath : null
        };
    }

    /// <summary>
    /// Converts or resumes one capture. Existing targets are only appended to
    /// with revision protection; a new filename is never silently replaced.
    /// </summary>
    /// <param name="request">Explicit user choice of scope and Markdown format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Target path and whether it was already published.</returns>
    public async Task<InboxConversionResult> ConvertAsync(
        InboxConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EntryId == Guid.Empty || !Enum.IsDefined(request.Mode))
        {
            throw new ArgumentException("Invalid Inbox conversion request.", nameof(request));
        }

        InboxReadResult items = await _inbox.ReadAllAsync(cancellationToken);
        InboxEntry entry = items.Entries.FirstOrDefault(item => item.Id == request.EntryId)
            ?? throw new FileNotFoundException("Capture Inbox introuvable ou déplacée.");

        string itemDirectory = Path.Combine(_root, WorkspaceLayout.InboxDirectoryName,
            "Items", entry.Id.ToString("N"));
        CheckExistingDirectories(itemDirectory);
        // FileShare.None serializes double-clicks, concurrent windows and
        // conversions launched by another Nodalis process.
        string lockPath = Path.Combine(itemDirectory, "conversion.lock");
        await using FileStream guard = new FileStream(lockPath,
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            1, FileOptions.Asynchronous);

        string destinationDirectory = ResolveScope(request.DestinationRelativeDirectory);
        string target = ResolveTarget(request, destinationDirectory);
        string targetRelative = Relative(target);
        string intentPath = Path.Combine(itemDirectory, IntentFileName);
        ConversionIntent? prior = File.Exists(intentPath)
            ? await AtomicJsonFile.ReadAsync<ConversionIntent>(intentPath, cancellationToken)
            : null;

        if (prior is null)
        {
            if (entry.ConvertedTargetRelativePath is not null)
            {
                throw new InvalidOperationException(
                    "Cette capture possède déjà une conversion ; aucune autre cible ne sera créée.");
            }

            if (request.Mode == InboxConversionMode.AppendDocument &&
                !File.Exists(target))
            {
                throw new FileNotFoundException("Le document cible n'existe plus.", target);
            }

            if (request.Mode == InboxConversionMode.NewDocument &&
                File.Exists(target))
            {
                throw new IOException("Un document porte déjà ce nom. Choisir un autre nom.");
            }

            prior = new ConversionIntent
            {
                EntryId = entry.Id,
                Mode = request.Mode,
                DestinationRelativeDirectory = Normalize(request.DestinationRelativeDirectory),
                TargetRelativePath = targetRelative
            };
            await AtomicJsonFile.WriteAsync(intentPath, prior, cancellationToken);
        }
        else if (prior.EntryId != entry.Id || prior.Mode != request.Mode ||
                 !string.Equals(prior.DestinationRelativeDirectory,
                     Normalize(request.DestinationRelativeDirectory),
                     StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(prior.TargetRelativePath, targetRelative,
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Une conversion a déjà été préparée pour cette capture. " +
                "Reprendre vers la même destination ; aucune seconde cible ne sera créée.");
        }

        string marker = Marker(entry.Id);
        // An existing target with our marker proves the write happened even
        // if the application stopped just before updating Inbox metadata.
        bool alreadyWritten = false;
        if (File.Exists(target))
        {
            CheckRegularFile(target);
            string existing = await File.ReadAllTextAsync(target, cancellationToken);
            alreadyWritten = existing.Contains(marker, StringComparison.Ordinal);
        }

        if (!alreadyWritten && entry.ConvertedTargetRelativePath is not null)
        {
            // A previously completed target might have been edited externally
            // and lost its origin marker. Never append a second copy silently.
            throw new IOException(
                "La cible d'une conversion terminée ne contient plus son marqueur " +
                "de provenance. Vérifier le document manuellement avant toute reprise.");
        }

        if (!alreadyWritten)
        {
            await CopyAttachmentsAsync(entry, itemDirectory,
                Path.GetDirectoryName(target)!, cancellationToken);
            string fragment = BuildMarkdown(entry, request.Mode,
                Path.GetDirectoryName(target)!, marker);
            if (request.Mode == InboxConversionMode.NewDocument)
            {
                await CreateWithoutOverwriteAsync(target,
                    "# " + Path.GetFileNameWithoutExtension(target) +
                    "\n\n" + fragment, cancellationToken);
            }
            else if (File.Exists(target))
            {
                TextDocumentSession session = await TextDocumentSession.OpenAsync(
                    target, cancellationToken);
                string updated = session.Content.TrimEnd() + "\n\n" + fragment;
                await session.SaveAsync(updated, cancellationToken);
            }
            else if (request.Mode is InboxConversionMode.QuickNote or
                     InboxConversionMode.MarkdownTask)
            {
                string heading = request.Mode == InboxConversionMode.QuickNote
                    ? "# Notes rapides" : "# Tâches";
                await CreateWithoutOverwriteAsync(target,
                    heading + "\n\n" + fragment, cancellationToken);
            }
            else
            {
                throw new IOException(
                    "Le document de destination a disparu. L'Inbox est conservée.");
            }
        }

        // The inbox source remains readable and recoverable after conversion.
        await _inbox.UpdateStateAsync(
            entry.Id, InboxItemState.Processed, targetRelative, cancellationToken);
        return new InboxConversionResult
        {
            TargetRelativePath = targetRelative,
            AlreadyConverted = alreadyWritten
        };
    }

    /// <summary>Previews the exact Markdown fragment for the selected destination.</summary>
    /// <param name="request">User-selected conversion.</param>
    /// <returns>Human-readable Markdown without side effects.</returns>
    public async Task<string> PreviewAsync(InboxConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InboxReadResult read = await _inbox.ReadAllAsync();
        InboxEntry entry = read.Entries.FirstOrDefault(item => item.Id == request.EntryId)
            ?? throw new FileNotFoundException("Capture Inbox introuvable.");
        string destination = ResolveScope(request.DestinationRelativeDirectory);
        string target = ResolveTarget(request, destination);
        return "Destination : " + Relative(target) + "\n\n" +
            BuildMarkdown(entry, request.Mode, Path.GetDirectoryName(target)!,
                Marker(entry.Id));
    }

    /// <summary>Formats traceable native Markdown without changing source text.</summary>
    /// <param name="entry">Inbox source.</param>
    /// <param name="mode">Target format.</param>
    /// <param name="scopeDirectory">Scoped destination.</param>
    /// <param name="marker">Idempotency marker.</param>
    /// <returns>Appendable Markdown fragment.</returns>
    private static string BuildMarkdown(
        InboxEntry entry, InboxConversionMode mode,
        string scopeDirectory, string marker)
    {
        string content = entry.ContentMarkdown.Trim();
        string origin = string.IsNullOrWhiteSpace(entry.Origin)
            ? "" : "\n\n_Source : " + entry.Origin.Replace("\r", " ").Replace("\n", " ") + "_";
        StringBuilder links = new StringBuilder();
        if (entry.Attachments.Count > 0)
        {
            links.Append("\n\nPièces jointes :");
            foreach (InboxAttachment attachment in entry.Attachments)
            {
                string filename = Path.GetFileName(attachment.RelativePath);
                string filepath = Path.Combine(scopeDirectory, "Attachments",
                    "Inbox-" + entry.Id.ToString("N"), filename);
                string link = "Attachments/Inbox-" + entry.Id.ToString("N") +
                    "/" + Uri.EscapeDataString(filename);
                links.Append("\n- [").Append(
                    attachment.OriginalFileName.Replace("[", "\\[").Replace("]", "\\]"))
                    .Append("](").Append(link).Append(')');
            }
        }

        if (mode == InboxConversionMode.MarkdownTask)
        {
            string first = content.Split(['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim().TrimStart('#', '-', ' ', '*') ?? "Capture Inbox";
            if (first.Length == 0)
            {
                first = "Capture Inbox";
            }

            first = first.Replace("[", "(").Replace("]", ")");
            if (first.Length > 180)
            {
                first = first[..180];
            }

            // The standard task parser recognizes this checkbox in normal
            // project Markdown, without a second Inbox-specific task manager.
            return "- [ ] " + first + "\n\n" + marker + "\n\n" +
                (content.Length == 0 ? "" : "> " +
                    content.Replace("\r\n", "\n").Replace("\n", "\n> ") + "\n") +
                origin + links + "\n";
        }

        return "## Inbox · " + entry.CreatedUtc.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm") + "\n\n" +
            content + origin + links + "\n\n" + marker + "\n";
    }

    /// <summary>Returns a stable cross-restart origin/idempotency tag.</summary>
    /// <param name="id">Capture ID.</param>
    /// <returns>Non-executable HTML comment.</returns>
    private static string Marker(Guid id) =>
        "<!-- nodalis-inbox:" + id.ToString("N") + " -->";

    /// <summary>Resolves a Global, Application or Project directory.</summary>
    /// <param name="relative">Workspace-relative directory.</param>
    /// <returns>Verified existing scope directory.</returns>
    private string ResolveScope(string relative)
    {
        string path = ResolveSafePath(relative, allowRoot: true);
        CheckExistingDirectories(path);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("Destination Inbox inexistante : " + relative);
        }

        if (!string.Equals(path, _root, StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(Path.Combine(path, WorkspaceLayout.ApplicationManifestFileName)) &&
            !File.Exists(Path.Combine(path, WorkspaceLayout.ProjectManifestFileName)))
        {
            throw new InvalidDataException(
                "La destination doit être Global, une application ou un projet Nodalis.");
        }

        return path;
    }

    /// <summary>Computes the exact target according to the chosen native format.</summary>
    /// <param name="request">Conversion choices.</param>
    /// <param name="scope">Verified scope.</param>
    /// <returns>Absolute Markdown file path.</returns>
    private string ResolveTarget(InboxConversionRequest request, string scope)
    {
        string target = request.Mode switch
        {
            InboxConversionMode.QuickNote => Path.Combine(
                scope, WorkspaceLayout.GlobalQuickNotesFileName),
            InboxConversionMode.MarkdownTask => Path.Combine(scope, "Tâches.md"),
            InboxConversionMode.NewDocument => Path.Combine(
                scope, SafeDocumentName(request.NewDocumentName)),
            InboxConversionMode.AppendDocument => ResolveSafePath(
                request.ExistingDocumentRelativePath ?? string.Empty, allowRoot: false),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        target = Path.GetFullPath(target);
        if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La cible doit être un fichier Markdown.");
        }

        string rootPrefix = Path.TrimEndingDirectorySeparator(scope) +
            Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La cible doit appartenir à la destination choisie.");
        }

        CheckExistingDirectories(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            CheckRegularFile(target);
        }

        return target;
    }

    /// <summary>Rejects separators, traversal and reserved names in new filenames.</summary>
    /// <param name="name">User label.</param>
    /// <returns>Validated Markdown filename.</returns>
    private static string SafeDocumentName(string? name)
    {
        string value = name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.Contains('/') || value.Contains('\\') || value.Contains(':') ||
            value.EndsWith('.') || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("Saisir un nom de fichier Markdown valide.");
        }

        if (!value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            value += ".md";
        }

        return value;
    }

    /// <summary>Rejects escaping, redirected or ambiguous workspace paths.</summary>
    /// <param name="relative">User-selected relative path.</param>
    /// <param name="allowRoot">Whether empty means workspace Global.</param>
    /// <returns>Canonical path inside the workspace.</returns>
    private string ResolveSafePath(string relative, bool allowRoot)
    {
        if (relative is null || (!allowRoot && string.IsNullOrWhiteSpace(relative)))
        {
            throw new InvalidDataException("Chemin Inbox vide.");
        }

        if (allowRoot && string.IsNullOrWhiteSpace(relative))
        {
            return _root;
        }

        if (Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split(['/', '\\']).Any(segment =>
                segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.')))
        {
            throw new InvalidDataException("Chemin Inbox ambigu ou non sûr.");
        }

        string full = Path.GetFullPath(Path.Combine(_root, relative));
        string prefix = _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La cible sort du workspace.");
        }

        return full;
    }

    /// <summary>Prevents traversing directory junctions in destination paths.</summary>
    /// <param name="path">Workspace-owned directory.</param>
    private void CheckExistingDirectories(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string cursor = full;
        while (cursor.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(cursor) &&
                (new DirectoryInfo(cursor).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("La destination contient une jonction ou un lien symbolique.");
            }

            if (string.Equals(cursor, _root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            string? parent = Path.GetDirectoryName(cursor);
            if (parent is null || parent.Length >= cursor.Length)
            {
                throw new IOException("Hiérarchie de destination invalide.");
            }

            cursor = parent;
        }
    }

    /// <summary>Rejects file system redirects at target.</summary>
    /// <param name="path">Existing Markdown file.</param>
    private static void CheckRegularFile(string path)
    {
        if ((new FileInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Un lien symbolique ne peut pas être une cible Inbox.");
        }
    }

    /// <summary>Relative target reference saved in Inbox metadata.</summary>
    /// <param name="path">Absolute target.</param>
    /// <returns>Forward-slash workspace path.</returns>
    private string Relative(string path) =>
        Path.GetRelativePath(_root, path).Replace('\\', '/');

    /// <summary>Normalizes root-relative scope for durable intent comparison.</summary>
    /// <param name="path">User-provided relative scope.</param>
    /// <returns>Normalized scope string.</returns>
    private static string Normalize(string path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim('/');

    /// <summary>
    /// Publishes a fresh Markdown target atomically and without overwriting a
    /// file created by somebody else after the user's preview.
    /// </summary>
    /// <param name="target">Destination file.</param>
    /// <param name="content">Complete Markdown.</param>
    /// <param name="token">Cancellation.</param>
    private static async Task CreateWithoutOverwriteAsync(
        string target, string content, CancellationToken token)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (StreamWriter writer = new StreamWriter(
                stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(content.AsMemory(), token);
                await writer.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, target); // no overwrite
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// Copies captured attachments as stable workspace-local assets. Identical
    /// files are accepted after restart; edited/colliding copies are never replaced.
    /// </summary>
    /// <param name="entry">Capture.</param>
    /// <param name="itemDirectory">Capture source directory.</param>
    /// <param name="scope">Destination scope.</param>
    /// <param name="token">Cancellation.</param>
    private async Task CopyAttachmentsAsync(
        InboxEntry entry, string itemDirectory, string scope,
        CancellationToken token)
    {
        if (entry.Attachments.Count == 0)
        {
            return;
        }

        string directory = Path.Combine(
            scope, "Attachments", "Inbox-" + entry.Id.ToString("N"));
        CheckExistingDirectories(directory);
        Directory.CreateDirectory(directory);
        CheckExistingDirectories(directory);
        foreach (InboxAttachment attachment in entry.Attachments)
        {
            token.ThrowIfCancellationRequested();
            string source = Path.Combine(itemDirectory,
                attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(directory,
                Path.GetFileName(attachment.RelativePath));
            if (!File.Exists(source) ||
                (new FileInfo(source).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Pièce jointe Inbox absente ou redirigée : " +
                    attachment.OriginalFileName);
            }

            if (File.Exists(destination))
            {
                CheckRegularFile(destination);
                byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source, token));
                byte[] targetHash = SHA256.HashData(await File.ReadAllBytesAsync(destination, token));
                if (!sourceHash.SequenceEqual(targetHash))
                {
                    throw new IOException("Une pièce jointe cible existe avec un contenu différent.");
                }

                continue;
            }

            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream input = new FileStream(source,
                    FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                await using (FileStream output = new FileStream(temporary,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await input.CopyToAsync(output, token);
                    await output.FlushAsync(token);
                }

                File.Move(temporary, destination); // no overwrite
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    /// <summary>Durable intent recorded before touching the target document.</summary>
    private sealed record ConversionIntent
    {
        public required Guid EntryId { get; init; }
        public required InboxConversionMode Mode { get; init; }
        public required string DestinationRelativeDirectory { get; init; }
        public required string TargetRelativePath { get; init; }
    }
}
