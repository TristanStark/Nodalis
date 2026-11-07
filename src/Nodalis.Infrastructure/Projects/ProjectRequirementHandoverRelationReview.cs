using System.Text;
using Nodalis.Core.Links;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Read-only kind of an object requiring human handover decisions.</summary>
public enum ProjectRequirementRelationReviewKind
{
    Criterion,
    Test,
    CampaignPassage,
    Incident,
    TypedRelation,
    Diagnostic
}

/// <summary>
/// One bounded, project-scoped relationship to requalify. The action is a
/// recommendation, never a source rewrite, a test result or a decision.
/// </summary>
/// <param name="Kind">The relationship domain.</param>
/// <param name="CriterionId">Original stable CA GUID, when applicable.</param>
/// <param name="Identity">Original test, passage, incident or relation ID.</param>
/// <param name="SourcePath">Existing source location, not a target allocation.</param>
/// <param name="Description">Human-readable current context.</param>
/// <param name="Action">Required explicit future human requalification.</param>
public sealed record ProjectRequirementRelationReviewItem(
    ProjectRequirementRelationReviewKind Kind, Guid? CriterionId,
    string Identity, string SourcePath, string Description, string Action);

/// <summary>
/// SHA-bound handover assessment generated from current link and REC catalogs.
/// No automatic link rewrite or source/project mutation is authorized.
/// </summary>
/// <param name="IntentId">Reviewed human handover intent.</param>
/// <param name="SourceSha256">Exact original EXI digest.</param>
/// <param name="Method">A stable move or B new successor.</param>
/// <param name="Items">Bounded requalification checklist.</param>
/// <param name="Diagnostics">Potentially incomplete or ambiguous evidence.</param>
public sealed record ProjectRequirementRelationReview(
    Guid IntentId, string SourceSha256, ProjectHandoverMethod Method,
    IReadOnlyList<ProjectRequirementRelationReviewItem> Items,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Only an overview, never an authorization to publish.</summary>
    public bool IsComplete => Diagnostics.Count == 0;

    /// <summary>Bounded, copyable worklist for later manual qualification.</summary>
    /// <returns>Text containing exact original IDs and required actions.</returns>
    public string FormatChecklist()
    {
        StringBuilder output = new();
        output.AppendLine("REVUE DE RELATIONS — PASSATION EXI")
            .Append("Intention : ").AppendLine(IntentId.ToString("D"))
            .Append("Empreinte source : ").AppendLine(SourceSha256)
            .Append("Mode : ").AppendLine(Method ==
                ProjectHandoverMethod.MoveOriginal
                    ? "A · même GUID, ancien propriétaire retiré"
                    : "B · nouveaux GUID, aucun résultat hérité")
            .AppendLine("Lecture seule. Aucun lien, test, campagne ou incident n'est modifié.")
            .AppendLine();

        if (Diagnostics.Count > 0)
        {
            output.AppendLine("DIAGNOSTICS — vérifier avant toute décision");
            foreach (string problem in Diagnostics.Take(35))
            {
                output.Append("- ").AppendLine(problem);
            }

            if (Diagnostics.Count > 35)
            {
                output.AppendLine("… Autres diagnostics non affichés : audit incomplet.");
            }

            output.AppendLine();
        }

        foreach (ProjectRequirementRelationReviewItem item in Items)
        {
            output.Append("[ ] ").Append(item.Kind).Append(" · ")
                .Append(item.Identity).Append(" · ")
                .AppendLine(item.Description);
            if (!string.IsNullOrWhiteSpace(item.SourcePath))
            {
                output.Append("     Source : ").AppendLine(item.SourcePath);
            }

            output.Append("     À décider : ").AppendLine(item.Action);
        }

        output.AppendLine().AppendLine(
            "Les anciennes preuves et les verdicts restent historiques. " +
            "Aucune requalification ne sera validée par ce rapport.");
        return output.ToString();
    }
}

/// <summary>
/// Pure join of the existing EXI/CA→test matrix, frozen REC passages,
/// explicitly linked maintenance incidents, and other typed relations.
/// All sources must have been loaded by a user-requested refresh.
/// </summary>
public static class ProjectRequirementHandoverRelationReview
{
    private const int MaxItems = 1200;

