using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Durable one-RM intent, never a copied definition in a requirement.</summary>
public sealed record SfdApprovedRuleIntent
{
    public const string CurrentSchema = "nodalis.sfd.approved-rule.v1";
    public required string Schema { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ResponseSha256 { get; init; }
    public required string RunId { get; init; }
    public required string ClientKey { get; init; }
    public required string BaseSha256 { get; init; }
    public required Guid RuleId { get; init; }
    public required string Reference { get; init; }
    public required string Statement { get; init; }
    public required string ApprovedCatalogSha256 { get; init; }
}

/// <summary>Canonical RM publication result.</summary>
public sealed record SfdApprovedRuleResult(
    Guid Id, string Reference, bool AlreadyApplied);

/// <summary>
/// Publishes ONE explicitly selected business rule in the unique Markdown
/// catalog. It uses the same cross-type publication lease as PM/ACT/EXU,
/// an intent written before CAS, and exact replay validation.
/// </summary>
public sealed class SfdApprovedRuleImportService
{
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdStructuredAuthoringService _rules;
    private readonly SfdImportReviewService _review;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Binds rule publications to one workspace.</summary>
    public SfdApprovedRuleImportService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _rules = new SfdStructuredAuthoringService(workspaceRoot);
        _review = new SfdImportReviewService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Adds a single explicitly accepted new RM, or verifies a byte-identical
    /// already-applied journal. AI reliability never becomes business approval.
    /// </summary>
    public async Task<SfdApprovedRuleResult> PublishAsync(
        string projectDirectory, string rawJson, SfdImportReviewSnapshot reviewed,
        string clientKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        SfdImportProposal proposal = envelope.Proposals.SingleOrDefault(x =>
            x.ClientKey == clientKey) ??
            throw new InvalidDataException("RM absente du lot examiné.");
        if (envelope.Step != "sfd-02" || proposal.Kind != "businessRule" ||
            proposal.Operation != "create" || proposal.TargetId is not null ||
            proposal.ParentKey is not null || proposal.UiMode is not null ||
            proposal.Conditions.Count != 0 ||
            proposal.ActivityKeys.Count != 0 ||
            proposal.RequirementKeys.Count != 0)
        {
            throw new InvalidDataException(
                "Seule la création d'une RM autonome est prise en charge.");
        }

        SfdConditionBlockCodec.RequireStatement(proposal.Title);
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
            throw new InvalidDataException("Projet SFD archivé ou ambigu.");
        }

        string responseSha = Hash(rawJson);
        if (reviewed.ProjectId != manifest.Id ||
            reviewed.ResponseSha256 != responseSha ||
            reviewed.Step != envelope.Step || reviewed.RunId != envelope.RunId ||
            reviewed.Items.SingleOrDefault(x => x.ClientKey == clientKey)
                is not
                {
                    Status: SfdImportReviewStatus.NewCandidate,
                    Kind: "businessRule", Operation: "create"
                })
        {
            throw new InvalidDataException("RM non approuvable depuis cette revue.");
        }

        string journalRoot = Path.Combine(root, ".sfd-ai-import");
        Directory.CreateDirectory(journalRoot);
        CheckDirectory(journalRoot);
        string lockPath = Path.Combine(journalRoot, ".publication.lock");
        if (File.Exists(lockPath) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou SFD non sûr.");
        }

