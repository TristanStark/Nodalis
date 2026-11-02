using Nodalis.Core.Projects;
using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One currently linked risk, or an explicitly unresolved historical GUID.
/// Counts refer to the latest human treatment plan, never checkbox completion.
/// </summary>
/// <param name="RiskId">Stable canonical RIS GUID from the Gate audit.</param>
/// <param name="Reference">Human RIS reference; null when source is missing.</param>
/// <param name="Title">Human RIS title; null when source is missing.</param>
/// <param name="RelativePath">Verified workspace Markdown source, or null.</param>
/// <param name="Qualification">Reviewed business status, if resolved.</param>
/// <param name="InitialSeverity">Human-rated initial severity, if resolved.</param>
/// <param name="ResidualSeverity">Human-rated residual severity, if resolved.</param>
/// <param name="HasTreatmentPlan">Whether the risk has a current plan.</param>
/// <param name="BlockedMeasures">Human-declared blocked measure slots.</param>
/// <param name="UnfinishedMeasures">Pending, in progress or blocked slots.</param>
/// <param name="LinkedTaskReferences">Distinct GUID references, not task completion.</param>
public sealed record ProjectGateRiskOverviewRiskRow(
    Guid RiskId, string? Reference, string? Title, string? RelativePath,
    ProjectRiskQualification? Qualification,
    ProjectRiskSeverity? InitialSeverity,
    ProjectRiskSeverity? ResidualSeverity,
    bool HasTreatmentPlan, int BlockedMeasures,
    int UnfinishedMeasures, int LinkedTaskReferences)
{
    /// <summary>Whether the referenced source cannot be safely resolved.</summary>
    public bool IsOrphan => RelativePath is null;

    /// <summary>Human-readable identifier, never a made-up RIS source.</summary>
    public string Label => IsOrphan
        ? RiskId.ToString("D") + " · fiche absente ou ambiguë"
        : Reference + " · " + Title;

    /// <summary>Distinct human statuses, not a release authorization.</summary>
    public string Detail => IsOrphan
        ? "Aucune source vérifiée · relation à examiner humainement"
        : "Initiale " + InitialSeverity + " · résiduelle " +
          ResidualSeverity + " · qualification " + Qualification +
          (HasTreatmentPlan
              ? " · mesures bloquées " + BlockedMeasures +
                " / non terminées " + UnfinishedMeasures +
                " · références de tâches " + LinkedTaskReferences
              : " · aucun plan de traitement");
}

/// <summary>
/// Read-only join of one original Gate reservation and its currently linked
/// risks; previously unlinked GUIDs are counted but not treated as active.
/// </summary>
/// <param name="Reservation">Unchanged frozen-decision overview row.</param>
/// <param name="Risks">Current risk links, resolved if unambiguous.</param>
/// <param name="HistoricalUnlinks">Unlink events, shown as historical count only.</param>
public sealed record ProjectGateRiskOverviewRow(
    ProjectGateReservationOverviewRow Reservation,
    IReadOnlyList<ProjectGateRiskOverviewRiskRow> Risks,
    int HistoricalUnlinks)
{
    /// <summary>Currently unresolved active risk references.</summary>
    public int OrphanCount => Risks.Count(item => item.IsOrphan);

    /// <summary>Whether any verified risk declares a blocked measure.</summary>
    public bool HasBlockedMeasures =>
        Risks.Any(item => item.BlockedMeasures > 0);

    /// <summary>Whether current plan measures are not all completed.</summary>
    public bool HasUnfinishedMeasures =>
        Risks.Any(item => item.UnfinishedMeasures > 0);

    /// <summary>Unknown ratings are not treated as low risk.</summary>
    public bool HasUnassessedRisks => Risks.Any(item =>
        !item.IsOrphan && (item.InitialSeverity ==
            ProjectRiskSeverity.Unassessed ||
            item.ResidualSeverity == ProjectRiskSeverity.Unassessed));

    /// <summary>Concise reservation state, no approval implied.</summary>
    public string Label =>
        (Reservation.IsOpen ? "OUVERTE · " : "LEVÉE · ") +
        Reservation.Description;

    /// <summary>Searchable, compact cross-register summary.</summary>
    public string Detail => Reservation.GateName + " · " +
        Risks.Count + " RIS lié(s)" +
        (OrphanCount > 0 ? " · " + OrphanCount + " orphelin(s)" : "") +
        (HasBlockedMeasures ? " · mesures bloquées" : "") +
        (HasUnassessedRisks ? " · évaluations manquantes" : "");
}

/// <summary>
/// Project-local read-only overview, built from one Gate snapshot and one
/// canonical RIS catalog. No automatic Go/No-Go or reservation transitions.
/// </summary>
public sealed record ProjectGateRiskOverview
{
    public required Guid ProjectId { get; init; }

    public required IReadOnlyList<ProjectGateRiskOverviewRow> Rows { get; init; }

    public required IReadOnlyList<string> Diagnostics { get; init; }

    public required int OpenCount { get; init; }

    public required int LiftedCount { get; init; }

    public required int OpenWithoutRiskCount { get; init; }

    public required int OpenOrphanCount { get; init; }

    public required int OpenBlockedCount { get; init; }

    public required int OpenUnassessedCount { get; init; }
}

