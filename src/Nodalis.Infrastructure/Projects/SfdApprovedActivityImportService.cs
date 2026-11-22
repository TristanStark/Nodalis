using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Frozen approved ACT with the exact PM parent identity and SHA.</summary>
public sealed record SfdApprovedActivityIntent
{
    public const string CurrentSchema = "nodalis.sfd.approved-activity.v1";

    public required string Schema { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ResponseSha256 { get; init; }

    public required string RunId { get; init; }

    public required string ClientKey { get; init; }

    public required Guid BusinessId { get; init; }

    public required string Reference { get; init; }

    public required string Title { get; init; }

    public required Guid ParentProcessId { get; init; }

    public required string ParentSourceSha256 { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required string ApprovedMarkdown { get; init; }

    public required string ApprovedSha256 { get; init; }
}

/// <summary>Result of an explicitly approved single ACT creation or replay.</summary>
/// <param name="Id">Nodalis-owned stable identity.</param>
/// <param name="Reference">Visible ACT reference.</param>
/// <param name="AbsolutePath">Canonical activity Markdown.</param>
/// <param name="ParentProcessId">Verified existing PM identity.</param>
/// <param name="AlreadyApplied">True for a byte-identical replay.</param>
public sealed record SfdApprovedActivityResult(
    Guid Id, string Reference, string AbsolutePath,
    Guid ParentProcessId, bool AlreadyApplied);

/// <summary>
/// Human-approved, journaled publication of exactly ONE new ACT under a
/// previously published or manually created PM. No other SFD objects or
/// related fake C/SC/RM are created. No DOCX, network or commands.
/// </summary>
public sealed class SfdApprovedActivityImportService
{
    private const string JournalRootName = ".sfd-ai-import";
    private const string IntentFileName = "intent-activity.json";
    private const string StagingFolder = "staged-activity";
    private readonly SfdImportReviewService _review;
    private readonly SfdDocumentRegistryService _documents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-bound ACT publication service.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    public SfdApprovedActivityImportService(string workspaceRoot)
    {
        _review = new SfdImportReviewService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Commits a single reviewed ACT create. A same-run parentKey must already
    /// have been published as a PM. A persisted journal allows explicit retries.
    /// </summary>
    /// <param name="projectDirectory">Active selected SFD project.</param>
    /// <param name="rawJson">Identical response shown during comparison.</param>
    /// <param name="reviewed">Unapproved review snapshot, separately confirmed by caller.</param>
    /// <param name="clientKey">One explicitly selected new ACT key.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Created or already-applied canonical ACT identity.</returns>
    public async Task<SfdApprovedActivityResult> PublishAsync(
        string projectDirectory, string rawJson, SfdImportReviewSnapshot reviewed,
        string clientKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        SfdImportEnvelope response = SfdImportContract.Parse(rawJson);
        SfdImportProposal proposal = response.Proposals.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("Activité absente de la réponse IA.");
        if (response.Step != "sfd-01" ||
            proposal.Kind != "businessActivity" || proposal.Operation != "create" ||
            proposal.TargetId is not null || proposal.ParentKey is null ||
            proposal.Conditions.Count > 0 || proposal.ActivityKeys.Count > 0 ||
            proposal.RequirementKeys.Count > 0)
        {
            throw new InvalidDataException(
                "La publication ACT ne concerne qu'une activité nouvelle avec PM parent.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        ProjectManifest manifest = await ReadActiveProjectAsync(
            root, cancellationToken);
        SfdImportReviewItem row =
            reviewed.Items.SingleOrDefault(item => item.ClientKey == clientKey) ??
            throw new InvalidDataException("ACT non présente dans le plan examiné.");
        if (reviewed.ProjectId != manifest.Id ||
            reviewed.ResponseSha256 != Hash(rawJson) ||
            reviewed.RunId != response.RunId || reviewed.Step != response.Step ||
            row.Kind != "businessActivity" || row.Operation != "create" ||
            row.Status != SfdImportReviewStatus.NewCandidate)
        {
            throw new InvalidDataException(
                "L'ACT ne correspond plus à la proposition examinée.");
        }

        string journalRoot = Path.Combine(root, JournalRootName);
        Directory.CreateDirectory(journalRoot);
        RequireNormalDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".publication.lock");
        if (File.Exists(lockPath) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou de publication SFD non sûr.");
        }

        await using FileStream lease = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        string key = Hash(manifest.Id.ToString("D") + "\n" +
            reviewed.ResponseSha256 + "\n" + clientKey);
        string journal = Path.Combine(journalRoot, key);
        Directory.CreateDirectory(journal);
        RequireNormalDirectory(journal);
        string intentPath = Path.Combine(journal, IntentFileName);
        SfdApprovedActivityIntent intent;
        if (File.Exists(intentPath))
        {
            _sources.GetReferenceForExistingFile(intentPath);
            try
            {
                intent = await AtomicJsonFile.ReadAsync<SfdApprovedActivityIntent>(
                    intentPath, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Journal ACT illisible : publication suspendue.", exception);
            }

            ValidateIntent(intent, manifest.Id, reviewed.ResponseSha256,
                response.RunId, clientKey);
        }
        else
        {
            SfdImportReviewSnapshot latest = await _review.ReviewAsync(
                root, rawJson, cancellationToken);
            SfdImportReviewItem? currentRow = latest.Items.SingleOrDefault(item =>
                item.ClientKey == clientKey);
            if (latest.PlanSha256 != reviewed.PlanSha256 ||
                currentRow?.Status != SfdImportReviewStatus.NewCandidate ||
                currentRow.ResolvedParentId is not Guid parentId ||
                currentRow.ResolvedParentSha256 is not string parentSha ||
                row.ResolvedParentId != parentId ||
                row.ResolvedParentSha256 != parentSha)
            {
                throw new InvalidDataException(
                    "PM parent non publié, modifié ou revue périmée : relancez la comparaison.");
            }

            SfdDocumentCatalog catalog = await _documents.ListAsync(
                root, cancellationToken);
            if (catalog.Diagnostics.Count > 0)
            {
                throw new InvalidDataException("Fiches SFD ambiguës.");
            }

            SfdDocumentEntry parentProcess = catalog.Entries.SingleOrDefault(
                item => item.Card.Id == parentId &&
                    item.Card.Kind == SfdDocumentKind.Process) ??
                throw new InvalidDataException("Processus parent absent.");
            int highest = catalog.Entries
                .Where(item => item.Card.Kind == SfdDocumentKind.Activity &&
                    item.Card.ParentId == parentId)
                .Select(item => SfdDocumentMarkdownCodec.ActivitySequence(
                    item.Card.Reference))
                .DefaultIfEmpty(0).Max();
            if (highest >= 9999)
            {
                throw new InvalidDataException("Numérotation ACT épuisée pour ce PM.");
            }

            Guid id = Guid.NewGuid();
            string reference = SfdDocumentMarkdownCodec.ActivityReference(
                parentProcess.Card.Reference, highest + 1);
            DateTimeOffset created = DateTimeOffset.UtcNow;
            SfdDocumentCard card = new()
            {
                Id = id,
                ProjectId = manifest.Id,
                Kind = SfdDocumentKind.Activity,
                Reference = reference,
                Title = proposal.Title,
                ParentId = parentId,
                CreatedUtc = created,
                Certainty = SfdAssertionCertainty.ToConfirm
            };
            string markdown = SfdDocumentMarkdownCodec.Format(card);
            if (!string.IsNullOrWhiteSpace(proposal.Description))
            {
                markdown += "\n## Description proposée par IA — à qualifier\n\n" +
                    proposal.Description.Replace("<", "&lt;", StringComparison.Ordinal)
                        .Replace(">", "&gt;", StringComparison.Ordinal).Trim() + "\n";
            }

            intent = new SfdApprovedActivityIntent
            {
                Schema = SfdApprovedActivityIntent.CurrentSchema,
                ProjectId = manifest.Id,
                ResponseSha256 = reviewed.ResponseSha256,
                RunId = response.RunId,
                ClientKey = clientKey,
                BusinessId = id,
                Reference = reference,
                Title = proposal.Title,
                ParentProcessId = parentId,
                ParentSourceSha256 = parentSha,
                CreatedUtc = created,
                ApprovedMarkdown = markdown,
                ApprovedSha256 = Hash(markdown)
            };
            await AtomicFileWriter.WriteNewAllTextAsync(intentPath,
                JsonSerializer.Serialize(intent, JsonDefaults.Options),
                cancellationToken);
        }

        return await ApplyIntentAsync(root, journal, intent, cancellationToken);
    }

    /// <summary>Completes a durable ACT create under its real current PM path.</summary>
    /// <param name="root">Verified SFD project root.</param>
    /// <param name="journal">Durable staging folder.</param>
    /// <param name="intent">Previously validated immutable approval.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Committed or recognized same-identity ACT.</returns>
    private async Task<SfdApprovedActivityResult> ApplyIntentAsync(
        string root, string journal, SfdApprovedActivityIntent intent,
        CancellationToken cancellationToken)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Catalogue SFD incohérent : reprise ACT refusée.");
        }

        SfdDocumentEntry pm = catalog.Entries.SingleOrDefault(entry =>
            entry.Card.Id == intent.ParentProcessId &&
            entry.Card.Kind == SfdDocumentKind.Process) ??
            throw new InvalidDataException("PM parent supprimé ou déplacé hors projet.");
        _sources.GetReferenceForExistingFile(pm.AbsolutePath);
        SfdTextRevision parent = await SfdMarkdownRevisionWriter.ReadAsync(
            pm.AbsolutePath, cancellationToken);
        SfdDocumentCard parentCard = SfdDocumentMarkdownCodec.Parse(parent.Content);
        if (parentCard.Id != intent.ParentProcessId ||
            parentCard.ProjectId != intent.ProjectId ||
            parent.Sha256 != intent.ParentSourceSha256)
        {
            throw new InvalidDataException(
                "PM parent modifié depuis la revue : nouvelle approbation requise.");
        }

        if (SfdDocumentMarkdownCodec.IsCompactActivityReference(intent.Reference))
        {
            return await ApplyFlatActivityAsync(root, journal, pm, intent,
                cancellationToken);
        }

        string activitiesDirectory = Path.Combine(
            Path.GetDirectoryName(pm.AbsolutePath)!, "ACTIVITES");
        RequireNormalDirectory(activitiesDirectory);
        string targetDirectory = Path.Combine(activitiesDirectory,
            intent.Reference + " - " + SafeTitle(intent.Title));
        string target = Path.Combine(targetDirectory, "presentation activité.md");
        if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
        {
            if (!File.Exists(target))
            {
                throw new InvalidDataException(
                    "Collision de dossier ACT : reprise refusée.");
            }

            _sources.GetReferenceForExistingFile(target);
            SfdTextRevision actual = await SfdMarkdownRevisionWriter.ReadAsync(
                target, cancellationToken);
            SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(actual.Content);
            if (card.Id != intent.BusinessId ||
                card.ParentId != intent.ParentProcessId ||
                card.ProjectId != intent.ProjectId ||
                actual.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "L'ACT a été modifiée ou son identité est en conflit.");
            }

            return new SfdApprovedActivityResult(intent.BusinessId,
                intent.Reference, target, intent.ParentProcessId, true);
        }

        if (catalog.Entries.Any(entry =>
            entry.Card.Id == intent.BusinessId ||
            entry.Card.Reference == intent.Reference))
        {
            throw new InvalidDataException(
                "Identité ou référence ACT déjà présente à un autre emplacement.");
        }

        string stage = Path.Combine(journal, StagingFolder);
        Directory.CreateDirectory(stage);
        RequireNormalDirectory(stage);
        string stagedFile = Path.Combine(stage, "presentation activité.md");
        if (File.Exists(stagedFile))
        {
            _sources.GetReferenceForExistingFile(stagedFile);
            SfdTextRevision stored = await SfdMarkdownRevisionWriter.ReadAsync(
                stagedFile, cancellationToken);
            if (stored.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "ACT staged modifiée depuis l'intention approuvée.");
            }
        }
        else
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                stagedFile, intent.ApprovedMarkdown, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Check parent again immediately before the single atomic directory move.
        SfdTextRevision finalParent = await SfdMarkdownRevisionWriter.ReadAsync(
            pm.AbsolutePath, cancellationToken);
        if (finalParent.Sha256 != intent.ParentSourceSha256)
        {
            throw new InvalidDataException(
                "Le PM parent a changé avant la publication ACT.");
        }

        RequireNormalDirectory(activitiesDirectory);
        Directory.Move(stage, targetDirectory);
        return new SfdApprovedActivityResult(intent.BusinessId,
            intent.Reference, target, intent.ParentProcessId, false);
    }

