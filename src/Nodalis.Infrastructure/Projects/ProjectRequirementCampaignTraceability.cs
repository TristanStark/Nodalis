using Nodalis.Core.Tests;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One historical REC passage for a test linked to a CA criterion.
/// Outcome is a human declaration, not proof verification or EXI approval.
/// </summary>
/// <param name="CampaignId">Stable REC campaign ID.</param>
/// <param name="CampaignReference">Human campaign code.</param>
/// <param name="CampaignRelativePath">Canonical project-local REC source.</param>
/// <param name="PassageId">Stable frozen passage ID.</param>
/// <param name="PassageNumber">Sequence within this campaign only.</param>
/// <param name="StartedUtc">Passage creation time.</param>
/// <param name="Environment">Frozen execution environment.</param>
/// <param name="ApplicationVersion">Frozen software version.</param>
/// <param name="TestId">Exact linked test block GUID.</param>
/// <param name="TestReference">Human code at the passage start.</param>
/// <param name="FrozenSha256">SHA-256 of the stored test definition.</param>
/// <param name="SourceRelativePath">Test source at passage start.</param>
/// <param name="MatchesLinkedSource">Whether original and current indexed paths agree.</param>
/// <param name="Outcome">Human result, or null if not executed in this passage.</param>
/// <param name="Actor">Executor, if declared.</param>
/// <param name="RecordedUtc">Human result timestamp.</param>
/// <param name="Rationale">Human justification; no inference of conformity.</param>
/// <param name="EvidenceNote">Unverified note, never a checked proof.</param>
public sealed record ProjectRequirementCampaignOccurrence(
    Guid CampaignId, string CampaignReference, string CampaignRelativePath,
    Guid PassageId, int PassageNumber, DateTimeOffset StartedUtc,
    string Environment, string ApplicationVersion, Guid TestId,
    string TestReference, string FrozenSha256, string SourceRelativePath,
    bool MatchesLinkedSource, ProjectTestOutcome? Outcome,
    string? Actor, DateTimeOffset? RecordedUtc, string? Rationale,
    string? EvidenceNote);

/// <summary>
/// Read-only historical association. A linked test without a passage has
/// no result, and no result in any context confers EXI/CA verification.
/// </summary>
/// <param name="Test">Currently uniquely linked test source.</param>
/// <param name="Occurrences">All exact frozen passage records, oldest first.</param>
public sealed record ProjectRequirementCampaignTestHistory(
    ProjectRequirementTestSource Test,
    IReadOnlyList<ProjectRequirementCampaignOccurrence> Occurrences);

/// <summary>
/// A criterion with linked tests and their independent REC execution history.
/// </summary>
/// <param name="Criterion">Canonical link coverage row.</param>
/// <param name="Tests">One history per uniquely linked test block.</param>
public sealed record ProjectRequirementCampaignCriterionHistory(
    ProjectRequirementTraceabilityRow Criterion,
    IReadOnlyList<ProjectRequirementCampaignTestHistory> Tests);

/// <summary>
/// Project-local, read-only EXI/CA/test/REC history and source diagnostics.
/// A missing REC file is not interpreted as a passed or unexecuted test.
/// </summary>
/// <param name="Rows">Criterion history in requirement and CA order.</param>
/// <param name="Diagnostics">Campaign catalog limitations and unsafe sources.</param>
public sealed record ProjectRequirementCampaignOverview(
    IReadOnlyList<ProjectRequirementCampaignCriterionHistory> Rows,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>
    /// Distinct historic campaign/passage/test selections. A test linked to
    /// two CA criteria must not inflate the project-level passage count.
    /// </summary>
    public int PassageTestCount => Rows.SelectMany(row => row.Tests)
        .SelectMany(test => test.Occurrences)
        .Select(item => (item.CampaignId, item.PassageId, item.TestId))
        .Distinct().Count();

    /// <summary>
    /// Distinct human results. The same test can verify several criteria
    /// without turning one execution into several reported results.
    /// </summary>
    public int DeclaredResultCount => Rows.SelectMany(row => row.Tests)
        .SelectMany(test => test.Occurrences)
        .Where(item => item.Outcome.HasValue)
        .Select(item => (item.CampaignId, item.PassageId, item.TestId))
        .Distinct().Count();
}

