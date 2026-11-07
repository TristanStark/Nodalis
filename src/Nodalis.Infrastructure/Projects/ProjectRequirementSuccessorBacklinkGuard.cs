using System.Text;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Conservative on-demand guard for withdrawing a published EXI B successor.
/// Detects GUID references anywhere under active Applications and the local
/// visible EXI-xxxx reference inside the recipient project. No background scan.
/// </summary>
internal sealed class ProjectRequirementSuccessorBacklinkGuard
{
    private const int MaxFiles = 4000;
    private const long MaxBytes = 64 * 1024 * 1024;
    private const long MaxSingleFileBytes = 2 * 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates an explicit, workspace-bounded link safety check.</summary>
    /// <param name="workspaceRoot">Single canonical workspace.</param>
    internal ProjectRequirementSuccessorBacklinkGuard(string workspaceRoot)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
    }

    /// <summary>
    /// Rejects withdrawal when any other active document refers to the
    /// successor GUID, or the recipient contains its human reference.
    /// A workspace too large or with unsafe paths fails closed.
    /// </summary>
    /// <param name="journal">Completed original B transaction.</param>
    /// <param name="targetProject">Verified recipient project directory.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Completed read-only verification.</returns>
    internal async Task AssertNoLinksAsync(
        ProjectRequirementSuccessorJournal journal, string targetProject,
        CancellationToken token)
    {
        string target = _sources.ResolveExistingFile(journal.TargetRelativePath);
        IReadOnlyList<string> guidReferences =
            await new ProjectHandoverTaskExecutionPlanner(_root)
                .FindOtherReferencesAsync(journal.SuccessorRequirementId,
                    target, target, token);
        if (guidReferences.Count > 0)
        {
            throw new InvalidDataException(
                "Le successeur EXI possède des références GUID hors de sa fiche : " +
                string.Join(", ", guidReferences.Take(4)) +
                ". Le rollback automatique est interdit.");
        }

        Stack<string> folders = new();
        folders.Push(targetProject);
        int inspected = 0;
        long bytes = 0;
        while (folders.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string directory = folders.Pop();
            RequirePlain(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(
                directory).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Lien symbolique empêchant la vérification EXI.");
                }

                if ((attr & FileAttributes.Directory) != 0)
                {
                    if (File.Exists(Path.Combine(path,
                        WorkspaceLayout.ProjectManifestFileName)))
                    {
                        continue;
                    }

                    folders.Push(path);
                    continue;
                }

                if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(path, target,
                    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), "Passations.md",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), "Bilan de clôture.md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long length = new FileInfo(path).Length;
                inspected++;
                bytes += length;
                if (inspected > MaxFiles || bytes > MaxBytes ||
                    length > MaxSingleFileBytes)
                {
                    throw new InvalidDataException(
                        "Inspection des backlinks EXI trop volumineuse : " +
                        "aucun retrait automatique.");
                }

                string verified = _sources.ResolveExistingFile(
                    _sources.GetReferenceForExistingFile(path));
                string content = new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(verified, token));
                if (MentionsCode(content, journal.TargetReference))
                {
                    throw new InvalidDataException(
                        "Une autre source du projet destinataire cite " +
                        journal.TargetReference + " : " +
                        Path.GetRelativePath(_root, verified) +
                        ". Le successeur doit être délié manuellement.");
                }
            }
        }
    }

    /// <summary>Detects a standalone EXI code without matching longer codes.</summary>
    /// <param name="text">Another source document.</param>
    /// <param name="code">Canonical EXI-xxxx reference.</param>
    /// <returns>Whether that reference is mentioned by another source.</returns>
    private static bool MentionsCode(string text, string code)
    {
        int start = 0;
        while ((start = text.IndexOf(code, start,
            StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + code.Length;
            bool before = start == 0 ||
                !char.IsLetterOrDigit(text[start - 1]);
            bool after = end == text.Length ||
                !char.IsLetterOrDigit(text[end]);
            if (before && after)
            {
                return true;
            }

            start = end;
        }

        return false;
    }

    /// <summary>Rejects directory traversal through reparse points.</summary>
    /// <param name="directory">Existing real folder.</param>
    private static void RequirePlain(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Répertoire lié non vérifiable pour rollback EXI.");
        }
    }
}