    /// <summary>
    /// Recovers or atomically publishes one new flat ACT Markdown file; historical
    /// directory-based approved intents retain the original replay path.
    /// </summary>
    /// <param name="root">Validated project root.</param>
    /// <param name="journal">Current approved, durable import journal.</param>
    /// <param name="pm">Verified process document.</param>
    /// <param name="intent">Approved immutable ACT contents.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Exactly one current or recovered flat ACT file.</returns>
    private async Task<SfdApprovedActivityResult> ApplyFlatActivityAsync(
        string root, string journal, SfdDocumentEntry pm,
        SfdApprovedActivityIntent intent, CancellationToken cancellationToken)
    {
        string activities = Path.Combine(
            Path.GetDirectoryName(pm.AbsolutePath)!, "ACTIVITES");
        RequireNormalDirectory(activities);
        string target = Path.Combine(activities, intent.Reference + ".md");
        if (File.Exists(target))
        {
            _sources.GetReferenceForExistingFile(target);
            SfdTextRevision actual = await SfdMarkdownRevisionWriter.ReadAsync(
                target, cancellationToken);
            SfdDocumentCard existing = SfdDocumentMarkdownCodec.Parse(actual.Content);
            if (existing.Id != intent.BusinessId ||
                existing.ParentId != intent.ParentProcessId ||
                existing.ProjectId != intent.ProjectId ||
                actual.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "Le fichier ACT cible existe déjà avec un autre contenu.");
            }

            return new SfdApprovedActivityResult(intent.BusinessId,
                intent.Reference, target, intent.ParentProcessId, true);
        }

