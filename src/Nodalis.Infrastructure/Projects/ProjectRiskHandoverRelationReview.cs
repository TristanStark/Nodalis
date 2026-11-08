using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>The independently reviewed relationship domain.</summary>
public enum ProjectRiskHandoverRelationKind
{
    /// <summary>Risk treatment task association.</summary>
    Task,
    /// <summary>Frozen Gate reservation and explicit RIS relation.</summary>
    GateReservation
}

/// <summary>
/// One risk relation requiring a manual handover decision. It never represents
/// an authorization, a successful retest or a newly approved Go/No-Go.
/// </summary>
/// <param name="Kind">Original source relationship domain.</param>
/// <param name="Identity">Original immutable task or reservation GUID.</param>
/// <param name="SourcePath">Existing risk source, never a target allocation.</param>
/// <param name="State">Current versus historical human linkage.</param>
/// <param name="Description">Context frozen from the actual source.</param>
/// <param name="Action">Suggested human requalification, never performed.</param>
/// <param name="RequiresDecision">An active relation needing new human action.</param>
public sealed record ProjectRiskHandoverRelationItem(
    ProjectRiskHandoverRelationKind Kind, Guid Identity, string SourcePath,
    string State, string Description, string Action, bool RequiresDecision);

/// <summary>
/// Read-only, SHA-pinned checklist. IsComplete means the sources were
/// reconciled, NOT that any business link has been approved or transferred.
/// </summary>
/// <param name="IntentId">Original recorded human intention.</param>
/// <param name="SourceSha256">Exact RIS bytes reviewed for this list.</param>
/// <param name="Method">A original move or B successor.</param>
/// <param name="Items">Bounded stable relation worklist.</param>
/// <param name="Diagnostics">Incomplete or mismatched source concerns.</param>
public sealed record ProjectRiskHandoverRelationReview(
    Guid IntentId, string SourceSha256, ProjectHandoverMethod Method,
    IReadOnlyList<ProjectRiskHandoverRelationItem> Items,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Source inventory completeness, never an execution approval.</summary>
    public bool IsComplete => Diagnostics.Count == 0;

    /// <summary>Active links requiring explicit requalification.</summary>
    public int ActionCount => Items.Count(item => item.RequiresDecision);

    /// <summary>Produces a bounded, copyable human action register.</summary>
    /// <returns>Read-only checklist, including exact source GUIDs.</returns>
    public string FormatChecklist()
    {
        StringBuilder output = new();
        output.AppendLine("REVUE DE RELATIONS — PASSATION RIS")
            .Append("Intention : ").AppendLine(IntentId.ToString("D"))
            .Append("SHA-256 source : ").AppendLine(SourceSha256)
            .Append("Mode : ").AppendLine(Method ==
                ProjectHandoverMethod.MoveOriginal
                    ? "A · GUID et historique conservés, propriétaire modifié"
                    : "B · nouveau GUID, aucune décision héritée")
            .Append("Relations recensées : ").Append(Items.Count)
            .Append(" · liées à requalifier : ").AppendLine(ActionCount.ToString())
            .AppendLine("LECTURE SEULE : aucun lien, état de tâche ou verdict " +
                "Go/No-Go n'est modifié.")
            .AppendLine();

        if (Diagnostics.Count > 0)
        {
            output.AppendLine("DIAGNOSTICS : inventaire incomplet");
            foreach (string diagnostic in Diagnostics.Take(25))
            {
                output.Append("- ").AppendLine(diagnostic);
            }

            output.AppendLine();
        }

        if (Items.Count == 0)
        {
            output.AppendLine("Aucune association RIS→tâche ou RIS→réserve " +
                "Gate recensée dans les catalogues inspectés.");
        }

        foreach (ProjectRiskHandoverRelationItem item in Items)
        {
            output.Append("[ ] ").Append(item.Kind).Append(" · ")
                .AppendLine(item.Identity.ToString("D"))
                .Append("     État : ").AppendLine(item.State)
                .Append("     Source : ").AppendLine(item.SourcePath)
                .Append("     Contexte : ").AppendLine(item.Description)
                .Append("     Décision humaine : ").AppendLine(item.Action);
        }

        output.AppendLine().AppendLine(
            "La revue ne valide aucun transfert. Les décisions, réserves, " +
            "cotations et preuves originales restent historiques. " +
            "Les contrôles transactionnels vérifient encore les références " +
            "entrantes avant exécution et restauration.");
        return output.ToString();
    }
}

/// <summary>
/// Joins one exact RIS with chronological current/historical task relations
/// and independently projected Gate reservation backlinks. No disk scan or
/// WPF collection change is triggered by this pure computation.
/// </summary>
public static class ProjectRiskHandoverRelationReviewBuilder
{
    private const int MaxItems = 1000;

