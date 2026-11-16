using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Immutable single-FCT intent. The approved N:N link set is committed after
/// the FCT file; a replay completes missing links without duplicating either.
/// </summary>
public sealed record SfdApprovedFeatureIntent
{
    public const string CurrentSchema = "nodalis.sfd.approved-feature.v1";

    public required string Schema { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ResponseSha256 { get; init; }
    public required string RunId { get; init; }
    public required string ClientKey { get; init; }
    public required Guid BusinessId { get; init; }
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required SfdFunctionalityMode Mode { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required Dictionary<Guid, string> ActivityRevisions { get; init; }
    public string? BaseLinksSha256 { get; init; }
    public required string ApprovedMarkdown { get; init; }
    public required string ApprovedSha256 { get; init; }
}

/// <summary>One new or exactly replayed FCT and its approved ACT relationships.</summary>
/// <param name="Id">Nodalis FCT identity.</param>
/// <param name="Reference">Visible FCT code.</param>
/// <param name="AbsolutePath">Canonical Markdown document.</param>
/// <param name="LinkedActivityCount">Number of approved ACT relationships.</param>
/// <param name="AlreadyApplied">Whether both FCT and its links were already complete.</param>
public sealed record SfdApprovedFeatureResult(
    Guid Id, string Reference, string AbsolutePath,
    int LinkedActivityCount, bool AlreadyApplied);

/// <summary>
/// Single reviewed FCT create with guaranteed with/sans-IHM type, a frozen ACT
/// set, durable intent, idempotent N:N links, and no inferred business records.
/// Both operations share the global .publication.lock for other AI imports.
/// </summary>
public sealed class SfdApprovedFeatureImportService
{
    private readonly SfdImportReviewService _review;
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdActivityFunctionalityService _links;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-bounded FCT publisher.</summary>
    /// <param name="workspaceRoot">Authorized Nodalis workspace.</param>
    public SfdApprovedFeatureImportService(string workspaceRoot)
    {
        _review = new SfdImportReviewService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _links = new SfdActivityFunctionalityService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>Publishes ONE explicitly approved FCT with its reviewed ACT GUID links.</summary>
    /// <param name="projectDirectory">Active SFD project.</param>
    /// <param name="rawJson">Exact response previously reviewed in the UI.</param>
    /// <param name="reviewed">Frozen comparison, separate from human approval.</param>
    /// <param name="clientKey">Selected FCT client key.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Newly committed FCT, or byte-identical safe replay.</returns>
    public async Task<SfdApprovedFeatureResult> PublishAsync(
        string projectDirectory, string rawJson, SfdImportReviewSnapshot reviewed,
        string clientKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        SfdImportProposal proposal = envelope.Proposals.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("FCT absente du lot examiné.");
        if (envelope.Step != "sfd-03" || proposal.Kind != "feature" ||
            proposal.Operation != "create" || proposal.TargetId is not null ||
            proposal.ParentKey is not null ||
            proposal.RequirementKeys.Count > 0 ||
            proposal.Conditions.Count > 0)
        {
            throw new InvalidDataException(
                "Publication limitée à une FCT nouvelle avec liens ACT explicites.");
        }

        SfdFunctionalityMode mode = proposal.UiMode switch
        {
            "withUi" => SfdFunctionalityMode.WithInterface,
            "withoutUi" => SfdFunctionalityMode.WithoutInterface,
            _ => throw new InvalidDataException(
                "Chaque FCT requiert une catégorie avec IHM ou sans IHM.")
        };

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        string manifestPath = Path.Combine(root, WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (catalog.Diagnostics.Count > 0 || manifest.Id != catalog.ProjectId ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet SFD archivé ou identités incohérentes.");
        }

        SfdImportReviewItem row = reviewed.Items.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("FCT absente du plan examiné.");
        string responseSha = Hash(rawJson);
        if (reviewed.ProjectId != manifest.Id ||
            reviewed.ResponseSha256 != responseSha ||
            reviewed.Step != envelope.Step || reviewed.RunId != envelope.RunId ||
            row.Kind != "feature" || row.Operation != "create" ||
            row.Status is not (SfdImportReviewStatus.NewCandidate or
                SfdImportReviewStatus.IncompleteDraft))
        {
            throw new InvalidDataException(
                "La FCT n'est pas une proposition de création approuvable.");
        }

        string journalRoot = Path.Combine(root, ".sfd-ai-import");
        Directory.CreateDirectory(journalRoot);
        CheckDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".publication.lock");
        if (File.Exists(lockPath) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou de publication FCT non sûr.");
        }

        await using FileStream lease = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        string hash = Hash(manifest.Id.ToString("D") + "\n" +
            responseSha + "\n" + clientKey);
        string journal = Path.Combine(journalRoot, hash);
        Directory.CreateDirectory(journal);
        CheckDirectory(journal);
        string intentPath = Path.Combine(journal, "intent-feature.json");
        SfdApprovedFeatureIntent intent;
        if (File.Exists(intentPath))
        {
            _sources.GetReferenceForExistingFile(intentPath);
            try
            {
                intent = await AtomicJsonFile.ReadAsync<SfdApprovedFeatureIntent>(
                    intentPath, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Journal FCT endommagé.", exception);
            }

            CheckIntent(intent, manifest.Id, responseSha,
                envelope.RunId, clientKey, proposal.Title, mode, proposal.ActivityKeys);
        }
        else
        {
            SfdImportReviewSnapshot latest = await _review.ReviewAsync(
                root, rawJson, cancellationToken);
            SfdImportReviewItem? currentRow = latest.Items.SingleOrDefault(item =>
                item.ClientKey == clientKey);
            if (latest.PlanSha256 != reviewed.PlanSha256 ||
                latest.ActivityFunctionalitySha256 !=
                    reviewed.ActivityFunctionalitySha256 ||
                currentRow?.Status is not (SfdImportReviewStatus.NewCandidate or
                    SfdImportReviewStatus.IncompleteDraft) ||
                currentRow.ActivityRevisions.Count != proposal.ActivityKeys.Count ||
                !SameActivityHashes(currentRow.ActivityRevisions,
                    row.ActivityRevisions))
            {
                throw new InvalidDataException(
                    "ACT ou associations modifiées depuis la revue : relecture FCT requise.");
            }

            SfdActivityFunctionalitySnapshot links = await _links.ReadAsync(
                root, cancellationToken);
            if (links.Sha256 != reviewed.ActivityFunctionalitySha256)
            {
                throw new InvalidDataException(
                    "Registre ACT/FCT modifié depuis la revue.");
            }

            string prefix = "FCT";
            int highest = catalog.Entries
                .Where(item => item.Card.Kind == SfdDocumentKind.Functionality)
                .Select(item => int.Parse(item.Card.Reference[(prefix.Length + 1)..],
                    CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0).Max();
            if (highest >= 9999)
            {
                throw new InvalidDataException("Numérotation FCT épuisée.");
            }

            string reference = prefix + "-" + (highest + 1).ToString("D4",
                CultureInfo.InvariantCulture);
            SfdDocumentCard card = new()
            {
                Id = Guid.NewGuid(),
                ProjectId = manifest.Id,
                Kind = SfdDocumentKind.Functionality,
                Reference = reference,
                Title = proposal.Title,
                FunctionalityMode = mode,
                Certainty = SfdAssertionCertainty.ToConfirm,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            string markdown = SfdDocumentMarkdownCodec.Format(card);
            if (!string.IsNullOrWhiteSpace(proposal.Description))
            {
                markdown += "\n## Description proposée par IA — à qualifier\n\n" +
                    EscapeUntrusted(proposal.Description) + "\n";
            }

            intent = new SfdApprovedFeatureIntent
            {
                Schema = SfdApprovedFeatureIntent.CurrentSchema,
                ProjectId = manifest.Id,
                ResponseSha256 = responseSha,
                RunId = envelope.RunId,
                ClientKey = clientKey,
                BusinessId = card.Id,
                Reference = reference,
                Title = proposal.Title,
                Mode = mode,
                CreatedUtc = card.CreatedUtc,
                ActivityRevisions = new Dictionary<Guid, string>(row.ActivityRevisions),
                BaseLinksSha256 = links.Sha256,
                ApprovedMarkdown = markdown,
                ApprovedSha256 = Hash(markdown)
            };
            await AtomicFileWriter.WriteNewAllTextAsync(intentPath,
                JsonSerializer.Serialize(intent, JsonDefaults.Options),
                cancellationToken);
        }

        return await ApplyAsync(root, journal, intent, cancellationToken);
    }

    /// <summary>
    /// Replays an interrupted transaction: the FCT directory is committed
    /// first, then the reviewed ACT↔FCT links are atomically saved as one set.
    /// </summary>
    /// <param name="root">Selected active project.</param>
    /// <param name="journal">Verified durable intent folder.</param>
    /// <param name="intent">Frozen reviewed FCT plus ACT set.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Safe creation or idempotent replay result.</returns>
    private async Task<SfdApprovedFeatureResult> ApplyAsync(string root,
        string journal, SfdApprovedFeatureIntent intent, CancellationToken ct)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(root, ct);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Catalogue SFD incohérent : publication FCT refusée.");
        }

        foreach ((Guid id, string expectedSha) in intent.ActivityRevisions)
        {
            SfdDocumentEntry entry = catalog.Entries.SingleOrDefault(item =>
                item.Card.Id == id && item.Card.Kind == SfdDocumentKind.Activity) ??
                throw new InvalidDataException("ACT liée absente du projet.");
            _sources.GetReferenceForExistingFile(entry.AbsolutePath);
            SfdTextRevision text = await SfdMarkdownRevisionWriter.ReadAsync(
                entry.AbsolutePath, ct);
            SfdDocumentCard identity = SfdDocumentMarkdownCodec.Parse(text.Content);
            if (identity.Id != id || identity.ProjectId != intent.ProjectId ||
                text.Sha256 != expectedSha)
            {
                throw new InvalidDataException(
                    "Une ACT liée a changé depuis la revue FCT.");
            }
        }

        string parent = Path.Combine(root, "SPECS FONCTIONNELLES", "FONCTIONNALITES");
        CheckDirectory(parent);
        string folder = Path.Combine(parent,
            intent.Reference + " - " + SafeTitle(intent.Title));
        string target = Path.Combine(folder, "presentation fonctionnalité.md");
        bool alreadyDocument = false;
        if (Directory.Exists(folder) || File.Exists(folder))
        {
            if (!File.Exists(target))
            {
                throw new InvalidDataException(
                    "Dossier FCT préexistant sans fiche approuvée.");
            }

            _sources.GetReferenceForExistingFile(target);
            SfdTextRevision existing = await SfdMarkdownRevisionWriter.ReadAsync(
                target, ct);
            SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(existing.Content);
            if (card.Id != intent.BusinessId ||
                card.FunctionalityMode != intent.Mode ||
                card.ProjectId != intent.ProjectId ||
                existing.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "La FCT a été modifiée ou son identité ne correspond plus.");
            }

            alreadyDocument = true;
        }
        else
        {
            if (catalog.Entries.Any(entry =>
                entry.Card.Id == intent.BusinessId ||
                entry.Card.Reference == intent.Reference))
            {
                throw new InvalidDataException(
                    "GUID ou référence FCT déjà occupé à un autre emplacement.");
            }

            SfdActivityFunctionalitySnapshot before = await _links.ReadAsync(root, ct);
            if (before.Sha256 != intent.BaseLinksSha256)
            {
                throw new InvalidDataException(
                    "Registre ACT/FCT changé avant création de la FCT.");
            }

            string stage = Path.Combine(journal, "staged-feature");
            Directory.CreateDirectory(stage);
            CheckDirectory(stage);
            string stagedFile = Path.Combine(stage, "presentation fonctionnalité.md");
            if (File.Exists(stagedFile))
            {
                _sources.GetReferenceForExistingFile(stagedFile);
                SfdTextRevision staged = await SfdMarkdownRevisionWriter.ReadAsync(
                    stagedFile, ct);
                if (staged.Sha256 != intent.ApprovedSha256)
                {
                    throw new InvalidDataException(
                        "Snapshot FCT staged ne correspond plus au journal.");
                }
            }
            else
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    stagedFile, intent.ApprovedMarkdown, ct);
            }

            ct.ThrowIfCancellationRequested();
            CheckDirectory(parent);
            Directory.Move(stage, folder);
        }

        SfdActivityFunctionalitySnapshot links = await _links.ReadAsync(root, ct);
        Guid[] requested = intent.ActivityRevisions.Keys.OrderBy(id => id).ToArray();
        Guid[] existingLinks = links.Registry.Links.Where(link =>
            link.FunctionalityId == intent.BusinessId)
            .Select(link => link.ActivityId).OrderBy(id => id).ToArray();
        if (existingLinks.SequenceEqual(requested))
        {
            return new SfdApprovedFeatureResult(
                intent.BusinessId, intent.Reference, target,
                requested.Length, alreadyDocument);
        }

        if (existingLinks.Length > 0 ||
            links.Sha256 != intent.BaseLinksSha256)
        {
            throw new InvalidDataException(
                "Le registre N:N a changé depuis l'approbation. " +
                "Aucune association n'a été écrasée.");
        }

        SfdActivityFunctionalityRegistry registry = links.Registry with
        {
            Links = links.Registry.Links.Concat(requested.Select(activityId =>
                new SfdActivityFunctionalityLink
                {
                    ActivityId = activityId,
                    FunctionalityId = intent.BusinessId
                })).ToList()
        };
        await _links.SaveAsync(root, registry, links.Sha256, ct);
        return new SfdApprovedFeatureResult(
            intent.BusinessId, intent.Reference, target,
            requested.Length, false);
    }

    /// <summary>Compares two source-revision maps without order dependence.</summary>
    /// <param name="left">Current lookup.</param>
    /// <param name="right">Reviewed lookup.</param>
    /// <returns>Whether all GUIDs and source hashes match exactly.</returns>
    private static bool SameActivityHashes(
        IReadOnlyDictionary<Guid, string> left,
        IReadOnlyDictionary<Guid, string> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out string? hash) && pair.Value == hash);