        SfdDocumentCatalog catalog = await _documents.ListAsync(root,
            cancellationToken);
        if (catalog.Entries.Any(item => item.Card.Id == intent.BusinessId ||
            item.Card.Reference == intent.Reference))
        {
            throw new InvalidDataException(
                "Identité ACT présente ailleurs : reprise refusée.");
        }

        string stagedFile = Path.Combine(journal, "staged-activity.md");
        if (File.Exists(stagedFile))
        {
            _sources.GetReferenceForExistingFile(stagedFile);
            SfdTextRevision staged = await SfdMarkdownRevisionWriter.ReadAsync(
                stagedFile, cancellationToken);
            if (staged.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException("Brouillon ACT journalisé modifié.");
            }
        }
        else
        {
            await AtomicFileWriter.WriteNewAllTextAsync(stagedFile,
                intent.ApprovedMarkdown, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SfdTextRevision currentParent = await SfdMarkdownRevisionWriter.ReadAsync(
            pm.AbsolutePath, cancellationToken);
        if (currentParent.Sha256 != intent.ParentSourceSha256)
        {
            throw new InvalidDataException("Le PM a changé avant publication.");
        }

        RequireNormalDirectory(activities);
        File.Move(stagedFile, target);
        return new SfdApprovedActivityResult(intent.BusinessId,
            intent.Reference, target, intent.ParentProcessId, false);
    }

    /// <summary>Restricts publication to the current unarchived SFD project.</summary>
    /// <param name="root">Selected project directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified manifest.</returns>
    private async Task<ProjectManifest> ReadActiveProjectAsync(string root,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(path);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, cancellationToken);
        if (manifest.Id == Guid.Empty ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.KindSchemaVersion != 1 ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "ACT non publiable : projet SFD absent, incompatible ou archivé.");
        }

