using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Milestones;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Milestones;

/// <summary>
/// Maintains one directly linked Markdown task per saved milestone. The
/// relationship is a reproducible persistent task GUID derived from the
/// stable milestone GUID, not from its mutable name/date or line number.
/// </summary>
public sealed class MilestoneTaskSynchronizationService
{
    private readonly string _workspaceRoot;

    /// <summary>Creates a project-scoped, no-network task mirror.</summary>
    /// <param name="workspaceRoot">Authoritative single workspace.</param>
    public MilestoneTaskSynchronizationService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>Stable identity of the checkbox corresponding to one milestone.</summary>
    /// <param name="milestoneId">Persistent milestone identity.</param>
    /// <returns>Deterministic task identity, distinct from source line numbers.</returns>
    public static Guid TaskIdForMilestone(Guid milestoneId)
    {
        if (milestoneId == Guid.Empty)
        {
            throw new ArgumentException(
                "L'identifiant du jalon est requis.", nameof(milestoneId));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "nodalis.milestone-task.v1/" + milestoneId.ToString("D")));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Creates or updates only the matching task in the owning project's
    /// Tâches.md. Never scans or modifies another project's tasks, never
    /// adopts manual checkboxes, and writes nothing for unchanged content.
    /// </summary>
    /// <param name="projectDirectory">Exact persisted project directory.</param>
    /// <param name="milestone">Already saved canonical milestone.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Whether a task source changed.</returns>
    public async Task<bool> SynchronizeAsync(
        string projectDirectory, MilestoneItem milestone,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(milestone);
        ProjectJourneySourceService safe = new(_workspaceRoot);
        string folder = Path.GetFullPath(projectDirectory);
        string manifestPath = Path.Combine(folder,
            WorkspaceLayout.ProjectManifestFileName);
        safe.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != milestone.ProjectId ||
            manifest.ArchivedUtc is not null ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Impossible de synchroniser une tâche hors du projet actif.");
        }

        string taskFile = Path.Combine(folder, "Tâches.md");
        if (File.Exists(taskFile))
        {
            safe.GetReferenceForExistingFile(taskFile);
        }
        else
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                taskFile, "# Tâches\n\n", cancellationToken);
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            taskFile, cancellationToken);
        string newline = session.Content.Contains("\r\n",
            StringComparison.Ordinal) ? "\r\n" : "\n";
        string normalized = session.Content.Replace("\r\n", "\n",
            StringComparison.Ordinal).Replace('\r', '\n');
        bool trailing = normalized.EndsWith('\n');
        List<string> lines = normalized.Split('\n').ToList();
        if (trailing && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        TaskPersistentIdentityScan scan = TaskPersistentIdentityCodec.Inspect(lines);
        if (scan.Issues.Count > 0)
        {
            throw new InvalidDataException(
                "Tâches.md contient des identifiants ambigus ; " +
                "aucune tâche de jalon n'a été modifiée.");
        }

        Guid taskId = TaskIdForMilestone(milestone.Id);
        TaskPersistentIdentityEntry[] existing = scan.Entries.Where(item =>
            item.Id == taskId).ToArray();
        if (existing.Length > 1)
        {
            throw new InvalidDataException(
                "Plusieurs tâches portent l'identifiant du même jalon.");
        }

        string generated = BuildGeneratedLine(milestone);
        if (existing.Length == 0)
        {
            // A legacy manually created task is not adopted or overwritten:
            // a duplicate visible title is ambiguous without an identity.
            string readable = "Jalon : " + CleanName(milestone.Name);
            if (scan.Entries.Any(item => item.Id is null &&
                    (item.RawLine.StartsWith("- [ ] " + readable,
                         StringComparison.OrdinalIgnoreCase) ||
                     item.RawLine.StartsWith("- [x] " + readable,
                         StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidDataException(
                    "Une tâche manuelle porte déjà ce nom de jalon. " +
                    "L'association nécessite une qualification avant " +
                    "toute création automatique.");
            }

            lines.Add(generated);
        }
        else
        {
            int index = existing[0].LineNumber - 1;
            string original = lines[index];
            Guid? found = TaskPersistentIdentityCodec.ReadMarker(
                original, out string clean);
            if (found != taskId || !clean.StartsWith("- [",
                    StringComparison.Ordinal) ||
                !clean.Contains("] Jalon : ", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "La tâche liée au jalon a été transformée en tâche " +
                    "manuelle : aucune modification automatique autorisée.");
            }

            // Preserve all user-added metadata except the target date,
            // which belongs to the milestone. Only this identified task
            // receives the new name/date/completion state.
            string[] parts = clean.Split('|');
            List<string> extras = parts.Skip(1)
                .Select(part => part.Trim())
                .Where(part => !part.StartsWith("Échéance:",
                    StringComparison.OrdinalIgnoreCase) &&
                    !part.StartsWith("Due:",
                    StringComparison.OrdinalIgnoreCase))
                .Where(part => part.Length > 0).ToList();
            string body = BuildBase(milestone);
            if (extras.Count > 0)
            {
                body += " | " + string.Join(" | ", extras);
            }

            else
            {
                body += " | Tags: #jalon";
            }

            lines[index] = TaskPersistentIdentityCodec.InsertMarker(
                body, taskId);
        }

        string output = string.Join(newline, lines) +
            (trailing ? newline : string.Empty);
        if (string.Equals(output, session.Content, StringComparison.Ordinal))
        {
            return false;
        }

        await session.SaveAsync(output, cancellationToken);
        return true;
    }

    /// <summary>Builds the canonical checkbox with a stable identity marker.</summary>
    /// <param name="milestone">Persisted milestone values.</param>
    /// <returns>One Markdown checkbox line.</returns>
    private static string BuildGeneratedLine(MilestoneItem milestone) =>
        TaskPersistentIdentityCodec.InsertMarker(
            BuildBase(milestone) + " | Tags: #jalon",
            TaskIdForMilestone(milestone.Id));

    /// <summary>Builds the readable name, deadline and completion state.</summary>
    /// <param name="milestone">Persisted milestone.</param>
    /// <returns>Readable checkbox without persistent marker.</returns>
    private static string BuildBase(MilestoneItem milestone)
    {
        string normalizedStatus = milestone.Status.Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);
        string withoutAccents = new string(normalizedStatus.Where(character =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
                System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        bool done = withoutAccents is
            "termine" or "terminee" or "clos" or "cloture" or
            "done" or "complete" or "completed";
        string body = "- [" + (done ? "x" : " ") + "] Jalon : " +
            CleanName(milestone.Name);
        if (milestone.TargetDate is DateOnly date)
        {
            body += " | Échéance: " + date.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return body;
    }

    /// <summary>Eliminates delimiters and marker-shaped text from a task title.</summary>
    /// <param name="name">Milestone text from the Markdown table.</param>
    /// <returns>Safe single-line task label.</returns>
    private static string CleanName(string name) =>
        name.Replace('|', '∣').Replace('\r', ' ').Replace('\n', ' ')
            .Replace('<', '‹').Replace('>', '›').Trim();
}