/// <summary>
/// Pure, side-effect-free projection of already-validated project-local REC
/// documents onto the independently verified EXI/CA/test link matrix.
/// Does NOT infer current source validity, execution success or proof quality.
/// </summary>
public static class ProjectRequirementCampaignTraceability
{
    /// <summary>
    /// Joins a current, unique test block GUID to its exact historical
    /// snapshots. Campaign ownership and path-segment boundaries are checked
    /// again so foreign or relocated sources cannot certify local coverage.
    /// </summary>
    /// <param name="links">Canonical EXI/CA/test link projection.</param>
    /// <param name="catalog">Read-only REC catalog of the selected project.</param>
    /// <returns>Historical, non-qualifying execution associations.</returns>
    public static ProjectRequirementCampaignOverview Build(
        ProjectRequirementTraceabilityOverview links,
        ProjectTestCampaignCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(catalog);

        List<string> diagnostics = [.. catalog.Diagnostics];
        Dictionary<Guid, List<ProjectRequirementCampaignOccurrence>> byTestId =
            [];
        HashSet<(Guid Campaign, Guid Passage, Guid Test)> occurrencesSeen = [];
        Dictionary<Guid, ProjectTestCampaignEntry[]> campaignOwners =
            catalog.Campaigns.GroupBy(entry => entry.Campaign.Id)
                .ToDictionary(group => group.Key, group => group.ToArray());
        HashSet<Guid> linkedIds = links.Rows.Where(row =>
            row.Coverage == ProjectRequirementCoverage.LinkedTest)
            .SelectMany(row => row.Tests).Select(test => test.BlockId)
            .ToHashSet();

        // The catalog itself has already been checked against canonical REC
        // source bytes. Keep each passage and version separate: grouping by
        // test alone would cause stale "Passed" results to appear current.
        foreach (ProjectTestCampaignEntry entry in catalog.Campaigns)
        {
            ProjectTestCampaign campaign = entry.Campaign;
            if (campaign.Id == Guid.Empty || campaign.ProjectId == Guid.Empty)
            {
                diagnostics.Add("Campagne REC à identité absente : ignorée.");
                continue;
            }

            foreach (ProjectTestPassage passage in campaign.Passages)
            {
                Dictionary<Guid, ProjectTestResultEvent> outcomes =
                    passage.Results.ToDictionary(result => result.TestId);
                foreach (ProjectTestFrozenDefinition frozen in passage.Tests)
                {
                    if (!linkedIds.Contains(frozen.TestId))
                    {
                        continue;
                    }

                    if (!occurrencesSeen.Add((campaign.Id, passage.Id,
                            frozen.TestId)))
                    {
                        diagnostics.Add(campaign.Reference +
                            " : même test répété dans un passage.");
                        continue;
                    }

                    outcomes.TryGetValue(frozen.TestId,
                        out ProjectTestResultEvent? outcome);
                    ProjectRequirementCampaignOccurrence item = new(
                        campaign.Id, campaign.Reference, entry.RelativePath,
                        passage.Id, passage.Number, passage.StartedUtc,
                        passage.Environment, passage.ApplicationVersion,
                        frozen.TestId, frozen.Reference, frozen.Sha256,
                        frozen.SourceRelativePath, false, outcome?.Outcome,
                        outcome?.Actor, outcome?.RecordedUtc,
                        outcome?.Rationale, outcome?.EvidenceNote);
                    if (!byTestId.TryGetValue(frozen.TestId,
                            out List<ProjectRequirementCampaignOccurrence>? matches))
                    {
                        matches = [];
                        byTestId.Add(frozen.TestId, matches);
                    }

                    matches.Add(item);
                }
            }
        }

        List<ProjectRequirementCampaignCriterionHistory> rows = [];
        foreach (ProjectRequirementTraceabilityRow criterion in links.Rows)
        {
            List<ProjectRequirementCampaignTestHistory> tests = [];
            IReadOnlyList<ProjectRequirementTestSource> linkedTests =
                criterion.Coverage == ProjectRequirementCoverage.LinkedTest
                    ? criterion.Tests : [];
            foreach (ProjectRequirementTestSource test in linkedTests)
            {
                // A previously saved passage must not leak between projects,
                // even if the old source copied a stable test GUID.
                IReadOnlyList<ProjectRequirementCampaignOccurrence> candidates =
                    byTestId.TryGetValue(test.BlockId,
                        out List<ProjectRequirementCampaignOccurrence>? found)
                        ? found : [];
                ProjectRequirementCampaignOccurrence[] history = candidates
                    .Where(item => item.CampaignId != Guid.Empty &&
                        campaignOwners.TryGetValue(item.CampaignId,
                            out ProjectTestCampaignEntry[]? owners) &&
                        owners.Length == 1 &&
                        owners[0].Campaign.ProjectId ==
                            criterion.Requirement.Card.ProjectId &&
                        IsCampaignWithinProject(owners[0].RelativePath,
                            criterion.Requirement.RelativePath) &&
                        IsTestWithinProject(item.SourceRelativePath,
                            criterion.Requirement.RelativePath))
                    .Select(item => item with
                    {
                        MatchesLinkedSource = string.Equals(
                            item.SourceRelativePath, test.RelativePath,
                            StringComparison.OrdinalIgnoreCase)
                    })
                    .OrderBy(item => item.StartedUtc)
                    .ThenBy(item => item.CampaignReference,
                        StringComparer.Ordinal)
                    .ThenBy(item => item.PassageNumber)
                    .ToArray();
                if (history.Any(item => !item.MatchesLinkedSource))
                {
                    diagnostics.Add(
                        criterion.Requirement.Card.Reference + "/" +
                        criterion.Criterion.Reference + " : " +
                        "un ancien passage pointe vers un emplacement " +
                        "de test différent (requalification requise).");
                }

                tests.Add(new ProjectRequirementCampaignTestHistory(
                    test, history));
            }

            rows.Add(new ProjectRequirementCampaignCriterionHistory(
                criterion, tests));
        }

        return new ProjectRequirementCampaignOverview(rows, diagnostics);
    }

