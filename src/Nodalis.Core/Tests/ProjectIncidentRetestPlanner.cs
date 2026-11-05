using Nodalis.Core.Projects;

namespace Nodalis.Core.Tests;

/// <summary>
/// One advisory test to consider for retesting following an incident.
/// Reasons can include multiple historic failures of the same stable block.
/// </summary>
/// <param name="TestId">Stable test block identity.</param>
/// <param name="Reference">Frozen human test code.</param>
/// <param name="Reasons">Read-only history, never a retest verdict.</param>
/// <param name="HasLaterPassed">A later Passed exists but is not proof of fix.</param>
/// <param name="HasAdministrativeCorrection">A manual input rectification exists.</param>
public sealed record ProjectIncidentRetestCandidate(
    Guid TestId, string Reference, IReadOnlyList<string> Reasons,
    bool HasLaterPassed, bool HasAdministrativeCorrection);

/// <summary>
/// Project-scoped suggestion for ONE named incident. It cannot start a REC
/// passage without manual selection, current-source checks and approval.
/// </summary>
/// <param name="IncidentId">Canonical incident chosen by a person.</param>
/// <param name="Candidates">Current indexed TEST identities proposed.</param>
/// <param name="UnavailableCount">Historic tests not safely selectable.</param>
/// <param name="Warnings">Context and uncertainty warnings.</param>
public sealed record ProjectIncidentRetestProposal(
    string IncidentId, IReadOnlyList<ProjectIncidentRetestCandidate> Candidates,
    int UnavailableCount, IReadOnlyList<string> Warnings);

