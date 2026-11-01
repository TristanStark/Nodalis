namespace Nodalis.Core.Projects;

/// <summary>Human-only gate verdict. No status implies automatic approval.</summary>
public enum ProjectGateVerdict
{
    Go,
    GoWithReservations,
    NoGo,
    Deferred
}

/// <summary>Human assessment of one criterion at a frozen decision instant.</summary>
public enum ProjectGateCriterionStatus
{
    Conform,
    Deviation,
    Unknown,
    NotApplicable
}

/// <summary>Project-local registry independent of milestone state or documents.</summary>
public sealed record ProjectGateRegistry
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    public List<ProjectGateDefinition> Gates { get; init; } = [];

    /// <summary>Append-only history; no decision may be replaced or erased.</summary>
    public List<ProjectGateDecision> Decisions { get; init; } = [];

    /// <summary>
    /// Separate append-only human history of reservation lifting/reopening.
    /// Existing v1 registries deserialize with an empty history.
    /// </summary>
    public List<ProjectGateReservationEvent> ReservationEvents { get; init; } = [];

    /// <summary>Independent, append-only incident association history.</summary>
    public List<ProjectGateReservationIncidentLinkEvent> IncidentLinkEvents
    { get; init; } = [];
}

/// <summary>One named review point with stable identity and editable future criteria.</summary>
public sealed record ProjectGateDefinition
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public List<ProjectGateCriterion> Criteria { get; init; } = [];

    /// <summary>Non-owning references to existing, persisted milestone GUIDs.</summary>
    public List<Guid> MilestoneIds { get; init; } = [];
}

/// <summary>Stable requirement/criterion of a gate.</summary>
public sealed record ProjectGateCriterion
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public bool Optional { get; init; }
}

/// <summary>One frozen and human-confirmed assessment in a decision record.</summary>
public sealed record ProjectGateCriterionAssessment
{
    public required Guid CriterionId { get; init; }

    /// <summary>Frozen label: future gate edits never affect this decision.</summary>
    public required string Name { get; init; }

    /// <summary>Requirement optionality frozen at decision time.</summary>
    public bool Optional { get; init; }

    public required ProjectGateCriterionStatus Status { get; init; }

    public string Comment { get; init; } = string.Empty;
}

/// <summary>Proof location and content fingerprint at the instant of decision.</summary>
public sealed record ProjectGateEvidenceSnapshot
{
    public required string RelativePath { get; init; }

    public required string Sha256 { get; init; }
}

/// <summary>An open reservation in a GO-with-reservations decision.</summary>
public sealed record ProjectGateReservation
{
    public required Guid Id { get; init; }

    public required string Description { get; init; }

    public string? Owner { get; init; }

    public DateOnly? DueDate { get; init; }
}

/// <summary>
/// Immutable historical human review. Subsequent gate changes or a completed
/// milestone never retroactively alter this decision or lift reservations.
/// </summary>
public sealed record ProjectGateDecision
{
    public required Guid Id { get; init; }

    public required Guid GateId { get; init; }

    public required string GateName { get; init; }

    public DateTimeOffset RecordedUtc { get; init; } = DateTimeOffset.UtcNow;

    public required ProjectGateVerdict Verdict { get; init; }

    public required string Actor { get; init; }

    public required string Scope { get; init; }

    public required string Rationale { get; init; }

    public string? Version { get; init; }

    public string? Environment { get; init; }

    public List<ProjectGateCriterionAssessment> Assessments { get; init; } = [];

    public List<ProjectGateEvidenceSnapshot> Evidence { get; init; } = [];

    public List<ProjectGateReservation> Reservations { get; init; } = [];
}

/// <summary>Explicit human change of a reservation's current state.</summary>
public enum ProjectGateReservationEventKind
{
    Lifted,
    Reopened
}

/// <summary>
/// Immutable reservation lifecycle event. The originating GO-with-reservations
/// verdict and reservation description remain frozen in their decision.
/// </summary>
public sealed record ProjectGateReservationEvent
{
    public required Guid Id { get; init; }

    public required Guid DecisionId { get; init; }

    public required Guid ReservationId { get; init; }

    public required ProjectGateReservationEventKind Kind { get; init; }

    public DateTimeOffset RecordedUtc { get; init; } = DateTimeOffset.UtcNow;

    public required string Actor { get; init; }

    public required string Rationale { get; init; }

    /// <summary>Reviewed, frozen evidence; lifting requires at least one proof.</summary>
    public List<ProjectGateEvidenceSnapshot> Evidence { get; init; } = [];
}

/// <summary>Explicit manual linking or unlinking of a canonical incident.</summary>
public enum ProjectGateReservationIncidentLinkKind
{
    Linked,
    Unlinked
}

/// <summary>
/// Audited incident association with a reservation from an immutable
/// historical decision. Incidents remain owned by canonical Markdown.
/// </summary>
public sealed record ProjectGateReservationIncidentLinkEvent
{
    public required Guid Id { get; init; }
    public required Guid DecisionId { get; init; }
    public required Guid ReservationId { get; init; }
    public required string IncidentId { get; init; }
    public required ProjectGateReservationIncidentLinkKind Kind { get; init; }
    public DateTimeOffset RecordedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
}

/// <summary>Calculated current state, never written over the decision.</summary>
public sealed record ProjectGateReservationState(
    Guid DecisionId, Guid ReservationId, bool IsOpen,
    ProjectGateReservationEvent? LatestEvent);

/// <summary>Registry and exact disk revision; null revision means no registry yet.</summary>
public sealed record ProjectGateRegistrySnapshot(
    ProjectGateRegistry Registry, string? Sha256);

/// <summary>Explicit human review submitted to the gate registry.</summary>
public sealed record ProjectGateReview
{
    public required Guid GateId { get; init; }

    public required ProjectGateVerdict Verdict { get; init; }

    public required string Actor { get; init; }

    public required string Scope { get; init; }

    public required string Rationale { get; init; }

    public string? Version { get; init; }

    public string? Environment { get; init; }

    /// <summary>Exact one-to-one human assessments of current criteria.</summary>
    public List<ProjectGateCriterionAssessment> Assessments { get; init; } = [];

    /// <summary>Evidence selected explicitly by the user at decision time.</summary>
    public List<ProjectGateEvidenceSnapshot> Evidence { get; init; } = [];

    public List<ProjectGateReservation> Reservations { get; init; } = [];
}
