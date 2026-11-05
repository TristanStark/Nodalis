using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Revision-bound, read-only review of one existing incident before a human
/// decision is saved in the canonical maintenance sheet.
/// </summary>
/// <param name="Incident">Exact incident follow-up from REC and maintenance.</param>
/// <param name="MaintenanceSha256">SHA of the reviewed maintenance file.</param>
/// <param name="RecRegistrySha256">Fingerprint of all current project REC sources.</param>
/// <param name="History">Past human decisions, without automatic expiry rewriting.</param>
/// <param name="Warnings">Incomplete/ambiguous source diagnostics.</param>
public sealed record ProjectRecRevalidationPreview(
    ProjectRecIncidentFollowup Incident, string MaintenanceSha256,
    string RecRegistrySha256,
    IReadOnlyList<ProjectRecRevalidationEvent> History,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Most recent decision for this exact incident.</summary>
    public ProjectRecRevalidationEvent? Latest =>
        ProjectRecRevalidationCodec.Latest(History, Incident.IncidentId);

    /// <summary>
    /// A prior review is current only while its exact project REC fingerprint
    /// matches the sources, not just because the stored disposition is Confirmed.
    /// </summary>
    public bool LatestUsesCurrentRec =>
        Latest is ProjectRecRevalidationEvent previous &&
        string.Equals(previous.RecRegistrySha256, RecRegistrySha256,
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Writes one explicitly approved revalidation audit event in the EXISTING
/// Fiche de Maintenance.md, with both REC and maintenance optimistic revisions.
/// No incident status, historical test or campaign is ever auto-mutated.
/// </summary>
public sealed class ProjectRecRevalidationService
{
    private const long MaxMaintenanceBytes = 25L * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectTestCampaignService _campaigns;

    /// <summary>Constrain revalidation reviews to one configured workspace.</summary>
    /// <param name="workspaceRoot">Canonical workspace directory.</param>
    public ProjectRecRevalidationService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _campaigns = new ProjectTestCampaignService(_workspaceRoot);
    }

    /// <summary>
    /// Reads a bounded project snapshot with only its existing maintenance
    /// source and REC campaign files; never changes the filesystem.
    /// </summary>
    /// <param name="projectDirectory">Selected owning project.</param>
    /// <param name="incidentId">Exact selected canonical incident ID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current source revisions, audit and conservative follow-up.</returns>
    public async Task<ProjectRecRevalidationPreview> InspectAsync(
        string projectDirectory, string incidentId,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectGateValidation.IsIncidentId(incidentId))
        {
            throw new InvalidDataException(
                "Incident de revalidation sans identifiant stable.");
        }

        (ProjectRecRevalidationPreview preview, TextDocumentSession _) =
            await ReadCurrentAsync(projectDirectory, incidentId,
                cancellationToken);
        return preview;
    }

    /// <summary>
    /// Appends one independently justified human disposition. A Confirmed
    /// decision needs every affected test newly Passed in its explicitly
    /// declared environment and version plus three human attestations.
    /// </summary>
    /// <param name="projectDirectory">Verified, nonarchived project.</param>
    /// <param name="review">Exact revisions displayed to the human.</param>
    /// <param name="disposition">Human selected decision.</param>
    /// <param name="actor">Person approving or rejecting revalidation.</param>
    /// <param name="reason">Reason recorded in canonical Markdown.</param>
    /// <param name="evidenceReference">Human-referenced evidence (not verified by Nodalis).</param>
    /// <param name="environment">Explicit revalidation environment.</param>
    /// <param name="version">Explicit application version covered.</param>
    /// <param name="sourceChecked">Human declaration about current TEST sources.</param>
    /// <param name="evidenceChecked">Human declaration about evidence inspection.</param>
    /// <param name="scopeChecked">Human declaration about version and regressions.</param>
    /// <param name="cancellationToken">Cancellation before write.</param>
    /// <returns>Saved human event; no incident/REC status changes.</returns>
    public async Task<ProjectRecRevalidationEvent> RecordAsync(
        string projectDirectory, ProjectRecRevalidationPreview review,
        ProjectRecRevalidationDisposition disposition, string actor,
        string reason, string evidenceReference, string environment,
        string version, bool sourceChecked, bool evidenceChecked,
        bool scopeChecked,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!Enum.IsDefined(disposition) ||
            !Good(actor, 160) || !Good(reason, 2000) ||
            reason.Trim().Length < 10 ||
            evidenceReference is null ||
            evidenceReference.Length > 2000 ||
            evidenceReference.Any(char.IsControl) ||
            !Good(environment, 160) || !Good(version, 160))
        {
            throw new InvalidDataException(
                "Auteur, justification, preuve ou périmètre invalide.");
        }

        await VerifyWritableProjectAsync(projectDirectory, cancellationToken);
        (ProjectRecRevalidationPreview current,
            TextDocumentSession session) =
                await ReadCurrentAsync(projectDirectory,
                    review.Incident.IncidentId, cancellationToken);
        if (!string.Equals(current.MaintenanceSha256,
                review.MaintenanceSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.RecRegistrySha256,
                review.RecRegistrySha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "REC ou maintenance modifiés depuis la revue : " +
                "actualisez avant d'enregistrer la décision.");
        }

        ProjectRecIncidentFollowup followup = current.Incident;
        if (followup.MissingSource ||
            followup.Tests.Count == 0 ||
            current.Warnings.Any(item => item.Contains(
                "ambigu", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "La source incident est absente ou des sources REC " +
                "sont ambiguës : décision non enregistrable.");
        }

        if (disposition == ProjectRecRevalidationDisposition.Confirmed)
        {
            if (!followup.ReviewMayBeProposed ||
                !sourceChecked || !evidenceChecked || !scopeChecked ||
                evidenceReference.Trim().Length < 10 ||
                followup.Tests.Any(item =>
                    !item.Candidate ||
                    !string.Equals(item.LaterEnvironment,
                        environment, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(item.LaterVersion,
                        version, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "La confirmation exige tous les TEST réexécutés et " +
                    "conformes dans la version et l'environnement " +
                    "explicitement contrôlés, plus les trois attestations.");
            }

            if (current.Latest is ProjectRecRevalidationEvent previous &&
                previous.Disposition ==
                    ProjectRecRevalidationDisposition.Confirmed &&
                current.LatestUsesCurrentRec)
            {
                throw new InvalidOperationException(
                    "Cet incident a déjà une revalidation confirmée sur " +
                    "les mêmes sources REC ; ne dupliquez pas la décision.");
            }
        }

        ProjectRecRevalidationTestSnapshot[] tests = followup.Tests
            .Select(item => new ProjectRecRevalidationTestSnapshot
            {
                TestId = item.TestId,
                Reference = item.Reference,
                FailureResultId = item.FailureResultId,
                FailureUtc = item.FailureUtc,
                RetestResultId = item.RetestResultId,
                RetestUtc = item.LaterUtc,
                RetestOutcome = item.LaterOutcome,
                RetestEnvironment = item.LaterEnvironment,
                RetestVersion = item.LaterVersion
            }).ToArray();
        ProjectRecRevalidationEvent decision = new()
        {
            Id = Guid.NewGuid(),
            IncidentId = followup.IncidentId,
            PreviousDecisionId = current.Latest?.Id,
            Disposition = disposition,
            Actor = actor.Trim(),
            Rationale = reason.Trim(),
            EvidenceReference = evidenceReference.Trim(),
            TargetEnvironment = environment.Trim(),
            TargetVersion = version.Trim(),
            SourceDefinitionsChecked = sourceChecked,
            EvidenceChecked = evidenceChecked,
            ScopeChecked = scopeChecked,
            RecRegistrySha256 = current.RecRegistrySha256,
            Tests = tests,
            RecordedUtc = DateTimeOffset.UtcNow
        };

        string markdown = ProjectRecRevalidationCodec.Append(
            session.Content, decision);
        cancellationToken.ThrowIfCancellationRequested();
        await session.SaveAsync(markdown, cancellationToken);
        return decision;
    }

    /// <summary>
    /// Opens an exact bounded source session and joins valid local REC
    /// definitions with the real incident and its human decision history.
    /// </summary>
    /// <param name="projectDirectory">Validated project location.</param>
    /// <param name="incidentId">Unique maintenance incident identity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Revision-bound human preview and source session.</returns>
    private async Task<(ProjectRecRevalidationPreview Preview,
        TextDocumentSession Session)> ReadCurrentAsync(
        string projectDirectory, string incidentId,
        CancellationToken cancellationToken)
    {
        Guid projectId = await ReadProjectIdAsync(
            projectDirectory, cancellationToken);
        ProjectTestCampaignCatalog catalog =
            await _campaigns.ListAsync(projectDirectory, cancellationToken);
        if (catalog.Campaigns.Count == 0)
        {
            throw new InvalidDataException(
                "Aucune campagne REC disponible pour cet incident.");
        }

        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Des campagnes REC sont illisibles ou ambiguës : " +
                "corrigez-les avant une décision de revalidation.");
        }

        string path = Path.Combine(Path.GetFullPath(projectDirectory),
            "Fiche de Maintenance.md");
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                "Fiche de maintenance absente : revalidation impossible.");
        }

        string resolved = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(path));
        if (new FileInfo(resolved).Length > MaxMaintenanceBytes)
        {
            throw new InvalidDataException(
                "Fiche de maintenance trop volumineuse pour la revalidation.");
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            resolved, cancellationToken);
        MaintenanceParseResult records =
            MaintenanceMarkdownCodec.Parse(session.Content);
        if (records.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Historique de maintenance incomplet : " +
                "corrigez la fiche avant la décision REC.");
        }

        IReadOnlyList<ProjectRecRevalidationEvent> history =
            ProjectRecRevalidationCodec.Read(session.Content);
        ProjectRecAnomalyOverview anomalies =
            ProjectRecAnomalyProjection.Build(projectId,
                catalog.Campaigns.Select(item => item.Campaign).ToArray(),
                records.Records, []);
        ProjectRecIncidentFollowup[] exact = anomalies.IncidentFollowups
            .Where(item => string.Equals(item.IncidentId, incidentId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length != 1)
        {
            throw new InvalidDataException(
                "L'incident n'a plus de résultat REC associé de façon unique.");
        }

        ProjectRecIncidentFollowup followup = exact[0];
        if (followup.MissingSource)
        {
            throw new InvalidDataException(
                "La source canonique de l'incident est absente ou ambiguë.");
        }

        string hash = RecFingerprint(catalog);
        ProjectRecRevalidationPreview preview = new(
            followup, session.Revision.Sha256, hash, history, []);
        return (preview, session);
    }

    /// <summary>
    /// Computes a stable digest of all campaign paths, GUIDs, and source SHAs
    /// (not just visible UI values), so revalidation cannot use stale REC.
    /// </summary>
    /// <param name="catalog">All unambiguous campaigns in this project.</param>
    /// <returns>SHA-256 of the deterministic current REC catalog snapshot.</returns>
    private static string RecFingerprint(ProjectTestCampaignCatalog catalog)
    {
        StringBuilder data = new();
        foreach (ProjectTestCampaignEntry entry in catalog.Campaigns
            .OrderBy(item => item.RelativePath,
                StringComparer.OrdinalIgnoreCase))
        {
            data.Append(entry.RelativePath).Append('|')
                .Append(entry.Campaign.Id.ToString("D")).Append('|')
                .Append(entry.Campaign.ProjectId.ToString("D")).Append('|')
                .Append(entry.SourceSha256).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(data.ToString())));
    }

    /// <summary>Verifies an exact existing project manifest and workspace scope.</summary>
    /// <param name="projectDirectory">Current workspace project folder.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Manifest GUID, without creating sources.</returns>
    private async Task<Guid> ReadProjectIdAsync(
        string projectDirectory, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(projectDirectory);
        string manifestPath = Path.Combine(path,
            WorkspaceLayout.ProjectManifestFileName);
        string verified = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(manifestPath));
        ProjectManifest manifest =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                verified, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans GUID stable.");
        }

        return manifest.Id;
    }

    /// <summary>Rejects archived or non-application project mutation.</summary>
    /// <param name="projectDirectory">Workspace-scoped project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Completed ownership check.</returns>
    private async Task VerifyWritableProjectAsync(
        string projectDirectory, CancellationToken cancellationToken)
    {
        string full = Path.GetFullPath(projectDirectory);
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (segments.Length < 3 ||
            !string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Décision interdite pour un projet archivé ou hors workspace.");
        }

        string path = Path.Combine(full,
            WorkspaceLayout.ProjectManifestFileName);
        string verified = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(path));
        ProjectManifest manifest =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                verified, cancellationToken);
        if (manifest.Id == Guid.Empty || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Revalidation d'un projet archivé ou invalide interdite.");
        }
    }

    /// <summary>Validates a single-line, bounded human declaration.</summary>
    /// <param name="value">Human entry.</param>
    /// <param name="limit">Maximum character count.</param>
    /// <returns>Whether the audit field can be written.</returns>
    private static bool Good(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit &&
        !value.Any(char.IsControl);
}
