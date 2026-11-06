using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One unresolved or review-worthy canonical project object.</summary>
/// <param name="Kind">Original business register, not a new object type.</param>
/// <param name="Id">Stable identity of the existing object.</param>
/// <param name="Reference">Existing human-readable reference.</param>
/// <param name="Title">Current source title.</param>
/// <param name="Detail">Human-declared state, not an inferred verdict.</param>
/// <param name="RelativePath">Existing source path when available.</param>
public sealed record ProjectClosureAuditItem(
    string Kind, Guid Id, string Reference, string Title,
    string Detail, string? RelativePath);

/// <summary>Read-only partial audit, recomputed from canonical project sources.</summary>
/// <param name="ProjectId">Exact project that was inspected.</param>
/// <param name="Items">Open and review-worthy objects, without ownership transfer.</param>
/// <param name="Diagnostics">Unreadable or ambiguous sources and coverage limits.</param>
public sealed record ProjectClosureAuditSnapshot(
    Guid ProjectId, IReadOnlyList<ProjectClosureAuditItem> Items,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Formats a bounded, truthful pre-archive confirmation preview.</summary>
    /// <param name="maximumItems">Maximum number of individual objects displayed.</param>
    /// <returns>Preview including omissions and limits, not a closure decision.</returns>
    public string FormatPreview(int maximumItems = 10)
    {
        if (maximumItems is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        StringBuilder preview = new();
        preview.AppendLine("Pré-audit de clôture (lecture seule)")
            .AppendLine()
            .AppendLine(Items.Count + " élément(s) à examiner dans les registres couverts.");
        foreach (IGrouping<string, ProjectClosureAuditItem> group in
            Items.GroupBy(item => item.Kind).OrderBy(group => group.Key,
                StringComparer.Ordinal))
        {
            preview.AppendLine(" • " + group.Key + " : " + group.Count());
        }

        foreach (ProjectClosureAuditItem item in Items.Take(maximumItems))
        {
            preview.AppendLine(" - " + item.Reference + " · " + item.Title +
                " (" + item.Detail + ")");
        }

        if (Items.Count > maximumItems)
        {
            preview.AppendLine("… et " + (Items.Count - maximumItems) +
                " autre(s) élément(s).");
        }

        if (Diagnostics.Count > 0)
        {
            preview.AppendLine()
                .AppendLine("Vigilance : " + Diagnostics.Count +
                    " diagnostic(s) / limite(s) de couverture.");
            foreach (string diagnostic in Diagnostics.Take(4))
            {
                preview.AppendLine(" • " + diagnostic);
            }

            if (Diagnostics.Count > 4)
            {
                preview.AppendLine("… diagnostics supplémentaires non affichés.");
            }
        }

        preview.AppendLine()
            .AppendLine("Ce pré-audit ne constitue pas un bilan signé et ne " +
                "clôture aucun objet. Les tâches, jalons, incidents et " +
                "campagnes restent à vérifier séparément.");
        return preview.ToString();
    }
}

/// <summary>
/// Pure projection of existing canonical registries, without inferring a
/// business approval or replacing any source document.
/// </summary>
public static class ProjectClosureAuditProjection
{
    /// <summary>Calculates a partial closure inventory from already read sources.</summary>
    /// <param name="projectId">Selected stable project ID.</param>
    /// <param name="risks">Existing risk sources and diagnostics.</param>
    /// <param name="requirements">Existing requirement sources and diagnostics.</param>
    /// <param name="deployments">Existing MEP sources and diagnostics.</param>
    /// <param name="reservations">Existing append-only MEP reservation ledger.</param>
    /// <returns>Detached, sorted, read-only inventory with coverage notes.</returns>
    public static ProjectClosureAuditSnapshot Build(
        Guid projectId, ProjectRiskCatalog risks,
        ProjectRequirementCatalog requirements,
        ProjectDeploymentCatalog deployments,
        ProjectDeploymentReservationSnapshot reservations)
    {
        ArgumentNullException.ThrowIfNull(risks);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(deployments);
        ArgumentNullException.ThrowIfNull(reservations);
        if (projectId == Guid.Empty || requirements.ProjectId != projectId ||
            reservations.Ledger.ProjectId != projectId)
        {
            throw new InvalidDataException(
                "Les registres ne correspondent pas au projet sélectionné.");
        }

        List<ProjectClosureAuditItem> items = [];
        List<string> diagnostics = [];
        diagnostics.AddRange(risks.Diagnostics.Select(item => "Risques : " + item));
        diagnostics.AddRange(requirements.Diagnostics.Select(item =>
            "Exigences : " + item));
        diagnostics.AddRange(deployments.Diagnostics.Select(item =>
            "MEP : " + item));

        foreach (ProjectRiskEntry entry in risks.Risks)
        {
            ProjectRiskCard card = entry.Card;
            if (card.ProjectId != projectId)
            {
                throw new InvalidDataException("Risque rattaché à un autre projet.");
            }

            if (card.Lifecycle != ProjectRiskLifecycle.Closed ||
                card.Qualification != ProjectRiskQualification.Validated)
            {
                items.Add(new ProjectClosureAuditItem(
                    "Risques", card.Id, card.Reference, card.Title,
                    "cycle " + card.Lifecycle + ", qualification " +
                    card.Qualification, entry.RelativePath));
            }
        }

        foreach (ProjectRequirementEntry entry in requirements.Requirements)
        {
            ProjectRequirementCard card = entry.Card;
            if (card.ProjectId != projectId)
            {
                throw new InvalidDataException(
                    "Exigence rattachée à un autre projet.");
            }

            if (card.Validation is ProjectRequirementValidation.Rejected or
                ProjectRequirementValidation.Obsolete ||
                (card.Validation == ProjectRequirementValidation.Approved &&
                 card.Verification == ProjectRequirementVerification.Verified))
            {
                continue;
            }

            items.Add(new ProjectClosureAuditItem(
                "Exigences", card.Id, card.Reference, card.Title,
                "validation " + card.Validation + ", vérification " +
                card.Verification, entry.RelativePath));
        }

        foreach (ProjectDeploymentEntry entry in deployments.Operations)
        {
            ProjectDeploymentOperation operation = entry.Operation;
            if (operation.ProjectId != projectId)
            {
                throw new InvalidDataException("MEP rattachée à un autre projet.");
            }

            if (operation.Closure is null)
            {
                items.Add(new ProjectClosureAuditItem(
                    "MEP non clôturées", operation.Id, operation.Reference,
                    operation.Title, "bilan MEP absent", entry.RelativePath));
            }
        }

        IReadOnlyDictionary<Guid, ProjectDeploymentReservationState> states =
            ProjectDeploymentReservationCodec.GetStates(reservations.Ledger);
        foreach (ProjectDeploymentReservation reservation in
            reservations.Ledger.Items)
        {
            ProjectDeploymentReservationState state = states[reservation.Id];
            if (state.Status == ProjectDeploymentReservationStatus.Resolved)
            {
                continue;
            }

            items.Add(new ProjectClosureAuditItem(
                "Réserves MEP", reservation.Id, reservation.Reference,
                reservation.Title, "suivi " + state.Status +
                " · responsable " + state.Owner, reservations.RelativePath));
        }

        return new ProjectClosureAuditSnapshot(projectId,
            items.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Reference, StringComparer.Ordinal)
                .ToArray(), diagnostics.ToArray());
    }
}

