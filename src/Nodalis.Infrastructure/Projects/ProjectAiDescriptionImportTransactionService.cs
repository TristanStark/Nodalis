using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One durable human-reviewed description publication, not an approval.</summary>
public sealed record ProjectAiDescriptionImportJournal
{
    /// <summary>Versioned journal discriminator.</summary>
    public required string Schema { get; init; }
    /// <summary>Exact raw response SHA, without saving the response itself.</summary>
    public required string RawResponseSha256 { get; init; }
    /// <summary>Untrusted external proposal identity; never a domain ID.</summary>
    public required Guid ProposalId { get; init; }
    /// <summary>Project manifest GUID.</summary>
    public required Guid ProjectId { get; init; }
    /// <summary>Existing EXI identity.</summary>
    public required Guid RequirementId { get; init; }
    /// <summary>Canonical EXI reference.</summary>
    public required string Reference { get; init; }
    /// <summary>Workspace-relative source path verified at commit time.</summary>
    public required string RelativePath { get; init; }
    /// <summary>SHA-256 of the source before publication.</summary>
    public required string OriginalSha256 { get; init; }
    /// <summary>SHA-256 of the staged document after publication.</summary>
    public required string PublishedSha256 { get; init; }
    /// <summary>SHA-256 of the original description field.</summary>
    public required string OriginalFieldSha256 { get; init; }
    /// <summary>SHA-256 of the human-approved description field.</summary>
    public required string ApprovedFieldSha256 { get; init; }
    /// <summary>Explicit provenance reason, never inferred from the AI.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Transactional publication of ONE explicitly human-approved EXI description.
/// All other Markdown and front-matter are preserved; same-volume, SHA-guarded
/// replacement and original/published snapshots support idempotent retry.
/// </summary>
public sealed class ProjectAiDescriptionImportTransactionService
{
    private const int MaxRequirementBytes = 1024 * 1024;
    private const string JournalRoot = ".nodalis-ai-import";
    private const string JournalFile = "intent.json";
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Constructs a workspace-scoped service without writing files.</summary>
    /// <param name="workspaceRoot">Canonical workspace root.</param>
    public ProjectAiDescriptionImportTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Commits only a reviewed description with a durable journal. A second
    /// attempt with identical approval is idempotent; divergent sources fail
    /// closed rather than overwriting local Markdown.
    /// </summary>
    /// <param name="projectDirectory">Active project, never an archive.</param>
    /// <param name="plan">Human-approved, snapshot-bound description plan.</param>
    /// <param name="token">Cancellation before publishing.</param>
    /// <returns>Applied or AlreadyApplied (same previously committed journal).</returns>
    public async Task<ProjectAiTitleCommitStatus> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Descriptions.Count != 1 || plan.Changes.Count != 0 ||
            plan.Creations.Count != 0 ||
            !IsHash(plan.RawResponseSha256))
        {
            throw new InvalidDataException(
                "Une seule description EXI peut être publiée après revue humaine.");
        }

