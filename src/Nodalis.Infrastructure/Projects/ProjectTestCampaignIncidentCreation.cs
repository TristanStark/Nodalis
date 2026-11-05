using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Approved exact source revisions before opening a human maintenance form.
/// No file is created, migrated or rewritten during this preview.
/// </summary>
/// <param name="MaintenanceSha256">Exact SHA-256 of the existing maintenance sheet.</param>
/// <param name="IncidentCount">Number of complete, canonical incidents already there.</param>
/// <param name="RelativePath">Resolved workspace-local maintenance source path.</param>
public sealed record ProjectRecIncidentCreationPreview(
    string MaintenanceSha256, int IncidentCount, string RelativePath);

/// <summary>
/// Two-file creation cannot be represented as one atomic Markdown write.
/// The first committed maintenance record is never deleted on a later REC
/// conflict; this result explicitly distinguishes an unlinked new incident.
/// </summary>
/// <param name="IncidentId">New canonical incident identity.</param>
/// <param name="LinkedCampaign">New REC revision if association succeeded.</param>
/// <param name="LinkWarning">Actionable warning when the REC link was not saved.</param>
public sealed record ProjectRecIncidentCreationResult(
    string IncidentId, ProjectTestCampaignEntry? LinkedCampaign,
    string? LinkWarning)
{
    /// <summary>Whether the separate REC association was durably committed.</summary>
    public bool IsLinked => LinkedCampaign is not null;
}

