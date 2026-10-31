using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Immutable opt-in plan: all source files are fingerprinted before approval.
/// Only unambiguous project-local affected links are proposed for transactional rewriting.
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

    /// <summary>Hash-protected project-local rewrites approved with the migration.</summary>
    public IReadOnlyList<ComplexMigrationLinkRewrite> LinkRewrites { get; init; } = [];

    /// <summary>Cross-project backlinks requiring separate durable file snapshots.</summary>
    public IReadOnlyList<ComplexMigrationLinkRewrite> ExternalLinkRewrites { get; init; } = [];

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
        string sourceAncestor = root;
        while (sourceAncestor.Length > _workspaceRoot.Length)
        {
            if ((File.GetAttributes(sourceAncestor) & FileAttributes.ReparsePoint) != 0)
            {
                blockers.Add("Le chemin du projet traverse une jonction ou un lien symbolique.");
            }

            sourceAncestor = Path.GetDirectoryName(sourceAncestor) ?? _workspaceRoot;
        }

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

        // Unambiguous local and cross-project link repairs are frozen into
        // one journal: a project staging copy plus separate durable external
        // file snapshots that are published under explicit recovery control.
        ComplexMigrationLinkAudit linkAnalysis = new([], []);
        if (preview.Moves.Any(move =>
                move.State == ComplexMigrationMoveState.Candidate))
        {
            ComplexProjectMigrationLinkImpactService linkAudit = new(
                _workspaceRoot, root, preview.Moves);
            linkAnalysis = await linkAudit.AnalyzeAsync(cancellationToken);
            if (linkAnalysis.Blockers.Count > 0)
            {
                blockers.Add("Références Markdown à requalifier avant déplacement (" +
                    linkAnalysis.Blockers.Count + ") : " +
                    string.Join(", ", linkAnalysis.Blockers.Take(5)));
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
            LinkRewrites = linkAnalysis.Rewrites,
            ExternalLinkRewrites = linkAnalysis.ExternalRewrites,
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


}
