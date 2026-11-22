using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Explicit immutable, per-GUID SFD base source used for real three-way
/// comparison. Stored locally only, never sent to any external AI.
/// </summary>
public sealed record SfdDocumentBaseline
{
    public const string CurrentSchema = "nodalis.sfd.base.v1";

    public required string Schema { get; init; }

    public required Guid ProjectId { get; init; }

    public required Guid TargetId { get; init; }

    public required DateTimeOffset CapturedUtc { get; init; }

    public required string BaseSha256 { get; init; }

    public required string Markdown { get; init; }
}

/// <summary>
/// One-time explicit capture of an existing canonical Markdown source.
/// Never overwrites a captured base, never changes a business file, and
/// verifies source SHA from the UI review before writing the baseline.
/// </summary>
public sealed class SfdDocumentBaselineService
{
    private readonly SfdDocumentRegistryService _documents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Binds base capture to one workspace.</summary>
    /// <param name="workspaceRoot">Authorized workspace path.</param>
    public SfdDocumentBaselineService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>Reads an existing explicit base without creating any files.</summary>
    /// <param name="projectDirectory">Selected project root.</param>
    /// <param name="targetId">Canonical SFD document GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified prior base, or null before the first user capture.</returns>
    public async Task<SfdDocumentBaseline?> ReadAsync(
        string projectDirectory, Guid targetId,
        CancellationToken cancellationToken = default)
    {
        if (targetId == Guid.Empty)
        {
            throw new InvalidDataException("Identifiant de base SFD vide.");
        }

        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        if (catalog.Diagnostics.Count > 0 ||
            !catalog.Entries.Any(item => item.Card.Id == targetId))
        {
            throw new InvalidDataException(
                "Document SFD non canonique : lecture de base refusée.");
        }

        string path = BaselinePath(projectDirectory, targetId);
        if (!File.Exists(path))
        {
            return null;
        }

        _sources.GetReferenceForExistingFile(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("Base SFD trop volumineuse.");
        }

        SfdDocumentBaseline baseline;
        try
        {
            baseline = await AtomicJsonFile.ReadAsync<SfdDocumentBaseline>(
                path, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Base SFD illisible : aucune comparaison autorisée.", exception);
        }

        Verify(baseline, catalog.ProjectId, targetId);
        return baseline;
    }

    /// <summary>
    /// Captures current source ONLY after user approval, and only if the
    /// exact SHA is still the one displayed in the import review.
    /// </summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="targetId">Selected existing canonical GUID.</param>
    /// <param name="reviewedSha256">Exact source SHA from the user's review.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New or matching immutable base.</returns>
    public async Task<SfdDocumentBaseline> CaptureAsync(
        string projectDirectory, Guid targetId, string reviewedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedSha256);
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        string manifestPath = Path.Combine(root, WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != catalog.ProjectId ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.KindSchemaVersion != 1 ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null ||
            catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Capture de base impossible sur un projet non SFD ou archivé.");
        }

        SfdDocumentEntry entry = catalog.Entries.SingleOrDefault(item =>
            item.Card.Id == targetId) ??
            throw new InvalidDataException("Document cible hors du projet.");
        _sources.GetReferenceForExistingFile(entry.AbsolutePath);

        string journalRoot = Path.Combine(root, ".sfd-ai-import");
        Directory.CreateDirectory(journalRoot);
        EnsureNormalDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".publication.lock");
        if (File.Exists(lockPath) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou SFD non sûr.");
        }

        await using FileStream lease = new(lockPath,
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            4096, FileOptions.Asynchronous);
        string baselineDirectory = Path.Combine(journalRoot, "baselines");
        Directory.CreateDirectory(baselineDirectory);
        EnsureNormalDirectory(baselineDirectory);
        string path = BaselinePath(root, targetId);
        if (File.Exists(path))
        {
            SfdDocumentBaseline prior =
                await ReadAsync(root, targetId, cancellationToken) ??
                throw new InvalidDataException("Base SFD inaccessible.");
            if (!string.Equals(prior.BaseSha256, reviewedSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Base déjà figée avec une autre révision. " +
                    "Aucune remise à zéro implicite n'est autorisée.");
            }

            return prior;
        }

