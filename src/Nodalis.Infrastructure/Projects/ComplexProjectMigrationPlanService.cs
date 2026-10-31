using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Immutable opt-in plan: all source files are fingerprinted before approval.
/// Blockers are intentionally conservative until relative link rewriting is implemented.
/// </summary>
public sealed record ComplexProjectMigrationPlan
{
    /// <summary>Existing project identity, preserved across migration.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Canonical absolute source project path.</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>SHA-256 over all source file and directory names plus file hashes.</summary>
    public required string SourceFingerprint { get; init; }

    /// <summary>Proposed section and singleton moves.</summary>
    public IReadOnlyList<ComplexMigrationMove> Moves { get; init; } = [];

    /// <summary>Directories required by the destination blueprint.</summary>
    public IReadOnlyList<string> MissingDirectories { get; init; } = [];

    /// <summary>Conditions that must be fixed before migration is exposed.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = [];

    /// <summary>Informational observations that do not permit implicit writes.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True only for at least one safe, eligible move.</summary>
    public bool CanExecute =>
        Blockers.Count == 0 &&
        Moves.Any(move => move.State == ComplexMigrationMoveState.Candidate);
}

/// <summary>
/// Performs read-only, evidence-based preflight and source hashing for
/// transactional Complex migration. Does not mutate the workspace.
/// </summary>
public sealed class ComplexProjectMigrationPlanService
{
    private readonly string _workspaceRoot;

    /// <summary>Creates a scoped, read-only migration planner.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    public ComplexProjectMigrationPlanService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
    }

    /// <summary>
    /// Checks filesystem integrity, required roles, unresolved Markdown
    /// references and optimistic concurrency before allowing an approval.
    /// </summary>
    /// <param name="projectDirectory">Complex project root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Immutable preflight including the complete source fingerprint.</returns>
    public async Task<ComplexProjectMigrationPlan> PrepareAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ComplexProjectMigrationPreview preview =
            await new ComplexProjectMigrationPreviewService(_workspaceRoot)
                .PreviewAsync(projectDirectory, cancellationToken);
        string root = Path.GetFullPath(projectDirectory);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            Path.Combine(root, WorkspaceLayout.ProjectManifestFileName),
            cancellationToken);
        List<string> blockers = [];
        List<string> warnings = [.. preview.Warnings];

        if (manifest.ArchivedUtc is not null)
        {
            blockers.Add("Le projet est archivé : migration des archives non prise en charge.");
        }

        if (manifest.Sections.GroupBy(section => section.Id)
            .Any(group => group.Key == Guid.Empty || group.Count() != 1))
        {
            blockers.Add("Identifiants techniques de sections manquants ou dupliqués.");
        }

        foreach (ComplexMigrationMove move in preview.Moves)
        {
            if (move.State is ComplexMigrationMoveState.Blocked or
                ComplexMigrationMoveState.MissingSource)
            {
                blockers.Add($"{move.SectionName} : {move.Diagnostic}");
            }
        }

        bool milestoneExists = manifest.Sections.Any(section =>
            string.Equals(section.TemplateKey, "milestones",
                StringComparison.OrdinalIgnoreCase));
        if (!milestoneExists)
        {
            blockers.Add("Rôle Jalons absent : impossible de garantir un document unique.");
        }

        // Adding the parent section labels must never redefine user sections.
        string[] newSections = ["Exigences", "Architecture", "Analyse", "Management"];
        foreach (string name in newSections)
        {
            SectionManifest? existing = manifest.Sections.FirstOrDefault(section =>
                string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null &&
                !string.Equals(existing.RelativeDirectory,
                    name switch
                    {
                        "Exigences" => "Documentation/Exigences",
                        "Architecture" => "Documentation/Technique/Architecture",
                        _ => name
                    }, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"La section personnalisée « {name} » doit être requalifiée.");
            }
        }

        // The whole project is copied to staging, so refuse junctions anywhere,
        // including attachments and user-created directories.
        IReadOnlyList<string> entries = EnumerateTree(root, cancellationToken);
        foreach (string entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                blockers.Add("Jonction ou lien symbolique non autorisé : " +
                    Path.GetRelativePath(root, entry));
            }
        }

        // Until a complete cross-document/cross-project link rewriter exists,
        // the first executable subset must not silently invalidate any local
        // Markdown, wiki, image or HTML reference in the workspace.
        string[] proposedSources = preview.Moves
            .Where(move => move.State == ComplexMigrationMoveState.Candidate)
            .Select(move => move.From)
            .ToArray();

        if (proposedSources.Length > 0)
        {
            IReadOnlyList<string> references = await FindUnresolvedLinksAsync(
                cancellationToken);
            if (references.Count > 0)
            {
                blockers.Add("Références Markdown à requalifier avant déplacement (" +
                    references.Count + ") : " +
                    string.Join(", ", references.Take(5)));
            }
        }

        string fingerprint = await FingerprintAsync(root, cancellationToken);

        return new ComplexProjectMigrationPlan
        {
            ProjectId = preview.ProjectId,
            ProjectDirectory = root,
            SourceFingerprint = fingerprint,
            Moves = preview.Moves,
            MissingDirectories = preview.MissingDirectories,
            Blockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Fingerprints names, empty folders and all file bytes. A mismatch means
    /// the user must reapprove a new plan rather than overwriting newer edits.
    /// </summary>
    /// <param name="projectRoot">Existing project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lowercase SHA-256 tree fingerprint.</returns>
    public static async Task<string> FingerprintAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(projectRoot);
        StringBuilder inventory = new();
        foreach (string entry in EnumerateTree(root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Lien symbolique interdit dans le projet : {relative}");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                inventory.Append("D|").Append(relative).Append('\n');
            }
            else
            {
                await using FileStream input = new(entry, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                byte[] hash = await SHA256.HashDataAsync(input, cancellationToken);
                inventory.Append("F|").Append(relative).Append('|')
                    .Append(Convert.ToHexString(hash)).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(inventory.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// Enumerates every child path without following a reparse directory.
    /// </summary>
    /// <param name="root">Existing tree root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sorted relative-to-root path inventory as absolute paths.</returns>
    internal static IReadOnlyList<string> EnumerateTree(
        string root,
        CancellationToken cancellationToken)
    {
        List<string> entries = [];
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(entry);
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0 &&
                    (attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(entry);
                }
            }
        }

        return entries.OrderBy(path => Path.GetRelativePath(root, path),
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Fail closed for all explicit Markdown links while link rewriting is
    /// unfinished. Workspace-wide scanning includes external project backlinks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to twenty sample Markdown paths with references.</returns>
    private async Task<IReadOnlyList<string>> FindUnresolvedLinksAsync(
        CancellationToken cancellationToken)
    {
        List<string> found = [];
        foreach (string file in Directory.EnumerateFiles(_workspaceRoot,
                     "*.md", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A junction elsewhere in the workspace must not escape the
            // workspace boundary when reading references.
            string relative = Path.GetRelativePath(_workspaceRoot, file);
            string[] segments = relative.Split(Path.DirectorySeparatorChar);
            string ancestor = _workspaceRoot;
            foreach (string segment in segments.SkipLast(1))
            {
                ancestor = Path.Combine(ancestor, segment);
                if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Lien symbolique détecté pendant l'analyse des références.");
                }
            }

            string content = await File.ReadAllTextAsync(file, cancellationToken);
            if (Regex.IsMatch(content,
                @"\]\(|\[\[|(?i:\b(?:src|href)\s*=)",
                RegexOptions.CultureInvariant))
            {
                found.Add(relative.Replace('\\', '/'));
            }
        }

        return found;
    }
}