        ProjectAiPreparedDescriptionChange change = plan.Descriptions.Single();
        if (change.ProposalId == Guid.Empty || change.ProjectId == Guid.Empty ||
            change.RequirementId == Guid.Empty ||
            !ProjectRequirementMarkdownCodec.IsReference(change.Reference) ||
            !IsHash(change.ExpectedSha256) ||
            change.CurrentDescription == change.ReviewedDescription ||
            change.ReviewedDescription.Length > 8000 ||
            change.ReviewedDescription.Contains('\0') ||
            change.ReviewedDescription.Contains('\r') ||
            change.ReviewedDescription.Contains('\uFEFF') ||
            change.ReviewedDescription !=
                change.ReviewedDescription.Trim('\n'))
        {
            throw new InvalidDataException("Description ou identité EXI non publiable.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        await VerifyProjectAsync(project, change.ProjectId, token);
        string target = _sources.ResolveExistingFile(change.RelativePath);
        string canonical = Path.GetFullPath(Path.Combine(project,
            "Documentation", "Exigences", change.Reference + ".md"));
        if (!string.Equals(target, canonical, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fichier EXI déplacé ou étranger au projet : publication refusée.");
        }

        string root = Path.Combine(project, JournalRoot);
        Directory.CreateDirectory(root);
        RequireDirectory(root);
        string lockPath = Path.Combine(root, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou du journal d'import non sûr.");
        }

        token.ThrowIfCancellationRequested();
        string directory = Path.Combine(root,
            "description-" + change.ProposalId.ToString("N"));
        string manifestPath = Path.Combine(directory, JournalFile);
        ProjectAiDescriptionImportJournal journal;
        if (Directory.Exists(directory))
        {
            RequireDirectory(directory);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException(
                    "Préparation incomplète, aucune reprise automatique sans journal.");
            }

            journal = await ReadJournalAsync(manifestPath, token);
            RequireMatching(journal, change, plan.RawResponseSha256);
        }
        else
        {
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_workspaceRoot)
                    .ListAsync(project, token);
            ProjectRequirementEntry? entry = catalog.Requirements.SingleOrDefault(
                item => item.Card.Id == change.RequirementId);
            if (entry is null || entry.Card.ProjectId != change.ProjectId ||
                entry.Card.Reference != change.Reference ||
                entry.RelativePath != change.RelativePath ||
                !string.Equals(entry.SourceSha256, change.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Description, change.CurrentDescription,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Description modifiée depuis la revue, ou EXI ambiguë : " +
                    "relancer la comparaison et la confirmation.");
            }

            byte[] originalBytes = await ReadBoundedAsync(target, token);
            string originalHash = Hash(originalBytes);
            if (!string.Equals(originalHash, change.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Document modifié pendant la revue : écrasement interdit.");
            }

            string original = new UTF8Encoding(false, true)
                .GetString(originalBytes);
            ProjectRequirementCard card =
                ProjectRequirementMarkdownCodec.Parse(original);
            if (card.Id != change.RequirementId ||
                card.ProjectId != change.ProjectId ||
                card.Reference != change.Reference)
            {
                throw new InvalidDataException(
                    "Identité EXI non conforme à la revue humaine.");
            }

            string published = ProjectRequirementDescriptionCodec.Rewrite(
                original, card, change.CurrentDescription,
                change.ReviewedDescription);
            byte[] publishedBytes = new UTF8Encoding(false).GetBytes(published);
            journal = new ProjectAiDescriptionImportJournal
            {
                Schema = "nodalis.ai-description-commit.v1",
                RawResponseSha256 = plan.RawResponseSha256,
                ProposalId = change.ProposalId,
                ProjectId = change.ProjectId,
                RequirementId = change.RequirementId,
                Reference = change.Reference,
                RelativePath = change.RelativePath,
                OriginalSha256 = originalHash,
                PublishedSha256 = Hash(publishedBytes),
                OriginalFieldSha256 = Hash(Encoding.UTF8.GetBytes(
                    change.CurrentDescription)),
                ApprovedFieldSha256 = Hash(Encoding.UTF8.GetBytes(
                    change.ReviewedDescription)),
                Reason = change.Reason
            };
            Directory.CreateDirectory(directory);
            RequireDirectory(directory);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, "original.md"), original, token);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, "published.md"), published, token);
            token.ThrowIfCancellationRequested();

            // Intent is published last. No live EXI write before the durable
            // original/published snapshots and their manifest.
            await AtomicFileWriter.WriteNewAllTextAsync(manifestPath,
                JsonSerializer.Serialize(journal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                token);
        }

        await VerifySnapshotAsync(Path.Combine(directory, "original.md"),
            journal.OriginalSha256, token);
        string staged = Path.Combine(directory, "published.md");
        await VerifySnapshotAsync(staged, journal.PublishedSha256, token);

        string currentHash = Hash(await ReadBoundedAsync(target, token));
        if (string.Equals(currentHash, journal.PublishedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            return ProjectAiTitleCommitStatus.AlreadyApplied;
        }

        if (!string.Equals(currentHash, journal.OriginalSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "EXI divergente du journal : annulation, inspection manuelle requise.");
        }

        await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(target,
            journal.OriginalSha256, staged, journal.PublishedSha256, token);
        string finalHash = Hash(await ReadBoundedAsync(target,
            CancellationToken.None));
        if (!string.Equals(finalHash, journal.PublishedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Vérification post-publication échouée : journal à conserver.");
        }

        return ProjectAiTitleCommitStatus.Applied;
    }

    /// <summary>Requires the original human authorization on every retry.</summary>
    /// <param name="journal">Durable original intent.</param>
    /// <param name="change">Newly supplied user-approved plan.</param>
    /// <param name="rawHash">SHA of the reviewed external response.</param>
    private static void RequireMatching(ProjectAiDescriptionImportJournal journal,
        ProjectAiPreparedDescriptionChange change, string rawHash)
    {
        if (journal.Schema != "nodalis.ai-description-commit.v1" ||
            journal.RawResponseSha256 != rawHash ||
            journal.ProposalId != change.ProposalId ||
            journal.ProjectId != change.ProjectId ||
            journal.RequirementId != change.RequirementId ||
            journal.Reference != change.Reference ||
            journal.RelativePath != change.RelativePath ||
            journal.Reason != change.Reason ||
            !string.Equals(journal.OriginalSha256, change.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !IsHash(journal.PublishedSha256) ||
            journal.OriginalFieldSha256 != Hash(Encoding.UTF8.GetBytes(
                change.CurrentDescription)) ||
            journal.ApprovedFieldSha256 != Hash(Encoding.UTF8.GetBytes(
                change.ReviewedDescription)))
        {
            throw new InvalidDataException(
                "Journal déjà existant pour une autorisation différente.");
        }
    }

    /// <summary>Reads a bounded immutable journal, never following symlinks.</summary>
    /// <param name="path">Existing local intent file.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Deserialized persisted intent.</returns>
    private static async Task<ProjectAiDescriptionImportJournal>
        ReadJournalAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 16000)
        {
            throw new InvalidDataException("Journal absent ou non sûr.");
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectAiDescriptionImportJournal>(
                await File.ReadAllTextAsync(path, token),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidDataException("Journal vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Journal invalide : récupération automatique interdite.", exception);
        }
    }

    /// <summary>Checks that a previously saved snapshot was not altered.</summary>
    /// <param name="path">Snapshot path.</param>
    /// <param name="expectedSha">Immutable original SHA.</param>
    /// <param name="token">Cancellation.</param>
    private static async Task VerifySnapshotAsync(string path,
        string expectedSha, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            !string.Equals(Hash(await ReadBoundedAsync(path, token)),
                expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Instantané d'import manquant ou modifié : reprise refusée.");
        }
    }

    /// <summary>Verifies project identity and active, local membership.</summary>
    /// <param name="project">Absolute selected project path.</param>
    /// <param name="projectId">Approved owning project identity.</param>
    /// <param name="token">Cancellation.</param>
    private async Task VerifyProjectAsync(string project,
        Guid projectId, CancellationToken token)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (segments.Length < 4 ||
            !string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !segments.Skip(2).Any(segment => string.Equals(segment,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Import IA interdit hors projet actif du workspace.");
        }

        string path = Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.ResolveExistingFile(_sources.GetReferenceForExistingFile(path));
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, token);
        if (manifest.Id != projectId || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Import sur projet archivé, déplacé ou non propriétaire interdit.");
        }
    }

    /// <summary>Reads no more than one MiB of canonical Markdown.</summary>
    /// <param name="path">Verified source or staged snapshot.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Bounded exact bytes.</returns>
    private static async Task<byte[]> ReadBoundedAsync(string path,
        CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxRequirementBytes)
        {
            throw new InvalidDataException("Fiche EXI trop grande.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxRequirementBytes)
        {
            throw new InvalidDataException("Fiche EXI modifiée pendant la lecture.");
        }

        return bytes;
    }

    /// <summary>Rejects links and junctions in journal directories.</summary>
    /// <param name="directory">Existing directory to inspect.</param>
    private static void RequireDirectory(string directory)
    {
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Journal sous jonction ou absent.");
        }
    }

    /// <summary>Verifies a canonical SHA-256 hexadecimal fingerprint.</summary>
    /// <param name="hash">SHA-256 text.</param>
    /// <returns>True for exactly 64 hexadecimal characters.</returns>
    private static bool IsHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Hashes exact bytes for optimistic compare-and-swap.</summary>
    /// <param name="bytes">Raw content bytes.</param>
    /// <returns>Upper-case SHA-256 fingerprint.</returns>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
