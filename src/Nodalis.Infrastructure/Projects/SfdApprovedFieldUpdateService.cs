using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable, individually selected SFD update of ONLY title and AI-owned
/// description. Original exact bytes and derived target bytes are frozen before
/// touching the business file so a replay is deterministic and auditable.
/// </summary>
public sealed record SfdApprovedFieldUpdateIntent
{
    public const string CurrentSchema = "nodalis.sfd.fields-update.v1";

    public required string Schema { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid TargetId { get; init; }
    public required string RunId { get; init; }
    public required string ClientKey { get; init; }
    public required string ResponseSha256 { get; init; }
    public required string BaselineSha256 { get; init; }
    public required string BeforeSha256 { get; init; }
    public required string BeforeMarkdown { get; init; }
    public required string AfterSha256 { get; init; }
    public required string AfterMarkdown { get; init; }
    public required bool AcceptTitle { get; init; }
    public required bool AcceptDescription { get; init; }
}

/// <summary>Exactly one explicitly authorized field update or identical replay.</summary>
/// <param name="TargetId">Unchanged canonical SFD GUID.</param>
/// <param name="AbsolutePath">Current canonical business document.</param>
/// <param name="AlreadyApplied">True if these same approved bytes already exist.</param>
public sealed record SfdApprovedFieldUpdateResult(
    Guid TargetId, string AbsolutePath, bool AlreadyApplied);

/// <summary>
/// Minimal SFD update transaction: explicit immutable base, real three-way
/// field decisions, SHA guarded source, durable intent and no folder renaming.
/// Only PM and ACT title/AI-draft description are eligible in this slice.
/// </summary>
public sealed class SfdApprovedFieldUpdateService
{
    private readonly SfdImportReviewService _review;
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdDocumentBaselineService _baselines;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Binds updates to one authorized workspace.</summary>
    /// <param name="workspaceRoot">Workspace boundary.</param>
    public SfdApprovedFieldUpdateService(string workspaceRoot)
    {
        _review = new SfdImportReviewService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _baselines = new SfdDocumentBaselineService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Publishes only individually selected safe fields after user approval.
    /// Unsupported structure, relationship or certainty changes are rejected.
    /// </summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="rawJson">Exact previously reviewed AI response.</param>
    /// <param name="reviewed">Previous read-only, baseline-aware comparison.</param>
    /// <param name="clientKey">One selected existing PM or ACT.</param>
    /// <param name="acceptTitle">Explicit user title decision.</param>
    /// <param name="acceptDescription">Explicit user description decision.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Updated or already-applied canonical file.</returns>
    public async Task<SfdApprovedFieldUpdateResult> PublishAsync(
        string projectDirectory, string rawJson, SfdImportReviewSnapshot reviewed,
        string clientKey, bool acceptTitle, bool acceptDescription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (!acceptTitle && !acceptDescription)
        {
            throw new InvalidDataException(
                "Choisissez explicitement au moins un champ à appliquer.");
        }

        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        SfdImportProposal proposal = envelope.Proposals.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("Proposition absente de la réponse.");
        if (envelope.Step != "sfd-01" ||
            proposal.Kind is not ("businessProcess" or "businessActivity") ||
            proposal.Operation != "update" ||
            proposal.TargetId is not Guid targetId ||
            proposal.Conditions.Count > 0 ||
            proposal.ActivityKeys.Count > 0 ||
            proposal.RequirementKeys.Count > 0 ||
            proposal.UiMode is not null)
        {
            throw new InvalidDataException(
                "Seuls titre et description d'un PM ou ACT existant sont modifiables.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        SfdDocumentCatalog catalog = await _documents.ListAsync(root,
            cancellationToken);
        string manifestPath = Path.Combine(root,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (catalog.Diagnostics.Count > 0 || catalog.ProjectId != manifest.Id ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet non SFD, archivé ou incohérent.");
        }

        SfdDocumentEntry target = catalog.Entries.SingleOrDefault(item =>
            item.Card.Id == targetId &&
            item.Card.Kind is SfdDocumentKind.Process or
                SfdDocumentKind.Activity) ??
            throw new InvalidDataException("PM/ACT cible hors du projet.");
        if (proposal.Kind == "businessProcess" &&
            target.Card.Kind != SfdDocumentKind.Process ||
            proposal.Kind == "businessActivity" &&
            (target.Card.Kind != SfdDocumentKind.Activity ||
                proposal.ParentKey != target.Card.ParentId?.ToString("D")) ||
            proposal.Kind == "businessProcess" && proposal.ParentKey is not null)
        {
            throw new InvalidDataException(
                "Type ou parent PM/ACT différent de l'objet canonique.");
        }

        SfdImportReviewItem row = reviewed.Items.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("Mise à jour absente de la revue.");
        string rawSha = Hash(rawJson);
        if (reviewed.ProjectId != manifest.Id ||
            reviewed.ResponseSha256 != rawSha ||
            reviewed.Step != envelope.Step ||
            reviewed.RunId != envelope.RunId ||
            row.Operation != "update" ||
            row.ExistingId != targetId ||
            row.Status != SfdImportReviewStatus.UpdateCandidate ||
            row.ThreeWay is not { HasBaseline: true, HasConflict: false,
                HasProposedChange: true } preview ||
            !SelectedSafe(preview, acceptTitle, acceptDescription))
        {
            throw new InvalidDataException(
                "Champs non autorisés ou comparaison 3-way absente.");
        }

        string journalRoot = Path.Combine(root, ".sfd-ai-import");
        Directory.CreateDirectory(journalRoot);
        CheckDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".publication.lock");
        if (File.Exists(lockPath) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou d'import SFD non sûr.");
        }

        await using FileStream lease = new(lockPath,
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            4096, FileOptions.Asynchronous);
        string fingerprint = Hash(manifest.Id.ToString("D") + "\n" +
            rawSha + "\n" + clientKey + "\n" +
            (acceptTitle ? "T" : "-") + (acceptDescription ? "D" : "-"));
        string journal = Path.Combine(journalRoot, fingerprint);
        Directory.CreateDirectory(journal);
        CheckDirectory(journal);
        string intentPath = Path.Combine(journal, "intent-fields-update.json");
        SfdApprovedFieldUpdateIntent intent;
        if (File.Exists(intentPath))
        {
            _sources.GetReferenceForExistingFile(intentPath);
            try
            {
                intent = await AtomicJsonFile.ReadAsync<SfdApprovedFieldUpdateIntent>(
                    intentPath, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Journal 3-way corrompu : reprise impossible.", exception);
            }

            CheckIntent(intent, manifest.Id, rawSha,
                envelope.RunId, clientKey, proposal, acceptTitle,
                acceptDescription);
        }
        else
        {
            SfdImportReviewSnapshot latest = await _review.ReviewAsync(
                root, rawJson, cancellationToken);
            SfdImportReviewItem? currentRow = latest.Items.SingleOrDefault(item =>
                item.ClientKey == clientKey);
            if (latest.PlanSha256 != reviewed.PlanSha256 ||
                currentRow?.Status != SfdImportReviewStatus.UpdateCandidate ||
                currentRow.ThreeWay is not { HasBaseline: true,
                    HasConflict: false, HasProposedChange: true } currentPreview ||
                currentRow.ExistingSha256 != row.ExistingSha256 ||
                !SelectedSafe(currentPreview, acceptTitle, acceptDescription))
            {
                throw new InvalidDataException(
                    "Source ou champs modifiés depuis la revue : nouvel arbitrage requis.");
            }

            SfdDocumentBaseline baseline = await _baselines.ReadAsync(
                root, targetId, cancellationToken) ??
                throw new InvalidDataException("Base SFD non capturée.");
            if (baseline.BaseSha256 != preview.BaselineSha256)
            {
                throw new InvalidDataException("Base SFD remplacée.");
            }

            _sources.GetReferenceForExistingFile(target.AbsolutePath);
            SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
                target.AbsolutePath, cancellationToken);
            if (source.Sha256 != currentPreview.CurrentSha256)
            {
                throw new InvalidDataException(
                    "Le fichier a changé pendant la publication.");
            }

            SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
                source.Content);
            string after = SfdDocumentThreeWayCodec.Apply(
                source.Content, card,
                acceptTitle ? proposal.Title : null,
                acceptDescription ? proposal.Description : null);
            if (after == source.Content)
            {
                throw new InvalidDataException(
                    "Les champs sélectionnés ne produisent aucun changement.");
            }

            intent = new SfdApprovedFieldUpdateIntent
            {
                Schema = SfdApprovedFieldUpdateIntent.CurrentSchema,
                ProjectId = manifest.Id,
                TargetId = targetId,
                ResponseSha256 = rawSha,
                RunId = envelope.RunId,
                ClientKey = clientKey,
                BaselineSha256 = baseline.BaseSha256,
                BeforeSha256 = source.Sha256,
                BeforeMarkdown = source.Content,
                AfterSha256 = Hash(after),
                AfterMarkdown = after,
                AcceptTitle = acceptTitle,
                AcceptDescription = acceptDescription
            };
            await AtomicFileWriter.WriteNewAllTextAsync(intentPath,
                JsonSerializer.Serialize(intent, JsonDefaults.Options),
                cancellationToken);
        }

        return await ApplyIntentAsync(root, intent, cancellationToken);
    }

    /// <summary>Executes exactly one CAS replacement with replay protection.</summary>
    /// <param name="root">Verified SFD project.</param>
    /// <param name="intent">Previously approved immutable bytes.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Applied document or safe already-applied receipt.</returns>
    private async Task<SfdApprovedFieldUpdateResult> ApplyIntentAsync(
        string root, SfdApprovedFieldUpdateIntent intent,
        CancellationToken cancellationToken)
    {
        SfdDocumentBaseline baseline = await _baselines.ReadAsync(
            root, intent.TargetId, cancellationToken) ??
            throw new InvalidDataException("Base SFD disparue après approbation.");
        if (baseline.BaseSha256 != intent.BaselineSha256)
        {
            throw new InvalidDataException("Base SFD différente de l'intention.");
        }

        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException("Fiches SFD ambiguës.");
        }

        SfdDocumentEntry currentEntry = catalog.Entries.SingleOrDefault(entry =>
            entry.Card.Id == intent.TargetId &&
            entry.Card.Kind is SfdDocumentKind.Process or
                SfdDocumentKind.Activity) ??
            throw new InvalidDataException("Document PM/ACT supprimé ou hors projet.");
        _sources.GetReferenceForExistingFile(currentEntry.AbsolutePath);
        SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
            currentEntry.AbsolutePath, cancellationToken);
        if (source.Sha256 == intent.AfterSha256)
        {
            return new SfdApprovedFieldUpdateResult(
                intent.TargetId, currentEntry.AbsolutePath, AlreadyApplied: true);
        }

