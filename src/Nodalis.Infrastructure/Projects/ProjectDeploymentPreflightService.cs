using Nodalis.Core.Projects;
using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Human-facing areas of the read-only MEP preflight inspection.</summary>
public enum ProjectDeploymentPreflightArea
{
    Gate,
    Procedure,
    Rollback,
    Risks,
    Incidents,
    OperationalChecks
}

/// <summary>A diagnostic, never an automatic approval or MEP decision.</summary>
/// <param name="Area">Source of the observation.</param>
/// <param name="Detail">Human-readable result.</param>
/// <param name="NeedsNewDecision">Changed GO scope or proof requiring a new GO.</param>
public sealed record ProjectDeploymentPreflightFinding(
    ProjectDeploymentPreflightArea Area, string Detail,
    bool NeedsNewDecision = false);

/// <summary>
/// One non-authorizing snapshot of source conditions for a recorded attempt.
/// Human review of incidents, risks and operational context is always required.
/// </summary>
public sealed record ProjectDeploymentPreflightReport
{
    public required string OperationReference { get; init; }
    public required Guid AttemptId { get; init; }
    public required DateTimeOffset CheckedUtc { get; init; }
    public required bool HistoricalGateConditionsMatch { get; init; }
    public bool ExplicitHumanReviewRequired => true;
    public required int RisksFound { get; init; }
    public required int IncidentsFound { get; init; }
    public required IReadOnlyList<ProjectDeploymentPreflightFinding> Findings
    { get; init; }
}

/// <summary>
/// Read-only project-local MEP preflight. Scans canonical registries only on
/// explicit request; no GO decision, risk, incident or MEP is changed.
/// </summary>
public sealed class ProjectDeploymentPreflightService
{
    private readonly ProjectDeploymentService _deployments;
    private readonly ProjectGateRegistryService _gates;
    private readonly ProjectRiskRegistryService _risks;
    private readonly ProjectGateIncidentCatalogService _incidents;

    /// <summary>Constructs a safe reviewer confined to one workspace.</summary>
    /// <param name="workspaceRoot">Active Nodalis workspace.</param>
    public ProjectDeploymentPreflightService(string workspaceRoot)
    {
        _deployments = new ProjectDeploymentService(workspaceRoot);
        _gates = new ProjectGateRegistryService(workspaceRoot);
        _risks = new ProjectRiskRegistryService(workspaceRoot);
        _incidents = new ProjectGateIncidentCatalogService(workspaceRoot);
    }