        return manifest;
    }

    /// <summary>Validates durable ACT identity and exact approved Markdown bytes.</summary>
    /// <param name="intent">Untrusted persisted intent.</param>
    /// <param name="projectId">Canonical owner.</param>
    /// <param name="hash">Approved raw response SHA.</param>
    /// <param name="runId">Approved model run.</param>
    /// <param name="key">Approved ACT clientKey.</param>
    private static void ValidateIntent(SfdApprovedActivityIntent intent,
        Guid projectId, string hash, string runId, string key)
    {
        if (intent.Schema != SfdApprovedActivityIntent.CurrentSchema ||
            intent.ProjectId != projectId ||
            intent.ResponseSha256 != hash ||
            intent.RunId != runId || intent.ClientKey != key ||
            intent.BusinessId == Guid.Empty ||
            intent.ParentProcessId == Guid.Empty ||
            intent.Reference is null ||
            !System.Text.RegularExpressions.Regex.IsMatch(intent.Reference,
                @"^(ACT-[0-9]{4}|PM-[0-9]{3,4}-ACT-[0-9]{3,4}|PM[0-9]{3,4}-ACT[0-9]{3,4})$") ||
            string.IsNullOrWhiteSpace(intent.Title) || intent.Title.Length > 240 ||
            intent.ParentSourceSha256 is null ||
            intent.ParentSourceSha256.Length != 64 ||
            intent.ApprovedMarkdown is null ||
            intent.ApprovedSha256 is null ||
            Hash(intent.ApprovedMarkdown) != intent.ApprovedSha256)
        {
            throw new InvalidDataException("Intention ACT altérée ou hors contrat.");
        }

        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
            intent.ApprovedMarkdown);
        if (card.Id != intent.BusinessId ||
            card.ProjectId != projectId ||
            card.ParentId != intent.ParentProcessId ||
            card.Kind != SfdDocumentKind.Activity ||
            card.Reference != intent.Reference ||
            card.Title != intent.Title)
        {
            throw new InvalidDataException(
                "Identité ACT et snapshot approuvé en désaccord.");
        }
    }

    /// <summary>Rejects junctions and symlinks in every existing path ancestor.</summary>
    /// <param name="directory">Canonical intended directory.</param>
    private static void RequireNormalDirectory(string directory)
    {
        string? current = Path.GetFullPath(directory);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Jonction ou lien symbolique interdit dans le parcours ACT.");
            }

            current = Path.GetDirectoryName(current);
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "Dossier ACT canonique absent : " + directory);
        }
    }

    /// <summary>Derives the same Windows-safe title suffix as normal SFD authoring.</summary>
    /// <param name="title">User-approved ACT title.</param>
    /// <returns>Sanitized path segment.</returns>
    private static string SafeTitle(string title)
    {
        string safe = new string(title.Trim().Take(70)
            .Select(character => char.IsLetterOrDigit(character) ||
                character is ' ' or '-' or '_' ? character : '_').ToArray())
            .Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? "Sans titre" : safe;
    }

    /// <summary>Computes exact uppercase UTF-8 SHA-256.</summary>
    /// <param name="content">Response or approved Markdown.</param>
    /// <returns>Stable hexadecimal digest.</returns>
    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
