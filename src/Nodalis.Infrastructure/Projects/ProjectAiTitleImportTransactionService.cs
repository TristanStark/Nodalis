using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Final outcome of one explicitly authorized single-file AI title import.</summary>
public enum ProjectAiTitleCommitStatus
{
    Applied,
    AlreadyApplied
}

/// <summary>Durable recovery metadata; it is not an AI approval or a business registry.</summary>
public sealed record ProjectAiTitleImportJournal
{
    public required string Schema { get; init; }
    public required string RawResponseSha256 { get; init; }
    public required Guid ProposalId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid RequirementId { get; init; }
    public required string Reference { get; init; }
    public required string RelativePath { get; init; }
    public required string OriginalSha256 { get; init; }
    public required string PublishedSha256 { get; init; }
    public required string OriginalTitle { get; init; }
    public required string ApprovedTitle { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// First narrow write slice of external AI import: one human-approved existing
/// EXI title only. Stages exact original/published snapshots and a durable
/// journal before publishing. A retry cannot overwrite a divergent file.
/// </summary>
public sealed class ProjectAiTitleImportTransactionService
{
    private const int MaxRequirementBytes = 1024 * 1024;
    private const string JournalFolder = ".nodalis-ai-import";
    private const string JournalFile = "intent.json";
    private const string OriginalFile = "original.md";
    private const string PublishedFile = "published.md";
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-bound executor; constructing it is read-only.</summary>
    /// <param name="workspaceRoot">Current workspace root.</param>
    public ProjectAiTitleImportTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Commits precisely one previously reviewed title, or safely replays
    /// the exact same journal after an interrupted publication. No other
    /// fields, approvals, relations, files or model outputs are written.
    /// </summary>
    /// <param name="projectDirectory">Existing, active project directory.</param>
    /// <param name="plan">Human-reviewed and snapshot-bound plan.</param>
    /// <param name="cancellationToken">Cooperative cancellation before publication.</param>
    /// <returns>Applied or already applied without duplicate writes.</returns>
    public async Task<ProjectAiTitleCommitStatus> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Changes.Count != 1 || plan.Creations.Count != 0 ||
            plan.Descriptions.Count != 0 || plan.CriterionTitles.Count != 0 ||
            plan.CriterionCreations.Count != 0 ||
            plan.CriterionBodies.Count != 0 ||
            plan.RawResponseSha256.Length != 64 ||
            !plan.RawResponseSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Cette tranche autorise un seul titre d'exigence approuvé par transaction.");
        }

        ProjectAiPreparedTitleChange change = plan.Changes[0];
        if (change.ProposalId == Guid.Empty ||
            change.RequirementId == Guid.Empty ||
            change.ProjectId == Guid.Empty ||
            !ProjectRequirementMarkdownCodec.IsReference(change.Reference) ||
            change.ExpectedSha256.Length != 64 ||
            !change.ExpectedSha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(change.ReviewedTitle) ||
            change.ReviewedTitle != change.ReviewedTitle.Trim() ||
            change.ReviewedTitle.Length > 240 ||
            change.ReviewedTitle.Any(char.IsControl) ||
            change.CurrentTitle == change.ReviewedTitle)
        {
            throw new InvalidDataException("Plan de titre non publiable.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        await VerifyActiveProjectAsync(project, change.ProjectId, cancellationToken);
        string path = _sources.ResolveExistingFile(change.RelativePath);
        string canonical = Path.Combine(project, "Documentation", "Exigences",
            change.Reference + ".md");
        if (!string.Equals(path, Path.GetFullPath(canonical),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La cible approuvée n'est pas la fiche EXI canonique du projet.");
        }

        // All workspace mutations below require an explicit caller action.
        string root = Path.Combine(project, JournalFolder);
        Directory.CreateDirectory(root);
        RejectReparse(root);
        string lockPath = Path.Combine(root, ".lock");
        await using FileStream exclusive = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou du journal non sûr.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string transaction = Path.Combine(root, change.ProposalId.ToString("N"));
        string manifestPath = Path.Combine(transaction, JournalFile);
        ProjectAiTitleImportJournal journal;
        if (Directory.Exists(transaction))
        {
            RejectReparse(transaction);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException(
                    "Préparation d'import interrompue avant journal : " +
                    "aucun fichier métier n'a été publié ; récupération manuelle requise.");
            }

            if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Journal sous lien symbolique : reprise refusée.");
            }

            string json = await File.ReadAllTextAsync(
                manifestPath, cancellationToken);
            if (json.Length > 16000)
            {
                throw new InvalidDataException("Journal d'import trop volumineux.");
            }

            try
            {
                journal = JsonSerializer.Deserialize<ProjectAiTitleImportJournal>(
                    json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                    throw new InvalidDataException("Journal d'import vide.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Journal d'import malformé : récupération manuelle requise.",
                    exception);
            }

            RequireMatchingJournal(plan, change, journal);
        }
        else
        {
            // Rebuild the authoritative project-local inventory before staging.
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_workspaceRoot)
                    .ListAsync(project, cancellationToken);
            ProjectRequirementEntry? entry = catalog.Requirements
                .SingleOrDefault(item => item.Card.Id == change.RequirementId);
            if (entry is null ||
                entry.Card.ProjectId != change.ProjectId ||
                entry.Card.Reference != change.Reference ||
                entry.RelativePath != change.RelativePath ||
                !string.Equals(entry.SourceSha256, change.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                entry.Card.Title != change.CurrentTitle)
            {
                throw new InvalidDataException(
                    "La fiche EXI a changé ou son identité est ambiguë : " +
                    "recomparer et redemander la validation humaine.");
            }

            byte[] originalBytes = await ReadBoundedAsync(path, cancellationToken);
            string originalHash = Hash(originalBytes);
            if (!string.Equals(originalHash, change.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Changement concurrent après la prévisualisation : écriture refusée.");
            }

            string original = new UTF8Encoding(false, true).GetString(originalBytes);
            ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(original);
            if (card.Id != change.RequirementId ||
                card.ProjectId != change.ProjectId ||
                card.Reference != change.Reference ||
                card.Title != change.CurrentTitle)
            {
                throw new InvalidDataException("Identité EXI modifiée : écriture refusée.");
            }

            string published = RewriteTitle(original, card, change.ReviewedTitle);
            byte[] publishedBytes = new UTF8Encoding(false).GetBytes(published);
            journal = new ProjectAiTitleImportJournal
            {
                Schema = "nodalis.ai-title-commit.v1",
                RawResponseSha256 = plan.RawResponseSha256,
                ProposalId = change.ProposalId,
                ProjectId = change.ProjectId,
                RequirementId = change.RequirementId,
                Reference = change.Reference,
                RelativePath = change.RelativePath,
                OriginalSha256 = originalHash,
                PublishedSha256 = Hash(publishedBytes),
                OriginalTitle = change.CurrentTitle,
                ApprovedTitle = change.ReviewedTitle,
                Reason = change.Reason
            };
            Directory.CreateDirectory(transaction);
            RejectReparse(transaction);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(transaction, OriginalFile), original, cancellationToken);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(transaction, PublishedFile), published, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // No live Markdown is modified before this manifest is durable.
            await AtomicFileWriter.WriteNewAllTextAsync(manifestPath,
                JsonSerializer.Serialize(journal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
        }

        await VerifySnapshotAsync(Path.Combine(transaction, OriginalFile),
            journal.OriginalSha256, cancellationToken);
        string staged = Path.Combine(transaction, PublishedFile);
        await VerifySnapshotAsync(staged, journal.PublishedSha256,
            cancellationToken);

        string liveHash = Hash(await ReadBoundedAsync(path, cancellationToken));
        if (string.Equals(liveHash, journal.PublishedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            return ProjectAiTitleCommitStatus.AlreadyApplied;
        }

        if (!string.Equals(liveHash, journal.OriginalSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La fiche EXI diverge du journal : aucune reprise automatique. " +
                "Consulter les instantanés du journal avant requalification.");
        }

        // The shared writer rehashes under a Windows read lock, checks NTFS file
        // identity, then uses same-volume File.Replace: no blind overwrite.
        await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
            path, journal.OriginalSha256, staged, journal.PublishedSha256,
            cancellationToken);
        string finalHash = Hash(await ReadBoundedAsync(path, CancellationToken.None));
        if (!string.Equals(finalHash, journal.PublishedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Publication incohérente : journal à conserver pour récupération.");
        }

        return ProjectAiTitleCommitStatus.Applied;
    }

    /// <summary>Validates project membership, archive state and durable identity.</summary>
    /// <param name="project">Absolute project root.</param>
    /// <param name="projectId">Reviewed canonical project GUID.</param>
    /// <param name="token">Cancellation.</param>
    private async Task VerifyActiveProjectAsync(string project,
        Guid projectId, CancellationToken token)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 4 || !string.Equals(parts[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !parts.Skip(2).Any(part => string.Equals(part,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Import IA interdit hors d'un projet actif de l'application.");
        }

        string manifestPath = Path.Combine(
            project, WorkspaceLayout.ProjectManifestFileName);
        string safeManifest = _sources.GetReferenceForExistingFile(manifestPath);
        _sources.ResolveExistingFile(safeManifest);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, token);
        if (manifest.Id != projectId || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet archivé, déplacé ou identité différente : import refusé.");
        }
    }

    /// <summary>Checks that a retry describes the exact existing authorization.</summary>
    private static void RequireMatchingJournal(ProjectAiPreparedReviewPlan plan,
        ProjectAiPreparedTitleChange change, ProjectAiTitleImportJournal journal)
    {
        if (journal.Schema != "nodalis.ai-title-commit.v1" ||
            journal.RawResponseSha256 != plan.RawResponseSha256 ||
            journal.ProposalId != change.ProposalId ||
            journal.ProjectId != change.ProjectId ||
            journal.RequirementId != change.RequirementId ||
            journal.Reference != change.Reference ||
            journal.RelativePath != change.RelativePath ||
            !string.Equals(journal.OriginalSha256, change.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase) ||
            journal.OriginalTitle != change.CurrentTitle ||
            journal.ApprovedTitle != change.ReviewedTitle ||
            journal.Reason != change.Reason ||
            journal.PublishedSha256 is not { Length: 64 } ||
            !journal.PublishedSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Proposition déjà journalisée avec une autorisation différente : " +
                "réimport silencieux interdit.");
        }
    }

    /// <summary>Rewrites exactly two canonical title representations in memory.</summary>
    /// <param name="markdown">Exact source UTF-8.</param>
    /// <param name="card">Parsed, validated canonical source identity.</param>
    /// <param name="newTitle">Explicit human-reviewed title.</param>
    /// <returns>Source with only title metadata and its H1 updated.</returns>
    internal static string RewriteTitle(
        string markdown, ProjectRequirementCard card, string newTitle)
    {
        string[] lines = markdown.Split('\n');
        string oldMetadata = "title: " + JsonSerializer.Serialize(card.Title);
        string newMetadata = "title: " + JsonSerializer.Serialize(newTitle);
        int boundary = Array.FindIndex(lines, 1, line => line.TrimEnd('\r') == "---");
        if (boundary < 1)
        {
            throw new InvalidDataException("En-tête EXI incomplet.");
        }

        int metadata = -1;
        for (int index = 1; index < boundary; index++)
        {
            if (lines[index].TrimEnd('\r') == oldMetadata)
            {
                if (metadata >= 0)
                {
                    throw new InvalidDataException("Titre EXI ambigu.");
                }

                metadata = index;
            }
        }

        int heading = boundary + 1;
        while (heading < lines.Length &&
               string.IsNullOrWhiteSpace(lines[heading]))
        {
            heading++;
        }

        string expectedHeading = "# " + card.Reference + " — " + card.Title;
        if (metadata < 0 || heading == lines.Length ||
            lines[heading].TrimEnd('\r') != expectedHeading)
        {
            throw new InvalidDataException(
                "Titre de fiche non canonique : correction automatique refusée.");
        }

        lines[metadata] = newMetadata +
            (lines[metadata].EndsWith('\r') ? "\r" : "");
        lines[heading] = "# " + card.Reference + " — " + newTitle +
            (lines[heading].EndsWith('\r') ? "\r" : "");
        string rewritten = string.Join("\n", lines);
        ProjectRequirementCard parsed = ProjectRequirementMarkdownCodec.Parse(rewritten);
        if (parsed != (card with { Title = newTitle }))
        {
            throw new InvalidDataException(
                "La proposition modifierait des métadonnées non autorisées.");
        }

        return rewritten;
    }

    /// <summary>Reads only bounded canonical Markdown bytes.</summary>
    private static async Task<byte[]> ReadBoundedAsync(string path,
        CancellationToken token)
    {
        FileInfo info = new(path);
        if (info.Length > MaxRequirementBytes)
        {
            throw new InvalidDataException("Exigence trop volumineuse pour l'import.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxRequirementBytes)
        {
            throw new InvalidDataException("Exigence modifiée pendant la lecture.");
        }

        return bytes;
    }

    /// <summary>Checks an immutable original/published recovery snapshot.</summary>
    private static async Task VerifySnapshotAsync(string path, string expected,
        CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(Hash(await ReadBoundedAsync(path, token)), expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Instantané d'import manquant ou corrompu : récupération manuelle.");
        }
    }

    /// <summary>Rejects symlinked transaction directories before writes.</summary>
    private static void RejectReparse(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Journal sous jonction ou lien symbolique.");
        }
    }

    /// <summary>Computes a portable byte-level SHA-256.</summary>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