    /// <summary>
    /// Rechecks the exact MEP source revision and current project registries.
    /// This is NOT an approval; the operator must still inspect the actual
    /// environment, incident relevance, monitoring and procedure applicability.
    /// </summary>
    /// <param name="projectDirectory">Current canonical project directory.</param>
    /// <param name="snapshot">Exact MEP source selected by the operator.</param>
    /// <param name="attemptId">Historical attempt selected for review.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Transient read-only findings for the selected attempt.</returns>
    public async Task<ProjectDeploymentPreflightReport> ReviewAsync(
        string projectDirectory, ProjectDeploymentEntry snapshot,
        Guid attemptId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProjectDeploymentCatalog original = await _deployments.ListAsync(
            projectDirectory, cancellationToken);
        ProjectDeploymentEntry entry = original.Operations.SingleOrDefault(
            item => item.Operation.Id == snapshot.Operation.Id &&
                string.Equals(item.RelativePath, snapshot.RelativePath,
                    StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException(
                "Opération MEP absente ou ambiguë : actualisez le registre.");
        if (!string.Equals(entry.SourceSha256, snapshot.SourceSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "MEP modifiée pendant la préparation : actualisez la fiche.");
        }

        ProjectDeploymentAttempt attempt =
            entry.Operation.Attempts.SingleOrDefault(item =>
                item.Id == attemptId) ??
            throw new InvalidDataException(
                "Tentative absente de la révision MEP sélectionnée.");
        ProjectGateRegistrySnapshot gates = await _gates.ReadAsync(
            projectDirectory, cancellationToken);
        List<ProjectDeploymentPreflightFinding> findings = [];
        bool conditions = true;
        ProjectGateDecision? decision = gates.Registry.Decisions.SingleOrDefault(
            item => item.Id == attempt.GateDecisionId);
        if (decision is null)
        {
            conditions = false;
            findings.Add(new ProjectDeploymentPreflightFinding(
                ProjectDeploymentPreflightArea.Gate,
                "Décision GO absente du registre actuel du projet.", true));
        }
        else
        {
            ProjectGateDecision? latest = gates.Registry.Decisions.LastOrDefault(
                item => item.GateId == decision.GateId);
            if (latest?.Id != decision.Id)
            {
                conditions = false;
                findings.Add(new ProjectDeploymentPreflightFinding(
                    ProjectDeploymentPreflightArea.Gate,
                    "Une décision ultérieure remplace ce GO historique.", true));
            }

            List<ProjectGateEvidenceSnapshot> current = [];
            foreach (ProjectGateEvidenceSnapshot proof in decision.Evidence)
            {
                try
                {
                    current.Add(await _gates.CaptureEvidenceAsync(
                        proof.RelativePath, cancellationToken));
                }
                catch (Exception exception) when (exception is IOException or
                    InvalidDataException or UnauthorizedAccessException or
                    ArgumentException)
                {
                    // Omitted evidence is explicitly flagged as missing by
                    // ProjectGateReuseReview, never considered current.
                }
            }

            ProjectGateReuseAssessment reuse = ProjectGateReuseReview.Assess(
                gates.Registry, decision.Id, attempt.TargetVersion,
                attempt.Environment, attempt.Scope, current);
            conditions &= reuse.WithinHistoricalConditions;
            findings.AddRange(reuse.Findings.Select(item =>
                new ProjectDeploymentPreflightFinding(
                    ProjectDeploymentPreflightArea.Gate, item.Detail,
                    item.RequiresNewDecision)));
        }

        conditions &= await CompareProcedureAsync(
            attempt.Procedure, ProjectDeploymentPreflightArea.Procedure,
            findings, cancellationToken);
        conditions &= await CompareProcedureAsync(
            attempt.RollbackProcedure, ProjectDeploymentPreflightArea.Rollback,
            findings, cancellationToken);

        ProjectRiskCatalog riskCatalog = await _risks.ListAsync(
            projectDirectory, cancellationToken);
        foreach (string diagnostic in riskCatalog.Diagnostics.Take(20))
        {
            findings.Add(new ProjectDeploymentPreflightFinding(
                ProjectDeploymentPreflightArea.Risks,
                "Fiche RIS non qualifiable : " + diagnostic));
        }

        if (riskCatalog.Diagnostics.Count > 20)
        {
            findings.Add(new ProjectDeploymentPreflightFinding(
                ProjectDeploymentPreflightArea.Risks,
                (riskCatalog.Diagnostics.Count - 20) +
                " autre(s) diagnostic(s) RIS : consulter le registre."));
        }

        int risksToReview = 0;
        foreach (ProjectRiskEntry risk in riskCatalog.Risks)
        {
            ProjectRiskSeverity residual =
                ProjectRiskMatrix.Evaluate(risk.Card.Residual);
            if (risk.Card.Lifecycle != ProjectRiskLifecycle.Closed &&
                (risk.Card.Qualification != ProjectRiskQualification.Validated ||
                 residual is ProjectRiskSeverity.Unassessed or
                     ProjectRiskSeverity.High))
            {
                risksToReview++;
                if (risksToReview <= 20)
                {
                    findings.Add(new ProjectDeploymentPreflightFinding(
                        ProjectDeploymentPreflightArea.Risks,
                        risk.Card.Reference + " · " + risk.Card.Title +
                        " : " + risk.Card.Qualification +
                        ", résiduelle " + residual +
                        " (pertinence pour cette MEP à confirmer)."));
                }
            }
        }

        if (risksToReview > 20)
        {
            findings.Add(new ProjectDeploymentPreflightFinding(
                ProjectDeploymentPreflightArea.Risks,
                (risksToReview - 20) +
                " autre(s) risque(s) à examiner : ouvrir le registre RIS."));
        }

        ProjectGateIncidentCatalog incidents = await _incidents.ListAsync(
            projectDirectory, cancellationToken);
        if (incidents.Candidates.Count > 0 || incidents.ExcludedCount > 0)
        {
            findings.Add(new ProjectDeploymentPreflightFinding(
                ProjectDeploymentPreflightArea.Incidents,
                incidents.Candidates.Count + " incident(s) à ID stable, " +
                incidents.ExcludedCount +
                " incident(s) incomplet(s) ou ambigu(s). " +
                "Le statut ouvert/clos et la pertinence MEP doivent être " +
                "qualifiés dans la fiche de maintenance."));
        }
        else
        {
            findings.Add(new ProjectDeploymentPreflightFinding(
                ProjectDeploymentPreflightArea.Incidents,
                "Aucun incident canonique détecté dans la fiche ; vérifier " +
                "les incidents non enregistrés et la supervision réelle."));
        }

        // Even a positive GO comparison does not verify live infrastructure,
        // rollback feasibility, backups, support, release windows or timing.
        findings.Add(new ProjectDeploymentPreflightFinding(
            ProjectDeploymentPreflightArea.OperationalChecks,
            "Contrôles humains obligatoires : état réel, fenêtre MEP, " +
            "sauvegardes, supervision, support, sécurité et rollback. " +
            "Aucun verdict et aucune autorisation ne sont générés."));

        ProjectGateRegistrySnapshot afterGates = await _gates.ReadAsync(
            projectDirectory, cancellationToken);
        ProjectDeploymentCatalog afterMep = await _deployments.ListAsync(
            projectDirectory, cancellationToken);
        ProjectDeploymentEntry? after = afterMep.Operations.SingleOrDefault(
            item => item.Operation.Id == entry.Operation.Id &&
                string.Equals(item.RelativePath, entry.RelativePath,
                    StringComparison.OrdinalIgnoreCase));
        if (after is null || !string.Equals(after.SourceSha256,
                entry.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(afterGates.Sha256, gates.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "MEP ou gates modifiés pendant l'inspection : relancez la revue.");
        }

        return new ProjectDeploymentPreflightReport
        {
            OperationReference = entry.Operation.Reference,
            AttemptId = attempt.Id,
            CheckedUtc = DateTimeOffset.UtcNow,
            HistoricalGateConditionsMatch = conditions,
            ExplicitHumanReviewRequired = true,
            RisksFound = riskCatalog.Risks.Count,
            IncidentsFound = incidents.Candidates.Count,
            Findings = findings
        };
    }

    /// <summary>Compares the current source fingerprint to a frozen procedure.</summary>
    /// <param name="snapshot">Procedure recorded on the attempt.</param>
    /// <param name="area">Deployment or rollback plan.</param>
    /// <param name="findings">Append-only human-facing diagnostics.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>True only for one safely accessible identical SHA.</returns>
    private async Task<bool> CompareProcedureAsync(
        ProjectGateEvidenceSnapshot snapshot, ProjectDeploymentPreflightArea area,
        List<ProjectDeploymentPreflightFinding> findings,
        CancellationToken cancellationToken)
    {
        try
        {
            ProjectGateEvidenceSnapshot current =
                await _gates.CaptureEvidenceAsync(snapshot.RelativePath,
                    cancellationToken);
            if (string.Equals(snapshot.Sha256, current.Sha256,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            findings.Add(new ProjectDeploymentPreflightFinding(area,
                "Procédure modifiée depuis le snapshot de la tentative : " +
                snapshot.RelativePath));
            return false;
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or UnauthorizedAccessException or
            ArgumentException)
        {
            findings.Add(new ProjectDeploymentPreflightFinding(area,
                "Procédure absente, illisible ou non sûre : " +
                snapshot.RelativePath));
            return false;
        }
    }
}
