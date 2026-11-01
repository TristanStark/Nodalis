using Nodalis.Core.Projects;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// A read-only historical or current relation between a canonical RIS and
/// one reservation of a frozen human Gate decision.
/// </summary>
/// <param name="GateId">Existing stable Gate definition GUID.</param>
/// <param name="GateName">Frozen name at the original human decision.</param>
/// <param name="DecisionId">Immutable human decision GUID.</param>
/// <param name="ReservationId">Immutable reservation GUID.</param>
/// <param name="Description">Frozen reservation description.</param>
/// <param name="Verdict">Frozen human verdict, never recomputed.</param>
/// <param name="IsOpen">Independently reviewed reservation lifecycle.</param>
/// <param name="IsLinked">Current edge presence calculated from Link/Unlink events.</param>
/// <param name="LastChangeUtc">Last explicit relationship event time.</param>
public sealed record ProjectRiskGateBacklink(
    Guid GateId, string GateName, Guid DecisionId, Guid ReservationId,
    string Description, ProjectGateVerdict Verdict, bool IsOpen,
    bool IsLinked, DateTimeOffset LastChangeUtc)
{
    /// <summary>Compact WPF text without implying automatic Go/No-Go.</summary>
    public string Label => GateName + " · " + Description;

    /// <summary>Independent relationship and reservation status.</summary>
    public string Status =>
        (IsLinked ? "Associé" : "Dissocié · historique") +
        " · réserve " + (IsOpen ? "ouverte" : "levée") +
        " · verdict initial : " + Verdict;
}

/// <summary>One verified read-only inverse Gate/RIS lookup.</summary>
/// <param name="RiskId">Validated canonical risk ID.</param>
/// <param name="Links">Current and historic reservation references.</param>
/// <param name="Diagnostics">Nonfatal legacy risk document diagnostics.</param>
public sealed record ProjectRiskGateBacklinkCatalog(
    Guid? RiskId, IReadOnlyList<ProjectRiskGateBacklink> Links,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Resolves Gate reservations mentioning an existing project RIS GUID.
/// Reads only on explicit command; no materialization or source migrations.
/// </summary>
public sealed class ProjectRiskGateBacklinkService
{
    private readonly string _workspaceRoot;

    /// <summary>Creates a single workspace-scoped read-only resolver.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    public ProjectRiskGateBacklinkService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
    }

    /// <summary>
    /// Verifies that the selected source remains a canonical same-project RIS
    /// before projecting only this project's frozen Gate reservations.
    /// </summary>
    /// <param name="projectDirectory">Risk-owning project directory.</param>
    /// <param name="risk">Previously displayed risk snapshot.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Confirmed reciprocal relations and legacy diagnostics.</returns>
    public async Task<ProjectRiskGateBacklinkCatalog> ListAsync(
        string projectDirectory, ProjectRiskEntry risk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(risk);
        ProjectRiskRegistryService risks = new(_workspaceRoot);
        ProjectRiskCatalog source = await risks.ListAsync(
            projectDirectory, cancellationToken);
        ProjectRiskEntry[] candidates = source.Risks.Where(item =>
            item.Card.Id == risk.Card.Id &&
            item.Card.ProjectId == risk.Card.ProjectId &&
            string.Equals(item.RelativePath, risk.RelativePath,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (risk.Card.Id == Guid.Empty || candidates.Length != 1)
        {
            return new ProjectRiskGateBacklinkCatalog(null, [],
                ["Fiche RIS absente, déplacée, malformée ou ambiguë.",
                 .. source.Diagnostics.Take(10)]);
        }

        ProjectGateRegistryService gates = new(_workspaceRoot);
        ProjectGateRegistrySnapshot snapshot = await gates.ReadAsync(
            projectDirectory, cancellationToken);
        if (snapshot.Registry.ProjectId != risk.Card.ProjectId)
        {
            return new ProjectRiskGateBacklinkCatalog(null, [],
                ["La fiche RIS et le registre Gate n'appartiennent pas au même projet."]);
        }

        List<ProjectRiskGateBacklink> rows = [];
        foreach (IGrouping<(Guid Decision, Guid Reservation),
            ProjectGateReservationRiskLinkEvent> group in
            snapshot.Registry.RiskLinkEvents
                .Where(item => item.RiskId == risk.Card.Id)
                .GroupBy(item => (item.DecisionId, item.ReservationId)))
        {
            ProjectGateDecision decision = snapshot.Registry.Decisions.Single(
                item => item.Id == group.Key.Decision);
            ProjectGateReservation reservation = decision.Reservations.Single(
                item => item.Id == group.Key.Reservation);
            ProjectGateReservationState state =
                ProjectGateEditor.GetReservationState(snapshot.Registry,
                    decision.Id, reservation.Id);
            ProjectGateReservationRiskLinkEvent last = group.Last();
            rows.Add(new ProjectRiskGateBacklink(
                decision.GateId, decision.GateName, decision.Id,
                reservation.Id, reservation.Description,
                decision.Verdict, state.IsOpen,
                last.Kind == ProjectGateReservationRiskLinkKind.Linked,
                last.RecordedUtc));
        }

        return new ProjectRiskGateBacklinkCatalog(
            risk.Card.Id,
            rows.OrderByDescending(item => item.IsLinked)
                .ThenByDescending(item => item.IsOpen)
                .ThenByDescending(item => item.LastChangeUtc)
                .ThenBy(item => item.GateName,
                    StringComparer.CurrentCultureIgnoreCase).ToArray(),
            source.Diagnostics);
    }
}
