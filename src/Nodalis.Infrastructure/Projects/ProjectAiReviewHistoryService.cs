using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One field in a reviewed response. Unapproved suggestions are kept by
/// SHA-256 only, unless the user explicitly archives the raw response.
/// </summary>
public sealed record ProjectAiAuditField
{
    /// <summary>External, non-authoritative proposal identifier.</summary>
    public required Guid ProposalId { get; init; }
    /// <summary>Original proposal kind, for context only.</summary>
    public required string Kind { get; init; }
    /// <summary>Original proposal operation, for context only.</summary>
    public required string Operation { get; init; }
    /// <summary>Untrusted proposed field name.</summary>
    public required string Field { get; init; }
    /// <summary>Pending, Review, Ignore, Accept or Edit; never AI-derived.</summary>
    public required string Decision { get; init; }
    /// <summary>Exact SHA-256 of the suggested field text.</summary>
    public required string SuggestedSha256 { get; init; }
    /// <summary>Exact SHA-256 of the suggested baseline, if supplied.</summary>
    public string? BaseSha256 { get; init; }
    /// <summary>Explicitly accepted or human-edited text, never a pending value.</summary>
    public string? ApprovedValue { get; init; }
    /// <summary>Optional human reason for that disposition.</summary>
    public string? Reason { get; init; }
    /// <summary>Actual current-project comparison classification.</summary>
    public required string MatchStatus { get; init; }
    /// <summary>Resolved existing Nodalis ID, never a new model-assigned ID.</summary>
    public Guid? ExistingRequirementId { get; init; }
    /// <summary>Existing canonical CA block GUID, when comparing a criterion.</summary>
    public Guid? ExistingCriterionId { get; init; }
    /// <summary>Canonical source hash observed at comparison.</summary>
    public string? SourceSha256 { get; init; }
}

/// <summary>
/// Immutable human-review snapshot with an optional raw-response archive.
/// This record proves a user made decisions, NOT that publication succeeded.
/// </summary>
public sealed record ProjectAiReviewAudit
{
    /// <summary>Persisted schema discriminator.</summary>
    public required string Schema { get; init; }
    /// <summary>Unique local audit identity, independent of model IDs.</summary>
    public required Guid ReviewId { get; init; }
    /// <summary>Verified owning Nodalis project.</summary>
    public required Guid ProjectId { get; init; }
    /// <summary>UTC timestamp of a direct user save/confirmed publication.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
    /// <summary>Originating response GUID, never a permission to publish.</summary>
    public required Guid ResponseId { get; init; }
    /// <summary>Exact raw-response SHA-256, with or without raw storage.</summary>
    public required string RawResponseSha256 { get; init; }
    /// <summary>External prompt tag, informational only.</summary>
    public string? PromptKey { get; init; }
    /// <summary>Whether the user affirmatively requested a raw copy.</summary>
    public required bool RawArchived { get; init; }
    /// <summary>Expected raw archive SHA, only for an affirmative opt-in.</summary>
    public string? RawArchiveSha256 { get; init; }
    /// <summary>review-only, title, create, title-batch or mixed.</summary>
    public required string Action { get; init; }
    /// <summary>Planned local journal folder; it may not exist after preflight refusal.</summary>
    public string? TransactionKey { get; init; }
    /// <summary>Number of explicitly human-disposed fields.</summary>
    public required int ReviewedFields { get; init; }
    /// <summary>Number of explicitly ignored fields.</summary>
    public required int IgnoredFields { get; init; }
    /// <summary>Number of pending or to-review fields.</summary>
    public required int PendingFields { get; init; }
    /// <summary>All proposed fields, including unreviewed or ignored ones.</summary>
    public required List<ProjectAiAuditField> Fields { get; init; }
}

/// <summary>Distinct immutable completion/denial event for one local review.</summary>
public sealed record ProjectAiReviewOutcome
{
    /// <summary>Schema discriminator.</summary>
    public required string Schema { get; init; }
    /// <summary>Review whose confirmed publication was attempted.</summary>
    public required Guid ReviewId { get; init; }
    /// <summary>Time of the recorded result, UTC.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
    /// <summary>Applied, AlreadyApplied or Failed; user-driven only.</summary>
    public required string Result { get; init; }
    /// <summary>Exact referenced import journal, not a file path from the AI.</summary>
    public required string TransactionKey { get; init; }
}

