using Nodalis.Core.Projects;

namespace Nodalis.Core.Tests;

/// <summary>Human association changes; unlink never erases the original link.</summary>
public enum ProjectTestIncidentLinkKind
{
    Linked,
    Unlinked
}

/// <summary>
/// One explicit REC result-to-maintenance incident association decision.
/// The incident remains in its canonical Fiche de Maintenance.md source.
/// </summary>
public sealed record ProjectTestIncidentLinkEvent
{
    /// <summary>Unique durable audit identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Frozen REC passage identity.</summary>
    public required Guid PassageId { get; init; }

    /// <summary>Original human result event identity.</summary>
    public required Guid ResultId { get; init; }

    /// <summary>Frozen stable TEST block identity.</summary>
    public required Guid TestId { get; init; }

    /// <summary>Canonical maintenance INC-prefixed stable GUID.</summary>
    public required string IncidentId { get; init; }

    /// <summary>Explicit human link or unlink decision.</summary>
    public required ProjectTestIncidentLinkKind Kind { get; init; }

    /// <summary>Human actor approving the association.</summary>
    public required string Actor { get; init; }

    /// <summary>Human reason; never synthesized from a test outcome.</summary>
    public required string Rationale { get; init; }

    /// <summary>UTC timestamp of the decision, not of the test execution.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Read-only human-linked incident view for one recorded result. The incident
/// source, status and actual resolution are NOT inferred from this projection.
/// </summary>
/// <param name="IncidentId">Stable maintenance incident identity.</param>
/// <param name="LinkedUtc">Last explicit human association time.</param>
/// <param name="Actor">Person who linked the incident.</param>
public sealed record ProjectTestLinkedIncident(
    string IncidentId, DateTimeOffset LinkedUtc, string Actor);

/// <summary>
/// Conservative append-only link replay. Same incident may relate to many
/// tests, and the same test may link many distinct incidents independently.
/// </summary>
public static class ProjectTestIncidentLinkPolicy
{
    private const int MaxEvents = 4096;

    /// <summary>Validates all event identities, context and transition order.</summary>
    /// <param name="campaign">Untrusted REC snapshot.</param>
    public static void Validate(ProjectTestCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (campaign.IncidentLinks is null)
        {
            return;
        }

        if (campaign.IncidentLinks.Count > MaxEvents)
        {
            throw new InvalidDataException(
                "Historique d'associations REC/incident trop long.");
        }

        Dictionary<Guid, (Guid PassageId, ProjectTestResultEvent Result)> results = [];
        HashSet<Guid> occupied = [campaign.Id];
        foreach (ProjectTestPassage passage in campaign.Passages)
        {
            occupied.Add(passage.Id);
            foreach (ProjectTestResultEvent result in passage.Results)
            {
                if (!results.TryAdd(result.Id, (passage.Id, result)))
                {
                    throw new InvalidDataException(
                        "Résultat REC ambigu dans plusieurs passages.");
                }

                occupied.Add(result.Id);
            }
        }

        if (campaign.ResultCorrections is not null)
        {
            foreach (ProjectTestResultCorrectionEvent correction
                in campaign.ResultCorrections)
            {
                occupied.Add(correction.Id);
            }
        }

        Dictionary<(Guid ResultId, string IncidentId),
            (bool Linked, DateTimeOffset LastTime)> state = [];
        DateTimeOffset globalLast = DateTimeOffset.MinValue;
        foreach (ProjectTestIncidentLinkEvent entry in campaign.IncidentLinks)
        {
            if (entry is null || entry.Id == Guid.Empty ||
                !occupied.Add(entry.Id) ||
                !Enum.IsDefined(entry.Kind) ||
                !ProjectGateValidation.IsIncidentId(entry.IncidentId) ||
                !results.TryGetValue(entry.ResultId,
                    out (Guid PassageId, ProjectTestResultEvent Result) source) ||
                entry.PassageId != source.PassageId ||
                entry.TestId != source.Result.TestId ||
                source.Result.Outcome is not
                    (ProjectTestOutcome.Failed or ProjectTestOutcome.Blocked) ||
                !HumanField(entry.Actor, 160) ||
                !HumanField(entry.Rationale, 2000) ||
                entry.Rationale.Trim().Length < 10 ||
                entry.RecordedUtc == default ||
                entry.RecordedUtc < source.Result.RecordedUtc ||
                entry.RecordedUtc < globalLast)
            {
                throw new InvalidDataException(
                    "Association REC/incident sans résultat humain valide.");
            }

            (Guid ResultId, string IncidentId) key = (
                entry.ResultId, entry.IncidentId.ToUpperInvariant());
            state.TryGetValue(key,
                out (bool Linked, DateTimeOffset LastTime) previous);
            if (entry.RecordedUtc < previous.LastTime ||
                entry.Kind == ProjectTestIncidentLinkKind.Linked &&
                    previous.Linked ||
                entry.Kind == ProjectTestIncidentLinkKind.Unlinked &&
                    !previous.Linked)
            {
                throw new InvalidDataException(
                    "Lien REC/incident doublonné ou dissociation orpheline.");
            }

            state[key] = (
                entry.Kind == ProjectTestIncidentLinkKind.Linked,
                entry.RecordedUtc);
            globalLast = entry.RecordedUtc;
        }
    }

    /// <summary>Projects only active links to this exact result.</summary>
    /// <param name="campaign">Fully validated REC snapshot.</param>
    /// <param name="resultId">Original human result GUID.</param>
    /// <returns>Distinct active incident links, never inferred status.</returns>
    public static IReadOnlyList<ProjectTestLinkedIncident> Active(
        ProjectTestCampaign campaign, Guid resultId)
    {
        // REC snapshots are fully validated at Parse/OpenVerified. Replaying
        // associations must not rehash and reparse 150+ frozen TEST sections
        // on every WPF selection change; validate the small audit graph only.
        Validate(campaign);
        Dictionary<string, ProjectTestLinkedIncident> active = new(
            StringComparer.OrdinalIgnoreCase);
        foreach (ProjectTestIncidentLinkEvent entry
            in campaign.IncidentLinks ?? [])
        {
            if (entry.ResultId != resultId)
            {
                continue;
            }

            if (entry.Kind == ProjectTestIncidentLinkKind.Unlinked)
            {
                active.Remove(entry.IncidentId);
            }
            else
            {
                active[entry.IncidentId] = new ProjectTestLinkedIncident(
                    entry.IncidentId, entry.RecordedUtc, entry.Actor);
            }
        }

        return active.Values.OrderBy(item => item.IncidentId,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Checks a bounded human audit field.</summary>
    /// <param name="value">Actor or rationale.</param>
    /// <param name="maximum">Maximum characters.</param>
    /// <returns>True when suitable for local audit storage.</returns>
    private static bool HumanField(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
}
