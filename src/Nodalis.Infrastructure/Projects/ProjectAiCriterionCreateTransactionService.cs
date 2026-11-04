using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Journaled CA creation from a human-reviewed AI suggestion. All official
/// identifiers are allocated by Nodalis after the user accepts the suggestion.
/// </summary>
public sealed record ProjectAiCriterionCreateJournal
{
    /// <summary>Versioned intent schema.</summary>
    public required string Schema { get; init; }
    /// <summary>External response SHA; raw text is not archived here.</summary>
    public required string RawResponseSha256 { get; init; }
    /// <summary>External proposal key, not a Nodalis business identity.</summary>
    public required Guid ProposalId { get; init; }
    /// <summary>Current project's manifest ID.</summary>
    public required Guid ProjectId { get; init; }
    /// <summary>Existing containing EXI ID.</summary>
    public required Guid RequirementId { get; init; }
    /// <summary>Canonical EXI reference.</summary>
    public required string Reference { get; init; }
    /// <summary>Canonical containing Markdown file.</summary>
    public required string RelativePath { get; init; }
    /// <summary>Fresh locally assigned criterion block ID.</summary>
    public required Guid CriterionId { get; init; }
    /// <summary>Next free CA-xx code, confirmed by the user.</summary>
    public required string CriterionReference { get; init; }
    /// <summary>Human-approved criterion title.</summary>
    public required string ReviewedTitle { get; init; }
    /// <summary>Original EXI SHA prior to creation.</summary>
    public required string OriginalSha256 { get; init; }
    /// <summary>Full EXI SHA after appending the new CA.</summary>
    public required string PublishedSha256 { get; init; }
    /// <summary>Optional reason supplied by the user.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Transactionally adds exactly one CA inside a preexisting EXI, preserving
/// legacy CA markers, independent EXI states and unrelated Markdown. The
/// source must be SHA-identical to the approved review at publication time.
/// </summary>
public sealed class ProjectAiCriterionCreateTransactionService
{
    private const int MaxBytes = 1024 * 1024;
    private const string RootFolder = ".nodalis-ai-import";
    private const string JournalFile = "intent.json";
    private const string ReceiptFile = "completed.json";
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Initializes a service with a single, explicit workspace.</summary>
    /// <param name="workspaceRoot">Authoritative workspace root.</param>
    public ProjectAiCriterionCreateTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Computes the CA code to display before the user's confirmation. Does
    /// not reserve codes, generate IDs, create folders or modify Markdown.
    /// A later commit MUST recheck this same code under its project lock.
    /// </summary>
    /// <param name="projectDirectory">Active canonical project.</param>
    /// <param name="plan">One reviewed CA creation only.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>First free local CA code.</returns>
    public async Task<string> PreviewReferenceAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken token = default)
    {
        ProjectAiPreparedCriterionCreation creation = ValidatePlan(plan);
        string project = await VerifyProjectAsync(
            projectDirectory, creation.ProjectId, token);
        string target = ResolveSource(project, creation);
        ProjectRequirementCatalog catalog =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(project, token);
        CheckParent(catalog, creation);
        if (catalog.Requirements.Single(item =>
                item.Card.Id == creation.RequirementId).SourceSha256 !=
            creation.ExpectedSha256)
        {
            throw new InvalidDataException(
                "EXI modifiée depuis la comparaison : revoir le projet.");
        }

        byte[] original = await ReadBoundedAsync(target, token);
        if (Hash(original) != creation.ExpectedSha256)
        {
            throw new InvalidDataException(
                "EXI modifiée pendant l'aperçu de création CA.");
        }

        string source = DecodeOriginal(original);
        return ProjectRequirementCriterionCreationCodec.NextReference(source) ??
            throw new InvalidDataException(
                "Les 99 références CA du parent EXI sont utilisées.");
    }

