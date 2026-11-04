using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable proof of one reviewed, stable CA heading update. It is not an
/// acceptance-criterion verdict or permission to create new identifiers.
/// </summary>
public sealed record ProjectAiCriterionTitleJournal
{
    /// <summary>Versioned journal schema.</summary>
    public required string Schema { get; init; }
    /// <summary>Exact external response SHA, not its raw content.</summary>
    public required string RawResponseSha256 { get; init; }
    /// <summary>External proposal ID for idempotency only.</summary>
    public required Guid ProposalId { get; init; }
    /// <summary>Canonical project GUID.</summary>
    public required Guid ProjectId { get; init; }
    /// <summary>Containing requirement GUID.</summary>
    public required Guid RequirementId { get; init; }
    /// <summary>Canonical EXI reference.</summary>
    public required string Reference { get; init; }
    /// <summary>Canonical existing Markdown block GUID.</summary>
    public required Guid CriterionId { get; init; }
    /// <summary>Locally scoped CA-xx reference.</summary>
    public required string CriterionReference { get; init; }
    /// <summary>Verified workspace-relative source location.</summary>
    public required string RelativePath { get; init; }
    /// <summary>Reviewed original file SHA.</summary>
    public required string OriginalSha256 { get; init; }
    /// <summary>Approved full document SHA.</summary>
    public required string PublishedSha256 { get; init; }
    /// <summary>Reviewed local CA title text SHA.</summary>
    public required string OriginalTitleSha256 { get; init; }
    /// <summary>Approved CA title text SHA.</summary>
    public required string ApprovedTitleSha256 { get; init; }
    /// <summary>Human justification for uncertain/concurrent source.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Publishes exactly one explicitly approved CA heading in its containing
/// EXI file after a durable intent and a last-moment SHA-CAS check.
/// No state, test result, link, criterion GUID or other title is changed.
/// </summary>
public sealed class ProjectAiCriterionTitleImportTransactionService
{
    private const int MaxBytes = 1024 * 1024;
    private const string JournalRoot = ".nodalis-ai-import";
    private const string JournalName = "intent.json";
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-bound executor without side effects.</summary>
    /// <param name="workspaceRoot">Current workspace root.</param>
    public ProjectAiCriterionTitleImportTransactionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Applies one approved CA title or recognizes the same durable journal.
    /// Never accepts a different response or a concurrently changed EXI.
    /// </summary>
    /// <param name="projectDirectory">Active project directory.</param>
    /// <param name="plan">Human-selected plan tied to the source SHA.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>Atomic Applied or the exact AlreadyApplied transaction.</returns>
    public async Task<ProjectAiTitleCommitStatus> CommitAsync(
        string projectDirectory, ProjectAiPreparedReviewPlan plan,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CriterionTitles.Count != 1 || plan.Changes.Count != 0 ||
            plan.Creations.Count != 0 || plan.Descriptions.Count != 0 ||
            !IsHash(plan.RawResponseSha256))
        {
            throw new InvalidDataException(
                "Publication CA limitée à un titre approuvé, sans autre opération.");
        }