/// <summary>
/// Read-only, deterministic retest suggestion from explicit REC-to-INC
/// associations. No assumptions about incident resolution or interventions.
/// </summary>
public static class ProjectIncidentRetestPlanner
{
    /// <summary>
    /// Proposes current test IDs previously linked to one INC via a human
    /// failed/blocked REC result. Never copies verdicts between passages.
    /// </summary>
    /// <param name="projectId">Selected project manifest identity.</param>
    /// <param name="incidentId">Human-selected incident stable code.</param>
    /// <param name="campaigns">Canonical same-project REC source models.</param>
    /// <param name="selectableTestIds">Unique project-local current test IDs.</param>
    /// <returns>Editable, nonauthorizing test selection proposal.</returns>
    public static ProjectIncidentRetestProposal Propose(
        Guid projectId, string incidentId,
        IReadOnlyList<ProjectTestCampaign> campaigns,
        IReadOnlySet<Guid> selectableTestIds)
    {
        ArgumentNullException.ThrowIfNull(campaigns);
        ArgumentNullException.ThrowIfNull(selectableTestIds);
        if (projectId == Guid.Empty ||
            !ProjectGateValidation.IsIncidentId(incidentId) ||
            campaigns.Count > 500 ||
            selectableTestIds.Contains(Guid.Empty))
        {
            throw new InvalidDataException(
                "Contexte projet, incident ou tests à reprendre invalide.");
        }

        HashSet<Guid> campaignIds = [];
        Dictionary<Guid, List<string>> reasons = [];
        Dictionary<Guid, (string Reference, DateTimeOffset LastFailure,
            bool Correction)> details = [];
        Dictionary<Guid, DateTimeOffset> laterPasses = [];
        foreach (ProjectTestCampaign campaign in campaigns)
        {
            ProjectTestCampaignMarkdownCodec.Validate(campaign);
            if (campaign.ProjectId != projectId ||
                !campaignIds.Add(campaign.Id))
            {
                throw new InvalidDataException(
                    "Campagne REC étrangère ou identité non unique.");
            }

            HashSet<Guid> linkedResults = [];
            foreach (ProjectTestIncidentLinkEvent link
                in campaign.IncidentLinks ?? [])
            {
                if (!string.Equals(link.IncidentId, incidentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (link.Kind == ProjectTestIncidentLinkKind.Linked)
                {
                    linkedResults.Add(link.ResultId);
                }
                else
                {
                    linkedResults.Remove(link.ResultId);
                }
            }

            HashSet<Guid> correctedResults = (campaign.ResultCorrections ?? [])
                .Select(item => item.ResultId).ToHashSet();
            foreach (ProjectTestPassage passage in campaign.Passages)
            {
                Dictionary<Guid, ProjectTestFrozenDefinition> frozen =
                    passage.Tests.ToDictionary(item => item.TestId);
                foreach (ProjectTestResultEvent result in passage.Results)
                {
                    if (result.Outcome == ProjectTestOutcome.Passed)
                    {
                        if (!laterPasses.TryGetValue(result.TestId,
                                out DateTimeOffset known) ||
                            result.RecordedUtc > known)
                        {
                            laterPasses[result.TestId] = result.RecordedUtc;
                        }
                    }

                    if (!linkedResults.Contains(result.Id))
                    {
                        continue;
                    }

                    if (result.Outcome is not
                        (ProjectTestOutcome.Failed or ProjectTestOutcome.Blocked))
                    {
                        throw new InvalidDataException(
                            "Lien incident sans résultat REC non conforme.");
                    }

                    ProjectTestFrozenDefinition test = frozen[result.TestId];
                    if (!reasons.TryGetValue(result.TestId,
                            out List<string>? history))
                    {
                        history = [];
                        reasons.Add(result.TestId, history);
                        details.Add(result.TestId, (
                            test.Reference, result.RecordedUtc,
                            correctedResults.Contains(result.Id)));
                    }

                    history.Add(campaign.Reference + " / passage " +
                        passage.Number + " : " + result.Outcome +
                        " déclaré, " + passage.Environment +
                        " / " + passage.ApplicationVersion);
                    (string Reference, DateTimeOffset LastFailure,
                        bool Correction) previous = details[result.TestId];
                    details[result.TestId] = (
                        test.Reference, result.RecordedUtc > previous.LastFailure
                            ? result.RecordedUtc : previous.LastFailure,
                        previous.Correction || correctedResults.Contains(
                            result.Id));
                }
            }
        }

        int unavailable = reasons.Keys.Count(id =>
            !selectableTestIds.Contains(id));
        if (reasons.Keys.Count(id => selectableTestIds.Contains(id)) > 500)
        {
            throw new InvalidDataException(
                "Plus de 500 tests proposés pour cet incident : " +
                "une sélection manuelle ciblée est nécessaire.");
        }

        ProjectIncidentRetestCandidate[] candidates = reasons
            .Where(entry => selectableTestIds.Contains(entry.Key))
            .Select(entry =>
            {
                (string Reference, DateTimeOffset LastFailure,
                    bool Correction) info = details[entry.Key];
                bool laterPassed = laterPasses.TryGetValue(entry.Key,
                    out DateTimeOffset timestamp) &&
                    timestamp > info.LastFailure;
                return new ProjectIncidentRetestCandidate(
                    entry.Key, info.Reference, entry.Value.ToArray(),
                    laterPassed, info.Correction);
            })
            .OrderBy(item => item.Reference, StringComparer.Ordinal)
            .ThenBy(item => item.TestId).ToArray();

        List<string> warnings = [];
        if (unavailable > 0)
        {
            warnings.Add(unavailable + " test(s) lié(s) à l'incident " +
                "non sélectionnable(s) : source absente, déplacée, " +
                "ambiguë ou hors du référentiel indexé actuel.");
        }

        if (candidates.Any(item => item.HasLaterPassed))
        {
            warnings.Add("Des passages Passed plus récents existent, mais " +
                "ils ne prouvent pas la revalidation de cet incident.");
        }

        if (candidates.Any(item => item.HasAdministrativeCorrection))
        {
            warnings.Add("Certains verdicts historiques portent une " +
                "rectification administrative : contrôle humain requis.");
        }

        if (candidates.Length == 0)
        {
            warnings.Add("Aucun test actuellement présélectionnable via " +
                "les liens actifs REC/incident. Vérifiez la fiche et " +
                "utilisez la sélection manuelle si nécessaire.");
        }

        warnings.Add("La proposition ne clôture aucun incident, ne change " +
            "aucun verdict ni preuve ; la sélection reste modifiable " +
            "et un nouveau passage exige une confirmation humaine.");
        return new ProjectIncidentRetestProposal(
            incidentId, candidates, unavailable, warnings);
    }
}