    /// <summary>Rejects frozen sources from a sibling or another project.</summary>
    /// <param name="testPath">Historical test source.</param>
    /// <param name="requirementPath">Canonical EXI source.</param>
    /// <returns>Whether the test was under this exact project root.</returns>
    private static bool IsTestWithinProject(
        string testPath, string requirementPath)
    {
        const string section = "/Documentation/Exigences/";
        string requirement = requirementPath.Replace('\\', '/');
        int marker = requirement.LastIndexOf(
            section, StringComparison.OrdinalIgnoreCase);
        return marker > 0 &&
            testPath.Replace('\\', '/').StartsWith(
                requirement[..marker] + "/",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks exact REC folder under the same project as the EXI, not a
    /// sibling with the same textual prefix.
    /// </summary>
    /// <param name="campaignPath">Workspace-relative REC file.</param>
    /// <param name="requirementPath">Workspace-relative EXI file.</param>
    /// <returns>Whether the canonical project paths agree.</returns>
    private static bool IsCampaignWithinProject(
        string campaignPath, string requirementPath)
    {
        const string requirementSection = "/Documentation/Exigences/";
        string requirement = requirementPath.Replace('\\', '/');
        string campaign = campaignPath.Replace('\\', '/');
        int index = requirement.LastIndexOf(
            requirementSection, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            return false;
        }

        string expected = requirement[..index] + "/Tests/Campagnes/";
        return campaign.StartsWith(expected,
                StringComparison.OrdinalIgnoreCase) &&
            !campaign[expected.Length..].Contains('/');
    }
}