    /// <summary>Returns an actionable, conservative relation inventory.</summary>
    /// <param name="preview">Fresh recorded RIS A/B preview.</param>
    /// <param name="risks">Verified owning project risk catalog.</param>
    /// <param name="gates">Existing source Gate/RIS backlink snapshot.</param>
    /// <returns>Read-only checklist with incompleteness diagnostics.</returns>
    public static ProjectRiskHandoverRelationReview Build(
        ProjectRiskHandoverPreview preview, ProjectRiskCatalog risks,
        ProjectRiskGateBacklinkCatalog gates)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(risks);
        ArgumentNullException.ThrowIfNull(gates);
        if (preview.Intent.Id == Guid.Empty ||
            preview.Intent.SourceObjectId == Guid.Empty ||
            preview.Intent.SourceObjectKind != "Risques" ||
            preview.SourceRelativePath != preview.Intent.SourceRelativePath ||
            preview.SourceSha256.Length != 64)
        {
            throw new InvalidDataException(
                "Préflight RIS non identifié pour la revue de relations.");
        }

        List<string> diagnostics = [.. risks.Diagnostics, .. gates.Diagnostics];
        ProjectRiskEntry[] owners = risks.Risks.Where(item =>
            item.Card.Id == preview.Intent.SourceObjectId &&
            item.Card.ProjectId == preview.Intent.SourceProjectId &&
            item.RelativePath == preview.SourceRelativePath &&
            string.Equals(item.SourceSha256, preview.SourceSha256,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (owners.Length != 1 ||
            gates.RiskId != preview.Intent.SourceObjectId)
        {
            diagnostics.Add("Fiche RIS ou registre Gate modifié, étranger, " +
                "ambigu ou non réconcilié depuis le préflight.");
            return new ProjectRiskHandoverRelationReview(
                preview.Intent.Id, preview.SourceSha256,
                preview.Intent.Method, [], diagnostics.ToArray());
        }

        ProjectRiskEntry owner = owners[0];
        List<ProjectRiskHandoverRelationItem> items = [];
        Guid? currentTreatment = owner.Treatments.LastOrDefault()?.Id;
        foreach (IGrouping<(Guid Treatment, ProjectRiskMeasureKind Kind,
            Guid Task), ProjectRiskTaskLinkEvent> group in owner.TaskLinks
                .GroupBy(item => (item.TreatmentId, item.Kind, item.TaskId))
                .OrderBy(item => item.Key.TreatmentId)
                .ThenBy(item => item.Key.Kind)
                .ThenBy(item => item.Key.TaskId))
        {
            if (items.Count >= MaxItems)
            {
                diagnostics.Add("Liste de liens RIS tronquée au plafond " +
                    "de 1000 : compléter la revue manuellement.");
                return Finish(preview, items, diagnostics);
            }

            ProjectRiskTaskLinkEvent last = group.Last();
            bool active = last.Action == ProjectRiskTaskLinkAction.Link;
            bool current = group.Key.TreatmentId == currentTreatment;
            bool requiresDecision = active && current;
            string status = !active ? "Délié · historique" :
                current ? "Lié au plan de traitement courant" :
                "Lié à un plan remplacé · historique";
            items.Add(new ProjectRiskHandoverRelationItem(
                ProjectRiskHandoverRelationKind.Task, group.Key.TaskId,
                owner.RelativePath, status,
                "Plan " + group.Key.TreatmentId.ToString("D") +
                    " · mesure " + group.Key.Kind,
                requiresDecision
                    ? "Requalifier cette tâche et son propriétaire, puis " +
                      "délier/relier explicitement si nécessaire."
                    : "Conserver la trace sans réactiver cette tâche " +
                      "ni lui attribuer de résultat.",
                requiresDecision));
        }

        foreach (ProjectRiskGateBacklink gate in gates.Links
            .OrderBy(item => item.DecisionId)
            .ThenBy(item => item.ReservationId))
        {
            if (items.Count >= MaxItems)
            {
                diagnostics.Add("Liste de réserves Gate/RIS tronquée " +
                    "au plafond de 1000 : compléter la revue manuellement.");
                return Finish(preview, items, diagnostics);
            }

            items.Add(new ProjectRiskHandoverRelationItem(
                ProjectRiskHandoverRelationKind.GateReservation,
                gate.ReservationId, owner.RelativePath,
                gate.Status,
                "Gate " + gate.GateId.ToString("D") +
                    " · décision " + gate.DecisionId.ToString("D") +
                    " · " + gate.GateName + " · " + gate.Description,
                gate.IsLinked
                    ? "Requalifier la réserve et son association RIS dans " +
                      "le projet propriétaire ; aucune décision Go/No-Go " +
                      "n'est transférée ni réutilisée automatiquement."
                    : "Conserver la dissociation historique ; ne pas " +
                      "réactiver cette réserve ni son ancien verdict.",
                gate.IsLinked));
        }

        return Finish(preview, items, diagnostics);
    }

    /// <summary>Freezes bounded findings without changing business objects.</summary>
    /// <param name="preview">Original signed intention and source SHA.</param>
    /// <param name="items">Validated relationship rows.</param>
    /// <param name="diagnostics">Ambiguous or truncated inputs.</param>
    /// <returns>Immutable read-only projected worklist.</returns>
    private static ProjectRiskHandoverRelationReview Finish(
        ProjectRiskHandoverPreview preview,
        List<ProjectRiskHandoverRelationItem> items,
        List<string> diagnostics) =>
        new(preview.Intent.Id, preview.SourceSha256,
            preview.Intent.Method, items.ToArray(), diagnostics.ToArray());
}
