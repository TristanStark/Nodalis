using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Outcome of publishing one explicitly approved new EXI draft.</summary>
public enum ProjectAiRequirementCreateStatus
{
    Created,
    AlreadyCreated
}

/// <summary>
/// One durable human-initiated EXI creation. The GUID, reference and timestamp
/// are assigned by Nodalis, never accepted from an external model.
/// </summary>
public sealed record ProjectAiRequirementCreateJournal
{
    public required string Schema { get; init; }
    public required string RawResponseSha256 { get; init; }
    public required Guid ProposalId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid RequirementId { get; init; }
    public required string Reference { get; init; }
    public required string ReviewedTitle { get; init; }
    public required string PublishedSha256 { get; init; }
    public string? Reason { get; init; }
}

/// <summary>One canonical, verifiable created requirement.</summary>
/// <param name="Status">Creation or safe no-op retry.</param>
/// <param name="Entry">Persisted, project-owned EXI entry.</param>
public sealed record ProjectAiRequirementCreateResult(
    ProjectAiRequirementCreateStatus Status, ProjectRequirementEntry Entry);

/// <summary>
/// Atomically creates one human-reviewed EXI draft and a durable replay
/// journal. Uses the SAME exclusive project lock as reviewed title updates.
/// Does not create any other object, accept status or infer relations.
/// </summary>
public sealed class ProjectAiRequirementCreateTransactionService
{
    private const string JournalDirectoryName = ".nodalis-ai-import";
    private const string JournalFileName = "intent.json";
    private const string StagedFileName = "published.md";
    private const string ReceiptFileName = "applied.json";
    private const int MaxMarkdownBytes = 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Initializes a read-only service until an explicit commit.</summary>
    /// <param name="workspaceRoot">Current single workspace.</param>
    public ProjectAiRequirementCreateTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Reads the reference already assigned to this proposal, if journaled.
    /// No file or directory is created, and the returned value is NOT an
    /// authorization to publish.
    /// </summary>
    /// <param name="projectDirectory">Selected project root.</param>
    /// <param name="plan">Explicitly reviewed creation plan.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Existing journal reference, or null for an uncommitted draft.</returns>
    public async Task<string?> GetJournaledReferenceAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken cancellationToken = default)
    {
        ProjectAiPreparedRequirementCreation creation = ValidatePlan(plan);
        (string project, Guid projectId) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (creation.ProjectId != projectId)
        {
            throw new InvalidDataException(
                "Création qualifiée dans un autre projet : import refusé.");
        }

        string transaction = Path.Combine(project, JournalDirectoryName,
            creation.ProposalId.ToString("N"));
        if (!Directory.Exists(transaction))
        {
            return null;
        }

        ProjectAiRequirementCreateJournal journal = await ReadJournalAsync(
            transaction, cancellationToken);
        ValidateJournal(journal, plan, creation, projectId);
        return journal.Reference;
    }

    /// <summary>
    /// Commits one creation only after the user approved its EXACT EXI code.
    /// A no-overwrite move forbids replacing an existing requirement. A retry
    /// either yields the exact persisted identity or fails closed.
    /// </summary>
    /// <param name="projectDirectory">Selected project root.</param>
    /// <param name="plan">Plan with one explicitly accepted creation.</param>
    /// <param name="expectedReference">Nodalis-generated code shown to and approved by the user.</param>
    /// <param name="cancellationToken">Abort before publication.</param>
    /// <returns>Created requirement or verified idempotent no-op.</returns>
    public async Task<ProjectAiRequirementCreateResult> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        string expectedReference, CancellationToken cancellationToken = default)
    {
        ProjectAiPreparedRequirementCreation creation = ValidatePlan(plan);
        if (!ProjectRequirementMarkdownCodec.IsReference(expectedReference))
        {
            throw new InvalidDataException(
                "Référence EXI non approuvée ou hors plage.");
        }

        (string project, Guid projectId) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (creation.ProjectId != projectId)
        {
            throw new InvalidDataException(
                "La création IA a été approuvée pour un autre projet.");
        }

        string journalRoot = Path.Combine(project, JournalDirectoryName);
        Directory.CreateDirectory(journalRoot);
        RequireNormalDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou d'import sous lien symbolique.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string transaction = Path.Combine(journalRoot,
            creation.ProposalId.ToString("N"));
        ProjectAiRequirementCreateJournal journal;
        if (Directory.Exists(transaction))
        {
            journal = await ReadJournalAsync(transaction, cancellationToken);
            ValidateJournal(journal, plan, creation, projectId);
            if (journal.Reference != expectedReference)
            {
                throw new InvalidDataException(
                    "Référence déjà réservée différente : requalifier l'import.");
            }
        }
        else
        {
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_workspaceRoot)
                    .ListAsync(project, cancellationToken);
            if (catalog.NextReference != expectedReference)
            {
                throw new InvalidDataException(
                    "Prochaine référence EXI modifiée après confirmation.");
            }

            ProjectRequirementCard card = new()
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Reference = expectedReference,
                Title = creation.ReviewedTitle,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            string markdown = ProjectRequirementMarkdownCodec.Format(card);
            string hash = Hash(Encoding.UTF8.GetBytes(markdown));
            journal = new ProjectAiRequirementCreateJournal
            {
                Schema = "nodalis.ai-exi-create.v1",
                RawResponseSha256 = plan.RawResponseSha256,
                ProposalId = creation.ProposalId,
                ProjectId = projectId,
                RequirementId = card.Id,
                Reference = expectedReference,
                ReviewedTitle = creation.ReviewedTitle,
                PublishedSha256 = hash,
                Reason = creation.Reason
            };
            Directory.CreateDirectory(transaction);
            RequireNormalDirectory(transaction);
            // A partially prepared transaction with no intent.json is
            // intentionally left for explicit inspection, never auto-reused.
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(transaction, StagedFileName), markdown,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(transaction, JournalFileName),
                JsonSerializer.Serialize(journal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
        }

        RequireNormalDirectory(transaction);
        string snapshotPath = Path.Combine(transaction, StagedFileName);
        string staged = await ReadValidSnapshotAsync(snapshotPath, journal,
            cancellationToken);
        string docs = Path.Combine(project, "Documentation");
        string requirements = Path.Combine(docs, "Exigences");
        string destination = Path.Combine(requirements, journal.Reference + ".md");
        string receipt = Path.Combine(transaction, ReceiptFileName);

        // The receipt is written AFTER the canonical Markdown. If it exists
        // and the target is now missing, never recreate a deleted document.
        bool receiptExists = File.Exists(receipt);
        if (receiptExists)
        {
            string receiptText = await ReadSmallSafeAsync(receipt, cancellationToken);
            string? receiptHash;
            try
            {
                receiptHash = JsonSerializer.Deserialize<string>(receiptText);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Preuve de création JSON invalide : aucune reprise.",
                    exception);
            }

            if (receiptHash != journal.PublishedSha256)
            {
                throw new InvalidDataException(
                    "Preuve de création corrompue : aucun réimport automatique.");
            }
        }

        if (File.Exists(destination))
        {
            string verified = _sources.ResolveExistingFile(
                WorkspaceRelative(destination));
            string currentHash = Hash(await ReadBoundedAsync(
                verified, cancellationToken));
            if (!string.Equals(currentHash, journal.PublishedSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "La référence EXI est occupée par une autre fiche : " +
                    "aucun écrasement ni réimport automatique.");
            }

            if (!receiptExists)
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    receipt, JsonSerializer.Serialize(journal.PublishedSha256),
                    cancellationToken);
            }

            return new ProjectAiRequirementCreateResult(
                ProjectAiRequirementCreateStatus.AlreadyCreated,
                ReadCreatedEntry(staged, destination, journal));
        }

        if (receiptExists)
        {
            throw new InvalidDataException(
                "La fiche EXI créée précédemment a disparu. Restauration " +
                "automatique interdite : inspection humaine requise.");
        }

        // Recheck the current registry while the per-project lock is held.
        ProjectRequirementCatalog current =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(project, cancellationToken);
        if (current.NextReference != journal.Reference)
        {
            throw new InvalidDataException(
                "La référence EXI est maintenant réservée ailleurs. " +
                "Requalifier avant toute nouvelle publication.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(docs);
        RequireNormalDirectory(docs);
        Directory.CreateDirectory(requirements);
        RequireNormalDirectory(requirements);
        // File.Move in WriteNewAllTextAsync is no-overwrite, including races
        // with a different process not honoring the local project lock.
        await AtomicFileWriter.WriteNewAllTextAsync(
            destination, staged, cancellationToken);

        string verifiedDestination = _sources.ResolveExistingFile(
            WorkspaceRelative(destination));
        string finalHash = Hash(await ReadBoundedAsync(
            verifiedDestination, CancellationToken.None));
        if (!string.Equals(finalHash, journal.PublishedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Création publiée mais contenu divergent : conserver le journal.");
        }

        await AtomicFileWriter.WriteNewAllTextAsync(
            receipt, JsonSerializer.Serialize(journal.PublishedSha256),
            CancellationToken.None);
        return new ProjectAiRequirementCreateResult(
            ProjectAiRequirementCreateStatus.Created,
            ReadCreatedEntry(staged, destination, journal));
    }

    /// <summary>Guards the in-memory scope and human-approved fields.</summary>
    private static ProjectAiPreparedRequirementCreation ValidatePlan(
        ProjectAiPreparedReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Changes.Count != 0 || plan.Descriptions.Count != 0 ||
            plan.CriterionTitles.Count != 0 ||
            plan.CriterionCreations.Count != 0 ||
            plan.Creations.Count != 1 ||
            plan.RawResponseSha256 is not { Length: 64 } ||
            !plan.RawResponseSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Une seule création EXI qualifiée est autorisée à la fois.");
        }

        ProjectAiPreparedRequirementCreation creation = plan.Creations[0];
        if (creation.ProposalId == Guid.Empty ||
            creation.ProjectId == Guid.Empty ||
            string.IsNullOrWhiteSpace(creation.ReviewedTitle) ||
            creation.ReviewedTitle != creation.ReviewedTitle.Trim() ||
            creation.ReviewedTitle.Length > 240 ||
            creation.ReviewedTitle.Any(char.IsControl) ||
            creation.Reason is { Length: > 1200 })
        {
            throw new InvalidDataException(
                "Titre de création ou justification humaine non conforme.");
        }

        return creation;
    }

    /// <summary>Reads manifest and forbids writes in archived or foreign projects.</summary>
    private async Task<(string Project, Guid ProjectId)> ResolveProjectAsync(
        string projectDirectory, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || segments.Length < 4 ||
            !string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !segments.Skip(2).Any(segment => string.Equals(
                segment, WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Import IA interdit hors d'un projet actif du workspace.");
        }

        string manifestPath = Path.Combine(
            project, WorkspaceLayout.ProjectManifestFileName);
        string safe = _sources.GetReferenceForExistingFile(manifestPath);
        _sources.ResolveExistingFile(safe);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, token);
        if (manifest.Id == Guid.Empty || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet archivé ou sans identité canonique : création refusée.");
        }

        return (project, manifest.Id);
    }

    /// <summary>Loads an existing journal only from the guarded transaction folder.</summary>
    private static async Task<ProjectAiRequirementCreateJournal> ReadJournalAsync(
        string transaction, CancellationToken token)
    {
        RequireNormalDirectory(transaction);
        string path = Path.Combine(transaction, JournalFileName);
        string json = await ReadSmallSafeAsync(path, token);
        try
        {
            return JsonSerializer.Deserialize<ProjectAiRequirementCreateJournal>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidDataException("Journal de création EXI vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Journal de création EXI malformé, récupération manuelle.",
                exception);
        }
    }

    /// <summary>Matches replay authority to the exact original human choice.</summary>
    private static void ValidateJournal(
        ProjectAiRequirementCreateJournal journal,
        ProjectAiPreparedReviewPlan plan,
        ProjectAiPreparedRequirementCreation creation, Guid projectId)
    {
        if (journal.Schema != "nodalis.ai-exi-create.v1" ||
            journal.RawResponseSha256 != plan.RawResponseSha256 ||
            journal.ProposalId != creation.ProposalId ||
            journal.ProjectId != projectId ||
            journal.RequirementId == Guid.Empty ||
            !ProjectRequirementMarkdownCodec.IsReference(journal.Reference) ||
            journal.ReviewedTitle != creation.ReviewedTitle ||
            journal.Reason != creation.Reason ||
            journal.PublishedSha256 is not { Length: 64 } ||
            !journal.PublishedSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Journal incompatible avec la décision humaine : " +
                "aucun réimport ni écrasement automatique.");
        }
    }

    /// <summary>Reads immutable, expected canonical draft bytes from the transaction.</summary>
    private static async Task<string> ReadValidSnapshotAsync(
        string snapshotPath, ProjectAiRequirementCreateJournal journal,
        CancellationToken token)
    {
        if (!File.Exists(snapshotPath) ||
            (File.GetAttributes(snapshotPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Instantané de création absent ou non sûr.");
        }

        byte[] bytes = await ReadBoundedAsync(snapshotPath, token);
        if (!string.Equals(Hash(bytes), journal.PublishedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Instantané de création corrompu.");
        }

        string text = new UTF8Encoding(false, true).GetString(bytes);
        ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(text);
        if (card.Id != journal.RequirementId ||
            card.ProjectId != journal.ProjectId ||
            card.Reference != journal.Reference ||
            card.Title != journal.ReviewedTitle ||
            card.Validation != ProjectRequirementValidation.Draft ||
            card.Verification != ProjectRequirementVerification.NotVerified ||
            card.Implementation != ProjectRequirementImplementation.NotStarted ||
            card.Kind != ProjectRequirementKind.Unclassified)
        {
            throw new InvalidDataException(
                "L'instantané de création comporte des identités ou états non approuvés.");
        }

        return text;
    }

    /// <summary>Builds the same project-owned entry after creation or replay.</summary>
    private ProjectRequirementEntry ReadCreatedEntry(
        string published, string destination, ProjectAiRequirementCreateJournal journal)
    {
        string reference = _sources.GetReferenceForExistingFile(destination);
        return new ProjectRequirementEntry(
            ProjectRequirementMarkdownCodec.Parse(published),
            reference, journal.PublishedSha256);
    }

    /// <summary>Reads a bounded, non-symlinked small journal or receipt.</summary>
    private static async Task<string> ReadSmallSafeAsync(
        string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 16384)
        {
            throw new InvalidDataException("Journal d'import absent ou non sûr.");
        }

        return await File.ReadAllTextAsync(path, token);
    }

    /// <summary>Reads a Markdown source with a strict byte budget.</summary>
    private static async Task<byte[]> ReadBoundedAsync(
        string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxMarkdownBytes)
        {
            throw new InvalidDataException("Exigence EXI trop volumineuse.");
        }

        byte[] data = await File.ReadAllBytesAsync(path, token);
        if (data.Length > MaxMarkdownBytes)
        {
            throw new InvalidDataException("Taille EXI modifiée durant la lecture.");
        }

        return data;
    }

    /// <summary>Converts a canonical target into a portable workspace reference.</summary>
    private string WorkspaceRelative(string destination) =>
        Path.GetRelativePath(_workspaceRoot, destination)
            .Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Disallows journal or EXI directories redirected by a junction.</summary>
    private static void RequireNormalDirectory(string directory)
    {
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier d'import ou d'exigences introuvable ou sous jonction.");
        }
    }

    /// <summary>Computes a portable uppercase byte-level SHA-256.</summary>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
