using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Sfd;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only comparison of untrusted SFD JSON against project-local GUIDs.
/// No title matching, directory creation, importing, automatic approvals or
/// writes. Source hashes remain valid only for the specific review snapshot.
/// </summary>
public sealed class SfdImportReviewService
{
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdStructuredAuthoringService _rules;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Scopes all lookups to one Nodalis workspace.</summary>
    /// <param name="workspaceRoot">Selected workspace root.</param>
    public SfdImportReviewService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _rules = new SfdStructuredAuthoringService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Parses and compares 1..100 external proposals without modifying the
    /// workspace. Existing documents are resolved solely by their SFD GUID.
    /// </summary>
    /// <param name="projectDirectory">Selected project SFD.</param>
    /// <param name="rawJson">User-supplied external response, held in memory only.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Frozen, unapproved proposal review with content revisions.</returns>
    public async Task<SfdImportReviewSnapshot> ReviewAsync(
        string projectDirectory, string rawJson,
        CancellationToken cancellationToken = default)
    {
        SfdImportEnvelope envelope = SfdImportContract.Parse(rawJson);
        SfdDocumentCatalog catalog = await _documents.ListAsync(projectDirectory,
            cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Identités SFD ambiguës : corrigez le projet avant de comparer.");
        }

        Dictionary<Guid, SfdDocumentEntry> byId =
            catalog.Entries.ToDictionary(entry => entry.Card.Id);
        SfdBusinessRuleSnapshot rules = await _rules.ReadRulesAsync(
            projectDirectory, cancellationToken);
        Dictionary<Guid, SfdBusinessRule> byRule =
            rules.Rules.ToDictionary(rule => rule.Id);
        Dictionary<string, SfdImportProposal> byKey =
            envelope.Proposals.ToDictionary(item => item.ClientKey,
                StringComparer.Ordinal);
        List<SfdImportReviewItem> items = [];
        HashSet<Guid> updated = [];
        foreach (SfdImportProposal proposal in envelope.Proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SfdImportReviewStatus status = SfdImportReviewStatus.NewCandidate;
            string reason = "Création proposée ; aucune écriture effectuée.";
            SfdDocumentEntry? current = null;
            SfdBusinessRule? currentRule = null;
            string? revision = null;
            SfdDocumentKind? role = KindFor(proposal.Kind);

            if (proposal.Kind == "implementationRequirement")
            {
                status = SfdImportReviewStatus.Deferred;
                reason = "EMO : emplacement documentaire canonique non encore arbitré. " +
                    "Aucune création autorisée.";
            }
            else if (proposal.Kind == "businessRule" &&
                proposal.Operation is "update" or "link")
            {
                if (proposal.TargetId is not Guid ruleId ||
                    !byRule.TryGetValue(ruleId, out currentRule))
                {
                    status = SfdImportReviewStatus.Rejected;
                    reason = "GUID RM absent du catalogue canonique du projet.";
                }
                else if (!updated.Add(currentRule.Id))
                {
                    status = SfdImportReviewStatus.Rejected;
                    reason = "La même RM est proposée plusieurs fois dans le lot.";
                }
                else
                {
                    revision = rules.Sha256;
                    status = proposal.Operation == "link"
                        ? SfdImportReviewStatus.LinkCandidate
                        : SfdImportReviewStatus.UpdateCandidate;
                    reason = "RM existante reconnue par GUID ; catalogue SHA capturé. " +
                        "Toute modification ou liaison attend la revue humaine.";
                }
            }
            else if (proposal.Operation is "update" or "link")
            {
                if (proposal.TargetId is not Guid target ||
                    !byId.TryGetValue(target, out current))
                {
                    status = SfdImportReviewStatus.Rejected;
                    reason = "GUID cible absent de ce projet ; rapprochement par titre interdit.";
                }
                else if (role is null || current.Card.Kind != role)
                {
                    status = SfdImportReviewStatus.Rejected;
                    reason = "Le GUID cible ne correspond pas au type métier proposé.";
                }
                else if (!updated.Add(current.Card.Id))
                {
                    status = SfdImportReviewStatus.Rejected;
                    reason = "Plusieurs propositions modifient le même GUID ; arbitrage requis.";
                }
                else
                {
                    _sources.GetReferenceForExistingFile(current.AbsolutePath);
                    SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
                        current.AbsolutePath, cancellationToken);
                    SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(
                        source.Content);
                    if (card.Id != current.Card.Id ||
                        card.ProjectId != catalog.ProjectId)
                    {
                        throw new InvalidDataException(
                            "Identité de fichier SFD modifiée pendant la comparaison.");
                    }

                    revision = source.Sha256;
                    status = proposal.Operation == "link"
                        ? SfdImportReviewStatus.LinkCandidate
                        : SfdImportReviewStatus.UpdateCandidate;
                    reason = proposal.Operation == "link"
                        ? "Liaison proposée ; aucune association enregistrée."
                        : "Modification proposée ; base SHA capturée pour revue.";
                }
            }
            else if (role is null)
            {
                if (rules.Rules.Any(rule => string.Equals(rule.Statement,
                    proposal.Title, StringComparison.OrdinalIgnoreCase)))
                {
                    status = SfdImportReviewStatus.NeedsResolution;
                    reason = "Une RM de même libellé existe : confirmer " +
                        "explicitement la réutilisation de son GUID.";
                }
                else
                {
                    status = SfdImportReviewStatus.NewCandidate;
                    reason = "RM nouvelle proposée dans le catalogue partagé ; " +
                        "aucune duplication dans les sous-conditions.";
                }
            }
            else if (catalog.Entries.Any(item => item.Card.Kind == role.Value &&
                     string.Equals(item.Card.Title, proposal.Title,
                         StringComparison.OrdinalIgnoreCase)))
            {
                status = SfdImportReviewStatus.NeedsResolution;
                reason = "Titre similaire déjà présent ; identité à choisir " +
                    "explicitement, aucun rapprochement automatique.";
            }

            if (status is SfdImportReviewStatus.NewCandidate or
                SfdImportReviewStatus.UpdateCandidate or
                SfdImportReviewStatus.LinkCandidate)
            {
                string? issue = ResolveDependencies(proposal, envelope.Step,
                    byKey, byId, byRule);
                if (issue is not null)
                {
                    status = SfdImportReviewStatus.NeedsResolution;
                    reason = issue;
                }
                else if (Incomplete(proposal))
                {
                    status = SfdImportReviewStatus.IncompleteDraft;
                    reason = "Brouillon incomplet : cardinalités non satisfaites. " +
                        "Ne compléter aucun C, SC, RM ou lien de façon fictive.";
                }
            }

            // For ACT publication, record the EXACT PM identity and byte
            // revision from the visible review, not just a title or key.
            Guid? parentId = null;
            string? parentSha = null;
            if (proposal.Kind == "businessActivity" &&
                proposal.Operation == "create" &&
                proposal.ParentKey is string parentKey)
            {
                SfdDocumentEntry? parent = null;
                if (Guid.TryParseExact(parentKey, "D", out Guid existingGuid) &&
                    byId.TryGetValue(existingGuid, out SfdDocumentEntry? byGuid) &&
                    byGuid.Card.Kind == SfdDocumentKind.Process)
                {
                    parent = byGuid;
                }
                else if (byKey.TryGetValue(parentKey, out SfdImportProposal? proposedPm) &&
                    proposedPm.Kind == "businessProcess" &&
                    proposedPm.Operation == "create")
                {
                    parent = await new SfdApprovedProcessImportService(
                        _sources.WorkspaceRoot).FindPublishedAsync(
                        projectDirectory, rawJson, parentKey, cancellationToken);
                }

                if (parent is not null)
                {
                    _sources.GetReferenceForExistingFile(parent.AbsolutePath);
                    SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
                        parent.AbsolutePath, cancellationToken);
                    SfdDocumentCard parentCard =
                        SfdDocumentMarkdownCodec.Parse(source.Content);
                    if (parentCard.Id != parent.Card.Id ||
                        parentCard.ProjectId != catalog.ProjectId)
                    {
                        throw new InvalidDataException(
                            "Parent PM modifié pendant la préparation de l'ACT.");
                    }

                    parentId = parent.Card.Id;
                    parentSha = source.Sha256;
                }
            }