/// <summary>
/// Adds an incident to the existing canonical maintenance sheet only after
/// human approval. Tries a distinct REC audit-link commit and reports partial
/// completion explicitly instead of losing or duplicating the incident.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    /// <summary>
    /// Resolves REC context and a clean existing maintenance sheet with
    /// optimistic revisions; opening the incident form has no side effects.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="snapshot">Exact previously shown REC revision.</param>
    /// <param name="passageId">Historical frozen passage identity.</param>
    /// <param name="resultId">Failed/blocked human result identity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Exact maintenance revision and source hints.</returns>
    public async Task<ProjectRecIncidentCreationPreview> InspectIncidentCreationAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        Guid passageId, Guid resultId,
        CancellationToken cancellationToken = default)
    {
        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession _) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        RequireCreateableResult(campaign, passageId, resultId);
        (TextDocumentSession maintenance, string relative) =
            await OpenMaintenanceForRecAsync(directory, cancellationToken);
        MaintenanceParseResult parsed = MaintenanceMarkdownCodec.Parse(
            maintenance.Content);
        if (parsed.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Historique de maintenance ambigu ou invalide : " +
                "corrigez la fiche avant de créer un incident depuis REC.");
        }

        int canonical = parsed.Records.Count(item =>
            item.Kind == MaintenanceRecordKind.Incident &&
            ProjectGateValidation.IsIncidentId(item.Id));
        return new ProjectRecIncidentCreationPreview(
            maintenance.Revision.Sha256, canonical, relative);
    }

    /// <summary>
    /// Performs human-approved maintenance creation, then attempts the
    /// separate human audit link to the immutable REC result. A failed link
    /// is reported as a partial success and never rolls back maintenance.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="snapshot">Previously selected REC revision.</param>
    /// <param name="passageId">Frozen source passage.</param>
    /// <param name="resultId">Human Failed/Blocked event.</param>
    /// <param name="preview">Maintenance SHA approved before the form.</param>
    /// <param name="incident">Explicit human-edited incident, with new ID.</param>
    /// <param name="actor">Human requester for the audit link.</param>
    /// <param name="reason">Explicit association reason.</param>
    /// <param name="cancellationToken">Cancellation before each commit.</param>
    /// <returns>Incident ID, linked revision or manual recovery warning.</returns>
    public async Task<ProjectRecIncidentCreationResult> CreateIncidentForResultAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        Guid passageId, Guid resultId,
        ProjectRecIncidentCreationPreview preview,
        MaintenanceRecord incident, string actor, string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(incident);
        if (incident.Kind != MaintenanceRecordKind.Incident ||
            !ProjectGateValidation.IsIncidentId(incident.Id) ||
            incident.IncidentId is not null ||
            preview.MaintenanceSha256.Length != 64 ||
            !preview.MaintenanceSha256.All(Uri.IsHexDigit) ||
            !HumanField(actor, 160) ||
            !HumanField(reason, 2000) || reason.Trim().Length < 10)
        {
            throw new InvalidDataException(
                "Création incident REC : identification ou audit non valide.");
        }

        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession _) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        (ProjectTestPassage passage, ProjectTestResultEvent result,
            ProjectTestFrozenDefinition test) =
                RequireCreateableResult(campaign, passageId, resultId);

        (TextDocumentSession maintenance, string relative) =
            await OpenMaintenanceForRecAsync(directory, cancellationToken);
        if (!string.Equals(preview.MaintenanceSha256,
                maintenance.Revision.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(preview.RelativePath, relative,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fiche de maintenance modifiée pendant le formulaire : " +
                "réouvrez la création pour éviter d'écraser une modification.");
        }

        Dictionary<string, string> details = new(
            StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in incident.Details)
        {
            details.Add(entry.Key, entry.Value);
        }

        // Durable provenance, never an implied proof or status.
        details["REC - campagne"] = campaign.Reference + " / " +
            campaign.Id.ToString("D");
        details["REC - passage"] = passage.Number + " / " +
            passage.Id.ToString("D");
        details["REC - test"] = test.Reference + " / " +
            result.TestId.ToString("D");
        details["REC - resultat"] = result.Id.ToString("D");
        details["REC - verdict historique"] = result.Outcome.ToString();
        details["REC - version"] = SafeLine(passage.ApplicationVersion, 300);
        details["REC - environnement"] = SafeLine(passage.Environment, 300);
        details["REC - auteur association"] = actor.Trim();
        details["REC - justification association"] = reason.Trim();
        if (!string.IsNullOrWhiteSpace(result.EvidenceNote))
        {
            details["REC - preuve declaree non verifiee"] =
                SafeLine(result.EvidenceNote, 500);
        }

        MaintenanceRecord created = incident with { Details = details };
        string updated = MaintenanceMarkdownCodec.Append(
            maintenance.Content, created);
        cancellationToken.ThrowIfCancellationRequested();
        // Atomic revision check happens on the authoritative existing
        // Fiche de Maintenance.md, not on a new incident shadow register.
        await maintenance.SaveAsync(updated, cancellationToken);

        try
        {
            // Separate, SHA-CAS verified REC transaction. It may fail if
            // REC changes between the two files; the incident is still real.
            ProjectTestCampaignEntry linked = await RecordIncidentLinkAsync(
                projectDirectory, snapshot, passageId, resultId,
                created.Id, ProjectTestIncidentLinkKind.Linked,
                actor, reason, cancellationToken);
            return new ProjectRecIncidentCreationResult(
                created.Id, linked, null);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
                InvalidOperationException or UnauthorizedAccessException or
                OperationCanceledException)
        {
            return new ProjectRecIncidentCreationResult(
                created.Id, null,
                "Incident créé dans la Fiche de Maintenance, mais lien REC " +
                "non confirmé (" + exception.GetType().Name +
                "). Actualisez la campagne puis utilisez " +
                "« Incidents liés… » avec l'identifiant " + created.Id +
                " ; ne recréez pas l'incident.");
        }
    }

    /// <summary>Rejects nonhuman, nonfailed and closed REC contexts.</summary>
    /// <param name="campaign">Validated current REC.</param>
    /// <param name="passageId">Exact passage identity.</param>
    /// <param name="resultId">Exact historic result identity.</param>
    /// <returns>Unambiguous passage, result and frozen definition.</returns>
    private static (ProjectTestPassage Passage, ProjectTestResultEvent Result,
        ProjectTestFrozenDefinition Test) RequireCreateableResult(
        ProjectTestCampaign campaign, Guid passageId, Guid resultId)
    {
        if (campaign.State is not
            (ProjectTestCampaignState.InProgress or
             ProjectTestCampaignState.InQualification))
        {
            throw new InvalidOperationException(
                "La campagne REC doit rester ouverte pour créer un incident.");
        }

        ProjectTestPassage? passage = campaign.Passages
            .SingleOrDefault(item => item.Id == passageId);
        ProjectTestResultEvent? result = passage?.Results
            .SingleOrDefault(item => item.Id == resultId);
        if (result is null || result.Outcome is not
            (ProjectTestOutcome.Failed or ProjectTestOutcome.Blocked))
        {
            throw new InvalidDataException(
                "Création depuis REC : résultat humain non conforme " +
                "ou bloqué requis.");
        }

        ProjectTestFrozenDefinition test = passage!.Tests.Single(item =>
            item.TestId == result.TestId);
        return (passage, result, test);
    }

    /// <summary>
    /// Retrieves the one already existing project-local maintenance file and
    /// rejects links and oversized sources before opening a text session.
    /// </summary>
    /// <param name="directory">Validated project directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Source session and verified workspace-relative path.</returns>
    private async Task<(TextDocumentSession Session, string Relative)>
        OpenMaintenanceForRecAsync(string directory,
            CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "Fiche de Maintenance.md");
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                "Fiche de Maintenance.md absente : aucune nouvelle fiche " +
                "ne sera créée depuis une campagne REC.");
        }

        string relative = _sources.GetReferenceForExistingFile(path);
        string resolved = _sources.ResolveExistingFile(relative);
        if (new FileInfo(resolved).Length > 25 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Fiche de maintenance trop volumineuse pour la création REC.");
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            resolved, cancellationToken);
        return (session, relative);
    }

    /// <summary>Converts a contextual description to safe one-line provenance.</summary>
    /// <param name="value">Untrusted human Markdown line(s).</param>
    /// <param name="maximum">Maximum persisted length.</param>
    /// <returns>Compact note, not a verification of external evidence.</returns>
    private static string SafeLine(string value, int maximum)
    {
        string line = new(value.Select(character =>
            char.IsControl(character) ? ' ' : character).ToArray());
        return line.Length <= maximum ? line.Trim() :
            line[..maximum].Trim();
    }
}
