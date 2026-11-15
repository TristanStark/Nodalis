using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable, scoped intent for ONE human-approved new PM. It contains the
/// complete approved Markdown snapshot but not the raw external AI response.
/// </summary>
public sealed record SfdApprovedProcessIntent
{
    public const string CurrentSchema = "nodalis.sfd.approved-process.v1";

    public required string Schema { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ResponseSha256 { get; init; }

    public required string ClientKey { get; init; }

    public required string RunId { get; init; }

    public required Guid BusinessId { get; init; }

    public required string Reference { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required string ApprovedMarkdown { get; init; }

    public required string ApprovedSha256 { get; init; }
}

/// <summary>One approved PM creation or a recognized identical replay.</summary>
/// <param name="Id">Nodalis-assigned persistent PM GUID.</param>
/// <param name="Reference">Nodalis-assigned visible PM reference.</param>
/// <param name="AbsolutePath">Canonical Markdown path.</param>
/// <param name="AlreadyApplied">True when the exact journaled snapshot was published.</param>
public sealed record SfdApprovedProcessResult(
    Guid Id, string Reference, string AbsolutePath, bool AlreadyApplied);

/// <summary>
/// First conservative #220 publication path: exactly ONE explicit PM creation.
/// Durable intent precedes the only business write. Replays use the same GUID
/// and bytes, and never silently overwrite changed content.
/// </summary>
public sealed class SfdApprovedProcessImportService
{
    private const string JournalRootName = ".sfd-ai-import";
    private const string IntentFileName = "intent.json";
    private const string StageName = "staged-process";
    private readonly SfdImportReviewService _review;
    private readonly SfdDocumentRegistryService _documents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-constrained single-PM publication service.</summary>
    /// <param name="workspaceRoot">Active Nodalis workspace.</param>
    public SfdApprovedProcessImportService(string workspaceRoot)
    {
        _review = new SfdImportReviewService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Publishes only the exact chosen PM creation from a frozen, human-reviewed
    /// JSON response. Retrying the same approved response/key is idempotent.
    /// Other operations and roles remain explicitly unsupported.
    /// </summary>
    /// <param name="projectDirectory">Verified active SFD project.</param>
    /// <param name="rawJson">Untrusted external response already displayed to the user.</param>
    /// <param name="reviewed">Snapshot explicitly reviewed before confirmation.</param>
    /// <param name="clientKey">User-selected PM proposal key.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Applied or already-applied canonical PM identity.</returns>
    public async Task<SfdApprovedProcessResult> PublishAsync(
        string projectDirectory, string rawJson, SfdImportReviewSnapshot reviewed,
        string clientKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        SfdImportProposal proposal = envelope.Proposals.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("Clé PM absente de la réponse relue.");
        if (envelope.Step != "sfd-01" ||
            proposal.Kind != "businessProcess" ||
            proposal.Operation != "create" || proposal.TargetId is not null ||
            proposal.ParentKey is not null || proposal.Conditions.Count > 0 ||
            proposal.ActivityKeys.Count > 0 || proposal.RequirementKeys.Count > 0)
        {
            throw new InvalidDataException(
                "La publication pilote ne prend en charge qu'un PM nouveau, sans liens.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        ProjectManifest project = await EnsureActiveAsync(root, cancellationToken);
        if (reviewed.ProjectId != project.Id ||
            reviewed.ResponseSha256 != Hash(rawJson) ||
            reviewed.Step != envelope.Step ||
            reviewed.RunId != envelope.RunId ||
            reviewed.Items.SingleOrDefault(item => item.ClientKey == clientKey)
                is not SfdImportReviewItem row ||
            row.Status != SfdImportReviewStatus.NewCandidate)
        {
            throw new InvalidDataException(
                "La proposition n'est plus celle qui a été examinée.");
        }

        string journalRoot = Path.Combine(root, JournalRootName);
        Directory.CreateDirectory(journalRoot);
        EnsureNormalDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".publication.lock");
        if (File.Exists(lockPath) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou de publication SFD non sûr.");
        }

        await using FileStream lease = new(lockPath,
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            4096, FileOptions.Asynchronous);
        string fingerprint = Hash(project.Id.ToString("D") + "\n" +
            reviewed.ResponseSha256 + "\n" + clientKey);
        string intentDirectory = Path.Combine(journalRoot, fingerprint);
        Directory.CreateDirectory(intentDirectory);
        EnsureNormalDirectory(intentDirectory);
        string intentPath = Path.Combine(intentDirectory, IntentFileName);

        SfdApprovedProcessIntent intent;
        if (File.Exists(intentPath))
        {
            _sources.GetReferenceForExistingFile(intentPath);
            try
            {
                intent = await AtomicJsonFile.ReadAsync<SfdApprovedProcessIntent>(
                    intentPath, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Journal PM SFD malformé : reprise impossible.", exception);
            }

            VerifyIntent(intent, project.Id, reviewed.ResponseSha256,
                clientKey, envelope.RunId);
        }
        else
        {
            SfdImportReviewSnapshot latest = await _review.ReviewAsync(
                root, rawJson, cancellationToken);
            if (latest.PlanSha256 != reviewed.PlanSha256 ||
                latest.Items.SingleOrDefault(item => item.ClientKey == clientKey)
                    ?.Status != SfdImportReviewStatus.NewCandidate)
            {
                throw new InvalidDataException(
                    "Le projet a changé depuis la comparaison : nouvelle revue requise.");
            }

            SfdDocumentCatalog catalog = await _documents.ListAsync(
                root, cancellationToken);
            int highest = catalog.Entries
                .Where(item => item.Card.Kind == SfdDocumentKind.Process)
                .Select(item => int.Parse(item.Card.Reference[3..],
                    System.Globalization.CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0).Max();
            if (highest >= 9999)
            {
                throw new InvalidDataException("Numérotation PM épuisée.");
            }

            Guid id = Guid.NewGuid();
            string reference = "PM-" + (highest + 1).ToString("D4",
                System.Globalization.CultureInfo.InvariantCulture);
            DateTimeOffset created = DateTimeOffset.UtcNow;
            SfdDocumentCard card = new()
            {
                Id = id,
                ProjectId = project.Id,
                Kind = SfdDocumentKind.Process,
                Reference = reference,
                Title = proposal.Title,
                CreatedUtc = created,
                Certainty = SfdAssertionCertainty.ToConfirm
            };
            string markdown = SfdDocumentMarkdownCodec.Format(card);
            if (!string.IsNullOrWhiteSpace(proposal.Description))
            {
                markdown += "\n## Description proposée par IA — à qualifier\n\n" +
                    EscapeAsUntrustedMarkdown(proposal.Description) + "\n";
            }

            intent = new SfdApprovedProcessIntent
            {
                Schema = SfdApprovedProcessIntent.CurrentSchema,
                ProjectId = project.Id,
                ResponseSha256 = reviewed.ResponseSha256,
                ClientKey = clientKey,
                RunId = envelope.RunId,
                BusinessId = id,
                Reference = reference,
                Title = proposal.Title,
                CreatedUtc = created,
                ApprovedMarkdown = markdown,
                ApprovedSha256 = Hash(markdown)
            };
            // Complete intent must survive power loss BEFORE any PM is published.
            await AtomicFileWriter.WriteNewAllTextAsync(intentPath,
                JsonSerializer.Serialize(intent, JsonDefaults.Options),
                cancellationToken);
        }

        return await PublishIntentAsync(root, intentDirectory, intent,
            cancellationToken);
    }

    /// <summary>
    /// Resolves an already published PM from the SAME response's original
    /// clientKey, without creating it or matching titles. A renamed PM remains
    /// resolvable by its Nodalis GUID, not its old directory.
    /// </summary>
    /// <param name="projectDirectory">Active SFD project root.</param>
    /// <param name="rawJson">Identical reviewed response.</param>
    /// <param name="clientKey">Previously approved PM key.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Currently indexed canonical PM or null if not yet published.</returns>
    public async Task<SfdDocumentEntry?> FindPublishedAsync(
        string projectDirectory, string rawJson, string clientKey,
        CancellationToken cancellationToken = default)
    {
        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        SfdImportProposal? proposal = envelope.Proposals.SingleOrDefault(item =>
            item.ClientKey == clientKey);
        if (envelope.Step != "sfd-01" || proposal is null ||
            proposal.Kind != "businessProcess" || proposal.Operation != "create")
        {
            return null;
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        ProjectManifest project = await EnsureActiveAsync(root, cancellationToken);
        string hash = Hash(rawJson);
        string key = Hash(project.Id.ToString("D") + "\n" + hash + "\n" + clientKey);
        string directory = Path.Combine(root, JournalRootName, key);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        EnsureNormalDirectory(directory);
        string path = Path.Combine(directory, IntentFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        _sources.GetReferenceForExistingFile(path);
        SfdApprovedProcessIntent intent;
        try
        {
            intent = await AtomicJsonFile.ReadAsync<SfdApprovedProcessIntent>(
                path, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Journal PM malformé : impossible de résoudre le parent.", exception);
        }

        VerifyIntent(intent, project.Id, hash, clientKey, envelope.RunId);
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Identités SFD incohérentes : parent PM introuvable.");
        }

        SfdDocumentEntry? actual = catalog.Entries.SingleOrDefault(item =>
            item.Card.Id == intent.BusinessId);
        if (actual is null)
        {
            return null;
        }

        if (actual.Card.Kind != SfdDocumentKind.Process ||
            actual.Card.Reference != intent.Reference ||
            actual.Card.ProjectId != project.Id)
        {
            throw new InvalidDataException(
                "Le PM journalisé ne correspond plus à sa fiche canonique.");
        }

        return actual;
    }

    /// <summary>Replays a verified durable intent without assigning a second GUID.</summary>
    /// <param name="root">Verified active project.</param>
    /// <param name="intentDirectory">Journal folder scoped by response/key.</param>
    /// <param name="intent">Frozen approved source and identity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current canonical publication status.</returns>
    private async Task<SfdApprovedProcessResult> PublishIntentAsync(
        string root, string intentDirectory, SfdApprovedProcessIntent intent,
        CancellationToken cancellationToken)
    {
        string parent = Path.Combine(root, "SPECS FONCTIONNELLES",
            "PROCESSUS METIERS");
        EnsureNormalDirectory(parent);
        string targetDirectory = Path.Combine(parent,
            intent.Reference + " - " + SafeTitleSegment(intent.Title));
        string target = Path.Combine(targetDirectory, "presentation processus.md");
        if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
        {
            if (!File.Exists(target))
            {
                throw new InvalidDataException(
                    "Le dossier PM existe sans la fiche approuvée : conflit.");
            }

            _sources.GetReferenceForExistingFile(target);
            SfdTextRevision actual = await SfdMarkdownRevisionWriter.ReadAsync(
                target, cancellationToken);
            SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(actual.Content);
            if (card.Id != intent.BusinessId ||
                actual.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "Le PM a été modifié après publication ou l'identité est en conflit.");
            }

            return new SfdApprovedProcessResult(intent.BusinessId,
                intent.Reference, target, AlreadyApplied: true);
        }

        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        if (catalog.Diagnostics.Count > 0 ||
            catalog.Entries.Any(entry =>
                entry.Card.Id == intent.BusinessId ||
                entry.Card.Reference == intent.Reference))
        {
            throw new InvalidDataException(
                "Identifiant ou référence PM déjà utilisée : reprise suspendue.");
        }

        string staging = Path.Combine(intentDirectory, StageName);
        if (!Directory.Exists(staging))
        {
            Directory.CreateDirectory(staging);
        }

        EnsureNormalDirectory(staging);
        string stageFile = Path.Combine(staging, "presentation processus.md");
        if (File.Exists(stageFile))
        {
            _sources.GetReferenceForExistingFile(stageFile);
            SfdTextRevision stored = await SfdMarkdownRevisionWriter.ReadAsync(
                stageFile, cancellationToken);
            if (stored.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "Snapshot de récupération SFD modifié : reprise refusée.");
            }
        }
        else
        {
            await AtomicFileWriter.WriteNewAllTextAsync(stageFile,
                intent.ApprovedMarkdown, cancellationToken);
        }

        Directory.CreateDirectory(Path.Combine(staging, "ACTIVITES"));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNormalDirectory(parent);
        Directory.Move(staging, targetDirectory);
        return new SfdApprovedProcessResult(intent.BusinessId,
            intent.Reference, target, AlreadyApplied: false);
    }

    /// <summary>Reads and checks the exact project owner, kind and archive state.</summary>
    /// <param name="root">Selected project root.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Active manifest.</returns>
    private async Task<ProjectManifest> EnsureActiveAsync(string root,
        CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(root,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.KindSchemaVersion != 1 ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Publication IA interdite : projet SFD absent ou archivé.");
        }

        return manifest;
    }

    /// <summary>Rejects stale or tampered durable reviewed intents.</summary>
    /// <param name="intent">Stored journal entry.</param>
    /// <param name="projectId">Current project GUID.</param>
    /// <param name="rawHash">Approved original response hash.</param>
    /// <param name="clientKey">Human-selected external key.</param>
    /// <param name="runId">Exact response run ID.</param>
    private static void VerifyIntent(SfdApprovedProcessIntent intent,
        Guid projectId, string rawHash, string clientKey, string runId)
    {
        if (intent.Schema != SfdApprovedProcessIntent.CurrentSchema ||
            intent.ProjectId != projectId ||
            intent.ResponseSha256 != rawHash ||
            intent.ClientKey != clientKey || intent.RunId != runId ||
            intent.BusinessId == Guid.Empty ||
            intent.CreatedUtc == default ||
            string.IsNullOrWhiteSpace(intent.Title) ||
            intent.Title.Length > 240 ||
            intent.Reference is null || intent.Reference.Length != 7 ||
            intent.ApprovedMarkdown is null ||
            intent.ApprovedSha256 is null ||
            !intent.Reference.StartsWith("PM-", StringComparison.Ordinal) ||
            !int.TryParse(intent.Reference[3..], out int number) ||
            number is < 1 or > 9999 ||
            Hash(intent.ApprovedMarkdown) != intent.ApprovedSha256)
        {
            throw new InvalidDataException(
                "Intent de publication SFD altéré ou incompatible.");
        }

        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
            intent.ApprovedMarkdown);
        if (card.Id != intent.BusinessId ||
            card.ProjectId != projectId ||
            card.Reference != intent.Reference ||
            card.Title != intent.Title ||
            card.Kind != SfdDocumentKind.Process)
        {
            throw new InvalidDataException(
                "Métadonnées et snapshot du PM approuvé en désaccord.");
        }
    }

    /// <summary>Rejects directory junctions in existing import or project paths.</summary>
    /// <param name="path">Existing canonical directory.</param>
    private static void EnsureNormalDirectory(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Lien symbolique ou jonction interdit dans le journal SFD.");
            }

            current = Path.GetDirectoryName(current);
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                "Répertoire de publication SFD manquant : " + path);
        }
    }

    /// <summary>Creates a safe visible folder label independent of external paths.</summary>
    /// <param name="title">Approved user-visible business title.</param>
    /// <returns>Bounded filename segment.</returns>
    private static string SafeTitleSegment(string title)
    {
        string slug = new string(title.Trim().Take(70).Select(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '-' or '_'
                ? character : '_').ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(slug) ? "Sans titre" : slug;
    }

    /// <summary>Escapes HTML in untrusted descriptive text before Markdown writing.</summary>
    /// <param name="text">Reviewed external description.</param>
    /// <returns>Portable Markdown-safe paragraph text.</returns>
    private static string EscapeAsUntrustedMarkdown(string text) =>
        text.Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Trim();

    /// <summary>Computes an exact UTF-8 uppercase SHA-256 for journal identity.</summary>
    /// <param name="source">Snapshot or response text.</param>
    /// <returns>Uppercase hex digest.</returns>
    private static string Hash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}
