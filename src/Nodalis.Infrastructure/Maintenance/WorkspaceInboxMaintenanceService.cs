using System.Security.Cryptography;
using Nodalis.Core.Inbox;
using Nodalis.Core.Maintenance;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Maintenance;

/// <summary>
/// Idempotent, conflict-checked conversion of existing Inbox captures into
/// structured maintenance records without deleting or migrating the source.
/// </summary>
public sealed class WorkspaceInboxMaintenanceService
{
    private const string IntentFileName = "maintenance-conversion.json";
    private readonly string _workspaceRoot;
    private readonly WorkspaceInboxService _inbox;

    /// <summary>Creates a workspace-local converter without modifying the workspace.</summary>
    /// <param name="workspaceRoot">Canonical workspace path.</param>
    public WorkspaceInboxMaintenanceService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _inbox = new WorkspaceInboxService(_workspaceRoot);
    }

    /// <summary>Reads a previously prepared request to resume the exact same target.</summary>
    /// <param name="entryId">Inbox source identity.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Locked choices or null before conversion starts.</returns>
    public async Task<MaintenanceInboxRequest?> ReadPreparedAsync(
        Guid entryId, CancellationToken token = default)
    {
        string itemDir = ItemDirectory(entryId);
        string file = Path.Combine(itemDir, IntentFileName);
        if (!File.Exists(file))
        {
            return null;
        }

        CheckSafeDirectories(itemDir);
        return await AtomicJsonFile.ReadAsync<MaintenanceInboxRequest>(file, token);
    }

    /// <summary>Projects the result, checking the project's existing maintenance sheet.</summary>
    /// <param name="request">User-selected choices.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Complete resulting Markdown; no file is modified.</returns>
    public async Task<string> PreviewAsync(
        MaintenanceInboxRequest request, CancellationToken token = default)
    {
        InboxEntry entry = await LoadEntryAsync(request.EntryId, token);
        string target = TargetPath(request.ProjectRelativePath);
        CheckSafeFile(target);
        TextDocumentSession document = await TextDocumentSession.OpenAsync(target, token);
        MaintenanceRecord? incident = SelectIncident(document.Content, request);
        return MaintenanceInboxMarkdown.Append(document.Content, entry, request, incident);
    }

    /// <summary>
    /// Converts or resumes safely. A durable intent and a stable source marker
    /// prevent duplicates even if saving Inbox state fails after the target is written.
    /// </summary>
    /// <param name="request">Confirmed project and record choices.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Existing or newly added maintenance source and record ID.</returns>
    public async Task<MaintenanceInboxResult> ConvertAsync(
        MaintenanceInboxRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        InboxEntry entry = await LoadEntryAsync(request.EntryId, token);
        string itemDir = ItemDirectory(entry.Id);
        CheckSafeDirectories(itemDir);
        await using FileStream guard = new FileStream(
            Path.Combine(itemDir, "conversion.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
            FileOptions.Asynchronous);

        if (File.Exists(Path.Combine(itemDir, "conversion.json")))
        {
            throw new InvalidOperationException(
                "Cette capture possède une conversion Inbox standard. Reprenez-la dans Convertir.");
        }

        string target = TargetPath(request.ProjectRelativePath);
        string relative = Path.GetRelativePath(_workspaceRoot, target).Replace('\\', '/');
        string intentPath = Path.Combine(itemDir, IntentFileName);
        MaintenanceInboxRequest? prepared = File.Exists(intentPath)
            ? await AtomicJsonFile.ReadAsync<MaintenanceInboxRequest>(intentPath, token)
            : null;

        if (prepared is not null && prepared != request)
        {
            throw new InvalidOperationException(
                "Une conversion de maintenance a déjà été préparée. " +
                "Reprenez avec le même projet, mode, incident et résumé.");
        }

        CheckSafeFile(target);
        TextDocumentSession session = await TextDocumentSession.OpenAsync(target, token);
        string marker = MaintenanceInboxMarkdown.Marker(entry.Id);
        bool alreadyWritten = session.Content.Contains(marker, StringComparison.Ordinal);
        if (entry.ConvertedTargetRelativePath is not null &&
            (!alreadyWritten || !string.Equals(relative,
                entry.ConvertedTargetRelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException(
                "Cette capture est déjà convertie ou sa provenance a été modifiée. " +
                "Aucune nouvelle conversion n'a été créée.");
        }

        if (alreadyWritten && prepared is null)
        {
            throw new InvalidDataException(
                "Un bloc ayant cet identifiant Inbox est déjà présent sans intention de " +
                "conversion : contrôler la fiche manuellement.");
        }

        string resultId = request.Mode == MaintenanceInboxMode.Incident
            ? MaintenanceInboxMarkdown.StableId(entry.Id, MaintenanceRecordKind.Incident)
            : MaintenanceInboxMarkdown.StableId(entry.Id, MaintenanceRecordKind.Intervention);
        if (!alreadyWritten)
        {
            MaintenanceRecord? incident = SelectIncident(session.Content, request);
            string updated = MaintenanceInboxMarkdown.Append(
                session.Content, entry, request, incident);
            if (prepared is null)
            {
                await AtomicJsonFile.WriteAsync(intentPath, request, token);
            }

            // Copy files only after preview validation. Every copy is collision-checked,
            // so retries may safely resume after an interrupted copy.
            await CopyAttachmentsAsync(entry, itemDir,
                Path.GetDirectoryName(target)!, token);
            await session.SaveAsync(updated, token);
        }

        await _inbox.UpdateStateAsync(
            entry.Id, InboxItemState.Processed, relative, token);
        return new MaintenanceInboxResult(relative, resultId, alreadyWritten);
    }

    /// <summary>Loads the real, active capture rather than trusting a stale UI snapshot.</summary>
    /// <param name="entryId">Inbox ID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Current canonical source.</returns>
    private async Task<InboxEntry> LoadEntryAsync(Guid entryId, CancellationToken token)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("ID de capture absent.");
        }

        InboxReadResult result = await _inbox.ReadAllAsync(token);
        return result.Entries.SingleOrDefault(item => item.Id == entryId)
            ?? throw new FileNotFoundException("La capture a été supprimée ou déplacée.");
    }

    /// <summary>Resolves a project containing an existing maintenance sheet.</summary>
    /// <param name="relative">Workspace-relative project directory.</param>
    /// <returns>Verified existing file path.</returns>
    private string TargetPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains(':') ||
            relative.Split(['/', '\\']).Any(part => part.Length == 0 ||
                part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')))
        {
            throw new InvalidDataException("Projet de destination invalide.");
        }

        string project = Path.GetFullPath(Path.Combine(_workspaceRoot, relative));
        if (!project.StartsWith(_workspaceRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Le projet sort du workspace.");
        }

        CheckSafeDirectories(project);
        if (!File.Exists(Path.Combine(project, WorkspaceLayout.ProjectManifestFileName)))
        {
            throw new InvalidDataException("Seuls les projets Nodalis existants sont acceptés.");
        }

        string sheet = Path.Combine(project, "Fiche de Maintenance.md");
        if (!File.Exists(sheet))
        {
            throw new FileNotFoundException(
                "Ce projet ne possède pas encore de fiche de maintenance.", sheet);
        }

        return sheet;
    }

    /// <summary>Selects a current validated incident if requested.</summary>
    /// <param name="markdown">Latest source.</param>
    /// <param name="request">Explicit target choice.</param>
    /// <returns>Existing incident or null for a newly created/standalone record.</returns>
    private static MaintenanceRecord? SelectIncident(
        string markdown, MaintenanceInboxRequest request)
    {
        MaintenanceParseResult result = MaintenanceMarkdownCodec.Parse(markdown);
        if (result.Diagnostics.Count != 0)
        {
            throw new InvalidDataException(
                "La fiche comporte des blocs de maintenance invalides : " +
                string.Join("; ", result.Diagnostics.Take(3).Select(item => item.Message)));
        }

        if (request.Mode != MaintenanceInboxMode.Intervention &&
            request.ExistingIncidentId is not null)
        {
            throw new InvalidDataException("Incident existant incompatible avec le mode choisi.");
        }

        if (request.Mode != MaintenanceInboxMode.Intervention ||
            request.ExistingIncidentId is null)
        {
            return null;
        }

        return result.Records.FirstOrDefault(item =>
            item.Kind == MaintenanceRecordKind.Incident &&
            string.Equals(item.Id, request.ExistingIncidentId,
                StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException("L'incident lié n'existe plus dans la fiche.");
    }

    /// <summary>Returns the physical Inbox directory containing the capture's files.</summary>
    /// <param name="id">Capture identity.</param>
    /// <returns>Workspace-owned item directory.</returns>
    private string ItemDirectory(Guid id) =>
        Path.Combine(_workspaceRoot, WorkspaceLayout.InboxDirectoryName,
            "Items", id.ToString("N"));

    /// <summary>Rejects directory junctions and symlinks along managed paths.</summary>
    /// <param name="directory">Target directory inside the workspace.</param>
    private void CheckSafeDirectories(string directory)
    {
        string cursor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        while (cursor.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(cursor) &&
                (new DirectoryInfo(cursor).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Un lien symbolique ou une jonction est interdit.");
            }

            if (string.Equals(cursor, _workspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            string? parent = Path.GetDirectoryName(cursor);
            if (parent is null || parent.Length >= cursor.Length)
            {
                throw new IOException("Chemin de travail non sûr.");
            }

            cursor = parent;
        }
    }

    /// <summary>Checks the regular-file requirement before using a Markdown document.</summary>
    /// <param name="path">Source target file.</param>
    private static void CheckSafeFile(string path)
    {
        if ((new FileInfo(path).Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("La fiche de maintenance est un lien symbolique.");
        }
    }

    /// <summary>
    /// Copies Inbox attachments to a project-local folder only if present.
    /// Identical prior copies are reused; conflicting or redirected files are rejected.
    /// </summary>
    /// <param name="entry">Source capture.</param>
    /// <param name="sourceDirectory">Inbox item directory.</param>
    /// <param name="projectDirectory">Project root.</param>
    /// <param name="token">Cancellation.</param>
    private async Task CopyAttachmentsAsync(InboxEntry entry,
        string sourceDirectory, string projectDirectory, CancellationToken token)
    {
        if (entry.Attachments.Count == 0)
        {
            return;
        }

        string destinationDirectory = Path.Combine(projectDirectory,
            WorkspaceLayout.AttachmentsDirectoryName, "Inbox-" + entry.Id.ToString("N"));
        CheckSafeDirectories(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        CheckSafeDirectories(destinationDirectory);

        foreach (InboxAttachment attachment in entry.Attachments)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(attachment.RelativePath);
            if (string.IsNullOrEmpty(name) || name is "." or ".." ||
                name.Contains(':') || name.Contains('/') || name.Contains('\\') ||
                attachment.RelativePath.Split(['/', '\\']).Any(part =>
                    part.Length == 0 || part is "." or ".."))
            {
                throw new InvalidDataException("Pièce jointe non sûre : " + attachment.OriginalFileName);
            }

            string source = Path.GetFullPath(Path.Combine(sourceDirectory,
                attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.StartsWith(sourceDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
            {
                throw new IOException("Pièce jointe Inbox absente : " + attachment.OriginalFileName);
            }

            CheckSafeDirectories(Path.GetDirectoryName(source)!);
            CheckSafeFile(source);
            string target = Path.Combine(destinationDirectory, name);
            if (File.Exists(target))
            {
                CheckSafeFile(target);
                byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source, token));
                byte[] targetHash = SHA256.HashData(await File.ReadAllBytesAsync(target, token));
                if (!sourceHash.SequenceEqual(targetHash))
                {
                    throw new IOException("Pièce jointe déjà présente avec un contenu différent : " +
                        attachment.OriginalFileName);
                }

                continue;
            }

            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
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

                File.Move(temporary, target); // Never overwrite a user file.
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
}
