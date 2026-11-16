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

    /// <summary>Source EXR claimed by the external model, never proof of validity.</summary>
    public string? ProposedRegulatorySource { get; init; }

    public required IReadOnlyList<string> SourceKeys { get; init; }

    public required IReadOnlyList<string> ActivityKeys { get; init; }

    public required IReadOnlyList<string> RequirementKeys { get; init; }

    public required int ConditionCount { get; init; }

    public required int SubconditionCount { get; init; }

    public required int RuleLinkCount { get; init; }

    /// <summary>Full external C/SC wording shown before any human approval.</summary>
    public required IReadOnlyList<SfdImportCondition> ProposedConditions { get; init; }

    public required SfdImportReviewStatus Status { get; init; }

    public Guid? ExistingId { get; init; }

    public string? ExistingReference { get; init; }

    public string? ExistingTitle { get; init; }

    public string? ExistingSha256 { get; init; }

    /// <summary>Actual PM parent GUID used for ACT publication, if resolved.</summary>
    public Guid? ResolvedParentId { get; init; }

    /// <summary>Exact parent PM SHA captured at review time.</summary>
    public string? ResolvedParentSha256 { get; init; }

    /// <summary>Exact reviewed canonical ACT source revisions used by proposed FCT links.</summary>
    public required IReadOnlyDictionary<Guid, string> ActivityRevisions { get; init; }

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

    /// <summary>Exact shared RM Markdown byte revision captured in this review.</summary>
    public required string RulesCatalogSha256 { get; init; }

    /// <summary>Exact N:N registry revision, null before its first creation.</summary>
    public string? ActivityFunctionalitySha256 { get; init; }

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