    /// <summary>Rejects malformed persisted FCT identity, mode and ACT intent.</summary>
    /// <param name="intent">On-disk durable intent.</param>
    /// <param name="projectId">Canonical owner.</param>
    /// <param name="rawSha">Original reviewed AI response.</param>
    /// <param name="runId">Original run.</param>
    /// <param name="key">Selected FCT proposal.</param>
    /// <param name="title">Exact reviewed title.</param>
    /// <param name="mode">Approved IHM category.</param>
    /// <param name="activityKeys">Explicit AI ACT references.</param>
    private static void CheckIntent(SfdApprovedFeatureIntent intent,
        Guid projectId, string rawSha, string runId, string key,
        string title, SfdFunctionalityMode mode,
        IReadOnlyList<string> activityKeys)
    {
        if (intent.Schema != SfdApprovedFeatureIntent.CurrentSchema ||
            intent.ProjectId != projectId ||
            intent.ResponseSha256 != rawSha ||
            intent.RunId != runId || intent.ClientKey != key ||
            intent.BusinessId == Guid.Empty ||
            intent.Title != title || intent.Mode != mode ||
            intent.Reference is null ||
            !intent.Reference.StartsWith("FCT-", StringComparison.Ordinal) ||
            intent.Reference.Length != 8 ||
            !int.TryParse(intent.Reference[4..], out int number) ||
            number is < 1 or > 9999 ||
            intent.ActivityRevisions is null ||
            intent.ActivityRevisions.Count != activityKeys.Count ||
            intent.ActivityRevisions.Values.Any(sha => sha is null ||
                sha.Length != 64) ||
            intent.ActivityRevisions.Keys.Any(id => id == Guid.Empty) ||
            intent.ApprovedMarkdown is null ||
            intent.ApprovedSha256 is null ||
            Hash(intent.ApprovedMarkdown) != intent.ApprovedSha256)
        {
            throw new InvalidDataException(
                "Journal FCT altéré ou hors du contrat approuvé.");
        }

        foreach (string token in activityKeys)
        {
            if (!Guid.TryParseExact(token, "D", out Guid id) ||
                !intent.ActivityRevisions.ContainsKey(id))
            {
                throw new InvalidDataException(
                    "ACT liées du journal différentes du lot approuvé.");
            }
        }

        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
            intent.ApprovedMarkdown);
        if (card.Id != intent.BusinessId ||
            card.Kind != SfdDocumentKind.Functionality ||
            card.ProjectId != intent.ProjectId ||
            card.FunctionalityMode != intent.Mode ||
            card.Reference != intent.Reference ||
            card.Title != intent.Title)
        {
            throw new InvalidDataException(
                "Snapshot FCT non conforme à l'intention.");
        }
    }

    /// <summary>Rejects reparse points in existing directory ancestors.</summary>
    /// <param name="directory">Expected safe path.</param>
    private static void CheckDirectory(string directory)
    {
        string? current = Path.GetFullPath(directory);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Lien ou jonction interdit dans le stockage FCT.");
            }

            current = Path.GetDirectoryName(current);
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "Dossier FCT canonique absent.");
        }
    }

    /// <summary>Derives a safe readable folder title with no external paths.</summary>
    /// <param name="title">Reviewed FCT title.</param>
    /// <returns>Windows-safe suffix.</returns>
    private static string SafeTitle(string title)
    {
        string name = new string(title.Trim().Take(70).Select(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '-' or '_'
                ? character : '_').ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "Sans titre" : name;
    }

    /// <summary>Encodes exact upper-case SHA256 for immutable snapshots.</summary>
    /// <param name="text">Reviewed source.</param>
    /// <returns>Stable hash.</returns>
    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Escapes untrusted HTML from generated descriptive fields.</summary>
    /// <param name="value">Reviewed proposed description.</param>
    /// <returns>Markdown-compatible plain text.</returns>
    private static string EscapeUntrusted(string value) =>
        value.Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Trim();
}