        ProjectAiPreparedCriterionTitleChange change =
            plan.CriterionTitles.Single();
        if (change.ProposalId == Guid.Empty || change.CriterionId == Guid.Empty ||
            change.RequirementId == Guid.Empty || change.ProjectId == Guid.Empty ||
            !ProjectRequirementMarkdownCodec.IsReference(change.Reference) ||
            !ProjectRequirementCriterionCodec.IsReference(
                change.CriterionReference) ||
            !IsHash(change.ExpectedSha256) ||
            change.CurrentTitle == change.ReviewedTitle ||
            string.IsNullOrWhiteSpace(change.ReviewedTitle) ||
            change.ReviewedTitle != change.ReviewedTitle.Trim() ||
            change.ReviewedTitle.Length > 240 ||
            change.ReviewedTitle.Any(char.IsControl) ||
            change.ReviewedTitle.StartsWith('#') ||
            change.ReviewedTitle.EndsWith('#') ||
            change.ReviewedTitle.Contains("<!--", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Plan CA malformé ou identité de bloc non conforme.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        await VerifyProjectAsync(project, change.ProjectId, token);
        string sourcePath = _sources.ResolveExistingFile(change.RelativePath);
        string canonical = Path.GetFullPath(Path.Combine(project,
            "Documentation", "Exigences", change.Reference + ".md"));
        if (!string.Equals(sourcePath, canonical,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Source CA non canonique ou hors du projet actif.");
        }

        string root = Path.Combine(project, JournalRoot);
        Directory.CreateDirectory(root);
        RequireDirectory(root);
        string lockPath = Path.Combine(root, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Verrou sous lien symbolique : import CA refusé.");
        }

        token.ThrowIfCancellationRequested();

        // Rebuild once under the shared lock, with all CA GUIDs, not only
        // the specific file. A recycled ID in a second EXI fails closed.
        ProjectRequirementCatalog catalog =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(project, token);
        ProjectRequirementEntry[] parents = catalog.Requirements.Where(entry =>
            entry.Card.Id == change.RequirementId).ToArray();
        ProjectRequirementCriterion[] criterionMatches = catalog.Requirements
            .SelectMany(entry => entry.Criteria)
            .Where(criterion => criterion.Id == change.CriterionId).ToArray();
        if (parents.Length != 1 || criterionMatches.Length != 1 ||
            !criterionMatches[0].LocallyIdentified ||
            criterionMatches[0].Reference != change.CriterionReference ||
            parents[0].Card.Reference != change.Reference ||
            parents[0].Card.ProjectId != change.ProjectId ||
            parents[0].RelativePath != change.RelativePath ||
            !parents[0].Criteria.Any(criterion =>
                criterion.Id == change.CriterionId &&
                criterion.LocallyIdentified) ||
            parents[0].CriterionDiagnostics.Any(warning =>
                warning.StartsWith("Section de critères répétée",
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "GUID CA dupliqué, section ambiguë ou EXI différent.");
        }

        string directory = Path.Combine(root,
            "criterion-" + change.ProposalId.ToString("N"));
        string journalPath = Path.Combine(directory, JournalName);
        ProjectAiCriterionTitleJournal journal;
        if (Directory.Exists(directory))
        {
            RequireDirectory(directory);
            journal = await ReadJournalAsync(journalPath, token);
            RequireJournal(journal, change, plan.RawResponseSha256);
        }
        else
        {
            if (parents[0].SourceSha256 != change.ExpectedSha256 ||
                criterionMatches[0].Title != change.CurrentTitle)
            {
                throw new InvalidDataException(
                    "Critère modifié depuis l'approbation : requalifier.");
            }

            byte[] originalBytes = await ReadBoundedAsync(sourcePath, token);
            string originalHash = Hash(originalBytes);
            if (originalHash != change.ExpectedSha256)
            {
                throw new InvalidDataException(
                    "Écriture externe concurrente après la comparaison CA.");
            }

            string original = new UTF8Encoding(false, true)
                .GetString(originalBytes);
            ProjectRequirementCard card =
                ProjectRequirementMarkdownCodec.Parse(original);
            if (card.Id != change.RequirementId ||
                card.ProjectId != change.ProjectId ||
                card.Reference != change.Reference)
            {
                throw new InvalidDataException("Métadonnées EXI obsolètes.");
            }

            string published = ProjectRequirementCriterionTitleCodec.RewriteTitle(
                original, card, change.CriterionId, change.CurrentTitle,
                change.ReviewedTitle);
            byte[] publishedBytes = new UTF8Encoding(false).GetBytes(published);
            journal = new ProjectAiCriterionTitleJournal
            {
                Schema = "nodalis.ai-criterion-title-commit.v1",
                RawResponseSha256 = plan.RawResponseSha256,
                ProposalId = change.ProposalId,
                ProjectId = change.ProjectId,
                RequirementId = change.RequirementId,
                Reference = change.Reference,
                CriterionId = change.CriterionId,
                CriterionReference = change.CriterionReference,
                RelativePath = change.RelativePath,
                OriginalSha256 = originalHash,
                PublishedSha256 = Hash(publishedBytes),
                OriginalTitleSha256 = Hash(Encoding.UTF8.GetBytes(
                    change.CurrentTitle)),
                ApprovedTitleSha256 = Hash(Encoding.UTF8.GetBytes(
                    change.ReviewedTitle)),
                Reason = change.Reason
            };
            Directory.CreateDirectory(directory);
            RequireDirectory(directory);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, "original.md"), original, token);
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, "published.md"), published, token);
            token.ThrowIfCancellationRequested();
            // Only a complete, durable intent makes publication recoverable.
            await AtomicFileWriter.WriteNewAllTextAsync(journalPath,
                JsonSerializer.Serialize(journal,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                token);
        }

        string originalFile = Path.Combine(directory, "original.md");
        string approvedFile = Path.Combine(directory, "published.md");
        byte[] originalSnapshot = await ReadSnapshotAsync(originalFile,
            journal.OriginalSha256, token);
        await ReadSnapshotAsync(approvedFile, journal.PublishedSha256, token);

        // Reconstruct from the reviewed original on EVERY replay. Matching
        // forged JSON and published.md hashes alone do not prove consent.
        string originalText = new UTF8Encoding(false, true)
            .GetString(originalSnapshot);
        ProjectRequirementCard originalCard =
            ProjectRequirementMarkdownCodec.Parse(originalText);
        if (originalCard.Id != change.RequirementId ||
            originalCard.ProjectId != change.ProjectId ||
            originalCard.Reference != change.Reference)
        {
            throw new InvalidDataException("Source originale CA remplacée.");
        }

        string authorized = ProjectRequirementCriterionTitleCodec.RewriteTitle(
            originalText, originalCard, change.CriterionId,
            change.CurrentTitle, change.ReviewedTitle);
        if (Hash(new UTF8Encoding(false).GetBytes(authorized)) !=
            journal.PublishedSha256)
        {
            throw new InvalidDataException(
                "Instantané CA non égal à l'unique titre autorisé.");
        }

        string liveSha = Hash(await ReadBoundedAsync(sourcePath, token));
        if (liveSha == journal.PublishedSha256)
        {
            return ProjectAiTitleCommitStatus.AlreadyApplied;
        }

        if (liveSha != journal.OriginalSha256)
        {
            throw new InvalidDataException(
                "EXI diverge du journal CA : reprise automatique interdite.");
        }

        await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
            sourcePath, journal.OriginalSha256, approvedFile,
            journal.PublishedSha256, token);
        if (Hash(await ReadBoundedAsync(sourcePath,
            CancellationToken.None)) != journal.PublishedSha256)
        {
            throw new InvalidDataException(
                "Publication CA à inspecter : hash final incohérent.");
        }