        SfdTextRevision current = await SfdMarkdownRevisionWriter.ReadAsync(
            entry.AbsolutePath, cancellationToken);
        if (!string.Equals(current.Sha256, reviewedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le document a changé depuis la revue : nouvelle comparaison requise.");
        }

        SfdDocumentCard identity = SfdDocumentMarkdownCodec.Parse(
            current.Content);
        if (identity.Id != targetId || identity.ProjectId != manifest.Id ||
            identity.Reference != entry.Card.Reference ||
            identity.Kind != entry.Card.Kind)
        {
            throw new InvalidDataException(
                "Le GUID de la fiche SFD a changé pendant la capture.");
        }

        SfdDocumentBaseline baseline = new()
        {
            Schema = SfdDocumentBaseline.CurrentSchema,
            ProjectId = manifest.Id,
            TargetId = targetId,
            CapturedUtc = DateTimeOffset.UtcNow,
            BaseSha256 = current.Sha256,
            Markdown = current.Content
        };
        // First capture wins; no automatic reset, no business document write.
        await AtomicFileWriter.WriteNewAllTextAsync(path,
            JsonSerializer.Serialize(baseline, JsonDefaults.Options),
            cancellationToken);
        return baseline;
    }

    /// <summary>Rejects a corrupted or cross-project baseline.</summary>
    /// <param name="baseline">Stored local Markdown snapshot.</param>
    /// <param name="projectId">Expected project owner.</param>
    /// <param name="targetId">Expected existing document.</param>
    private static void Verify(SfdDocumentBaseline baseline,
        Guid projectId, Guid targetId)
    {
        if (baseline.Schema != SfdDocumentBaseline.CurrentSchema ||
            baseline.ProjectId != projectId ||
            baseline.TargetId != targetId ||
            baseline.CapturedUtc == default ||
            baseline.Markdown is null ||
            baseline.Markdown.Length > 1024 * 1024 ||
            baseline.BaseSha256 is null ||
            !string.Equals(Hash(baseline.Markdown), baseline.BaseSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Base SFD modifiée ou hors contrat.");
        }

        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
            baseline.Markdown);
        if (card.Id != targetId || card.ProjectId != projectId)
        {
            throw new InvalidDataException(
                "Base SFD appartenant à un autre objet ou projet.");
        }
    }

    /// <summary>Gets a hard-coded, GUID-only local snapshot path.</summary>
    /// <param name="root">Verified SFD project.</param>
    /// <param name="targetId">Canonical document ID.</param>
    /// <returns>Private local source snapshot path.</returns>
    private static string BaselinePath(string root, Guid targetId) =>
        Path.Combine(Path.GetFullPath(root), ".sfd-ai-import", "baselines",
            targetId.ToString("D") + ".json");

    /// <summary>Rejects reparse points anywhere above a local baseline folder.</summary>
    /// <param name="directory">Existing, project-contained directory.</param>
    private static void EnsureNormalDirectory(string directory)
    {
        string? current = Path.GetFullPath(directory);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Lien symbolique ou jonction interdit pour la base SFD.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>Computes a strict UTF-8 SHA-256 for local source snapshots.</summary>
    /// <param name="value">Markdown content.</param>
    /// <returns>Uppercase hex fingerprint.</returns>
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Three-way SFD field diff with no persistent side effects.</summary>
public static class SfdDocumentThreeWayService
{
    /// <summary>Compares real baseline, current document and one AI proposal.</summary>
    /// <param name="targetId">Canonical target identity.</param>
    /// <param name="current">Current exact UTF-8 source.</param>
    /// <param name="currentSha">Current exact byte revision.</param>
    /// <param name="baseline">Prior explicit base, or null if not captured.</param>
    /// <param name="proposal">Validated untrusted external candidate.</param>
    /// <returns>Read-only title and managed-description diff.</returns>
    public static SfdThreeWayPreview Compare(Guid targetId,
        string current, string currentSha, SfdDocumentBaseline? baseline,
        SfdImportProposal proposal)
    {
        if (baseline is null)
        {
            return new SfdThreeWayPreview
            {
                TargetId = targetId,
                CurrentSha256 = currentSha,
                Fields = []
            };
        }

        SfdDocumentCard beforeCard = SfdDocumentMarkdownCodec.Parse(
            baseline.Markdown);
        SfdDocumentCard currentCard = SfdDocumentMarkdownCodec.Parse(current);
        if (baseline.TargetId != targetId ||
            beforeCard.Id != targetId || currentCard.Id != targetId ||
            beforeCard.ProjectId != currentCard.ProjectId ||
            beforeCard.Kind != currentCard.Kind)
        {
            throw new InvalidDataException(
                "La base ne correspond plus à l'identité du document SFD.");
        }

        return new SfdThreeWayPreview
        {
            TargetId = targetId,
            CurrentSha256 = currentSha,
            BaselineSha256 = baseline.BaseSha256,
            Fields =
            [
                SfdDocumentThreeWayCodec.Compare("title",
                    beforeCard.Title, currentCard.Title, proposal.Title),
                SfdDocumentThreeWayCodec.Compare("description",
                    SfdDocumentThreeWayCodec.Description(baseline.Markdown),
                    SfdDocumentThreeWayCodec.Description(current),
                    proposal.Description)
            ]
        };
    }
}