    /// <summary>
    /// Saves one new CA with a Nodalis-generated stable GUID. The caller
    /// must separately confirm the previously previewed next reference.
    /// Recoverable snapshots and intent precede the only business write.
    /// A completed receipt forbids re-import after later manual rollback.
    /// </summary>
    /// <param name="projectDirectory">Verified active project.</param>
    /// <param name="plan">Human-approved CA creation plan.</param>
    /// <param name="expectedReference">Exact CA code shown in confirmation.</param>
    /// <param name="token">Cancellation before the business write.</param>
    /// <returns>Applied or AlreadyApplied for the same exact transaction.</returns>
    public async Task<ProjectAiTitleCommitStatus> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        string expectedReference, CancellationToken token = default)
    {
        ProjectAiPreparedCriterionCreation creation = ValidatePlan(plan);
        if (!ProjectRequirementCriterionCodec.IsReference(expectedReference))
        {
            throw new InvalidDataException("Référence CA non canonique.");
        }

        string project = await VerifyProjectAsync(
            projectDirectory, creation.ProjectId, token);
        string target = ResolveSource(project, creation);
        string root = Path.Combine(project, RootFolder);
        Directory.CreateDirectory(root);
        RequireDirectory(root);
        string lockPath = Path.Combine(root, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou CA non sûr.");
        }

        token.ThrowIfCancellationRequested();
        ProjectRequirementCatalog catalog =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(project, token);
        ProjectRequirementEntry parent = CheckParent(catalog, creation);
        string directory = Path.Combine(root,
            "criterion-create-" + creation.ProposalId.ToString("N"));
        string manifest = Path.Combine(directory, JournalFile);
        ProjectAiCriterionCreateJournal journal;
        if (Directory.Exists(directory))
        {
            RequireDirectory(directory);
            journal = await ReadJournalAsync(manifest, token);
            RequireMatching(journal, creation, plan.RawResponseSha256,
                expectedReference);
        }
        else
        {
            if (parent.SourceSha256 != creation.ExpectedSha256)
            {
                throw new InvalidDataException(
                    "Fiche EXI modifiée : nouvelle comparaison obligatoire.");
            }

            byte[] sourceBytes = await ReadBoundedAsync(target, token);
            if (Hash(sourceBytes) != creation.ExpectedSha256)
            {
                throw new InvalidDataException(
                    "Source EXI modifiée après la décision humaine.");
            }

            string original = DecodeOriginal(sourceBytes);
            string nextCode =
                ProjectRequirementCriterionCreationCodec.NextReference(original) ??
                throw new InvalidDataException("Plus de codes CA disponibles.");
            if (nextCode != expectedReference)
            {
                throw new InvalidDataException(
                    "Le prochain code CA a changé depuis la confirmation.");
            }

            ProjectRequirementCard card =
                ProjectRequirementMarkdownCodec.Parse(original);
            if (card.Id != creation.RequirementId ||
                card.ProjectId != creation.ProjectId ||
                card.Reference != creation.Reference)
            {
                throw new InvalidDataException("EXI parente différente du plan.");
            }

            Guid generated = Guid.NewGuid();
            if (catalog.Requirements.Any(item => item.Card.Id == generated) ||
                catalog.Requirements.SelectMany(item => item.Criteria)
                    .Any(item => item.Id == generated))
            {
                throw new InvalidDataException(
                    "Collision d'identifiant CA : nouvelle transaction nécessaire.");
            }

            string published = ProjectRequirementCriterionCreationCodec.Insert(
                original, card, expectedReference, creation.ReviewedTitle,
                generated);
            journal = new ProjectAiCriterionCreateJournal
            {
                Schema = "nodalis.ai-criterion-create.v1",
                RawResponseSha256 = plan.RawResponseSha256,
                ProposalId = creation.ProposalId,
                ProjectId = creation.ProjectId,
                RequirementId = creation.RequirementId,
                Reference = creation.Reference,
                RelativePath = creation.RelativePath,
                CriterionId = generated,
                CriterionReference = expectedReference,
                ReviewedTitle = creation.ReviewedTitle,
                OriginalSha256 = creation.ExpectedSha256,
                PublishedSha256 = Hash(new UTF8Encoding(false).GetBytes(published)),
                Reason = creation.Reason
            };
            Directory.CreateDirectory(directory);
            RequireDirectory(directory);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, "original.md"), original, token);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, "published.md"), published, token);
            token.ThrowIfCancellationRequested();
            await AtomicFileWriter.WriteNewAllTextAsync(manifest,
                JsonSerializer.Serialize(journal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)), token);
        }

        string originalPath = Path.Combine(directory, "original.md");
        string publishedPath = Path.Combine(directory, "published.md");
        byte[] originalSnapshot = await ReadSnapshotAsync(
            originalPath, journal.OriginalSha256, token);
        await ReadSnapshotAsync(publishedPath, journal.PublishedSha256, token);
        string originalText = DecodeOriginal(originalSnapshot);
        ProjectRequirementCard originalCard =
            ProjectRequirementMarkdownCodec.Parse(originalText);
        if (originalCard.Id != creation.RequirementId ||
            originalCard.ProjectId != creation.ProjectId ||
            originalCard.Reference != creation.Reference ||
            ProjectRequirementCriterionCreationCodec.NextReference(
                originalText) != expectedReference)
        {
            throw new InvalidDataException(
                "Journal CA de création non conforme au document approuvé.");
        }

        // Reconstruct the ONLY allowed Markdown from the reviewed original.
        // Forging both published.md and its journal SHA cannot bypass this.
        string expected = ProjectRequirementCriterionCreationCodec.Insert(
            originalText, originalCard, expectedReference,
            creation.ReviewedTitle, journal.CriterionId);
        if (Hash(new UTF8Encoding(false).GetBytes(expected)) !=
            journal.PublishedSha256)
        {
            throw new InvalidDataException(
                "Le journal de création contient des changements non approuvés.");
        }

        // A CA identifier generated in the project may occur exactly once:
        // in this EXI after a completed or interrupted publication, nowhere
        // else. Never follow another project's AI-supplied path.
        ProjectRequirementCriterion[] known = catalog.Requirements
            .SelectMany(item => item.Criteria)
            .Where(item => item.Id == journal.CriterionId).ToArray();
        if (catalog.Requirements.Any(item =>
                item.Card.Id == journal.CriterionId) ||
            known.Length > 1 ||
            known.Length == 1 && !parent.Criteria.Any(item =>
                item.Id == journal.CriterionId &&
                item.Reference == journal.CriterionReference &&
                item.Title == creation.ReviewedTitle))
        {
            throw new InvalidDataException(
                "Identité CA attribuée réutilisée par un autre objet.");
        }

        string receiptPath = Path.Combine(directory, ReceiptFile);
        bool completed = File.Exists(receiptPath);
        if (completed)
        {
            if ((File.GetAttributes(receiptPath) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(receiptPath).Length > 256)
            {
                throw new InvalidDataException("Preuve de création CA suspecte.");
            }

            string? recorded;
            try
            {
                recorded = JsonSerializer.Deserialize<string>(
                    await File.ReadAllTextAsync(receiptPath, token));
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Preuve de création CA illisible.", exception);
            }

            if (recorded != journal.PublishedSha256)
            {
                throw new InvalidDataException(
                    "Preuve de création CA différente du document publié.");
            }
        }

        string liveHash = Hash(await ReadBoundedAsync(target, token));
        if (liveHash == journal.PublishedSha256)
        {
            if (!completed)
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    receiptPath, JsonSerializer.Serialize(journal.PublishedSha256),
                    CancellationToken.None);
            }

            return ProjectAiTitleCommitStatus.AlreadyApplied;
        }

        if (completed || liveHash != journal.OriginalSha256)
        {
            throw new InvalidDataException(
                "EXI modifiée, supprimée ou restaurée après publication : " +
                "aucun écrasement depuis l'ancien journal.");
        }

        await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
            target, journal.OriginalSha256, publishedPath,
            journal.PublishedSha256, token);
        if (Hash(await ReadBoundedAsync(target, CancellationToken.None)) !=
            journal.PublishedSha256)
        {
            throw new InvalidDataException(
                "Publication CA incertaine : conserver le journal.");
        }

        await AtomicFileWriter.WriteNewAllTextAsync(receiptPath,
            JsonSerializer.Serialize(journal.PublishedSha256),
            CancellationToken.None);
        return ProjectAiTitleCommitStatus.Applied;
    }

    /// <summary>Validates only one approved CA creation, no hidden other changes.</summary>
    /// <param name="plan">Snapshot-bound human review.</param>
    /// <returns>One safe, inert creation request.</returns>
    private static ProjectAiPreparedCriterionCreation ValidatePlan(
        ProjectAiPreparedReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CriterionCreations.Count != 1 ||
            plan.CriterionTitles.Count != 0 || plan.Descriptions.Count != 0 ||
            plan.Changes.Count != 0 || plan.Creations.Count != 0 ||
            !IsHash(plan.RawResponseSha256))
        {
            throw new InvalidDataException(
                "Création CA réservée à un unique choix humain approuvé.");
        }

        ProjectAiPreparedCriterionCreation create =
            plan.CriterionCreations.Single();
        if (create.ProjectId == Guid.Empty ||
            create.RequirementId == Guid.Empty ||
            create.ProposalId == Guid.Empty ||
            !ProjectRequirementMarkdownCodec.IsReference(create.Reference) ||
            !IsHash(create.ExpectedSha256) ||
            string.IsNullOrWhiteSpace(create.ReviewedTitle) ||
            create.ReviewedTitle != create.ReviewedTitle.Trim() ||
            create.ReviewedTitle.Length > 240 ||
            create.ReviewedTitle.Any(char.IsControl) ||
            create.ReviewedTitle.StartsWith('#') ||
            create.ReviewedTitle.EndsWith('#') ||
            create.ReviewedTitle.Contains("<!--", StringComparison.Ordinal) ||
            create.Reason is { Length: > 1200 })
        {
            throw new InvalidDataException("Plan de création CA non canonique.");
        }

        return create;
    }

    /// <summary>Reads project membership, rejecting archive and workspace escape.</summary>
    /// <param name="projectDirectory">Selected local project.</param>
    /// <param name="projectId">Approved owning GUID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Canonical, active project folder.</returns>
    private async Task<string> VerifyProjectAsync(
        string projectDirectory, Guid projectId, CancellationToken token)
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
            !segments.Skip(2).Any(segment => string.Equals(segment,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Création CA hors projet actif interdite.");
        }

        string manifest = Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(manifest));
        ProjectManifest metadata = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifest, token);
        if (metadata.Id != projectId || metadata.ArchivedUtc.HasValue ||
            metadata.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet CA archivé ou appartenant à un autre workspace.");
        }

        return project;
    }

    /// <summary>Resolves the exact existing canonical EXI within its project.</summary>
    /// <param name="project">Verified project path.</param>
    /// <param name="create">Human plan with canonical parent identity.</param>
    /// <returns>Symlink-safe full path to existing EXI source.</returns>
    private string ResolveSource(
        string project, ProjectAiPreparedCriterionCreation create)
    {
        string target = _sources.ResolveExistingFile(create.RelativePath);
        string expected = Path.GetFullPath(Path.Combine(project,
            "Documentation", "Exigences", create.Reference + ".md"));
        if (!string.Equals(target, expected,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Référence CA liée à une EXI d'un autre projet.");
        }

        return target;
    }

    /// <summary>Requires an unambiguous containing EXI in the current project.</summary>
    /// <param name="catalog">Fresh canonical registry.</param>
    /// <param name="create">Reviewed CA creation intent.</param>
    /// <returns>Owning requirement, not the new CA identity.</returns>
    private static ProjectRequirementEntry CheckParent(
        ProjectRequirementCatalog catalog,
        ProjectAiPreparedCriterionCreation create)
    {
        ProjectRequirementEntry[] parents = catalog.Requirements
            .Where(item => item.Card.Id == create.RequirementId).ToArray();
        if (parents.Length != 1 || parents[0].Card.ProjectId != create.ProjectId ||
            parents[0].Card.Reference != create.Reference ||
            parents[0].RelativePath != create.RelativePath ||
            catalog.Requirements.SelectMany(item => item.Criteria)
                .Any(item => item.Id == create.RequirementId))
        {
            throw new InvalidDataException(
                "EXI parente CA absente, ambiguë ou déplacée.");
        }

        return parents[0];
    }

    /// <summary>Refuses a journal conflicting with the confirmed CA creation.</summary>
    /// <param name="journal">Previously persisted intent.</param>
    /// <param name="create">Exact human-approved plan.</param>
    /// <param name="rawHash">Fingerprinted external response.</param>
    /// <param name="code">CA reference shown in UI confirmation.</param>
    private static void RequireMatching(
        ProjectAiCriterionCreateJournal journal,
        ProjectAiPreparedCriterionCreation create, string rawHash, string code)
    {
        if (journal.Schema != "nodalis.ai-criterion-create.v1" ||
            journal.RawResponseSha256 != rawHash ||
            journal.ProposalId != create.ProposalId ||
            journal.ProjectId != create.ProjectId ||
            journal.RequirementId != create.RequirementId ||
            journal.Reference != create.Reference ||
            journal.RelativePath != create.RelativePath ||
            journal.CriterionReference != code ||
            journal.ReviewedTitle != create.ReviewedTitle ||
            journal.Reason != create.Reason ||
            journal.OriginalSha256 != create.ExpectedSha256 ||
            journal.CriterionId == Guid.Empty ||
            !IsHash(journal.PublishedSha256))
        {
            throw new InvalidDataException(
                "Journal CA différent de la création précédemment acceptée.");
        }
    }

    /// <summary>Reads one bounded persisted intent without following a symlink.</summary>
    /// <param name="path">Journal intent.json.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified record, subject to plan and snapshot checks.</returns>
    private static async Task<ProjectAiCriterionCreateJournal> ReadJournalAsync(
        string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 16000)
        {
            throw new InvalidDataException(
                "Journal CA absent, trop long ou sous lien symbolique.");
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectAiCriterionCreateJournal>(
                await File.ReadAllTextAsync(path, token),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidDataException("Journal CA vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Journal CA corrompu : inspection manuelle obligatoire.", exception);
        }
    }

    /// <summary>Checks an immutable journal snapshot before replay.</summary>
    /// <param name="path">Existing original or published Markdown copy.</param>
    /// <param name="expectedSha">Original SHA-256 journal value.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Validated exact bytes.</returns>
    private static async Task<byte[]> ReadSnapshotAsync(string path,
        string expectedSha, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Snapshot CA absent ou redirigé.");
        }

        byte[] bytes = await ReadBoundedAsync(path, token);
        if (Hash(bytes) != expectedSha)
        {
            throw new InvalidDataException(
                "Snapshot de création CA modifié depuis l'intention.");
        }

        return bytes;
    }

    /// <summary>Decodes no-BOM UTF-8 so journal hashes remain exact.</summary>
    /// <param name="bytes">Read-only canonical source.</param>
    /// <returns>Exact decoded Markdown.</returns>
    private static string DecodeOriginal(byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new InvalidDataException(
                "Cette EXI utilise un BOM UTF-8 : normaliser manuellement " +
                "le document avant un import CA.");
        }

        return new UTF8Encoding(false, true).GetString(bytes);
    }

    /// <summary>Rejects oversized Markdown with a post-read recheck.</summary>
    /// <param name="path">Canonical EXI or journal snapshot.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Exact bounded source bytes.</returns>
    private static async Task<byte[]> ReadBoundedAsync(
        string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxBytes)
        {
            throw new InvalidDataException("Fiche EXI trop volumineuse.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxBytes)
        {
            throw new InvalidDataException(
                "Fiche EXI modifiée pendant la lecture.");
        }

        return bytes;
    }

    /// <summary>Rejects junctions and symbolic links under AI journal root.</summary>
    /// <param name="path">Existing journal directory.</param>
    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Journal CA sous jonction ou absent.");
        }
    }

    /// <summary>Verifies one canonical SHA-256 hexadecimal fingerprint.</summary>
    /// <param name="hash">Textual fingerprint.</param>
    /// <returns>True only for 256-bit hexadecimal hash.</returns>
    private static bool IsHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Hash of exact source bytes, never a model-supplied SHA.</summary>
    /// <param name="bytes">Raw bytes.</param>
    /// <returns>Uppercase SHA-256 digest.</returns>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
