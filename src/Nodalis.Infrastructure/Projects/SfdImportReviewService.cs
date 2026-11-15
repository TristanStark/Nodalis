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
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Scopes all lookups to one Nodalis workspace.</summary>
    /// <param name="workspaceRoot">Selected workspace root.</param>
    public SfdImportReviewService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
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
            string? revision = null;
            SfdDocumentKind? role = KindFor(proposal.Kind);

            if (proposal.Kind == "implementationRequirement")
            {
                status = SfdImportReviewStatus.Deferred;
                reason = "EMO : emplacement documentaire canonique non encore arbitré. " +
                    "Aucune création autorisée.";
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
                status = SfdImportReviewStatus.NewCandidate;
                reason = "RM nouvelle proposée dans le catalogue partagé ; " +
                    "aucune duplication dans les sous-conditions.";
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
                    byKey, byId);
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
                Status = status,
                ExistingId = current?.Card.Id,
                ExistingReference = current?.Card.Reference,
                ExistingTitle = current?.Card.Title,
                ExistingSha256 = revision,
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
                item.ExistingId, item.ExistingSha256, item.Title
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
    /// <returns>Blocking explanation, or null when structurally resolvable.</returns>
    private static string? ResolveDependencies(SfdImportProposal proposal,
        string step,
        IReadOnlyDictionary<string, SfdImportProposal> byKey,
        IReadOnlyDictionary<Guid, SfdDocumentEntry> byId)
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

        if (proposal.Conditions.SelectMany(condition => condition.Subconditions)
            .SelectMany(subcondition => subcondition.BusinessRuleKeys)
            .Any(key => !byKey.TryGetValue(key, out SfdImportProposal? sameRun) ||
                sameRun.Kind != "businessRule"))
        {
            return "Référence RM non résolue dans ce lot ; catalogue existant " +
                "à vérifier avant toute publication.";
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
        if (proposal.Kind == "feature" && proposal.ActivityKeys.Count == 0)
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
