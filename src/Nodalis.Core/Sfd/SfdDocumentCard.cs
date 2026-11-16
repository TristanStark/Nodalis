namespace Nodalis.Core.Sfd;

/// <summary>Canonical SFD document roles supported by the first authoring slice.</summary>
public enum SfdDocumentKind
{
    Process,
    Activity,
    UserRequirement,
    SystemRequirement,
    RegulatoryRequirement,
    Functionality
}

/// <summary>A functionality has exactly one of these two presentation modes.</summary>
public enum SfdFunctionalityMode
{
    WithInterface,
    WithoutInterface
}

/// <summary>Assertion certainty, independent of any document approval workflow.</summary>
public enum SfdAssertionCertainty
{
    ToConfirm,
    Inferred,
    Confirmed
}

/// <summary>
/// Portable, versioned identity for a SFD document. Conditions, sub-conditions
/// and business rules are NOT materialized when a draft document is created.
/// </summary>
public sealed record SfdDocumentCard
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required SfdDocumentKind Kind { get; init; }

    public required string Reference { get; init; }

    public required string Title { get; init; }

    public Guid? ParentId { get; init; }

    public SfdFunctionalityMode? FunctionalityMode { get; init; }

    public SfdAssertionCertainty Certainty { get; init; } = SfdAssertionCertainty.ToConfirm;

    public required DateTimeOffset CreatedUtc { get; init; }
}