        await using FileStream lease = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        string key = Hash(manifest.Id.ToString("D") + "\n" +
            responseSha + "\n" + clientKey);
        string journal = Path.Combine(journalRoot, key);
        Directory.CreateDirectory(journal);
        CheckDirectory(journal);
        string path = Path.Combine(journal, "intent-rule.json");
        SfdApprovedRuleIntent intent;
        if (File.Exists(path))
        {
            _sources.GetReferenceForExistingFile(path);
            try
            {
                intent = await AtomicJsonFile.ReadAsync<SfdApprovedRuleIntent>(
                    path, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Journal RM altéré.", exception);
            }

            CheckIntent(intent, manifest.Id, responseSha, envelope.RunId,
                clientKey, proposal.Title);
        }
        else
        {
            SfdImportReviewSnapshot latest = await _review.ReviewAsync(
                root, rawJson, cancellationToken);
            if (latest.PlanSha256 != reviewed.PlanSha256 ||
                latest.RulesCatalogSha256 != reviewed.RulesCatalogSha256 ||
                latest.Items.SingleOrDefault(x => x.ClientKey == clientKey)
                    ?.Status != SfdImportReviewStatus.NewCandidate)
            {
                throw new InvalidDataException(
                    "Catalogue RM ou propositions modifiés : nouvelle revue obligatoire.");
            }

            SfdBusinessRuleSnapshot rules = await _rules.ReadRulesAsync(
                root, cancellationToken);
            if (rules.Sha256 != reviewed.RulesCatalogSha256 ||
                rules.Rules.Any(rule => string.Equals(rule.Statement,
                    proposal.Title, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "La RM existe déjà ou le catalogue a changé.");
            }

            string catalogPath = RulesPath(root);
            _sources.GetReferenceForExistingFile(catalogPath);
            SfdTextRevision original = await SfdMarkdownRevisionWriter.ReadAsync(
                catalogPath, cancellationToken);
            if (original.Sha256 != rules.Sha256)
            {
                throw new InvalidDataException("Révision RM différente.");
            }

            SfdBusinessRule rule = new()
            {
                Id = Guid.NewGuid(),
                Reference = SfdBusinessRuleMarkdownCodec.NextReference(rules.Rules),
                Statement = proposal.Title.Trim(),
                // Source keys describe unverified evidence, not a verified citation.
                Source = "",
                Certainty = SfdAssertionCertainty.ToConfirm
            };
            SfdBusinessRuleMarkdownCodec.Validate(rule);
            string approved = SfdBusinessRuleMarkdownCodec.Apply(
                original.Content, rules.Rules.Append(rule).ToArray());
            intent = new SfdApprovedRuleIntent
            {
                Schema = SfdApprovedRuleIntent.CurrentSchema,
                ProjectId = manifest.Id,
                ResponseSha256 = responseSha,
                RunId = envelope.RunId,
                ClientKey = clientKey,
                BaseSha256 = original.Sha256,
                RuleId = rule.Id,
                Reference = rule.Reference,
                Statement = rule.Statement,
                ApprovedCatalogSha256 = Hash(approved)
            };
            await AtomicFileWriter.WriteNewAllTextAsync(path,
                JsonSerializer.Serialize(intent, JsonDefaults.Options),
                cancellationToken);
        }

        return await ApplyAsync(root, intent, cancellationToken);
    }

    /// <summary>
    /// Finds a previously published rule by clientKey of the same raw response.
    /// No title matching, no GUID fabrication, and no implicit publication.
    /// </summary>
    public async Task<SfdBusinessRule?> FindPublishedAsync(
        string projectDirectory, string rawJson, string clientKey,
        CancellationToken cancellationToken = default)
    {
        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        if (envelope.Step != "sfd-02" ||
            !envelope.Proposals.Any(x => x.ClientKey == clientKey &&
                x.Kind == "businessRule" && x.Operation == "create"))
        {
            return null;
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        string key = Hash(catalog.ProjectId.ToString("D") + "\n" +
            Hash(rawJson) + "\n" + clientKey);
        string path = Path.Combine(root, ".sfd-ai-import", key, "intent-rule.json");
        if (!File.Exists(path))
        {
            return null;
        }

        _sources.GetReferenceForExistingFile(path);
        SfdApprovedRuleIntent intent;
        try
        {
            intent = await AtomicJsonFile.ReadAsync<SfdApprovedRuleIntent>(
                path, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Journal RM illisible.", exception);
        }

        SfdImportProposal proposal = envelope.Proposals.Single(x =>
            x.ClientKey == clientKey);
        CheckIntent(intent, catalog.ProjectId, Hash(rawJson),
            envelope.RunId, clientKey, proposal.Title);
        SfdBusinessRuleSnapshot rules = await _rules.ReadRulesAsync(
            root, cancellationToken);
        SfdBusinessRule? found = rules.Rules.SingleOrDefault(x =>
            x.Id == intent.RuleId);
        if (found is null)
        {
            return null;
        }

        if (found.Reference != intent.Reference ||
            found.Statement != intent.Statement ||
            found.Certainty != SfdAssertionCertainty.ToConfirm)
        {
            throw new InvalidDataException(
                "La RM importée a été modifiée : rapprochement automatique interdit.");
        }

        return found;
    }

    /// <summary>Applies or recognizes the exact approved RM under catalog CAS.</summary>
    /// <param name="root">Verified active project.</param>
    /// <param name="intent">Immutable approved rule identity.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>New or already-published RM.</returns>
    private async Task<SfdApprovedRuleResult> ApplyAsync(
        string root, SfdApprovedRuleIntent intent, CancellationToken ct)
    {
        string path = RulesPath(root);
        _sources.GetReferenceForExistingFile(path);
        SfdTextRevision current = await SfdMarkdownRevisionWriter.ReadAsync(
            path, ct);
        IReadOnlyList<SfdBusinessRule> rules =
            SfdBusinessRuleMarkdownCodec.Parse(current.Content);
        SfdBusinessRule? already = rules.SingleOrDefault(x =>
            x.Id == intent.RuleId);
        if (already is not null)
        {
            if (current.Sha256 != intent.ApprovedCatalogSha256 ||
                already.Reference != intent.Reference ||
                already.Statement != intent.Statement ||
                already.Source != "" ||
                already.Certainty != SfdAssertionCertainty.ToConfirm)
            {
                throw new InvalidDataException(
                    "Catalogue modifié après la publication RM : aucun écrasement.");
            }

            return new SfdApprovedRuleResult(
                intent.RuleId, intent.Reference, true);
        }

        if (current.Sha256 != intent.BaseSha256 ||
            rules.Any(x => x.Reference == intent.Reference ||
                string.Equals(x.Statement, intent.Statement,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "La base RM a changé ou un doublon existe : relecture requise.");
        }

        SfdBusinessRule rule = new()
        {
            Id = intent.RuleId,
            Reference = intent.Reference,
            Statement = intent.Statement,
            Source = "",
            Certainty = SfdAssertionCertainty.ToConfirm
        };
        SfdBusinessRuleMarkdownCodec.Validate(rule);
        string output = SfdBusinessRuleMarkdownCodec.Apply(
            current.Content, rules.Append(rule).ToArray());
        if (Hash(output) != intent.ApprovedCatalogSha256)
        {
            throw new InvalidDataException(
                "Catalogue RM différent de l'intention approuvée.");
        }

        await SfdMarkdownRevisionWriter.ReplaceAsync(
            path, output, current.Sha256, ct);
        return new SfdApprovedRuleResult(
            intent.RuleId, intent.Reference, false);
    }

    /// <summary>Rejects stale, malformed or incompatible persisted RM intent.</summary>
    /// <param name="intent">Stored recovery intent.</param>
    /// <param name="projectId">Expected owner.</param>
    /// <param name="responseSha">Reviewed response fingerprint.</param>
    /// <param name="runId">External run ID.</param>
    /// <param name="clientKey">Approved temporary key.</param>
    /// <param name="statement">Exactly reviewed RM wording.</param>
    private static void CheckIntent(SfdApprovedRuleIntent intent,
        Guid projectId, string responseSha, string runId, string clientKey,
        string statement)
    {
        if (intent.Schema != SfdApprovedRuleIntent.CurrentSchema ||
            intent.ProjectId != projectId ||
            intent.ResponseSha256 != responseSha ||
            intent.RunId != runId ||
            intent.ClientKey != clientKey ||
            intent.RuleId == Guid.Empty ||
            intent.Statement != statement.Trim() ||
            intent.Reference is null || intent.Reference.Length != 7 ||
            !intent.Reference.StartsWith("RM-", StringComparison.Ordinal) ||
            !int.TryParse(intent.Reference[3..], out int number) ||
            number is < 1 or > 9999 ||
            intent.BaseSha256 is null || intent.BaseSha256.Length != 64 ||
            intent.ApprovedCatalogSha256 is null ||
            intent.ApprovedCatalogSha256.Length != 64)
        {
            throw new InvalidDataException("Intention RM altérée ou incohérente.");
        }
    }

    /// <summary>Returns the one canonical rules Markdown file.</summary>
    /// <param name="root">SFD project directory.</param>
    /// <returns>Full catalog path.</returns>
    private static string RulesPath(string root) =>
        Path.Combine(root, "DOCUMENTATION", "liste des règles métiers.md");

    /// <summary>Rejects journal junction and symlink ancestry.</summary>
    /// <param name="path">Journal directory.</param>
    private static void CheckDirectory(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Jonction interdite dans le journal RM.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    /// <summary>Computes exact uppercase UTF-8 SHA-256.</summary>
    /// <param name="content">Reviewed snapshot.</param>
    /// <returns>SHA-256 digest.</returns>
    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