        return ProjectAiTitleCommitStatus.Applied;
    }

    /// <summary>Checks a saved journal against the exact reapproved plan.</summary>
    /// <param name="journal">Read-only original intent.</param>
    /// <param name="change">Approved CA update.</param>
    /// <param name="responseSha">External response SHA.</param>
    private static void RequireJournal(ProjectAiCriterionTitleJournal journal,
        ProjectAiPreparedCriterionTitleChange change, string responseSha)
    {
        if (journal.Schema != "nodalis.ai-criterion-title-commit.v1" ||
            journal.RawResponseSha256 != responseSha ||
            journal.ProposalId != change.ProposalId ||
            journal.ProjectId != change.ProjectId ||
            journal.RequirementId != change.RequirementId ||
            journal.Reference != change.Reference ||
            journal.CriterionId != change.CriterionId ||
            journal.CriterionReference != change.CriterionReference ||
            journal.RelativePath != change.RelativePath ||
            journal.OriginalSha256 != change.ExpectedSha256 ||
            journal.OriginalTitleSha256 != Hash(Encoding.UTF8.GetBytes(
                change.CurrentTitle)) ||
            journal.ApprovedTitleSha256 != Hash(Encoding.UTF8.GetBytes(
                change.ReviewedTitle)) ||
            journal.Reason != change.Reason || !IsHash(journal.PublishedSha256))
        {
            throw new InvalidDataException(
                "Le journal CA décrit une décision différente ou non fiable.");
        }
    }

    /// <summary>Reads one immutable, bounded, non-symlinked JSON intent.</summary>
    /// <param name="path">Canonical transaction manifest.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Required journal data or an explicit refusal.</returns>
    private static async Task<ProjectAiCriterionTitleJournal> ReadJournalAsync(
        string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 16000)
        {
            throw new InvalidDataException("Journal CA absent ou sous lien symbolique.");
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectAiCriterionTitleJournal>(
                await File.ReadAllTextAsync(path, token),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                throw new InvalidDataException("Journal CA vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Journal CA non analysable.", exception);
        }
    }

    /// <summary>Reads an exact snapshot, refusing symbolic links and tampering.</summary>
    /// <param name="path">Snapshot source.</param>
    /// <param name="expectedSha">Recorded SHA-256.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified source bytes.</returns>
    private static async Task<byte[]> ReadSnapshotAsync(
        string path, string expectedSha, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Instantané CA absent ou redirigé.");
        }

        byte[] content = await ReadBoundedAsync(path, token);
        if (Hash(content) != expectedSha)
        {
            throw new InvalidDataException("Instantané CA corrompu.");
        }

        return content;
    }

    /// <summary>Checks the project's manifest and archive state.</summary>
    /// <param name="project">Selected project directory.</param>
    /// <param name="projectId">Approved canonical project GUID.</param>
    /// <param name="token">Cancellation.</param>
    private async Task VerifyProjectAsync(string project, Guid projectId,
        CancellationToken token)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 4 ||
            !string.Equals(parts[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !parts.Skip(2).Any(part => string.Equals(part,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Import CA en dehors d'un projet actif interdit.");
        }

        string manifestPath = Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(manifestPath));
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, token);
        if (manifest.Id != projectId || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet CA archivé, déplacé ou identité divergente.");
        }
    }

    /// <summary>Reads a bounded source file, with a second post-read bound.</summary>
    /// <param name="path">Canonical source file.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Bytes before any rewrite.</returns>
    private static async Task<byte[]> ReadBoundedAsync(string path,
        CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxBytes)
        {
            throw new InvalidDataException("Document CA trop volumineux.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxBytes)
        {
            throw new InvalidDataException(
                "Fichier CA modifié pendant la lecture.");
        }

        return bytes;
    }

    /// <summary>Rejects a missing directory or symlink/junction.</summary>
    /// <param name="directory">A private journal directory.</param>
    private static void RequireDirectory(string directory)
    {
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Dossier du journal CA redirigé.");
        }
    }

    /// <summary>Exact SHA-256 hash validation.</summary>
    /// <param name="hash">Hexadecimal digest.</param>
    /// <returns>True for one 256-bit hexadecimal hash.</returns>
    private static bool IsHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Hashes exact bytes, without interpreting Markdown.</summary>
    /// <param name="bytes">Raw bytes.</param>
    /// <returns>SHA-256 uppercase hexadecimal.</returns>
    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
