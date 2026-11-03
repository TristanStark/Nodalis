namespace Nodalis.Core.Tests;

/// <summary>
/// Reasons to propose a targeted repeat. A suggestion never changes a
/// human outcome or authorizes a new passage on its own.
/// </summary>
public enum ProjectTestRetestReason
{
    NotRun,
    Failed,
    Blocked,
    SourceNeedsReview
}

/// <summary>One stable test candidate from the latest frozen passage.</summary>
/// <param name="TestId">Current frozen stable block identity.</param>
/// <param name="Reference">Human test reference at the previous passage.</param>
/// <param name="Reasons">Explicit, non-automatic reasons for a rerun.</param>
public sealed record ProjectTestRetestCandidate(
    Guid TestId, string Reference,
    IReadOnlyList<ProjectTestRetestReason> Reasons);

/// <summary>Pure, read-only proposal. Human approval is required.</summary>
/// <param name="PassageId">Passage from which the proposal originates.</param>
/// <param name="Candidates">Suggested test identities in selection order.</param>
/// <param name="Warnings">Unverified sources and proposal limitations.</param>
public sealed record ProjectTestRetestProposal(
    Guid? PassageId, IReadOnlyList<ProjectTestRetestCandidate> Candidates,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds a re-execution proposal from the *latest* passage only. An older
/// Passed never fills an unexecuted later passage. Current-source issues
/// can be supplied separately; missing information is explicitly warned.
/// </summary>
public static class ProjectTestRetestPlanner
{
    /// <summary>
    /// Recommends review for NotRun, Failed, Blocked, and explicitly changed
    /// or uncertain current definitions. Never starts or writes a passage.
    /// </summary>
    /// <param name="campaign">Validated REC snapshot.</param>
    /// <param name="sourceReviewIds">
    /// Current-source issues by GUID, or null if sources were not assessed.
    /// An empty set means all latest source definitions were assessed Current.
    /// </param>
    /// <returns>Nonbinding human selection proposal.</returns>
    public static ProjectTestRetestProposal Propose(
        ProjectTestCampaign campaign, IReadOnlySet<Guid>? sourceReviewIds)
    {
        ProjectTestCampaignMarkdownCodec.Validate(campaign);
        List<string> warnings = [];
        if (campaign.Passages.Count == 0)
        {
            warnings.Add("Aucun passage précédent : " +
                "créez un passage avec sélection manuelle.");
            return new ProjectTestRetestProposal(null, [], warnings);
        }

        ProjectTestPassage latest = campaign.Passages[^1];
        Dictionary<Guid, ProjectTestResultEvent> results = latest.Results
            .ToDictionary(result => result.TestId);
        List<ProjectTestRetestCandidate> candidates = [];

        if (sourceReviewIds is null)
        {
            warnings.Add("Les définitions courantes des tests ne sont pas " +
                "contrôlées : la proposition est basée sur les verdicts " +
                "du dernier passage uniquement.");
        }

        foreach (ProjectTestFrozenDefinition test in latest.Tests)
        {
            List<ProjectTestRetestReason> reasons = [];
            if (!results.TryGetValue(test.TestId,
                    out ProjectTestResultEvent? result))
            {
                reasons.Add(ProjectTestRetestReason.NotRun);
            }
            else if (result.Outcome == ProjectTestOutcome.Failed)
            {
                reasons.Add(ProjectTestRetestReason.Failed);
            }
            else if (result.Outcome == ProjectTestOutcome.Blocked)
            {
                reasons.Add(ProjectTestRetestReason.Blocked);
            }

            if (sourceReviewIds?.Contains(test.TestId) == true)
            {
                reasons.Add(ProjectTestRetestReason.SourceNeedsReview);
            }

            if (reasons.Count != 0)
            {
                candidates.Add(new ProjectTestRetestCandidate(
                    test.TestId, test.Reference, reasons));
            }
        }

        if (candidates.Count == 0)
        {
            warnings.Add("Aucun test du dernier passage ne répond aux " +
                "critères de reprise connus. Sélection libre possible.");
        }

        warnings.Add("Cette proposition n'exécute aucun test, " +
            "ne valide aucun critère EXI et n'efface aucun verdict passé.");
        return new ProjectTestRetestProposal(
            latest.Id, candidates, warnings);
    }
}