/// <summary>
/// Performs a bounded, explicit read of existing local project registries.
/// No creation, implicit status change or global background refresh.
/// </summary>
public sealed class ProjectClosureAuditService
{
    private readonly string _workspaceRoot;

    /// <summary>Creates a local pre-archive audit service for one workspace.</summary>
    /// <param name="workspaceRoot">Workspace containing the selected project.</param>
    public ProjectClosureAuditService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>Reads four independent project-owned canonical registries.</summary>
    /// <param name="projectDirectory">Project directory verified by the readers.</param>
    /// <param name="cancellationToken">Cancellation of the on-demand audit.</param>
    /// <returns>Partial audit with explicit coverage limitations.</returns>
    public async Task<ProjectClosureAuditSnapshot> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ProjectRequirementCatalog requirements =
            await new ProjectRequirementRegistryService(_workspaceRoot)
                .ListAsync(projectDirectory, cancellationToken);
        ProjectRiskCatalog risks =
            await new ProjectRiskRegistryService(_workspaceRoot)
                .ListAsync(projectDirectory, cancellationToken);
        ProjectDeploymentCatalog deployments =
            await new ProjectDeploymentService(_workspaceRoot)
                .ListAsync(projectDirectory, cancellationToken);
        ProjectDeploymentReservationSnapshot reservations =
            await new ProjectDeploymentReservationService(_workspaceRoot)
                .ReadAsync(projectDirectory, cancellationToken);

        return ProjectClosureAuditProjection.Build(requirements.ProjectId,
            risks, requirements, deployments, reservations);
    }
}
