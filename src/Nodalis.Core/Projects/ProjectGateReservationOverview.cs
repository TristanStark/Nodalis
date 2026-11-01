namespace Nodalis.Core.Projects;

/// <summary>
/// One non-owning, read-only reservation row from the canonical gate register.
/// The current state is a projection of human events, not a rewritten verdict.
/// </summary>
public sealed record ProjectGateReservationOverviewRow
{
    public required Guid GateId { get; init; }

    public required string GateName { get; init; }

    public required Guid DecisionId { get; init; }

    public required DateTimeOffset DecisionRecordedUtc { get; init; }

    public required ProjectGateVerdict HistoricalVerdict { get; init; }

    public required Guid ReservationId { get; init; }

    public required string Description { get; init; }

    public string? Owner { get; init; }

    public DateOnly? DueDate { get; init; }

    public required bool IsOpen { get; init; }

    public required bool IsOverdue { get; init; }

    public required int EventCount { get; init; }

    public ProjectGateReservationEvent? LatestEvent { get; init; }
}

/// <summary>Read-only project-level summary of historical reservations.</summary>
public sealed record ProjectGateReservationOverview
{
    public required Guid ProjectId { get; init; }

    public required IReadOnlyList<ProjectGateReservationOverviewRow> Rows { get; init; }

    public required int OpenCount { get; init; }

    public required int LiftedCount { get; init; }

    public required int OverdueCount { get; init; }

    public required int MissingOwnerCount { get; init; }
}

/// <summary>
/// Computes a bounded, workspace-scan-free overview from one validated
/// project register. Complexity is linear before sorting: no per-row
/// Validate/GetReservationState call and no filesystem access.
/// </summary>
public static class ProjectGateReservationOverviewBuilder
{
    /// <summary>
    /// Aggregates all reservation-bearing historical decisions for one project.
    /// Each reservation is shown once, even after repeated reopenings.
    /// </summary>
    /// <param name="registry">Canonical snapshot.</param>
    /// <param name="today">Explicit local day for deterministic overdue checks.</param>
    /// <returns>Current projection and counts, without side effects.</returns>
    public static ProjectGateReservationOverview Build(
        ProjectGateRegistry registry, DateOnly today)
    {
        ProjectGateValidation.Validate(registry);
        Dictionary<(Guid DecisionId, Guid ReservationId),
            (ProjectGateReservationEvent? Latest, int Count)> history = [];
        foreach (ProjectGateReservationEvent change in registry.ReservationEvents)
        {
            (Guid DecisionId, Guid ReservationId) key =
                (change.DecisionId, change.ReservationId);
            history.TryGetValue(key,
                out (ProjectGateReservationEvent? Latest, int Count) current);
            history[key] = (change, current.Count + 1);
        }

        List<ProjectGateReservationOverviewRow> rows = [];
        foreach (ProjectGateDecision decision in registry.Decisions)
        {
            foreach (ProjectGateReservation reservation in decision.Reservations)
            {
                history.TryGetValue((decision.Id, reservation.Id),
                    out (ProjectGateReservationEvent? Latest, int Count) changes);
                bool isOpen = changes.Latest is null ||
                    changes.Latest.Kind == ProjectGateReservationEventKind.Reopened;
                rows.Add(new ProjectGateReservationOverviewRow
                {
                    GateId = decision.GateId,
                    GateName = decision.GateName,
                    DecisionId = decision.Id,
                    DecisionRecordedUtc = decision.RecordedUtc,
                    HistoricalVerdict = decision.Verdict,
                    ReservationId = reservation.Id,
                    Description = reservation.Description,
                    Owner = reservation.Owner,
                    DueDate = reservation.DueDate,
                    IsOpen = isOpen,
                    IsOverdue = isOpen && reservation.DueDate is DateOnly date &&
                        date < today,
                    EventCount = changes.Count,
                    LatestEvent = changes.Latest
                });
            }
        }

        ProjectGateReservationOverviewRow[] ordered = rows
            .OrderByDescending(row => row.IsOpen)
            .ThenByDescending(row => row.IsOverdue)
            .ThenBy(row => row.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(row => row.DecisionRecordedUtc)
            .ThenBy(row => row.ReservationId).ToArray();
        return new ProjectGateReservationOverview
        {
            ProjectId = registry.ProjectId,
            Rows = ordered,
            OpenCount = ordered.Count(row => row.IsOpen),
            LiftedCount = ordered.Count(row => !row.IsOpen),
            OverdueCount = ordered.Count(row => row.IsOverdue),
            MissingOwnerCount = ordered.Count(row =>
                row.IsOpen && string.IsNullOrWhiteSpace(row.Owner))
        };
    }
}
