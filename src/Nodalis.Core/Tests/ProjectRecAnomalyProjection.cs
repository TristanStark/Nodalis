using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;

namespace Nodalis.Core.Tests;

/// <summary>
/// One historic failed/blocked human REC declaration. Linked maintenance
/// incidents and interventions are informative, never a resolution verdict.
/// </summary>
public sealed record ProjectRecAnomalyRow(
    string CampaignReference, Guid CampaignId, int PassageNumber,
    Guid PassageId, Guid TestId, string TestReference, Guid ResultId,
    ProjectTestOutcome OriginalOutcome, ProjectTestOutcome? RectifiedOutcome,
    string Environment, string ApplicationVersion, DateTimeOffset RecordedUtc,
    string? DeclaredEvidence, IReadOnlyList<string> LinkedIncidentIds,
    IReadOnlyList<string> MissingIncidentIds,
    IReadOnlyList<string> IncidentDescriptions, int InterventionCount)
{
    /// <summary>One readable, source-derived list label.</summary>
    public string Label => CampaignReference + " · Passage " + PassageNumber +
        " · " + TestReference;

    /// <summary>Historic status and explicit link diagnostics.</summary>
    public string Summary => OriginalOutcome + " · " +
        (LinkedIncidentIds.Count == 0
            ? "sans incident associé"
            : LinkedIncidentIds.Count + " incident(s) associé(s)") +
        (MissingIncidentIds.Count > 0
            ? " · " + MissingIncidentIds.Count + " source(s) absente(s)" : "");
}

/// <summary>
/// Derived projection only. No extra anomaly register is persisted, and an
/// intervention or later Passed outcome never closes a former failure.
/// </summary>
/// <param name="Rows">Historical failures, one row per recorded REC result.</param>
/// <param name="Diagnostics">Incomplete or ambiguous source warnings.</param>
public sealed record ProjectRecAnomalyOverview(
    IReadOnlyList<ProjectRecAnomalyRow> Rows,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>
    /// Calculated incident-level follow-up from the same project sources.
    /// No additional file, write or extra directory scan is required.
    /// </summary>
    public IReadOnlyList<ProjectRecIncidentFollowup> IncidentFollowups
    {
        get;
        init;
    } = [];

    /// <summary>Failures without an active incident association.</summary>
    public int UnlinkedCount => Rows.Count(item =>
        item.LinkedIncidentIds.Count == 0);

    /// <summary>Failures with one or more unresolved incident source IDs.</summary>
    public int MissingSourceCount => Rows.Count(item =>
        item.MissingIncidentIds.Count > 0);
}

