namespace Nodalis.Core.Sfd;

/// <summary>Read-only classification of an untrusted SFD proposal, never an approval.</summary>
public enum SfdImportReviewStatus
{
    NewCandidate,
    UpdateCandidate,
    LinkCandidate,
    NeedsResolution,
    IncompleteDraft,
    Deferred,
    Rejected
}

/// <summary>
/// One frozen comparison against existing canonical SFD identities, including
/// a source SHA only when an actual current document was resolved by GUID.
/// </summary>
public sealed record SfdImportReviewItem
{
    public required string ClientKey { get; init; }

    public required string Kind { get; init; }

    public required string Operation { get; init; }

    public required string Title { get; init; }

    public required string ProposedDescription { get; init; }

    public string? ProposedParentKey { get; init; }

    public string? ProposedUiMode { get; init; }

    public required IReadOnlyList<string> SourceKeys { get; init; }

    public required IReadOnlyList<string> ActivityKeys { get; init; }

    public required IReadOnlyList<string> RequirementKeys { get; init; }

    public required int ConditionCount { get; init; }

    public required int SubconditionCount { get; init; }

    public required int RuleLinkCount { get; init; }

    public required SfdImportReviewStatus Status { get; init; }

    public Guid? ExistingId { get; init; }

    public string? ExistingReference { get; init; }

    public string? ExistingTitle { get; init; }

    public string? ExistingSha256 { get; init; }

    public required string Message { get; init; }

    public required bool NeedsHumanReview { get; init; }
}

/// <summary>
/// Volatile read-only review: project ID, source/response fingerprints,
/// classified rows and never a publication authorization or receipt.
/// </summary>
public sealed record SfdImportReviewSnapshot
{
    public required Guid ProjectId { get; init; }

    public required string ResponseSha256 { get; init; }

    public required string PlanSha256 { get; init; }

    public required string Step { get; init; }

    public required string RunId { get; init; }

    public required IReadOnlyList<SfdImportReviewItem> Items { get; init; }

    public int Blocked => Items.Count(item => item.Status is
        SfdImportReviewStatus.Rejected or
        SfdImportReviewStatus.NeedsResolution or
        SfdImportReviewStatus.Deferred);

    public int Incomplete => Items.Count(item =>
        item.Status == SfdImportReviewStatus.IncompleteDraft);
}
