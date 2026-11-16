namespace Nodalis.Core.Sfd;

/// <summary>
/// Whether a proposed SFD field is safe to consider against an explicit,
/// immutable baseline. This is NOT a decision to write or an approval.
/// </summary>
public enum SfdThreeWayFieldStatus
{
    Unchanged,
    ProposedChange,
    AlreadyCurrent,
    KeptHumanChange,
    Conflict
}

/// <summary>One actual baseline / current / proposed field comparison.</summary>
/// <param name="Name">Stable field key, never an untrusted path.</param>
/// <param name="BaseValue">Explicitly captured source value.</param>
/// <param name="CurrentValue">Current filesystem value.</param>
/// <param name="ProposedValue">External AI suggestion.</param>
/// <param name="Status">Three-way relationship of these values.</param>
public sealed record SfdThreeWayField(
    string Name, string BaseValue, string CurrentValue,
    string ProposedValue, SfdThreeWayFieldStatus Status);

/// <summary>
/// Read-only diff for a single existing SFD GUID. A null baseline means that
/// the user has not explicitly frozen a business reference state yet.
/// </summary>
public sealed record SfdThreeWayPreview
{
    public required Guid TargetId { get; init; }

    public required string CurrentSha256 { get; init; }

    public string? BaselineSha256 { get; init; }

    public required IReadOnlyList<SfdThreeWayField> Fields { get; init; }

    public bool HasBaseline => BaselineSha256 is not null;

    public bool HasConflict => Fields.Any(field =>
        field.Status == SfdThreeWayFieldStatus.Conflict);

    public bool HasProposedChange => Fields.Any(field =>
        field.Status == SfdThreeWayFieldStatus.ProposedChange);
}