/// <summary>
/// Local, opt-in human-review history. The review itself is persisted only by
/// an explicit user save or after a separate publication confirmation.
/// Raw JSON is NEVER stored without an independent opt-in checkbox.
/// </summary>
public sealed class ProjectAiReviewHistoryService
{
    private const string RootName = ".nodalis-ai-import";
    private const string ReviewsName = "reviews";
    private const string ReviewFile = "review.json";
    private const string RawFile = "response.txt";
    private const string OutcomeFile = "outcome.json";
    private const int MaxReviewBytes = 16 * 1024 * 1024;
    private const int MaxRawCharacters = 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-constrained history service without writing.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    public ProjectAiReviewHistoryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Persists every explicit and pending field decision once, including a
    /// deterministic import journal name if publication was confirmed.
    /// A raw copy requires archiveRaw = true and a matching exact response.
    /// No business EXI or external network is ever written or accessed here.
    /// </summary>
    /// <param name="projectDirectory">Active project root.</param>
    /// <param name="preview">Exactly parsed external response.</param>
    /// <param name="reviews">Same canonical comparison used for approval.</param>
    /// <param name="decisions">Actual local, explicit human choices.</param>
    /// <param name="plan">Freshly recomputed pure review plan.</param>
    /// <param name="archiveRaw">Separate affirmative consent for raw storage.</param>
    /// <param name="rawResponse">Original source only when raw archive is opted in.</param>
    /// <param name="action">review-only/title/description/criterion-title/criterion-body/criterion-create/create/title-batch/mixed.</param>
    /// <param name="transactionKey">Local journal name for a confirmed publication.</param>
    /// <param name="token">Cooperative cancellation.</param>
    /// <returns>Immutable local audit review ID.</returns>
    public async Task<Guid> SaveAsync(
        string projectDirectory, ProjectAiResponsePreview preview,
        IReadOnlyList<ProjectAiRequirementReview> reviews,
        IReadOnlyList<ProjectAiFieldDecision> decisions,
        ProjectAiPreparedReviewPlan plan, bool archiveRaw,
        string? rawResponse, string action, string? transactionKey,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(plan);
        ProjectAiPreparedReviewPlan recomputed =
            ProjectAiImportDecisionPlanner.Prepare(preview, reviews, decisions);
        if (!string.Equals(recomputed.RawResponseSha256, plan.RawResponseSha256,
                StringComparison.Ordinal) ||
            !recomputed.Changes.SequenceEqual(plan.Changes) ||
            !recomputed.Creations.SequenceEqual(plan.Creations) ||
            !recomputed.Descriptions.SequenceEqual(plan.Descriptions) ||
            !recomputed.CriterionTitles.SequenceEqual(plan.CriterionTitles) ||
            !recomputed.CriterionBodies.SequenceEqual(plan.CriterionBodies) ||
            !recomputed.CriterionCreations.SequenceEqual(plan.CriterionCreations) ||
            recomputed.ReviewedFields != plan.ReviewedFields ||
            recomputed.IgnoredFields != plan.IgnoredFields ||
            recomputed.PendingFields != plan.PendingFields ||
            !AllowedAction(action, plan, transactionKey))
        {
            throw new InvalidDataException(
                "Revue, réponse et cible de publication incohérentes.");
        }

        string? rawHash = null;
        if (archiveRaw)
        {
            if (rawResponse is null ||
                rawResponse.Length is < 2 or > MaxRawCharacters ||
                ProjectAiResponseImport.Parse(rawResponse).RawSha256 !=
                    preview.RawSha256)
            {
                throw new InvalidDataException(
                    "Réponse brute différente : archivage refusé.");
            }

            rawHash = Hash(rawResponse);
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        Guid projectId = await VerifyProjectAsync(project, token);
        if (reviews.Count != preview.Document.Proposals.Count ||
            reviews.Any(item => item.ProjectId != projectId ||
                !preview.Document.Proposals.Any(proposal =>
                    ReferenceEquals(proposal, item.Candidate))) ||
            plan.Changes.Any(item => item.ProjectId != projectId) ||
            plan.Creations.Any(item => item.ProjectId != projectId) ||
            plan.Descriptions.Any(item => item.ProjectId != projectId) ||
            plan.CriterionTitles.Any(item => item.ProjectId != projectId) ||
            plan.CriterionBodies.Any(item => item.ProjectId != projectId) ||
            plan.CriterionCreations.Any(item => item.ProjectId != projectId))
        {
            throw new InvalidDataException(
                "L'historique ne peut concerner un autre projet ou une autre réponse.");
        }

        Dictionary<(Guid, string), ProjectAiFieldDecision> indexed = decisions
            .ToDictionary(item => (item.ProposalId, item.Field));
        Dictionary<Guid, ProjectAiRequirementReview> comparisons =
            reviews.ToDictionary(item => item.Candidate.ProposalId);
        List<ProjectAiAuditField> fields = [];
        foreach (ProjectAiImportCandidate proposal in preview.Document.Proposals)
        {
            ProjectAiRequirementReview review = comparisons[proposal.ProposalId];
            foreach (KeyValuePair<string, string> item in proposal.ProposedFields
                .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                indexed.TryGetValue((proposal.ProposalId, item.Key),
                    out ProjectAiFieldDecision? decision);
                fields.Add(new ProjectAiAuditField
                {
                    ProposalId = proposal.ProposalId,
                    Kind = proposal.Kind,
                    Operation = proposal.Operation,
                    Field = item.Key,
                    Decision = decision?.Kind.ToString() ?? "Pending",
                    SuggestedSha256 = Hash(item.Value),
                    BaseSha256 = proposal.BaseFields.TryGetValue(
                        item.Key, out string? baseline)
                        ? Hash(baseline)
                        : null,
                    ApprovedValue = decision?.Kind switch
                    {
                        ProjectAiFieldDecisionKind.Accept => item.Value,
                        ProjectAiFieldDecisionKind.Edit => decision.EditedValue,
                        _ => null
                    },
                    Reason = decision?.Reason,
                    MatchStatus = review.Status.ToString(),
                    ExistingRequirementId = review.Requirement?.Card.Id,
                    ExistingCriterionId = review.Criterion?.Id,
                    SourceSha256 = review.Requirement?.SourceSha256
                });
            }
        }

        if (fields.Count > 3000)
        {
            throw new InvalidDataException(
                "Historique de revue trop volumineux.");
        }

        Guid reviewId = Guid.NewGuid();
        ProjectAiReviewAudit audit = new()
        {
            Schema = "nodalis.ai-review-audit.v1",
            ReviewId = reviewId,
            ProjectId = projectId,
            RecordedUtc = DateTimeOffset.UtcNow,
            ResponseId = preview.Document.ResponseId,
            RawResponseSha256 = preview.RawSha256,
            PromptKey = preview.Document.PromptKey,
            RawArchived = archiveRaw,
            RawArchiveSha256 = rawHash,
            Action = action,
            TransactionKey = transactionKey,
            ReviewedFields = plan.ReviewedFields,
            IgnoredFields = plan.IgnoredFields,
            PendingFields = plan.PendingFields,
            Fields = fields
        };
        string json = JsonSerializer.Serialize(audit,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (Encoding.UTF8.GetByteCount(json) > MaxReviewBytes)
        {
            throw new InvalidDataException(
                "Historique trop volumineux : enregistrement refusé.");
        }

        // Do not overwrite an import or audit folder. The same lock serializes
        // all local AI-related transactions and their provenance writes.
        string root = Path.Combine(project, RootName);
        Directory.CreateDirectory(root);
        RequireDirectory(root);
        string lockPath = Path.Combine(root, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou de l'historique non sûr.");
        }

        token.ThrowIfCancellationRequested();
        string reviewsRoot = Path.Combine(root, ReviewsName);
        Directory.CreateDirectory(reviewsRoot);
        RequireDirectory(reviewsRoot);
        string directory = Path.Combine(reviewsRoot, reviewId.ToString("N"));
        Directory.CreateDirectory(directory);
        RequireDirectory(directory);
        if (archiveRaw)
        {
            // Store exact pasted characters, including any code fence.
            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(directory, RawFile), rawResponse!, token);
        }

        // The metadata is written LAST: incomplete staging is not listed.
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, ReviewFile), json, token);
        return reviewId;
    }

    /// <summary>
    /// Records the result of a previously confirmed publication, but does not
    /// invent success when an import was interrupted between two writes.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="reviewId">Persisted audit identity.</param>
    /// <param name="result">Applied, AlreadyApplied, or Failed.</param>
    /// <param name="token">Cancellation.</param>
    public async Task RecordOutcomeAsync(
        string projectDirectory, Guid reviewId, string result,
        CancellationToken token = default)
    {
        if (result is not ("Applied" or "AlreadyApplied" or "Created" or
                "AlreadyCreated" or "Failed"))
        {
            throw new InvalidDataException("Résultat de publication inconnu.");
        }

        ProjectAiReviewAudit audit = await ReadAsync(
            projectDirectory, reviewId, token);
        if (audit.Action == "review-only" ||
            string.IsNullOrWhiteSpace(audit.TransactionKey))
        {
            throw new InvalidDataException("Aucune publication associée à cette revue.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string directory = AuditDirectory(project, reviewId);
        RequireDirectory(directory);
        ProjectAiReviewOutcome outcome = new()
        {
            Schema = "nodalis.ai-review-outcome.v1",
            ReviewId = reviewId,
            RecordedUtc = DateTimeOffset.UtcNow,
            Result = result,
            TransactionKey = audit.TransactionKey
        };
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, OutcomeFile),
            JsonSerializer.Serialize(outcome,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), token);
    }

    /// <summary>
    /// Reads the immutable publication outcome, if recorded, without inferring
    /// success from the existence of a review or an import journal.
    /// </summary>
    /// <param name="projectDirectory">Owning project directory.</param>
    /// <param name="reviewId">Persisted review identity.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>The independently recorded outcome, or null.</returns>
    public async Task<ProjectAiReviewOutcome?> ReadOutcomeAsync(
        string projectDirectory, Guid reviewId,
        CancellationToken token = default)
    {
        ProjectAiReviewAudit audit = await ReadAsync(
            projectDirectory, reviewId, token);
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string path = Path.Combine(
            AuditDirectory(project, reviewId), OutcomeFile);
        if (!File.Exists(path))
        {
            return null;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 4096)
        {
            throw new InvalidDataException("Résultat de revue non sûr.");
        }

        ProjectAiReviewOutcome? outcome;
        try
        {
            outcome = JsonSerializer.Deserialize<ProjectAiReviewOutcome>(
                await File.ReadAllTextAsync(path, token),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Résultat d'import archivé malformé.", exception);
        }

        if (outcome is null ||
            outcome.Schema != "nodalis.ai-review-outcome.v1" ||
            outcome.ReviewId != reviewId ||
            outcome.TransactionKey != audit.TransactionKey ||
            outcome.Result is not ("Applied" or "AlreadyApplied" or "Created" or
                "AlreadyCreated" or "Failed"))
        {
            throw new InvalidDataException(
                "Résultat d'import incohérent avec la revue archivée.");
        }

        return outcome;
    }

    /// <summary>
    /// Lists only complete, project-owned metadata (never raw response files).
    /// Corrupt/unreadable entries are left untouched and excluded.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Latest 100 review events, newest first.</returns>
    public async Task<IReadOnlyList<ProjectAiReviewAudit>> ListAsync(
        string projectDirectory, CancellationToken token = default)
    {
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        Guid owner = await VerifyProjectAsync(project, token);
        string reviewsRoot = Path.Combine(project, RootName, ReviewsName);
        if (!Directory.Exists(reviewsRoot))
        {
            return [];
        }

        RequireDirectory(Path.Combine(project, RootName));
        RequireDirectory(reviewsRoot);
        string[] directories = Directory.GetDirectories(reviewsRoot, "*",
            SearchOption.TopDirectoryOnly);
        if (directories.Length > 1000)
        {
            throw new InvalidDataException(
                "Trop de revues archivées : export ou nettoyage manuel requis.");
        }

        List<ProjectAiReviewAudit> result = [];
        foreach (string directory in directories)
        {
            token.ThrowIfCancellationRequested();
            string folder = Path.GetFileName(directory);
            if (!Guid.TryParseExact(folder, "N", out Guid reviewId))
            {
                continue;
            }

            try
            {
                ProjectAiReviewAudit audit = await ReadAsync(
                    project, reviewId, token);
                if (audit.ProjectId == owner)
                {
                    result.Add(audit);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or
                IOException or UnauthorizedAccessException or JsonException)
            {
                // A partial/corrupt archived review requires manual inspection,
                // not a background repair or a destructive sweep.
            }
        }

        return result.OrderByDescending(item => item.RecordedUtc)
            .Take(100).ToArray();
    }

    /// <summary>
    /// Reads one complete metadata snapshot, optionally verifying its retained
    /// raw archive SHA, without returning raw text to an implicit caller.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="reviewId">Exact local review ID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified metadata and field dispositions.</returns>
    public async Task<ProjectAiReviewAudit> ReadAsync(
        string projectDirectory, Guid reviewId,
        CancellationToken token = default)
    {
        if (reviewId == Guid.Empty)
        {
            throw new InvalidDataException("Identité de revue invalide.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        Guid owner = await VerifyProjectAsync(project, token);
        RequireDirectory(Path.Combine(project, RootName));
        RequireDirectory(Path.Combine(project, RootName, ReviewsName));
        string directory = AuditDirectory(project, reviewId);
        RequireDirectory(directory);
        string path = Path.Combine(directory, ReviewFile);
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > MaxReviewBytes)
        {
            throw new InvalidDataException(
                "Revue incomplète, absente ou détournée par un lien.");
        }

        ProjectAiReviewAudit? audit;
        try
        {
            audit = JsonSerializer.Deserialize<ProjectAiReviewAudit>(
                await File.ReadAllTextAsync(path, token),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Revue archivée illisible : aucune récupération automatique.",
                exception);
        }

        if (audit is null || audit.Schema != "nodalis.ai-review-audit.v1" ||
            audit.ReviewId != reviewId || audit.ProjectId != owner ||
            audit.RawResponseSha256 is not { Length: 64 } ||
            !audit.RawResponseSha256.All(Uri.IsHexDigit) ||
            audit.Fields is null ||
            audit.Fields.Count > 3000 ||
            audit.Fields.Any(field => field is null) ||
            audit.RawArchived != (audit.RawArchiveSha256 is not null))
        {
            throw new InvalidDataException(
                "Historique non conforme au projet et à sa réponse d'origine.");
        }

        string rawPath = Path.Combine(directory, RawFile);
        if (audit.RawArchived)
        {
            if (!File.Exists(rawPath) ||
                (File.GetAttributes(rawPath) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(rawPath).Length > 4 * MaxRawCharacters ||
                Hash(await File.ReadAllTextAsync(rawPath, token)) !=
                    audit.RawArchiveSha256)
            {
                throw new InvalidDataException(
                    "Copie brute archivée manquante ou altérée.");
            }
        }
        else if (File.Exists(rawPath))
        {
            // A metadata-only audit may not claim it has no raw file while
            // silently retaining one in the same directory.
            throw new InvalidDataException(
                "Réponse brute inattendue dans une revue sans consentement.");
        }

        return audit;
    }

    /// <summary>Validates the canonical owner and refuses archived projects.</summary>
    private async Task<Guid> VerifyProjectAsync(
        string project, CancellationToken token)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || parts.Length < 4 ||
            !string.Equals(parts[0], WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !parts.Skip(2).Any(item => string.Equals(item,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Historique IA interdit hors d'un projet actif du workspace.");
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
                "Projet archivé ou sans identité : archivage de revue refusé.");
        }

        return manifest.Id;
    }

    /// <summary>Restricts action labels to canonical, deterministic local journal names.</summary>
    private static bool AllowedAction(string action,
        ProjectAiPreparedReviewPlan plan, string? key)
    {
        if (action == "review-only")
        {
            return key is null;
        }

        if (string.IsNullOrWhiteSpace(key) ||
            key.Length > 72 ||
            key.Contains('/') || key.Contains('\\') ||
            key.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return action switch
        {
            "criterion-body" => plan.CriterionBodies.Count == 1 &&
                plan.Changes.Count == 0 && plan.Creations.Count == 0 &&
                plan.Descriptions.Count == 0 && plan.CriterionTitles.Count == 0 &&
                plan.CriterionCreations.Count == 0 &&
                key == "criterion-body-" +
                    plan.CriterionBodies[0].ProposalId.ToString("N"),
            "criterion-create" => plan.CriterionCreations.Count == 1 &&
                plan.Changes.Count == 0 && plan.Creations.Count == 0 &&
                plan.Descriptions.Count == 0 && plan.CriterionTitles.Count == 0 &&
                plan.CriterionBodies.Count == 0 &&
                key == "criterion-create-" +
                    plan.CriterionCreations[0].ProposalId.ToString("N"),
            "title" => plan.Changes.Count == 1 &&
                plan.Creations.Count == 0 && plan.Descriptions.Count == 0 &&
                plan.CriterionTitles.Count == 0 &&
                plan.CriterionCreations.Count == 0 &&
                plan.CriterionBodies.Count == 0 &&
                key == plan.Changes[0].ProposalId.ToString("N"),
            "description" => plan.Descriptions.Count == 1 &&
                plan.Changes.Count == 0 && plan.Creations.Count == 0 &&
                plan.CriterionTitles.Count == 0 &&
                plan.CriterionCreations.Count == 0 &&
                plan.CriterionBodies.Count == 0 &&
                key == "description-" +
                    plan.Descriptions[0].ProposalId.ToString("N"),
            "criterion-title" => plan.CriterionTitles.Count == 1 &&
                plan.Changes.Count == 0 && plan.Descriptions.Count == 0 &&
                plan.Creations.Count == 0 && plan.CriterionCreations.Count == 0 &&
                key == "criterion-" +
                    plan.CriterionTitles[0].ProposalId.ToString("N"),
            "create" => plan.Changes.Count == 0 &&
                plan.Creations.Count == 1 && plan.Descriptions.Count == 0 &&
                plan.CriterionTitles.Count == 0 &&
                plan.CriterionCreations.Count == 0 &&
                plan.CriterionBodies.Count == 0 &&
                key == plan.Creations[0].ProposalId.ToString("N"),
            "title-batch" => plan.Changes.Count is >= 2 and <= 20 &&
                plan.Creations.Count == 0 && plan.Descriptions.Count == 0 &&
                plan.CriterionTitles.Count == 0 &&
                plan.CriterionCreations.Count == 0 &&
                plan.CriterionBodies.Count == 0 &&
                key == "batch-" +
                    ProjectAiBatchTitleImportTransactionService.PlanFingerprint(plan),
            "mixed" => plan.Descriptions.Count == 0 &&
                plan.CriterionTitles.Count == 0 &&
                plan.CriterionCreations.Count == 0 &&
                plan.CriterionBodies.Count == 0 &&
                plan.Changes.Count > 0 && plan.Creations.Count > 0 &&
                plan.Changes.Count + plan.Creations.Count <= 20 &&
                key == "mixed-" +
                    ProjectAiMixedImportTransactionService.PlanFingerprint(plan),
            _ => false
        };
    }

    /// <summary>Canonical private location for a locally assigned review ID.</summary>
    private static string AuditDirectory(string project, Guid reviewId) =>
        Path.Combine(project, RootName, ReviewsName, reviewId.ToString("N"));

    /// <summary>Rejects redirected directories at every audit write/read.</summary>
    private static void RequireDirectory(string directory)
    {
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier d'historique inexistant ou sous jonction.");
        }
    }

    /// <summary>Hashes exact text bytes without sending them to a model.</summary>
    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
