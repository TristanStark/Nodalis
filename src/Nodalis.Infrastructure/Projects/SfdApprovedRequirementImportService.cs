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
/// Durable intent for ONE explicitly approved EXU/EXS/EXR document. C/SC GUIDs
/// and shared RM references are frozen before the first business-file write.
/// </summary>
public sealed record SfdApprovedRequirementIntent
{
    public const string CurrentSchema = "nodalis.sfd.approved-requirement.v1";

    public required string Schema { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ResponseSha256 { get; init; }
    public required string RunId { get; init; }
    public required string ClientKey { get; init; }
    public required Guid BusinessId { get; init; }
    public required SfdDocumentKind Kind { get; init; }
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required Guid ParentActivityId { get; init; }
    public required string ParentSourceSha256 { get; init; }
    public required string RulesSourceSha256 { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string ApprovedMarkdown { get; init; }
    public required string ApprovedSha256 { get; init; }
}

/// <summary>One explicitly published requirement, or an exact safe replay.</summary>
/// <param name="Id">Nodalis stable requirement GUID.</param>
/// <param name="Kind">EXU/EXS/EXR type.</param>
/// <param name="Reference">Visible reference.</param>
/// <param name="AbsolutePath">Canonical requirement document.</param>
/// <param name="ParentActivityId">Verified existing ACT.</param>
/// <param name="AlreadyApplied">True only for a byte-identical publication.</param>
public sealed record SfdApprovedRequirementResult(
    Guid Id, SfdDocumentKind Kind, string Reference, string AbsolutePath,
    Guid ParentActivityId, bool AlreadyApplied);

/// <summary>
/// Safe single-requirement creation from the SFD-02 external-response contract.
/// Existing RM are linked ONLY by canonical GUID; no definitions are duplicated
/// and no new C/SC/RM are invented to fill missing draft cardinalities.
/// </summary>
public sealed class SfdApprovedRequirementImportService
{
    private const string JournalRootName = ".sfd-ai-import";
    private const string IntentName = "intent-requirement.json";
    private const string StagedName = "staged-requirement.md";
    private readonly SfdImportReviewService _review;
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdStructuredAuthoringService _rules;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a project-local guarded requirement importer.</summary>
    /// <param name="workspaceRoot">Authorized Nodalis workspace.</param>
    public SfdApprovedRequirementImportService(string workspaceRoot)
    {
        _review = new SfdImportReviewService(workspaceRoot);
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _rules = new SfdStructuredAuthoringService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Publishes ONE EXU/EXS/EXR after explicit caller confirmation. A missing
    /// C, SC or RM may remain an incomplete draft; an UNKNOWN RM never may.
    /// </summary>
    /// <param name="projectDirectory">Existing, active SFD project.</param>
    /// <param name="rawJson">Exact reviewed external JSON.</param>
    /// <param name="reviewed">Frozen local comparison before user approval.</param>
    /// <param name="clientKey">One user-selected new requirement candidate.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Newly published or safely recognized requirement.</returns>
    public async Task<SfdApprovedRequirementResult> PublishAsync(
        string projectDirectory, string rawJson, SfdImportReviewSnapshot reviewed,
        string clientKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        SfdImportEnvelope response = SfdImportContract.Parse(rawJson);
        SfdImportProposal proposal = response.Proposals.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("Exigence absente de la réponse IA.");
        SfdDocumentKind kind = proposal.Kind switch
        {
            "userRequirement" => SfdDocumentKind.UserRequirement,
            "systemRequirement" => SfdDocumentKind.SystemRequirement,
            "regulatoryRequirement" => SfdDocumentKind.RegulatoryRequirement,
            _ => throw new InvalidDataException("EXU, EXS ou EXR requise.")
        };
        if (response.Step != "sfd-02" || proposal.Operation != "create" ||
            proposal.TargetId is not null ||
            proposal.ParentKey is null ||
            proposal.ActivityKeys.Count > 0 ||
            proposal.RequirementKeys.Count > 0 ||
            proposal.Conditions.Sum(item => item.Subconditions.Count) > 1800)
        {
            throw new InvalidDataException(
                "Publication limitée à une nouvelle EXU, EXS ou EXR sous ACT.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        ProjectManifest manifest = await ReadActiveProjectAsync(root,
            cancellationToken);
        SfdImportReviewItem row = reviewed.Items.SingleOrDefault(item =>
            item.ClientKey == clientKey) ??
            throw new InvalidDataException("Exigence absente du plan examiné.");
        if (reviewed.ProjectId != manifest.Id ||
            reviewed.ResponseSha256 != Hash(rawJson) ||
            reviewed.Step != response.Step || reviewed.RunId != response.RunId ||
            row.Kind != proposal.Kind || row.Operation != "create" ||
            row.Status is not (SfdImportReviewStatus.NewCandidate or
                SfdImportReviewStatus.IncompleteDraft))
        {
            throw new InvalidDataException(
                "La proposition d'exigence n'est plus celle examinée.");
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

        await using FileStream lease = new(lockPath,
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            4096, FileOptions.Asynchronous);
        string journalKey = Hash(manifest.Id.ToString("D") + "\n" +
            reviewed.ResponseSha256 + "\n" + clientKey);
        string journal = Path.Combine(journalRoot, journalKey);
        Directory.CreateDirectory(journal);
        RequireNormalDirectory(journal);
        string intentPath = Path.Combine(journal, IntentName);
        SfdApprovedRequirementIntent intent;
        if (File.Exists(intentPath))
        {
            _sources.GetReferenceForExistingFile(intentPath);
            try
            {
                intent = await AtomicJsonFile.ReadAsync<SfdApprovedRequirementIntent>(
                    intentPath, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Journal d'exigence malformé : reprise refusée.", exception);
            }

            VerifyIntent(intent, manifest.Id, reviewed.ResponseSha256,
                response.RunId, clientKey, kind);
        }
        else
        {
            SfdImportReviewSnapshot latest = await _review.ReviewAsync(
                root, rawJson, cancellationToken);
            SfdImportReviewItem? verified = latest.Items.SingleOrDefault(item =>
                item.ClientKey == clientKey);
            if (latest.PlanSha256 != reviewed.PlanSha256 ||
                verified?.Status is not (SfdImportReviewStatus.NewCandidate or
                    SfdImportReviewStatus.IncompleteDraft) ||
                verified.ResolvedParentId is not Guid activityId ||
                verified.ResolvedParentSha256 is not string activitySha ||
                row.ResolvedParentId != activityId ||
                row.ResolvedParentSha256 != activitySha)
            {
                throw new InvalidDataException(
                    "ACT absente, modifiée ou comparaison périmée : revoir l'exigence.");
            }

            SfdBusinessRuleSnapshot rules = await _rules.ReadRulesAsync(
                root, cancellationToken);
            if (rules.Sha256 != reviewed.RulesCatalogSha256)
            {
                throw new InvalidDataException(
                    "Catalogue RM modifié depuis la revue : nouvelle analyse requise.");
            }

            HashSet<Guid> ruleIds = rules.Rules.Select(rule => rule.Id).ToHashSet();
            List<SfdConditionBlock> blocks = [];
            int number = 0;
            foreach (SfdImportCondition candidate in proposal.Conditions)
            {
                number++;
                List<SfdSubconditionBlock> subs = [];
                int subNumber = 0;
                foreach (SfdImportSubcondition sub in candidate.Subconditions)
                {
                    subNumber++;
                    List<Guid> references = [];
                    foreach (string reference in sub.BusinessRuleKeys)
                    {
                        if (!Guid.TryParseExact(reference, "D", out Guid rm) ||
                            !ruleIds.Contains(rm))
                        {
                            throw new InvalidDataException(
                                "RM pas encore publiée : seul un GUID du catalogue " +
                                "partagé peut être lié à cette SC.");
                        }

                        references.Add(rm);
                    }

                    subs.Add(new SfdSubconditionBlock
                    {
                        Id = Guid.NewGuid(),
                        Reference = "SC-" + subNumber.ToString("D4",
                            CultureInfo.InvariantCulture),
                        Statement = SfdConditionBlockCodec.RequireStatement(
                            sub.Statement),
                        BusinessRuleIds = references
                    });
                }

                blocks.Add(new SfdConditionBlock
                {
                    Id = Guid.NewGuid(),
                    Reference = "C-" + number.ToString("D4",
                        CultureInfo.InvariantCulture),
                    Statement = SfdConditionBlockCodec.RequireStatement(
                        candidate.Statement),
                    Subconditions = subs
                });
            }

            SfdDocumentCatalog catalog = await _documents.ListAsync(
                root, cancellationToken);
            if (catalog.Diagnostics.Count > 0)
            {
                throw new InvalidDataException(
                    "Catalogue SFD incohérent : publication suspendue.");
            }

            string prefix = SfdDocumentMarkdownCodec.Prefix(kind);
            int highest = catalog.Entries
                .Where(item => item.Card.Kind == kind)
                .Select(item => int.Parse(item.Card.Reference[(prefix.Length + 1)..],
                    CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0).Max();
            if (highest >= 9999)
            {
                throw new InvalidDataException("Numérotation des exigences épuisée.");
            }

            string referenceCode = prefix + "-" + (highest + 1).ToString("D4",
                CultureInfo.InvariantCulture);
            SfdDocumentCard card = new()
            {
                Id = Guid.NewGuid(),
                ProjectId = manifest.Id,
                Kind = kind,
                Title = proposal.Title,
                Reference = referenceCode,
                ParentId = activityId,
                CreatedUtc = DateTimeOffset.UtcNow,
                // An AI cannot approve a requirement, even if it declares "confirmed".
                Certainty = SfdAssertionCertainty.ToConfirm
            };
            string markdown = SfdDocumentMarkdownCodec.Format(card);
            if (!string.IsNullOrWhiteSpace(proposal.Description))
            {
                markdown += "\n## Description proposée par IA — à qualifier\n\n" +
                    EscapeUntrusted(proposal.Description) + "\n";
            }

            if (kind == SfdDocumentKind.RegulatoryRequirement &&
                !string.IsNullOrWhiteSpace(proposal.RegulatorySource))
            {
                markdown = markdown.Replace(
                    "_À confirmer : ne pas considérer une obligation comme établie sans source._",
                    "_Source réglementaire proposée, **non vérifiée** : " +
                    EscapeUntrusted(proposal.RegulatorySource) + "_",
                    StringComparison.Ordinal);
            }

            markdown = SfdConditionBlockCodec.Apply(markdown, blocks);
            // Validate written identity and structured references before staging.
            SfdDocumentCard checkCard = SfdDocumentMarkdownCodec.Parse(markdown);
            IReadOnlyList<SfdConditionBlock> checkBlocks =
                SfdConditionBlockCodec.Parse(markdown);
            if (checkCard.Id != card.Id || checkBlocks.Count != blocks.Count)
            {
                throw new InvalidDataException(
                    "Structure EXU/EXS/EXR non reproductible.");
            }

            intent = new SfdApprovedRequirementIntent
            {
                Schema = SfdApprovedRequirementIntent.CurrentSchema,
                ProjectId = manifest.Id,
                ResponseSha256 = reviewed.ResponseSha256,
                RunId = response.RunId,
                ClientKey = clientKey,
                BusinessId = card.Id,
                Kind = kind,
                Reference = referenceCode,
                Title = proposal.Title,
                ParentActivityId = activityId,
                ParentSourceSha256 = activitySha,
                RulesSourceSha256 = rules.Sha256,
                CreatedUtc = card.CreatedUtc,
                ApprovedMarkdown = markdown,
                ApprovedSha256 = Hash(markdown)
            };
            // Durable intent first, no business files have been modified yet.
            await AtomicFileWriter.WriteNewAllTextAsync(intentPath,
                JsonSerializer.Serialize(intent, JsonDefaults.Options),
                cancellationToken);
        }

        return await ApplyAsync(root, journal, intent, cancellationToken);
    }

    /// <summary>Replays one durable source without overwriting changed requirements.</summary>
    /// <param name="root">Verified SFD project directory.</param>
    /// <param name="journal">Durable staging folder.</param>
    /// <param name="intent">Frozen user-approved requirement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted document or safe identical replay.</returns>
    private async Task<SfdApprovedRequirementResult> ApplyAsync(
        string root, string journal, SfdApprovedRequirementIntent intent,
        CancellationToken cancellationToken)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            root, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Catalogue SFD incohérent pendant la publication.");
        }

        SfdDocumentEntry act = catalog.Entries.SingleOrDefault(item =>
            item.Card.Id == intent.ParentActivityId &&
            item.Card.Kind == SfdDocumentKind.Activity) ??
            throw new InvalidDataException("ACT parente introuvable ou hors projet.");
        _sources.GetReferenceForExistingFile(act.AbsolutePath);
        SfdTextRevision activity = await SfdMarkdownRevisionWriter.ReadAsync(
            act.AbsolutePath, cancellationToken);
        SfdDocumentCard currentActivity = SfdDocumentMarkdownCodec.Parse(
            activity.Content);
        if (currentActivity.Id != intent.ParentActivityId ||
            currentActivity.ProjectId != intent.ProjectId ||
            activity.Sha256 != intent.ParentSourceSha256)
        {
            throw new InvalidDataException(
                "ACT parente modifiée depuis l'approbation : reprise suspendue.");
        }

        SfdBusinessRuleSnapshot rules = await _rules.ReadRulesAsync(
            root, cancellationToken);
        if (rules.Sha256 != intent.RulesSourceSha256)
        {
            throw new InvalidDataException(
                "Catalogue RM modifié depuis l'approbation : nouvelle revue requise.");
        }

        HashSet<Guid> existingRules = rules.Rules.Select(rule => rule.Id).ToHashSet();
        IReadOnlyList<SfdConditionBlock> conditions =
            SfdConditionBlockCodec.Parse(intent.ApprovedMarkdown);
        if (conditions.SelectMany(condition => condition.Subconditions)
            .SelectMany(sub => sub.BusinessRuleIds)
            .Any(id => !existingRules.Contains(id)))
        {
            throw new InvalidDataException(
                "Référence RM devenue inconnue : aucune publication possible.");
        }

        string directoryName = intent.Kind switch
        {
            SfdDocumentKind.UserRequirement => "Exigences utilisateurs",
            SfdDocumentKind.SystemRequirement => "Exigences vis à vis du système",
            SfdDocumentKind.RegulatoryRequirement => "Exigences règlementaires",
            _ => throw new InvalidDataException("Type de fiche EXU/EXS/EXR invalide.")
        };
        string directory = Path.Combine(
            Path.GetDirectoryName(act.AbsolutePath)!, directoryName);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        RequireNormalDirectory(directory);
        string target = Path.Combine(directory,
            intent.Reference + " - " + SafeTitle(intent.Title) + ".md");
        if (File.Exists(target))
        {
            _sources.GetReferenceForExistingFile(target);
            SfdTextRevision actual = await SfdMarkdownRevisionWriter.ReadAsync(
                target, cancellationToken);
            SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(actual.Content);
            if (card.Id != intent.BusinessId ||
                card.ProjectId != intent.ProjectId ||
                card.ParentId != intent.ParentActivityId ||
                actual.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "La fiche a été modifiée après publication ou son GUID diffère.");
            }

            return new SfdApprovedRequirementResult(intent.BusinessId,
                intent.Kind, intent.Reference, target, intent.ParentActivityId, true);
        }

        if (catalog.Entries.Any(item =>
            item.Card.Id == intent.BusinessId ||
            item.Card.Reference == intent.Reference))
        {
            throw new InvalidDataException(
                "Identité ou référence d'exigence déjà utilisée ailleurs.");
        }

        string stage = Path.Combine(journal, StagedName);
        if (File.Exists(stage))
        {
            _sources.GetReferenceForExistingFile(stage);
            SfdTextRevision saved = await SfdMarkdownRevisionWriter.ReadAsync(
                stage, cancellationToken);
            if (saved.Sha256 != intent.ApprovedSha256)
            {
                throw new InvalidDataException(
                    "Snapshot de récupération EXU/EXS/EXR modifié.");
            }
        }
        else
        {
            await AtomicFileWriter.WriteNewAllTextAsync(stage,
                intent.ApprovedMarkdown, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SfdTextRevision finalParent = await SfdMarkdownRevisionWriter.ReadAsync(
            act.AbsolutePath, cancellationToken);
        if (finalParent.Sha256 != intent.ParentSourceSha256)
        {
            throw new InvalidDataException("ACT modifiée pendant le staging.");
        }

        SfdBusinessRuleSnapshot finalRules = await _rules.ReadRulesAsync(
            root, cancellationToken);
        if (finalRules.Sha256 != intent.RulesSourceSha256)
        {
            throw new InvalidDataException("RM modifiées pendant le staging.");
        }

        // Default File.Move must never overwrite a newly created target.
        File.Move(stage, target);
        return new SfdApprovedRequirementResult(intent.BusinessId,
            intent.Kind, intent.Reference, target, intent.ParentActivityId, false);
    }

    /// <summary>Verifies the selected project is an active, compatible SFD project.</summary>
    /// <param name="root">Candidate project root.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Active canonical manifest.</returns>
    private async Task<ProjectManifest> ReadActiveProjectAsync(
        string root, CancellationToken cancellationToken)
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
                "Exigence non publiable : projet SFD absent, archivé ou incompatible.");
        }

        return manifest;
    }

    /// <summary>Checks immutable intent fields and the versioned Markdown identity.</summary>
    /// <param name="intent">Recovery intent.</param>
    /// <param name="projectId">Expected project owner.</param>
    /// <param name="rawHash">Expected external response hash.</param>
    /// <param name="runId">Expected response run identifier.</param>
    /// <param name="clientKey">Expected selected proposal key.</param>
    /// <param name="kind">Expected EXU/EXS/EXR role.</param>
    private static void VerifyIntent(SfdApprovedRequirementIntent intent,
        Guid projectId, string rawHash, string runId,
        string clientKey, SfdDocumentKind kind)
    {
        string expectedPrefix = SfdDocumentMarkdownCodec.Prefix(kind) + "-";
        if (intent.Schema != SfdApprovedRequirementIntent.CurrentSchema ||
            intent.ProjectId != projectId ||
            intent.ResponseSha256 != rawHash ||
            intent.RunId != runId || intent.ClientKey != clientKey ||
            intent.Kind != kind ||
            intent.BusinessId == Guid.Empty ||
            intent.ParentActivityId == Guid.Empty ||
            intent.Reference is null ||
            !intent.Reference.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            intent.Reference.Length != expectedPrefix.Length + 4 ||
            string.IsNullOrWhiteSpace(intent.Title) || intent.Title.Length > 240 ||
            intent.ParentSourceSha256 is null || intent.ParentSourceSha256.Length != 64 ||
            intent.RulesSourceSha256 is null || intent.RulesSourceSha256.Length != 64 ||
            intent.ApprovedMarkdown is null || intent.ApprovedSha256 is null ||
            Hash(intent.ApprovedMarkdown) != intent.ApprovedSha256)
        {
            throw new InvalidDataException("Intention de publication d'exigence altérée.");
        }

        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
            intent.ApprovedMarkdown);
        if (card.Id != intent.BusinessId ||
            card.Kind != intent.Kind ||
            card.ProjectId != intent.ProjectId ||
            card.ParentId != intent.ParentActivityId ||
            card.Reference != intent.Reference ||
            card.Title != intent.Title)
        {
            throw new InvalidDataException(
                "GUID ou métadonnées de l'exigence incompatibles avec le journal.");
        }

        SfdConditionBlockCodec.Parse(intent.ApprovedMarkdown);
    }

    /// <summary>Refuses symlink and junction ancestry in SFD storage.</summary>
    /// <param name="path">Expected existing path.</param>
    private static void RequireNormalDirectory(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null && Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Lien symbolique ou jonction interdit dans la publication SFD.");
            }

            current = Path.GetDirectoryName(current);
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("Dossier d'exigences introuvable.");
        }
    }

    /// <summary>Derives a bounded portable filename suffix.</summary>
    /// <param name="title">Approved requirement title.</param>
    /// <returns>Windows-safe segment.</returns>
    private static string SafeTitle(string title)
    {
        string result = new string(title.Trim().Take(70).Select(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '-' or '_'
                ? character : '_').ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "Sans titre" : result;
    }

    /// <summary>Escapes untrusted HTML/structural marker content.</summary>
    /// <param name="value">AI-supplied reviewed description/source.</param>
    /// <returns>Text safe for insertion into Markdown body.</returns>
    private static string EscapeUntrusted(string value) =>
        value.Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal).Trim();

    /// <summary>Computes exact UTF-8 SHA256 for an immutable snapshot.</summary>
    /// <param name="content">External response or file content.</param>
    /// <returns>Uppercase hash.</returns>
    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
