using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// REC-to-incident association uses the existing maintenance incident
/// repository; no second incident database and no automatic status updates.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    /// <summary>
    /// Appends a human-approved link/unlink for an existing nonconforming
    /// REC result and a stable project-local maintenance incident.
    /// </summary>
    /// <param name="projectDirectory">Owning project, not a guessed path.</param>
    /// <param name="snapshot">Last displayed REC source revision.</param>
    /// <param name="passageId">Immutable passage identity.</param>
    /// <param name="resultId">Recorded human result identity.</param>
    /// <param name="incidentId">Canonical stable INC identifier.</param>
    /// <param name="kind">Explicit human association transition.</param>
    /// <param name="actor">Person making the decision.</param>
    /// <param name="rationale">Written association reason.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New REC revision with an append-only audit event.</returns>
    public async Task<ProjectTestCampaignEntry> RecordIncidentLinkAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        Guid passageId, Guid resultId, string incidentId,
        ProjectTestIncidentLinkKind kind, string actor, string rationale,
        CancellationToken cancellationToken = default)
    {
        if (passageId == Guid.Empty || resultId == Guid.Empty ||
            !ProjectGateValidation.IsIncidentId(incidentId) ||
            !Enum.IsDefined(kind) ||
            !HumanField(actor, 160) || !HumanField(rationale, 2000) ||
            rationale.Trim().Length < 10)
        {
            throw new InvalidDataException(
                "Association REC/incident : référence ou justification invalide.");
        }

        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession session) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        if (campaign.State is not
            (ProjectTestCampaignState.InProgress or
             ProjectTestCampaignState.InQualification))
        {
            throw new InvalidOperationException(
                "La campagne REC doit être ouverte pour modifier ses liens.");
        }

        ProjectTestPassage? passage = campaign.Passages.SingleOrDefault(
            item => item.Id == passageId);
        ProjectTestResultEvent? result = passage?.Results.SingleOrDefault(
            item => item.Id == resultId);
        if (result is null ||
            result.Outcome is not
                (ProjectTestOutcome.Failed or ProjectTestOutcome.Blocked))
        {
            throw new InvalidDataException(
                "Sélectionnez un résultat REC non conforme ou bloqué enregistré.");
        }

        IReadOnlyList<ProjectTestLinkedIncident> active =
            ProjectTestIncidentLinkPolicy.Active(campaign, resultId);
        bool linked = active.Any(item =>
            string.Equals(item.IncidentId, incidentId,
                StringComparison.OrdinalIgnoreCase));
        if (kind == ProjectTestIncidentLinkKind.Linked && linked ||
            kind == ProjectTestIncidentLinkKind.Unlinked && !linked)
        {
            throw new InvalidDataException(
                "Association déjà en place ou dissociation sans lien actif.");
        }

        string canonical = incidentId;
        if (kind == ProjectTestIncidentLinkKind.Linked)
        {
            // Live maintenance document is the sole incident source of truth.
            // Ambiguous, malformed or foreign incidents are never linkable.
            ProjectGateIncidentCatalog catalog =
                await new ProjectGateIncidentCatalogService(_workspaceRoot)
                    .ListAsync(directory, cancellationToken);
            ProjectGateIncidentCandidate? candidate =
                catalog.Candidates.SingleOrDefault(item =>
                    string.Equals(item.Id, incidentId,
                        StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                throw new InvalidDataException(
                    "Incident introuvable ou non qualifiable dans ce projet.");
            }

            canonical = candidate.Id;
        }
        else
        {
            canonical = active.Single(item =>
                string.Equals(item.IncidentId, incidentId,
                    StringComparison.OrdinalIgnoreCase)).IncidentId;
        }

        ProjectTestIncidentLinkEvent entry = new()
        {
            Id = Guid.NewGuid(),
            PassageId = passageId,
            ResultId = resultId,
            TestId = result.TestId,
            IncidentId = canonical,
            Kind = kind,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            RecordedUtc = DateTimeOffset.UtcNow
        };
        ProjectTestCampaign updated = campaign with
        {
            IncidentLinks = (campaign.IncidentLinks ?? [])
                .Append(entry).ToArray()
        };
        return await CommitAsync(session, updated, cancellationToken);
    }
}