/// <summary>
/// Builds a non-authoritative REC/maintenance join from validated Markdown
/// sources, with no automatic closure, recovery or proof validation.
/// </summary>
public static class ProjectRecAnomalyProjection
{
    /// <summary>Produces a stable read-only view from canonical inputs.</summary>
    /// <param name="projectId">Current project manifest GUID.</param>
    /// <param name="campaigns">Project-local REC records already read.</param>
    /// <param name="maintenance">Parsed maintenance records, possibly partial.</param>
    /// <param name="diagnostics">Read errors and incomplete-source warnings.</param>
    /// <returns>Historical anomaly rows and conservative warnings.</returns>
    public static ProjectRecAnomalyOverview Build(
        Guid projectId, IReadOnlyList<ProjectTestCampaign> campaigns,
        IReadOnlyList<MaintenanceRecord> maintenance,
        IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(campaigns);
        ArgumentNullException.ThrowIfNull(maintenance);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (projectId == Guid.Empty || campaigns.Count > 500)
        {
            throw new InvalidDataException("Identité projet ou registre REC invalide.");
        }

        List<string> warnings = [.. diagnostics];
        Dictionary<string, MaintenanceRecord> incidents = new(
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> duplicated = maintenance
            .Where(item => item.Kind == MaintenanceRecordKind.Incident &&
                ProjectGateValidation.IsIncidentId(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MaintenanceRecord record in maintenance.Where(item =>
            item.Kind == MaintenanceRecordKind.Incident &&
            ProjectGateValidation.IsIncidentId(item.Id) &&
            !duplicated.Contains(item.Id)))
        {
            incidents.Add(record.Id, record);
        }

        if (duplicated.Count > 0)
        {
            warnings.Add(duplicated.Count +
                " identifiant(s) d'incident dupliqué(s) : sources ambiguës.");
            foreach (string id in duplicated)
            {
                incidents.Remove(id);
            }
        }

        Dictionary<string, int> interventions = maintenance
            .Where(item => item.Kind == MaintenanceRecordKind.Intervention &&
                ProjectGateValidation.IsIncidentId(item.IncidentId))
            .GroupBy(item => item.IncidentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(item => item.Id).Distinct(
                    StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.OrdinalIgnoreCase);

        List<ProjectRecAnomalyRow> rows = [];
        HashSet<Guid> campaignIds = [];
        foreach (ProjectTestCampaign campaign in campaigns)
        {
            ProjectTestCampaignMarkdownCodec.Validate(campaign);
            if (campaign.ProjectId != projectId ||
                !campaignIds.Add(campaign.Id))
            {
                throw new InvalidDataException(
                    "Campagne REC étrangère ou identité de campagne ambiguë.");
            }

            // One event replay per campaign, instead of validating and
            // scanning every frozen definition for each WPF row.
            Dictionary<Guid, Dictionary<string, string>> active = [];
            foreach (ProjectTestIncidentLinkEvent link
                in campaign.IncidentLinks ?? [])
            {
                if (!active.TryGetValue(link.ResultId,
                        out Dictionary<string, string>? known))
                {
                    known = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                    active.Add(link.ResultId, known);
                }

                if (link.Kind == ProjectTestIncidentLinkKind.Linked)
                {
                    known[link.IncidentId] = link.IncidentId;
                }
                else
                {
                    known.Remove(link.IncidentId);
                }
            }

            Dictionary<Guid, ProjectTestResultCorrectionEvent> corrections =
                (campaign.ResultCorrections ?? [])
                .ToDictionary(item => item.ResultId);
            foreach (ProjectTestPassage passage in campaign.Passages)
            {
                Dictionary<Guid, ProjectTestFrozenDefinition> tests = passage.Tests
                    .ToDictionary(item => item.TestId);
                foreach (ProjectTestResultEvent result in passage.Results)
                {
                    if (result.Outcome is not
                        (ProjectTestOutcome.Failed or
                         ProjectTestOutcome.Blocked))
                    {
                        continue;
                    }

                    if (rows.Count >= 10000)
                    {
                        throw new InvalidDataException(
                            "Plus de 10 000 anomalies REC : restreindre la vue.");
                    }

                    ProjectTestFrozenDefinition definition =
                        tests[result.TestId];
                    string[] linked = active.TryGetValue(result.Id,
                            out Dictionary<string, string>? known)
                        ? known.Values.OrderBy(id => id,
                            StringComparer.OrdinalIgnoreCase).ToArray()
                        : [];
                    string[] missing = linked.Where(id =>
                        !incidents.ContainsKey(id)).ToArray();
                    string[] descriptions = linked.Select(id =>
                        incidents.TryGetValue(id, out MaintenanceRecord? record)
                            ? id + " — " + record.Summary
                            : id + " — SOURCE ABSENTE OU AMBIGUË").ToArray();
                    int interventionsCount = linked.Where(id =>
                        incidents.ContainsKey(id)).Sum(id =>
                        interventions.GetValueOrDefault(id));
                    corrections.TryGetValue(result.Id,
                        out ProjectTestResultCorrectionEvent? rectification);
                    rows.Add(new ProjectRecAnomalyRow(
                        campaign.Reference, campaign.Id,
                        passage.Number, passage.Id, result.TestId,
                        definition.Reference, result.Id, result.Outcome,
                        rectification?.CorrectedOutcome,
                        passage.Environment, passage.ApplicationVersion,
                        result.RecordedUtc, result.EvidenceNote,
                        linked, missing, descriptions, interventionsCount));
                }
            }
        }

        if (rows.Any(item => item.MissingIncidentIds.Count > 0))
        {
            warnings.Add("Certains liens INC ne résolvent plus vers une source " +
                "de maintenance unique : aucune résolution inférée.");
        }

        warnings.Add("Les passages historiques, les interventions et les " +
            "rectifications administratives ne prouvent pas la revalidation " +
            "fonctionnelle. Vérification humaine nécessaire.");
        return new ProjectRecAnomalyOverview(rows
            .OrderByDescending(item => item.RecordedUtc)
            .ThenBy(item => item.CampaignReference,
                StringComparer.Ordinal)
            .ThenBy(item => item.PassageNumber)
            .ToArray(), warnings)
        {
            IncidentFollowups = rows.Any(item =>
                item.LinkedIncidentIds.Count > 0)
                ? ProjectRecIncidentFollowupPlanner.Build(
                    projectId, rows, campaigns, maintenance)
                : []
        };
    }
}
