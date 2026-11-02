namespace Nodalis.Core.Tests;

/// <summary>
/// Exact outcome of one test in one passage. Null means never executed in
/// that passage, never implicitly Passed.
/// </summary>
public sealed record ProjectTestPassageResultRow(
    int PassageNumber, Guid PassageId, Guid TestId, string Reference,
    string Environment, string ApplicationVersion, string FrozenSha256,
    ProjectTestOutcome? Outcome, string? Actor, DateTimeOffset? RecordedUtc,
    string? EvidenceNote);

/// <summary>
/// Latest passage for ONE test, ONE environment, ONE version and ONE frozen
/// definition hash. Incomparable contexts are separate rows by design.
/// </summary>
public sealed record ProjectTestConsolidatedRow(
    Guid TestId, string Reference, string Environment, string ApplicationVersion,
    string FrozenSha256, int LastPassageNumber, ProjectTestOutcome? LatestOutcome,
    int PassageCount, int RecordedResultCount);

/// <summary>
/// Pure historical views; never re-read a live test definition, merge
/// environments/versions or modify any campaign's human verdict.
/// </summary>
public static class ProjectTestCampaignProjection
{
    /// <summary>Returns exact immutable test selection and outcomes for a passage.</summary>
    /// <param name="campaign">Validated campaign snapshot.</param>
    /// <param name="passageId">Selected frozen passage identity.</param>
    /// <returns>Test rows in the exact captured selection order.</returns>
    public static IReadOnlyList<ProjectTestPassageResultRow> GetPassageRows(
        ProjectTestCampaign campaign, Guid passageId)
    {
        ProjectTestCampaignMarkdownCodec.Validate(campaign);
        ProjectTestPassage passage = campaign.Passages.Single(
            item => item.Id == passageId);
        Dictionary<Guid, ProjectTestResultEvent> results = passage.Results
            .ToDictionary(item => item.TestId);
        return passage.Tests.Select(test =>
        {
            results.TryGetValue(test.TestId, out ProjectTestResultEvent? result);
            return new ProjectTestPassageResultRow(
                passage.Number, passage.Id, test.TestId, test.Reference,
                passage.Environment, passage.ApplicationVersion,
                test.Sha256, result?.Outcome, result?.Actor,
                result?.RecordedUtc, result?.EvidenceNote);
        }).ToArray();
    }

    /// <summary>
    /// Derives latest test state per exact environment/version/source SHA.
    /// An unexecuted latest passage NEVER inherits an older Passed outcome.
    /// </summary>
    /// <param name="campaign">Validated campaign snapshot.</param>
    /// <returns>Context-separated read-only consolidation.</returns>
    public static IReadOnlyList<ProjectTestConsolidatedRow> Consolidate(
        ProjectTestCampaign campaign)
    {
        ProjectTestCampaignMarkdownCodec.Validate(campaign);

        List<(ProjectTestPassage Passage,
            ProjectTestFrozenDefinition Test,
            ProjectTestResultEvent? Result)> occurrences = [];
        foreach (ProjectTestPassage passage in campaign.Passages)
        {
            Dictionary<Guid, ProjectTestResultEvent> results = passage.Results
                .ToDictionary(item => item.TestId);
            foreach (ProjectTestFrozenDefinition test in passage.Tests)
            {
                results.TryGetValue(test.TestId,
                    out ProjectTestResultEvent? result);
                occurrences.Add((passage, test, result));
            }
        }

        return occurrences.GroupBy(item => (
                item.Test.TestId,
                item.Passage.Environment,
                item.Passage.ApplicationVersion,
                item.Test.Sha256))
            .Select(group =>
            {
                (ProjectTestPassage Passage,
                    ProjectTestFrozenDefinition Test,
                    ProjectTestResultEvent? Result) last =
                        group.OrderByDescending(item =>
                            item.Passage.Number).First();
                return new ProjectTestConsolidatedRow(
                    last.Test.TestId, last.Test.Reference,
                    last.Passage.Environment,
                    last.Passage.ApplicationVersion,
                    last.Test.Sha256, last.Passage.Number,
                    last.Result?.Outcome, group.Count(),
                    group.Count(item => item.Result is not null));
            })
            .OrderBy(row => row.TestId)
            .ThenBy(row => row.Environment,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ApplicationVersion,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.FrozenSha256, StringComparer.Ordinal)
            .ToArray();
    }
}