/// <summary>
/// Pure linear-time relation replay followed by indexed joins and sorting.
/// It never performs filesystem access or dereferences task GUIDs.
/// </summary>
public static class ProjectGateRiskOverviewBuilder
{
    /// <summary>
    /// Combines human Gate decisions with structurally validated RIS entries.
    /// A missing or duplicate RIS is an orphan, not an invented risk rating.
    /// </summary>
    /// <param name="registry">Validated Gate registry snapshot.</param>
    /// <param name="catalog">Project-local canonical RIS catalog.</param>
    /// <param name="today">Explicit day for overdue calculation.</param>
    /// <returns>Frozen, independently filterable cross-register projection.</returns>
    public static ProjectGateRiskOverview Build(
        ProjectGateRegistry registry, ProjectRiskCatalog catalog, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ProjectGateReservationOverview reservations =
            ProjectGateReservationOverviewBuilder.Build(registry, today);
        Dictionary<Guid, ProjectRiskEntry> unique = [];
        HashSet<Guid> duplicates = [];
        foreach (ProjectRiskEntry item in catalog.Risks)
        {
            if (item.Card.ProjectId != registry.ProjectId ||
                item.Card.Id == Guid.Empty ||
                !unique.TryAdd(item.Card.Id, item))
            {
                duplicates.Add(item.Card.Id);
            }
        }

        foreach (Guid duplicate in duplicates)
        {
            unique.Remove(duplicate);
        }

        Dictionary<(Guid Decision, Guid Reservation), HashSet<Guid>> active = [];
        Dictionary<(Guid Decision, Guid Reservation), int> removed = [];
        foreach (ProjectGateReservationRiskLinkEvent item in
            registry.RiskLinkEvents)
        {
            (Guid Decision, Guid Reservation) key =
                (item.DecisionId, item.ReservationId);
            if (!active.TryGetValue(key, out HashSet<Guid>? ids))
            {
                ids = [];
                active.Add(key, ids);
            }

            if (item.Kind == ProjectGateReservationRiskLinkKind.Linked)
            {
                ids.Add(item.RiskId);
            }
            else
            {
                ids.Remove(item.RiskId);
                removed[key] = removed.GetValueOrDefault(key) + 1;
            }
        }

        // A risk may be linked to many historic reservations. Project its
        // human measures/task GUIDs once rather than replaying each RIS log
        // for every reservation row or during later WPF search/filter.
        Dictionary<Guid, ProjectGateRiskOverviewRiskRow> riskRows = [];
        List<ProjectGateRiskOverviewRow> result = [];
        foreach (ProjectGateReservationOverviewRow reservation in
            reservations.Rows)
        {
            (Guid Decision, Guid Reservation) key =
                (reservation.DecisionId, reservation.ReservationId);
            List<ProjectGateRiskOverviewRiskRow> resolved = [];
            if (active.TryGetValue(key, out HashSet<Guid>? ids))
            {
                foreach (Guid id in ids)
                {
                    if (!riskRows.TryGetValue(id,
                        out ProjectGateRiskOverviewRiskRow? detail))
                    {
                        detail = MakeRisk(id, unique);
                        riskRows.Add(id, detail);
                    }

                    resolved.Add(detail);
                }
            }

            ProjectGateRiskOverviewRiskRow[] ordered = resolved
                .OrderBy(row => row.IsOrphan)
                .ThenBy(row => row.Reference, StringComparer.Ordinal)
                .ThenBy(row => row.RiskId).ToArray();
            result.Add(new ProjectGateRiskOverviewRow(
                reservation, ordered, removed.GetValueOrDefault(key)));
        }

        ProjectGateRiskOverviewRow[] rows = result.ToArray();
        return new ProjectGateRiskOverview
        {
            ProjectId = registry.ProjectId,
            Rows = rows,
            Diagnostics = catalog.Diagnostics.ToArray(),
            OpenCount = reservations.OpenCount,
            LiftedCount = reservations.LiftedCount,
            OpenWithoutRiskCount = rows.Count(row =>
                row.Reservation.IsOpen && row.Risks.Count == 0),
            OpenOrphanCount = rows.Count(row =>
                row.Reservation.IsOpen && row.OrphanCount > 0),
            OpenBlockedCount = rows.Count(row =>
                row.Reservation.IsOpen && row.HasBlockedMeasures),
            OpenUnassessedCount = rows.Count(row =>
                row.Reservation.IsOpen && row.HasUnassessedRisks)
        };
    }

    /// <summary>Projects explicit current-plan human progress only.</summary>
    /// <param name="riskId">GUID from the validated Gate audit.</param>
    /// <param name="sources">Same-project canonical risk dictionary.</param>
    /// <returns>Resolved risk summary or source-unavailable diagnostic row.</returns>
    private static ProjectGateRiskOverviewRiskRow MakeRisk(
        Guid riskId, IReadOnlyDictionary<Guid, ProjectRiskEntry> sources)
    {
        if (!sources.TryGetValue(riskId, out ProjectRiskEntry? entry))
        {
            return new ProjectGateRiskOverviewRiskRow(
                riskId, null, null, null, null, null, null,
                false, 0, 0, 0);
        }

        bool hasPlan = entry.CurrentTreatment is not null;
        ProjectRiskMeasureStatus[] states = hasPlan
            ? Enum.GetValues<ProjectRiskMeasureKind>()
                .Select(entry.MeasureStatus).ToArray()
            : [];
        int linked = hasPlan
            ? Enum.GetValues<ProjectRiskMeasureKind>()
                .SelectMany(entry.LinkedTaskIds).Distinct().Count()
            : 0;
        return new ProjectGateRiskOverviewRiskRow(
            riskId, entry.Card.Reference, entry.Card.Title,
            entry.RelativePath, entry.Card.Qualification,
            ProjectRiskMatrix.Evaluate(entry.Card.Initial),
            ProjectRiskMatrix.Evaluate(entry.Card.Residual),
            hasPlan,
            states.Count(item => item == ProjectRiskMeasureStatus.Blocked),
            states.Count(item => item != ProjectRiskMeasureStatus.Completed),
            linked);
    }
}