        if (source.Sha256 != intent.BeforeSha256)
        {
            throw new InvalidDataException(
                "La fiche PM/ACT a changé après l'approbation : aucun écrasement.");
        }

        await SfdMarkdownRevisionWriter.ReplaceAsync(
            currentEntry.AbsolutePath, intent.AfterMarkdown,
            intent.BeforeSha256, cancellationToken);
        return new SfdApprovedFieldUpdateResult(
            intent.TargetId, currentEntry.AbsolutePath, AlreadyApplied: false);
    }

    /// <summary>Allows only explicit fields whose original diff was safe.</summary>
    /// <param name="preview">Exact 3-way source values.</param>
    /// <param name="title">Title approval selection.</param>
    /// <param name="description">AI description approval selection.</param>
    /// <returns>Whether all chosen fields are ProposedChange.</returns>
    private static bool SelectedSafe(
        SfdThreeWayPreview preview, bool title, bool description) =>
        (!title || preview.Fields.Any(field =>
            field.Name == "title" &&
            field.Status == SfdThreeWayFieldStatus.ProposedChange)) &&
        (!description || preview.Fields.Any(field =>
            field.Name == "description" &&
            field.Status == SfdThreeWayFieldStatus.ProposedChange));

    /// <summary>Rejects tampered journal content, intent or field selections.</summary>
    /// <param name="intent">Persisted untrusted recovery data.</param>
    /// <param name="projectId">Canonical project ID.</param>
    /// <param name="rawSha">Previously reviewed JSON SHA.</param>
    /// <param name="runId">External run identifier.</param>
    /// <param name="clientKey">Selected proposal.</param>
    /// <param name="proposal">Parsed original external candidate.</param>
    /// <param name="acceptTitle">User title decision.</param>
    /// <param name="acceptDescription">User description decision.</param>
    private static void CheckIntent(SfdApprovedFieldUpdateIntent intent,
        Guid projectId, string rawSha, string runId,
        string clientKey, SfdImportProposal proposal,
        bool acceptTitle, bool acceptDescription)
    {
        if (intent.Schema != SfdApprovedFieldUpdateIntent.CurrentSchema ||
            intent.ProjectId != projectId ||
            intent.TargetId != proposal.TargetId ||
            intent.ResponseSha256 != rawSha ||
            intent.RunId != runId ||
            intent.ClientKey != clientKey ||
            intent.AcceptTitle != acceptTitle ||
            intent.AcceptDescription != acceptDescription ||
            intent.BaselineSha256 is null ||
            intent.BaselineSha256.Length != 64 ||
            intent.BeforeMarkdown is null ||
            intent.AfterMarkdown is null ||
            intent.BeforeSha256 != Hash(intent.BeforeMarkdown) ||
            intent.AfterSha256 != Hash(intent.AfterMarkdown))
        {
            throw new InvalidDataException("Journal 3-way invalide ou modifié.");
        }

        SfdDocumentCard before = SfdDocumentMarkdownCodec.Parse(
            intent.BeforeMarkdown);
        string expected = SfdDocumentThreeWayCodec.Apply(
            intent.BeforeMarkdown, before,
            acceptTitle ? proposal.Title : null,
            acceptDescription ? proposal.Description : null);
        if (before.Id != intent.TargetId || before.ProjectId != projectId ||
            before.Kind is not (SfdDocumentKind.Process or SfdDocumentKind.Activity) ||
            expected != intent.AfterMarkdown)
        {
            throw new InvalidDataException(
                "Le journal de mise à jour n'est pas dérivé des champs approuvés.");
        }
    }

    /// <summary>Rejects symlinks and junctions in a local update journal.</summary>
    /// <param name="directory">Existing approved directory.</param>
    private static void CheckDirectory(string directory)
    {
        string? current = Path.GetFullPath(directory);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Jonction ou lien symbolique interdit pour le journal SFD.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>Computes exact upper-case UTF-8 SHA-256.</summary>
    /// <param name="source">Journal contents or external response.</param>
    /// <returns>Stable SHA digest.</returns>
    private static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}
