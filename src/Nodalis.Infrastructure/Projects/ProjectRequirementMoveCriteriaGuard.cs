using System.Text;
using Nodalis.Core.Requirements;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// On-demand, fail-closed EXI A review of acceptance criterion backlinks.
/// One bounded pass validates ALL stable CA GUIDs; local CA-xx tokens are
/// examined in source/recipient projects only to avoid cross-project false hits.
/// </summary>
internal sealed class ProjectRequirementMoveCriteriaGuard
{
    private const int MaxFiles = 6000;
    private const long MaxTotalBytes = 96L * 1024 * 1024;
    private const long MaxFileBytes = 2L * 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates an explicit workspace-scoped scan service.</summary>
    /// <param name="workspaceRoot">Single Nodalis workspace root.</param>
    internal ProjectRequirementMoveCriteriaGuard(string workspaceRoot)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
    }

    /// <summary>
    /// Rejects unexpected references to CA GUIDs everywhere and local CA
    /// codes in either owner project, skipping only self-defining headings
    /// of other requirement cards. Read-only and bounded; no implicit repair.
    /// </summary>
    /// <param name="criteria">Approved CA identities, never generated here.</param>
    /// <param name="sourceRelative">Original requirement source to omit.</param>
    /// <param name="targetRelative">Proposed recipient document to omit.</param>
    /// <param name="token">Explicit cancellation.</param>
    /// <returns>Completed rejection-only link inspection.</returns>
    internal async Task AssertNoIncomingAsync(
        IReadOnlyList<ProjectRequirementReviewedCriterion> criteria,
        string sourceRelative, string targetRelative,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0)
        {
            return;
        }

        if (!ProjectJourneySourcePath.IsSafe(sourceRelative) ||
            !ProjectJourneySourcePath.IsSafe(targetRelative) ||
            criteria.Any(c => c.Id == Guid.Empty ||
                !ProjectRequirementCriterionCodec.IsReference(c.Reference)))
        {
            throw new InvalidDataException("Critères CA de passation invalides.");
        }

        string applications = Path.Combine(_root,
            WorkspaceLayout.ApplicationsDirectoryName);
        if (!Directory.Exists(applications))
        {
            throw new InvalidDataException(
                "Répertoire Applications absent : contrôle CA impossible.");
        }

        string source = Path.GetFullPath(Path.Combine(_root,
            sourceRelative.Replace('/', Path.DirectorySeparatorChar)));
        string target = Path.GetFullPath(Path.Combine(_root,
            targetRelative.Replace('/', Path.DirectorySeparatorChar)));
        const string marker = "/Documentation/Exigences/";
        if (!sourceRelative.Contains(marker, StringComparison.Ordinal) ||
            !targetRelative.Contains(marker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fiche EXI canonique requise.");
        }

        string sourceProject = Path.Combine(_root,
            sourceRelative[..sourceRelative.IndexOf(marker,
                StringComparison.Ordinal)].Replace('/',
                Path.DirectorySeparatorChar));
        string targetProject = Path.Combine(_root,
            targetRelative[..targetRelative.IndexOf(marker,
                StringComparison.Ordinal)].Replace('/',
                Path.DirectorySeparatorChar));
        Stack<string> directories = new();
        directories.Push(applications);
        int inspected = 0;
        long bytesRead = 0;
        while (directories.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string directory = directories.Pop();
            RequirePlain(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(
                directory).OrderBy(item => item, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Jonction dans Applications : critères CA non vérifiables.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(path);
                    continue;
                }

                if ((!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                     !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(path, source,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, target,
                        StringComparison.OrdinalIgnoreCase) ||
                    IsHistoricalProjectLedger(path))
                {
                    continue;
                }

                long fileLength = new FileInfo(path).Length;
                inspected++;
                bytesRead += fileLength;
                if (inspected > MaxFiles || bytesRead > MaxTotalBytes ||
                    fileLength > MaxFileBytes)
                {
                    throw new InvalidDataException(
                        "Audit CA incomplet (taille ou nombre de documents) : " +
                        "déplacement interdit.");
                }

                string verified = _sources.ResolveExistingFile(
                    _sources.GetReferenceForExistingFile(path));
                string text = new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(verified, token));
                if (criteria.Any(criterion =>
                    text.Contains(criterion.Id.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) ||
                    text.Contains(criterion.Id.ToString("N"),
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        "Lien entrant vers un GUID de critère CA : " +
                        Path.GetRelativePath(_root, verified));
                }

                bool local = IsWithin(path, sourceProject) ||
                    IsWithin(path, targetProject);
                if (local && criteria.Any(criterion =>
                    MentionsCode(text, path, criterion.Reference)))
                {
                    throw new InvalidDataException(
                        "Référence CA locale à requalifier : " +
                        Path.GetRelativePath(_root, verified));
                }
            }
        }
    }

    /// <summary>
    /// Excludes only the canonical append-only project management history,
    /// not other human notes with matching names.
    /// </summary>
    /// <param name="path">Examined Markdown or JSON source.</param>
    /// <returns>True for a real project-owned historical ledger.</returns>
    private static bool IsHistoricalProjectLedger(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        string? project = parent is null ?
            null : Path.GetDirectoryName(parent);
        if (parent is null || project is null ||
            !string.Equals(Path.GetFileName(parent), "Management",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(project,
                WorkspaceLayout.ProjectManifestFileName)))
        {
            return false;
        }

        string filename = Path.GetFileName(path);
        return string.Equals(filename, "Passations.md",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(filename, "Bilan de clôture.md",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tests strict containment in one project directory.</summary>
    /// <param name="path">Read document physical path.</param>
    /// <param name="project">Canonical project directory.</param>
    /// <returns>True when document is under that owner.</returns>
    private static bool IsWithin(string path, string project) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(project) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks CA tokens except declarations in other EXI headings.</summary>
    /// <param name="text">Another file content.</param>
    /// <param name="path">Real file path.</param>
    /// <param name="reference">Local CA-xx token.</param>
    /// <returns>True if another source might point at this CA.</returns>
    private static bool MentionsCode(
        string text, string path, string reference)
    {
        bool requirementCard = string.Equals(
            Path.GetFileName(Path.GetDirectoryName(path)), "Exigences",
            StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        foreach (string line in text.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n'))
        {
            if (requirementCard && line.StartsWith(
                "### " + reference + " ", StringComparison.Ordinal))
            {
                continue;
            }

            int start = 0;
            while ((start = line.IndexOf(reference, start,
                StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = start + reference.Length;
                if ((start == 0 || !char.IsLetterOrDigit(line[start - 1])) &&
                    (end == line.Length || !char.IsLetterOrDigit(line[end])))
                {
                    return true;
                }

                start = end;
            }
        }

        return false;
    }

    /// <summary>Fails closed on linked parent folders.</summary>
    /// <param name="directory">Canonical active folder.</param>
    private static void RequirePlain(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Lien symbolique bloquant l'audit CA.");
        }
    }
}
