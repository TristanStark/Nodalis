using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Human-only, SHA-guarded append of administrative corrections to REC.
/// Frozen passages and original outcomes are never modified.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    /// <summary>
    /// Appends a traceable correction to a mistaken result declaration.
    /// This is NOT a test execution; use a new passage for retesting.
    /// Closed/canceled campaigns must be explicitly reopened beforehand.
    /// </summary>
    /// <param name="projectDirectory">Project owning the REC.</param>
    /// <param name="snapshot">Last displayed REC and source SHA.</param>
    /// <param name="passageId">Frozen passage containing the result.</param>
    /// <param name="resultId">Original immutable result event identity.</param>
    /// <param name="correctedOutcome">Corrected human declaration.</param>
    /// <param name="actor">Human authorizing the correction.</param>
    /// <param name="reason">Explanation of the clerical error.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted REC with appended audit event and new SHA.</returns>
    public async Task<ProjectTestCampaignEntry> RectifyResultAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        Guid passageId, Guid resultId, ProjectTestOutcome correctedOutcome,
        string actor, string reason,
        CancellationToken cancellationToken = default)
    {
        if (passageId == Guid.Empty || resultId == Guid.Empty ||
            !Enum.IsDefined(correctedOutcome) ||
            !HumanField(actor, 160) || !HumanField(reason, 2000))
        {
            throw new InvalidDataException(
                "Rectification : passage, résultat, acteur ou motif invalide.");
        }

        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession session) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        if (campaign.State is not
            (ProjectTestCampaignState.InProgress or
             ProjectTestCampaignState.InQualification))
        {
            throw new InvalidOperationException(
                "Réouvrez la campagne avant de rectifier un résultat.");
        }

        ProjectTestPassage? passage = campaign.Passages.SingleOrDefault(
            item => item.Id == passageId);
        ProjectTestResultEvent? original = passage?.Results.SingleOrDefault(
            item => item.Id == resultId);
        if (original is null ||
            original.Outcome == correctedOutcome ||
            campaign.ResultCorrections?.Any(item => item.ResultId == resultId)
                == true ||
            campaign.ResultCorrections?.Count >= 2048)
        {
            throw new InvalidDataException(
                "Résultat absent, identique ou déjà rectifié.");
        }

        ProjectTestResultCorrectionEvent correction = new()
        {
            Id = Guid.NewGuid(),
            PassageId = passageId,
            ResultId = resultId,
            TestId = original.TestId,
            CorrectedOutcome = correctedOutcome,
            Actor = actor.Trim(),
            Reason = reason.Trim(),
            RecordedUtc = DateTimeOffset.UtcNow
        };
        ProjectTestCampaign updated = campaign with
        {
            ResultCorrections = (campaign.ResultCorrections ?? [])
                .Append(correction).ToArray()
        };
        return await CommitAsync(session, updated, cancellationToken);
    }
}
