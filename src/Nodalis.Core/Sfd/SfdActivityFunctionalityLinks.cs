namespace Nodalis.Core.Sfd;

/// <summary>One explicit N:N association of canonical ACT and FCT identities.</summary>
public sealed record SfdActivityFunctionalityLink
{
    public required Guid ActivityId { get; init; }

    public required Guid FunctionalityId { get; init; }
}

/// <summary>
/// Project-local, portable association registry; never inferred from physical
/// filenames and never duplicates a business activity document inside FCT.
/// </summary>
public sealed record SfdActivityFunctionalityRegistry
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    public List<SfdActivityFunctionalityLink> Links { get; init; } = [];
}

/// <summary>Frozen data and the SHA-256 revision required for safe updates.</summary>
/// <param name="Registry">Immutable-by-convention snapshot of existing N:N links.</param>
/// <param name="Sha256">Current byte hash, or null for a not-yet-created registry.</param>
public sealed record SfdActivityFunctionalitySnapshot(
    SfdActivityFunctionalityRegistry Registry, string? Sha256);