            if (proposal.Kind is ("userRequirement" or "systemRequirement" or
                    "regulatoryRequirement") &&
                proposal.Operation == "create" &&
                proposal.ParentKey is string activityToken &&
                Guid.TryParseExact(activityToken, "D", out Guid activityId) &&
                byId.TryGetValue(activityId, out SfdDocumentEntry? activity) &&
                activity.Card.Kind == SfdDocumentKind.Activity)
            {
                _sources.GetReferenceForExistingFile(activity.AbsolutePath);
                SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
                    activity.AbsolutePath, cancellationToken);
                SfdDocumentCard verified =
                    SfdDocumentMarkdownCodec.Parse(source.Content);
                if (verified.Id != activity.Card.Id ||
                    verified.ProjectId != catalog.ProjectId)
                {
                    throw new InvalidDataException(
                        "ACT modifiée pendant la comparaison des exigences.");
                }

                parentId = activityId;
                parentSha = source.Sha256;
            }

            if (proposal.SourceKeys.Count > 0)
            {
                reason += " Références de sources externes à vérifier manuellement " +
                    "contre les empreintes du prompt initial.";
            }

            items.Add(new SfdImportReviewItem
            {
                ClientKey = proposal.ClientKey,
                Kind = proposal.Kind,
                Operation = proposal.Operation,
                Title = proposal.Title,
                ProposedDescription = proposal.Description,
                ProposedParentKey = proposal.ParentKey,
                ProposedUiMode = proposal.UiMode,
                SourceKeys = proposal.SourceKeys.ToArray(),
                ActivityKeys = proposal.ActivityKeys.ToArray(),
                RequirementKeys = proposal.RequirementKeys.ToArray(),
                ConditionCount = proposal.Conditions.Count,
                SubconditionCount = proposal.Conditions.Sum(
                    condition => condition.Subconditions.Count),
                RuleLinkCount = proposal.Conditions.Sum(condition =>
                    condition.Subconditions.Sum(subcondition =>
                        subcondition.BusinessRuleKeys.Count)),
                Status = status,
                ExistingId = current?.Card.Id ?? currentRule?.Id,
                ExistingReference = current?.Card.Reference ?? currentRule?.Reference,
                ExistingTitle = current?.Card.Title ?? currentRule?.Statement,
                ExistingSha256 = revision,
                ResolvedParentId = parentId,
                ResolvedParentSha256 = parentSha,
                Message = reason,
                NeedsHumanReview = true
            });
        }

        string rawHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(rawJson)));
        string fingerprintJson = JsonSerializer.Serialize(new
        {
            project = catalog.ProjectId,
            rawHash,
            envelopes = new { envelope.Step, envelope.RunId },
            rows = items.Select(item => new
            {
                item.ClientKey, item.Kind, item.Operation, item.Status,
                item.ExistingId, item.ExistingSha256,
                item.ResolvedParentId, item.ResolvedParentSha256, item.Title
            })
        });
        string fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(fingerprintJson)));
        return new SfdImportReviewSnapshot
        {
            ProjectId = catalog.ProjectId,
            ResponseSha256 = rawHash,
            PlanSha256 = fingerprint,
            Step = envelope.Step,
            RunId = envelope.RunId,
            Items = items
        };
    }

    /// <summary>Maps one external canonical role without creating alternative EMO kinds.</summary>
    /// <param name="kind">Exact external JSON role.</param>
    /// <returns>Physical SFD document kind or null for RM/EMO.</returns>
    private static SfdDocumentKind? KindFor(string kind) => kind switch
    {
        "businessProcess" => SfdDocumentKind.Process,
        "businessActivity" => SfdDocumentKind.Activity,
        "userRequirement" => SfdDocumentKind.UserRequirement,
        "systemRequirement" => SfdDocumentKind.SystemRequirement,
        "regulatoryRequirement" => SfdDocumentKind.RegulatoryRequirement,
        "feature" => SfdDocumentKind.Functionality,
        _ => null
    };

    /// <summary>
    /// Resolves only real existing GUIDs or matching client keys from the
    /// current response; no title/path-based parent or relationship inference.
    /// </summary>
    /// <param name="proposal">Potential new or changed item.</param>
    /// <param name="step">Owning SFD generation stage.</param>
    /// <param name="byKey">Proposals from the same external response.</param>
    /// <param name="byId">Verified current project documents.</param>
    /// <param name="byRule">Existing canonical RM entries.</param>
    /// <returns>Blocking explanation, or null when structurally resolvable.</returns>
    private static string? ResolveDependencies(SfdImportProposal proposal,
        string step,
        IReadOnlyDictionary<string, SfdImportProposal> byKey,
        IReadOnlyDictionary<Guid, SfdDocumentEntry> byId,
        IReadOnlyDictionary<Guid, SfdBusinessRule> byRule)
    {
        string? parentKind = proposal.Kind switch
        {
            "businessActivity" => "businessProcess",
            "userRequirement" or "systemRequirement" or
                "regulatoryRequirement" => "businessActivity",
            _ => null
        };
        if (parentKind is not null &&
            !MatchesReference(proposal.ParentKey, parentKind, byKey, byId))
        {
            return "Parent métier non résolu par GUID existant ou clé du lot.";
        }

        if (parentKind is null && proposal.ParentKey is not null &&
            proposal.Kind != "implementationRequirement")
        {
            return "Parent non autorisé pour ce type d'objet métier.";
        }

        if (step == "sfd-03" && proposal.Kind == "feature" &&
            proposal.ActivityKeys.Any(key =>
                !MatchesReference(key, "businessActivity", byKey, byId)))
        {
            return "Une ou plusieurs activités ACT liées sont introuvables.";
        }

        if (proposal.RequirementKeys.Any(key =>
            !new[] { "userRequirement", "systemRequirement",
                "regulatoryRequirement" }.Any(role =>
                MatchesReference(key, role, byKey, byId))))
        {
            return "Exigence source non résolue par GUID canonique.";
        }

        foreach (string key in proposal.Conditions
            .SelectMany(condition => condition.Subconditions)
            .SelectMany(subcondition => subcondition.BusinessRuleKeys))
        {
            if (Guid.TryParseExact(key, "D", out Guid existingRule) &&
                byRule.ContainsKey(existingRule))
            {
                continue;
            }

            if (byKey.TryGetValue(key, out SfdImportProposal? sameRun) &&
                sameRun.Kind == "businessRule")
            {
                return "RM proposée dans ce lot mais non encore présente dans " +
                    "le catalogue canonique : publier/résoudre la RM avant l'exigence.";
            }

            return "Référence RM absente du catalogue canonique.";
        }

        return null;
    }

    /// <summary>Matches one exact existing SFD GUID or a current-batch client key.</summary>
    /// <param name="token">Temporary client key or canonical GUID.</param>
    /// <param name="externalKind">Required external role.</param>
    /// <param name="byKey">Same-response proposal map.</param>
    /// <param name="byId">Current canonical SFD documents.</param>
    /// <returns>Whether the reference has the required actual or proposed kind.</returns>
    private static bool MatchesReference(string? token, string externalKind,
        IReadOnlyDictionary<string, SfdImportProposal> byKey,
        IReadOnlyDictionary<Guid, SfdDocumentEntry> byId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (byKey.TryGetValue(token, out SfdImportProposal? proposed))
        {
            return proposed.Kind == externalKind &&
                proposed.Operation is "create" or "update";
        }

        if (Guid.TryParseExact(token, "D", out Guid existing) &&
            byId.TryGetValue(existing, out SfdDocumentEntry? entry))
        {
            return KindFor(externalKind) == entry.Card.Kind;
        }

        return false;
    }

    /// <summary>
    /// Flags the confirmed SFD completion cardinalities as draft gaps,
    /// without treating empty C/SC/RM as structurally invalid.
    /// </summary>
    /// <param name="proposal">Explicit external proposal.</param>
    /// <returns>Whether human completion is still required.</returns>
    private static bool Incomplete(SfdImportProposal proposal)
    {
        if (proposal.Kind == "feature" && proposal.ActivityKeys.Count == 0 ||
            proposal.Kind == "regulatoryRequirement" &&
                string.IsNullOrWhiteSpace(proposal.RegulatorySource))
        {
            return true;
        }

        bool requirements = proposal.Kind is "userRequirement" or
            "systemRequirement" or "regulatoryRequirement" or
            "implementationRequirement";
        if (!requirements)
        {
            return false;
        }

        return proposal.Conditions.Count == 0 ||
            proposal.Conditions.Any(condition =>
                condition.Subconditions.Count == 0 ||
                condition.Subconditions.Any(subcondition =>
                    subcondition.BusinessRuleKeys.Count == 0));
    }
}
