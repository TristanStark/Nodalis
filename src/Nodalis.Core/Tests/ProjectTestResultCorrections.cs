namespace Nodalis.Core.Tests;

/// <summary>
/// Explicit administrative rectification of a recorded REC outcome. The
/// original result stays in its frozen passage. A rectification is an audit
/// annotation, not a test rerun or proof of functional revalidation.
/// </summary>
public sealed record ProjectTestResultCorrectionEvent
{
    /// <summary>Stable audit event identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Frozen passage containing the original result.</summary>
    public required Guid PassageId { get; init; }

    /// <summary>Stable identity of the original result being corrected.</summary>
    public required Guid ResultId { get; init; }

    /// <summary>Stable test identity recorded in the passage.</summary>
    public required Guid TestId { get; init; }

    /// <summary>Human corrected declaration, separate from original outcome.</summary>
    public required ProjectTestOutcome CorrectedOutcome { get; init; }

    /// <summary>Human authorizing the administrative correction.</summary>
    public required string Actor { get; init; }

    /// <summary>Required reason for correcting a mistaken declaration.</summary>
    public required string Reason { get; init; }

    /// <summary>Time of correction, not execution time.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Read-only audit row. Initial and corrected outcomes are separately
/// visible, and neither is evidence of a new test execution.
/// </summary>
/// <param name="PassageId">Exact frozen passage.</param>
/// <param name="TestId">Stable block identity.</param>
/// <param name="ResultId">Original recorded result identity.</param>
/// <param name="OriginalOutcome">Untouched human declaration.</param>
/// <param name="CorrectedOutcome">Later administrative correction.</param>
/// <param name="Actor">Human who approved the correction.</param>
/// <param name="Reason">Explicit correction reason.</param>
/// <param name="RecordedUtc">Time of correction, not execution.</param>
public sealed record ProjectTestResultCorrectionRow(
    Guid PassageId, Guid TestId, Guid ResultId,
    ProjectTestOutcome OriginalOutcome, ProjectTestOutcome CorrectedOutcome,
    string Actor, string Reason, DateTimeOffset RecordedUtc);

/// <summary>
/// Validates and projects append-only result correction events without
/// rewriting frozen passages or changing test execution status.
/// </summary>
public static class ProjectTestResultCorrectionPolicy
{
    /// <summary>Validates cross-references, uniqueness and human audit fields.</summary>
    /// <param name="campaign">Campaign with optional correction journal.</param>
    public static void Validate(ProjectTestCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (campaign.ResultCorrections is null)
        {
            return;
        }

        if (campaign.ResultCorrections.Count > 2048)
        {
            throw new InvalidDataException("Journal de rectifications REC trop long.");
        }

        Dictionary<Guid, (Guid PassageId, ProjectTestResultEvent Result)> results = [];
        HashSet<Guid> occupiedIds = [campaign.Id];
        foreach (ProjectTestPassage passage in campaign.Passages)
        {
            occupiedIds.Add(passage.Id);
            foreach (ProjectTestResultEvent result in passage.Results)
            {
                if (!results.TryAdd(result.Id, (passage.Id, result)))
                {
                    throw new InvalidDataException(
                        "Résultat REC ambigu : même GUID dans plusieurs passages.");
                }

                occupiedIds.Add(result.Id);
            }
        }

        HashSet<Guid> correctedResultIds = [];
        DateTimeOffset last = DateTimeOffset.MinValue;
        foreach (ProjectTestResultCorrectionEvent correction
            in campaign.ResultCorrections)
        {
            if (correction is null ||
                correction.Id == Guid.Empty ||
                !occupiedIds.Add(correction.Id) ||
                correction.PassageId == Guid.Empty ||
                correction.TestId == Guid.Empty ||
                correction.ResultId == Guid.Empty ||
                !results.TryGetValue(correction.ResultId,
                    out (Guid PassageId, ProjectTestResultEvent Result) source) ||
                source.PassageId != correction.PassageId ||
                source.Result.TestId != correction.TestId ||
                !correctedResultIds.Add(correction.ResultId) ||
                !Enum.IsDefined(correction.CorrectedOutcome) ||
                correction.CorrectedOutcome == source.Result.Outcome ||
                !HumanField(correction.Actor, 160) ||
                !HumanField(correction.Reason, 2000) ||
                correction.RecordedUtc < source.Result.RecordedUtc ||
                correction.RecordedUtc < campaign.CreatedUtc ||
                correction.RecordedUtc < last)
            {
                throw new InvalidDataException(
                    "Rectification REC invalide, ambiguë ou non historisable.");
            }

            last = correction.RecordedUtc;
        }
    }

    /// <summary>
    /// Exposes original and corrected declarations separately for audit.
    /// Frozen result projections remain unchanged, by design.
    /// </summary>
    /// <param name="campaign">Validated REC campaign.</param>
    /// <param name="passageId">Requested passage identity.</param>
    /// <returns>Correction rows in append-only recorded order.</returns>
    public static IReadOnlyList<ProjectTestResultCorrectionRow> ForPassage(
        ProjectTestCampaign campaign, Guid passageId)
    {
        ProjectTestCampaignMarkdownCodec.Validate(campaign);
        ProjectTestPassage passage = campaign.Passages.Single(
            item => item.Id == passageId);
        Dictionary<Guid, ProjectTestResultEvent> originals = passage.Results
            .ToDictionary(item => item.Id);
        return (campaign.ResultCorrections ?? [])
            .Where(item => item.PassageId == passageId)
            .Select(item => new ProjectTestResultCorrectionRow(
                item.PassageId, item.TestId, item.ResultId,
                originals[item.ResultId].Outcome, item.CorrectedOutcome,
                item.Actor, item.Reason, item.RecordedUtc))
            .ToArray();
    }

    /// <summary>Checks a nonempty single-line human audit field.</summary>
    /// <param name="value">Actor or reason.</param>
    /// <param name="limit">Maximum permitted characters.</param>
    /// <returns>Whether this declaration is safe to store.</returns>
    private static bool HumanField(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= limit && !value.Any(char.IsControl);
}
