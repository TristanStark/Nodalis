using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Explicit human qualifications and decisions for one REC campaign.
/// No test verdict or EXI validation is derived from this lifecycle.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    /// <summary>
    /// Records one approved lifecycle action as an append-only event.
    /// Closure remains non-blocking even with failed/blocked/unrun tests;
    /// the reviewer must supply an explicit rationale and verdict.
    /// </summary>
    /// <param name="projectDirectory">Owning active project.</param>
    /// <param name="snapshot">User-reviewed source revision.</param>
    /// <param name="kind">Human-selected transition.</param>
    /// <param name="actor">Human responsible for the decision.</param>
    /// <param name="rationale">Required justification.</param>
    /// <param name="verdict">Required only on closure.</param>
    /// <param name="reservations">Explicit human reservations.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted campaign with immutable decision history.</returns>
    public async Task<ProjectTestCampaignEntry> RecordDecisionAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        ProjectTestCampaignDecisionKind kind,
        string actor, string rationale,
        ProjectTestCampaignVerdict? verdict,
        IReadOnlyList<string>? reservations,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) ||
            !HumanField(actor, 160) ||
            !HumanField(rationale, 2000) ||
            reservations is { Count: > 50 } ||
            reservations?.Any(item =>
                !HumanField(item, 500)) == true)
        {
            throw new InvalidDataException(
                "Décision, auteur, justification ou réserves invalides.");
        }

        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession session) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        ProjectTestCampaignState target =
            ProjectTestCampaignDecisionPolicy.Transition(kind, campaign.State);

        if (kind == ProjectTestCampaignDecisionKind.Close)
        {
            if (!verdict.HasValue || !Enum.IsDefined(verdict.Value) ||
                verdict == ProjectTestCampaignVerdict.ValidatedWithReservations &&
                    reservations is not { Count: > 0 } ||
                verdict == ProjectTestCampaignVerdict.Validated &&
                    reservations is { Count: > 0 })
            {
                throw new InvalidDataException(
                    "Verdict de clôture ou réserves non conformes.");
            }
        }
        else if (verdict.HasValue || reservations is { Count: > 0 })
        {
            throw new InvalidDataException(
                "Verdict réservé aux seules décisions de clôture.");
        }

        ProjectTestCampaignDecisionEvent decision = new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            FromState = campaign.State,
            ToState = target,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            Verdict = verdict,
            Reservations = reservations?.Select(item => item.Trim()).ToArray(),
            RecordedUtc = DateTimeOffset.UtcNow
        };
        ProjectTestCampaign updated = campaign with
        {
            State = target,
            DecisionHistory = (campaign.DecisionHistory ?? [])
                .Append(decision).ToArray()
        };
        return await CommitAsync(session, updated, cancellationToken);
    }
}
