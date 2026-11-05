using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;

namespace Nodalis.Core.Tests;

/// <summary>
/// Latest human REC observation AFTER the most recent active-link failure
/// for the same stable test. It is advisory and not a resolution proof.
/// </summary>
public sealed record ProjectRecRetestObservation(
    Guid TestId, string Reference, string FailureEnvironment,
    string FailureVersion, DateTimeOffset FailureUtc,
    ProjectTestOutcome? LaterOutcome, DateTimeOffset? LaterUtc,
    string? LaterCampaign, int? LaterPassageNumber,
    string? LaterEnvironment, string? LaterVersion,
    string? LaterEvidence)
{
    /// <summary>Whether a distinct later execution actually exists.</summary>
    public bool HasLaterExecution => LaterOutcome is not null;

    /// <summary>
    /// A later declared success in the original environment is only a
    /// candidate for human assessment, not verified incident recovery.
    /// </summary>
    public bool Candidate => LaterOutcome == ProjectTestOutcome.Passed &&
        string.Equals(LaterEnvironment, FailureEnvironment,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the tested version differs from the failed one.</summary>
    public bool VersionChanged => HasLaterExecution &&
        !string.Equals(LaterVersion, FailureVersion,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Concise, fully qualified traceability status.</summary>
    public string Summary => !HasLaterExecution
        ? Reference + " · non réexécuté après l'échec lié"
        : Reference + " · " + LaterOutcome + " · " +
            LaterCampaign + " / passage " + LaterPassageNumber +
            " · " + LaterEnvironment + " / " + LaterVersion +
            (Candidate
                ? " · conforme DÉCLARÉ, contrôle humain requis"
                : " · revalidation non démontrée") +
            (VersionChanged ? " · version différente" : "");
}

/// <summary>
/// One calculated incident follow-up across multiple REC campaigns and
/// all source-linked maintenance interventions; no shadow register.
/// </summary>
public sealed record ProjectRecIncidentFollowup(
    string IncidentId, string IncidentSummary, bool MissingSource,
    IReadOnlyList<ProjectRecRetestObservation> Tests,
    IReadOnlyList<string> InterventionSummaries,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Unique stable test count for this incident.</summary>
    public int AffectedTestCount => Tests.Count;

    /// <summary>Current tests with later Passed in matching environment.</summary>
    public int LaterPassedCount => Tests.Count(test => test.Candidate);

    /// <summary>
    /// Only a proposal for human review; never an automatically approved
    /// status or a guarantee about deployed versions and live test sources.
    /// </summary>
    public bool ReviewMayBeProposed =>
        !MissingSource && Tests.Count > 0 && Tests.All(test => test.Candidate);

    /// <summary>Short WPF overview without implying technical resolution.</summary>
    public string Label => IncidentId + " · " + IncidentSummary;

    /// <summary>Advisory progress label, not a status transition.</summary>
    public string Summary => LaterPassedCount + "/" + AffectedTestCount +
        " test(s) avec nouveau Passed dans le même environnement · " +
        InterventionSummaries.Count + " intervention(s) déclarée(s)" +
        (ReviewMayBeProposed ? " · examen humain envisageable" :
            " · reprise/revue restante");
}

/// <summary>
/// Read-only join incident → historic failures → latest REC outcomes and
/// technical interventions; no assumption that maintenance is resolved.
/// </summary>
public static class ProjectRecIncidentFollowupPlanner
{
    /// <summary>
    /// Computes incident-specific retest observations after the last active
    /// linked failure for each test, across all same-project campaigns.
    /// </summary>
    /// <param name="projectId">Selected project's manifest GUID.</param>
    /// <param name="failures">Existing normalized historic REC anomaly rows.</param>
    /// <param name="campaigns">Verified canonical REC sources of that project.</param>
    /// <param name="maintenance">Existing parsed maintenance source records.</param>
    /// <returns>Stable incident follow-up rows and explicit limits.</returns>
    public static IReadOnlyList<ProjectRecIncidentFollowup> Build(
        Guid projectId, IReadOnlyList<ProjectRecAnomalyRow> failures,
        IReadOnlyList<ProjectTestCampaign> campaigns,
        IReadOnlyList<MaintenanceRecord> maintenance)
    {
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(campaigns);
        ArgumentNullException.ThrowIfNull(maintenance);
        if (projectId == Guid.Empty || campaigns.Count > 500 ||
            failures.Count > 10000)
        {
            throw new InvalidDataException(
                "Contexte de revalidation REC invalide ou trop volumineux.");
        }

        HashSet<Guid> campaignIds = [];
        List<(ProjectTestPassage Passage, ProjectTestResultEvent Result,
            string Campaign)> observations = [];
        foreach (ProjectTestCampaign campaign in campaigns)
        {
            if (campaign.ProjectId != projectId ||
                !campaignIds.Add(campaign.Id))
            {
                throw new InvalidDataException(
                    "Campagne étrangère ou identité REC ambiguë.");
            }

            foreach (ProjectTestPassage passage in campaign.Passages)
            {
                observations.AddRange(passage.Results.Select(result =>
                    (passage, result, campaign.Reference)));
            }
        }

        // Maintenance identities may be duplicated by damaged Markdown;
        // ambiguous records must never be treated as verified.
        Dictionary<string, MaintenanceRecord[]> incidents = maintenance
            .Where(record => record.Kind == MaintenanceRecordKind.Incident &&
                ProjectGateValidation.IsIncidentId(record.Id))
            .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MaintenanceRecord[]> interventions = maintenance
            .Where(record =>
                record.Kind == MaintenanceRecordKind.Intervention &&
                ProjectGateValidation.IsIncidentId(record.IncidentId))
            .GroupBy(record => record.IncidentId!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        List<ProjectRecIncidentFollowup> followups = [];
        foreach (IGrouping<string, ProjectRecAnomalyRow> group in failures
            .SelectMany(row => row.LinkedIncidentIds.Select(id =>
                (IncidentId: id, Row: row)))
            .GroupBy(entry => entry.IncidentId,
                entry => entry.Row, StringComparer.OrdinalIgnoreCase))
        {
            if (!ProjectGateValidation.IsIncidentId(group.Key))
            {
                throw new InvalidDataException(
                    "Identifiant INC non canonique dans les liens REC.");
            }

            incidents.TryGetValue(group.Key,
                out MaintenanceRecord[]? existing);
            bool missing = existing?.Length != 1;
            string title = missing ? "SOURCE ABSENTE OU AMBIGUË" :
                existing![0].Summary;
            List<ProjectRecRetestObservation> tests = [];
            foreach (IGrouping<Guid, ProjectRecAnomalyRow> related
                in group.GroupBy(item => item.TestId))
            {
                ProjectRecAnomalyRow lastFailure = related
                    .OrderByDescending(item => item.RecordedUtc)
                    .ThenByDescending(item => item.PassageNumber)
                    .First();
                // The newest outcome of the test wins; a subsequent
                // failure supersedes a previous Passed. Same-passage
                // results cannot be treated as a new execution.
                (ProjectTestPassage Passage, ProjectTestResultEvent Result,
                    string Campaign)? latest = observations
                    .Where(item => item.Result.TestId == related.Key &&
                        item.Result.RecordedUtc > lastFailure.RecordedUtc &&
                        item.Passage.Id != lastFailure.PassageId)
                    .OrderByDescending(item => item.Result.RecordedUtc)
                    .ThenByDescending(item => item.Passage.StartedUtc)
                    .Cast<(ProjectTestPassage Passage,
                        ProjectTestResultEvent Result, string Campaign)?>()
                    .FirstOrDefault();
                tests.Add(new ProjectRecRetestObservation(
                    related.Key, lastFailure.TestReference,
                    lastFailure.Environment, lastFailure.ApplicationVersion,
                    lastFailure.RecordedUtc, latest?.Result.Outcome,
                    latest?.Result.RecordedUtc, latest?.Campaign,
                    latest?.Passage.Number, latest?.Passage.Environment,
                    latest?.Passage.ApplicationVersion,
                    latest?.Result.EvidenceNote));
            }

            interventions.TryGetValue(group.Key,
                out MaintenanceRecord[]? technical);
            string[] descriptions = (technical ?? [])
                .GroupBy(record => record.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(duplicate =>
                {
                    MaintenanceRecord record = duplicate.First();
                    string status = record.Details.TryGetValue(
                        "Statut", out string? value) ? value :
                        "statut technique non précisé";
                    return record.Id + " · " +
                        record.Date.ToString("dd/MM/yyyy") + " · " +
                        record.Summary + " · " + status +
                        " (déclaratif, pas une preuve de revalidation)";
                })
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            ProjectRecRetestObservation[] ordered = tests
                .OrderBy(item => item.Reference, StringComparer.Ordinal)
                .ThenBy(item => item.TestId).ToArray();
            List<string> warnings = [];
            if (missing)
            {
                warnings.Add("Source INC absente ou identifiant dupliqué : " +
                    "aucune décision de revalidation proposée.");
            }

            if (ordered.Any(test => !test.HasLaterExecution))
            {
                warnings.Add("Au moins un test lié n'a pas été réexécuté " +
                    "après son dernier échec actif.");
            }

            if (ordered.Any(test => test.HasLaterExecution &&
                !test.Candidate))
            {
                warnings.Add("Des résultats restent non conformes, bloqués " +
                    "ou dans un autre environnement.");
            }

            if (ordered.Any(test => test.VersionChanged))
            {
                warnings.Add("Versions différentes entre l'échec et les " +
                    "reprises : vérifier explicitement la version cible.");
            }

            warnings.Add("Les états des interventions sont déclaratifs. " +
                "Même si tous les tests sont Passed, vérifier les preuves, " +
                "les sources TEST actuelles, le périmètre et les tests de " +
                "non-régression avant toute décision humaine.");
            followups.Add(new ProjectRecIncidentFollowup(
                group.Key, title, missing, ordered, descriptions, warnings));
        }

        return followups.OrderBy(item => item.IncidentId,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