    /// <summary>Returns an actionable, non-authorizing relationship worklist.</summary>
    /// <param name="preview">Current SHA-checked EXI intention preview.</param>
    /// <param name="requirements">Source project requirement catalog.</param>
    /// <param name="index">Explicitly refreshed workspace links.</param>
    /// <param name="campaigns">Source project validated REC catalog.</param>
    /// <returns>Read-only, source-anchored relationship audit.</returns>
    public static ProjectRequirementRelationReview Build(
        ProjectRequirementHandoverPreview preview,
        ProjectRequirementCatalog requirements, LinkIndexCatalog index,
        ProjectTestCampaignCatalog campaigns)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(campaigns);
        if (preview.Intent.Id == Guid.Empty ||
            preview.Intent.SourceObjectId == Guid.Empty ||
            preview.SourceSha256.Length != 64 ||
            preview.SourceRelativePath != preview.Intent.SourceRelativePath)
        {
            throw new InvalidDataException(
                "Revue de relations impossible : intention EXI invalide.");
        }

        List<string> diagnostics = [];
        ProjectRequirementEntry[] selected = requirements.Requirements
            .Where(item => item.Card.Id == preview.Intent.SourceObjectId &&
                item.RelativePath == preview.SourceRelativePath &&
                string.Equals(item.SourceSha256, preview.SourceSha256,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length != 1)
        {
            diagnostics.Add("Source EXI absente, modifiée ou ambiguë " +
                "depuis le préflight : relancer une nouvelle revue.");
            return new ProjectRequirementRelationReview(
                preview.Intent.Id, preview.SourceSha256,
                preview.Intent.Method, [], diagnostics);
        }

        ProjectRequirementEntry owner = selected[0];
        ProjectRequirementTraceabilityOverview coverage =
            ProjectRequirementTraceability.Build(requirements, index);
        ProjectRequirementCampaignOverview history =
            ProjectRequirementCampaignTraceability.Build(coverage, campaigns);
        diagnostics.AddRange(requirements.Diagnostics);
        diagnostics.AddRange(coverage.Diagnostics);
        diagnostics.AddRange(history.Diagnostics);
        List<ProjectRequirementRelationReviewItem> items = [];
        string destination = preview.Intent.Method ==
            ProjectHandoverMethod.MoveOriginal
                ? "Vérifier l'emplacement du même GUID et relier les tests " +
                  "du nouveau projet après contrôle explicite."
                : "Décider d'un nouveau test et le relier au nouveau CA ; " +
                  "ne pas reprendre le résultat historique.";

        HashSet<Guid> caIds = owner.Criteria.Where(c => c.Id.HasValue)
            .Select(c => c.Id!.Value).ToHashSet();
        foreach (var criterion in owner.Criteria)
        {
            string identity = owner.Card.Reference + "/" + criterion.Reference +
                " · " + (criterion.Id?.ToString("D") ?? "GUID absent");
            items.Add(new ProjectRequirementRelationReviewItem(
                ProjectRequirementRelationReviewKind.Criterion,
                criterion.Id, identity, owner.RelativePath, criterion.Title,
                destination));

            ProjectRequirementTraceabilityRow? row = coverage.Rows
                .SingleOrDefault(r => r.Requirement.RelativePath ==
                    owner.RelativePath &&
                    r.Criterion.Reference == criterion.Reference);
            if (row is null || row.Diagnostic is not null)
            {
                diagnostics.Add(identity +
                    " : identité, index ou liens de test non réconciliés.");
                continue;
            }

            foreach (ProjectRequirementTestSource test in row.Tests)
            {
                items.Add(new ProjectRequirementRelationReviewItem(
                    ProjectRequirementRelationReviewKind.Test,
                    criterion.Id, test.BlockId.ToString("D"),
                    test.RelativePath, test.HeadingTitle,
                    "Revalider la définition, le contexte et le rattachement " +
                    "à ce CA ; aucun ancien test ne devient conforme."));
                foreach (ProjectTestCampaignEntry entry in campaigns.Campaigns)
                {
                    if (entry.Campaign.ProjectId != owner.Card.ProjectId)
                    {
                        diagnostics.Add(entry.RelativePath +
                            " : campagne associée à un autre projet.");
                        continue;
                    }

                    foreach (ProjectTestPassage passage in entry.Campaign.Passages
                        .Where(p => p.Tests.Any(t => t.TestId == test.BlockId)))
                    {
                        ProjectTestResultEvent? result = passage.Results
                            .SingleOrDefault(r => r.TestId == test.BlockId);
                        string description = entry.Campaign.Reference +
                            " / passage " + passage.Number +
                            " · contexte " + passage.Environment +
                            " / " + passage.ApplicationVersion +
                            " · résultat historique " +
                            (result?.Outcome.ToString() ?? "non exécuté");
                        items.Add(new ProjectRequirementRelationReviewItem(
                            ProjectRequirementRelationReviewKind.CampaignPassage,
                            criterion.Id, passage.Id.ToString("D"),
                            entry.RelativePath, description,
                            "Conserver cet historique dans la source. " +
                            "Programmer une campagne distincte si la " +
                            "nouvelle exigence doit être testée."));
                        if (result is null)
                        {
                            continue;
                        }

                        foreach (ProjectTestLinkedIncident incident in
                            ProjectTestIncidentLinkPolicy.Active(
                                entry.Campaign, result.Id))
                        {
                            items.Add(new ProjectRequirementRelationReviewItem(
                                ProjectRequirementRelationReviewKind.Incident,
                                criterion.Id, incident.IncidentId,
                                entry.RelativePath,
                                "Incident lié au résultat " +
                                result.Id.ToString("D") + " · " +
                                entry.Campaign.Reference,
                                "Décider séparément du traitement ou du " +
                                "successeur de l'incident ; aucun transfert " +
                                "ni changement de statut automatique."));
                        }
                    }
                }
            }
        }

        HashSet<Guid> targets = [owner.Card.Id, .. caIds];
        Guid[] documentIds = index.Targets.Where(t =>
                string.Equals(t.RelativePath, owner.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id).Distinct().ToArray();
        if (documentIds.Length != 1)
        {
            diagnostics.Add("Identifiant documentaire EXI non unique ou " +
                "absent dans l'index ; liens typés à vérifier.");
        }
        else
        {
            targets.Add(documentIds[0]);
        }

        Dictionary<Guid, string[]> paths = index.Targets
            .GroupBy(t => t.Id).ToDictionary(g => g.Key,
                g => g.Select(t => t.RelativePath).Distinct(
                    StringComparer.OrdinalIgnoreCase).ToArray());
        foreach (var relation in index.Relations.Where(r =>
            r.TargetId is Guid target && targets.Contains(target) ||
            documentIds.Contains(r.SourceId)).Take(MaxItems + 1))
        {
            string[] matches = paths.TryGetValue(relation.SourceId,
                out string[]? found) ? found : [];
            string source = matches.Length == 1 ? matches[0] :
                "(source ambiguë dans l'index)";
            if (matches.Length != 1)
            {
                diagnostics.Add("Relation typée à source ambiguë : " +
                    relation.RawTargetId);
            }

            items.Add(new ProjectRequirementRelationReviewItem(
                ProjectRequirementRelationReviewKind.TypedRelation,
                relation.TargetId is Guid ca && caIds.Contains(ca)
                    ? ca : null,
                relation.RawTargetId, source,
                relation.RelationType + " · " + relation.Excerpt,
                "Examiner et requalifier cette relation dans les " +
                "documents concernés. Aucun lien n'est réécrit."));
        }

        if (items.Count > MaxItems)
        {
            diagnostics.Add("Plus de " + MaxItems +
                " relations : audit tronqué, validation non fiable.");
            items = items.Take(MaxItems).ToList();
        }

        return new ProjectRequirementRelationReview(
            preview.Intent.Id, preview.SourceSha256, preview.Intent.Method,
            items, diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }
}
