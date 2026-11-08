using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Deliberately conservative, bounded on-demand RIS A link verification.
/// Readable RIS codes are workspace-local and potentially ambiguous: external
/// occurrences other than another requirement's own YAML/H1 block move A.
/// </summary>
internal sealed class ProjectRiskMoveReferenceGuard
{
    private const int MaxFiles = 6000;
    private const long MaxBytes = 96L * 1024 * 1024;
    private const long MaxOneFile = 2L * 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a root-confined explicit reference scanner.</summary>
    /// <param name="root">Canonical one-workspace directory.</param>
    internal ProjectRiskMoveReferenceGuard(string root)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _sources = new ProjectJourneySourceService(_root);
    }

    /// <summary>
    /// Requires no active GUID or readable-code mention in any other file.
    /// This includes cross-project references and fails closed on oversized
    /// or linked sources. A separate unrelated RIS card's own reference
    /// metadata and heading are ignored, not its prose body.
    /// </summary>
    /// <param name="id">Original stable RIS identity.</param>
    /// <param name="reference">Human-readable RIS-xxxx being relocated.</param>
    /// <param name="excludedRelative">Source or target owning this identity.</param>
    /// <param name="counterpartRelative">Other active project RIS path.</param>
    /// <param name="token">On-demand scan cancellation.</param>
    /// <returns>Completed verification without side effects.</returns>
    internal async Task AssertNoIncomingAsync(
        Guid id, string reference, string excludedRelative,
        string counterpartRelative, CancellationToken token)
    {
        if (id == Guid.Empty || !Nodalis.Core.Risks.
            ProjectRiskMarkdownCodec.IsReference(reference) ||
            !ProjectJourneySourcePath.IsSafe(excludedRelative) ||
            !ProjectJourneySourcePath.IsSafe(counterpartRelative))
        {
            throw new InvalidDataException("Identité RIS A non vérifiable.");
        }

        string applications = Path.Combine(_root,
            WorkspaceLayout.ApplicationsDirectoryName);
        if (!Directory.Exists(applications))
        {
            throw new InvalidDataException(
                "Applications absent : audit des références impossible.");
        }

        string ignoredPath = Path.GetFullPath(Path.Combine(_root,
            excludedRelative.Replace('/', Path.DirectorySeparatorChar)));
        // RIS-xxxx is a locally scoped code. A GUID mention anywhere
        // in Applications is always significant, but an unrelated app's
        // text-only RIS-xxxx cannot identify this particular risk.
        string firstApplication = ApplicationRoot(excludedRelative);
        string secondApplication = ApplicationRoot(counterpartRelative);
        Stack<string> pending = new();
        pending.Push(applications);
        int count = 0;
        long total = 0;
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            CheckPlain(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(
                directory).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Lien symbolique dans le workspace : aucune passation A.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                    continue;
                }

                if ((!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                     !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(path, ignoredPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    IsCanonicalHistoricalLedger(path))
                {
                    continue;
                }

                long length = new FileInfo(path).Length;
                count++;
                total += length;
                if (count > MaxFiles || total > MaxBytes ||
                    length > MaxOneFile)
                {
                    throw new InvalidDataException(
                        "Audit des références RIS A trop volumineux : " +
                        "déplacement ou rollback refusé.");
                }

                string verified = _sources.ResolveExistingFile(
                    _sources.GetReferenceForExistingFile(path));
                string content = new UTF8Encoding(false, true).GetString(
                    await File.ReadAllBytesAsync(verified, token));
                if (content.Contains(id.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) ||
                    content.Contains(id.ToString("N"),
                        StringComparison.OrdinalIgnoreCase) ||
                    (WithinApplication(path, firstApplication) ||
                     WithinApplication(path, secondApplication)) &&
                    HasOtherReadableReference(content, path, reference))
                {
                    throw new InvalidDataException(
                        "Référence entrante à requalifier avant passation A : " +
                        Path.GetRelativePath(_root, verified)
                            .Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }
    }

    /// <summary>Finds one active application directory from a RIS path.</summary>
    /// <param name="relative">Safe workspace-relative RIS document.</param>
    /// <returns>Absolute containing application root.</returns>
    private string ApplicationRoot(string relative)
    {
        string[] segments = relative.Split('/');
        if (segments.Length < 5 ||
            !string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(segments[1]) ||
            !relative.Contains("/Analyse/Risques/", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Portée d'application RIS A non vérifiable.");
        }

        return Path.GetFullPath(Path.Combine(_root,
            segments[0], segments[1]));
    }

    /// <summary>Checks app root without matching similarly prefixed apps.</summary>
    /// <param name="path">Existing physical file.</param>
    /// <param name="application">Verified containing application.</param>
    /// <returns>Whether a human code can belong to this application.</returns>
    private static bool WithinApplication(string path, string application) =>
        path.StartsWith(application + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Excludes only the canonical append-only management ledgers, never
    /// arbitrary user documents coincidentally named Passations.md.
    /// </summary>
    /// <param name="path">Physical Markdown file under Applications.</param>
    /// <returns>Whether the document is a historical management ledger.</returns>
    private static bool IsCanonicalHistoricalLedger(string path)
    {
        string name = Path.GetFileName(path);
        string? parent = Path.GetDirectoryName(path);
        string? directory = Path.GetFileName(parent);
        string? owner = parent is null ? null :
            Path.GetDirectoryName(parent);
        return string.Equals(directory, "Management",
                   StringComparison.OrdinalIgnoreCase) &&
            owner is not null &&
            File.Exists(Path.Combine(owner,
                WorkspaceLayout.ProjectManifestFileName)) &&
            (string.Equals(name, "Passations.md",
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, "Bilan de clôture.md",
                 StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds standalone RIS code, except the own YAML/H1 declaration of a
    /// separate existing requirement with the same human-local reference.
    /// </summary>
    /// <param name="content">Exact document text.</param>
    /// <param name="path">Checked Markdown or JSON path.</param>
    /// <param name="reference">RIS-xxxx token to search.</param>
    /// <returns>Presence of a potentially ambiguous human reference.</returns>
    private static bool HasOtherReadableReference(
        string content, string path, string reference)
    {
        bool ownCard = path.EndsWith(
            Path.DirectorySeparatorChar + "Risques" +
            Path.DirectorySeparatorChar + reference + ".md",
            StringComparison.OrdinalIgnoreCase);
        string[] lines = content.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n');
        foreach (string line in lines)
        {
            if (ownCard &&
                (line.Trim() == "reference: " + reference ||
                 line.StartsWith("# " + reference + " — ",
                    StringComparison.Ordinal)))
            {
                continue;
            }

            int found = 0;
            while ((found = line.IndexOf(reference, found,
                StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = found + reference.Length;
                if ((found == 0 ||
                     !char.IsLetterOrDigit(line[found - 1])) &&
                    (end == line.Length ||
                     !char.IsLetterOrDigit(line[end])))
                {
                    return true;
                }

                found = end;
            }
        }

        return false;
    }

    /// <summary>Rejects reparse points in active workspace folders.</summary>
    /// <param name="directory">Existing folder to inspect.</param>
    private static void CheckPlain(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Jonction ou lien symbolique non vérifiable pour RIS A.");
        }
    }
}
