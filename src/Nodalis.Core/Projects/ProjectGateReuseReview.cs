namespace Nodalis.Core.Projects;

/// <summary>Reasons why a historical gate requires a fresh qualification.</summary>
public enum ProjectGateReuseFindingKind
{
    NonApprovingVerdict,
    HistoricalContextMissing,
    CurrentContextMissing,
    VersionChanged,
    EnvironmentChanged,
    ScopeChanged,
    EvidenceNotReviewed,
    EvidenceMissing,
    EvidenceChanged,
    EvidenceAdded,
    OpenReservation
}

/// <summary>One non-authorizing observation about a frozen gate decision.</summary>
public sealed record ProjectGateReuseFinding(
    ProjectGateReuseFindingKind Kind, string Detail, bool RequiresNewDecision);

/// <summary>
/// A historical-condition comparison, never permission to deploy.
/// </summary>
public sealed record ProjectGateReuseAssessment
{
    public required Guid DecisionId { get; init; }
    public required ProjectGateVerdict HistoricalVerdict { get; init; }
    public required bool WithinHistoricalConditions { get; init; }
    public bool ExplicitHumanApprovalRequired => true;
    public required IReadOnlyList<ProjectGateReuseFinding> Findings { get; init; }
}

/// <summary>
/// Pure comparison of a frozen human GO with a proposed operation context.
/// The caller captures real current SHA fingerprints and separately reviews
/// procedure, rollback, incidents, risks and environment state before MEP.
/// </summary>
public static class ProjectGateReuseReview
{
    /// <summary>
    /// Checks scope, version, environment and every frozen evidence fingerprint,
    /// without altering any verdict, reservation or milestone.
    /// </summary>
    /// <param name="registry">One validated project-local registry.</param>
    /// <param name="decisionId">Stable ID of the historical decision.</param>
    /// <param name="version">Exact version proposed for deployment.</param>
    /// <param name="environment">Exact target environment.</param>
    /// <param name="scope">Exact target scope.</param>
    /// <param name="currentEvidence">Fresh file hashes, or null if unchecked.</param>
    /// <returns>Advisory differences; never a new authorization.</returns>
    public static ProjectGateReuseAssessment Assess(
        ProjectGateRegistry registry, Guid decisionId,
        string? version, string? environment, string? scope,
        IReadOnlyList<ProjectGateEvidenceSnapshot>? currentEvidence)
    {
        ProjectGateValidation.Validate(registry);
        ProjectGateDecision decision = registry.Decisions.SingleOrDefault(
            item => item.Id == decisionId) ??
            throw new KeyNotFoundException("Décision historique introuvable.");
        List<ProjectGateReuseFinding> findings = [];

        if (decision.Verdict is ProjectGateVerdict.NoGo or
            ProjectGateVerdict.Deferred)
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.NonApprovingVerdict,
                "NO-GO / Reporté : réutilisation interdite.", true));
        }

        if (string.IsNullOrWhiteSpace(decision.Version) ||
            string.IsNullOrWhiteSpace(decision.Environment))
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.HistoricalContextMissing,
                "Version ou environnement absents du dossier d'origine.", true));
        }

        if (string.IsNullOrWhiteSpace(version) ||
            string.IsNullOrWhiteSpace(environment) ||
            string.IsNullOrWhiteSpace(scope))
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.CurrentContextMissing,
                "Version, environnement et périmètre cibles obligatoires.", true));
        }

        if (!string.IsNullOrWhiteSpace(version) &&
            !string.IsNullOrWhiteSpace(decision.Version) &&
            !string.Equals(version, decision.Version, StringComparison.Ordinal))
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.VersionChanged,
                "Version différente du GO historique.", true));
        }

        if (!string.IsNullOrWhiteSpace(environment) &&
            !string.IsNullOrWhiteSpace(decision.Environment) &&
            !string.Equals(environment, decision.Environment,
                StringComparison.Ordinal))
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.EnvironmentChanged,
                "Environnement différent du GO historique.", true));
        }

        if (!string.IsNullOrWhiteSpace(scope) &&
            !string.Equals(scope, decision.Scope, StringComparison.Ordinal))
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.ScopeChanged,
                "Périmètre différent du GO historique.", true));
        }

        if (currentEvidence is null)
        {
            findings.Add(new ProjectGateReuseFinding(
                ProjectGateReuseFindingKind.EvidenceNotReviewed,
                "Les empreintes des preuves n'ont pas été recontrôlées.", true));
        }
        else
        {
            Dictionary<string, string> current = new(
                StringComparer.OrdinalIgnoreCase);
            foreach (ProjectGateEvidenceSnapshot proof in currentEvidence)
            {
                if (proof is null ||
                    !ProjectJourneySourcePath.IsSafe(proof.RelativePath) ||
                    proof.Sha256 is not { Length: 64 } ||
                    !proof.Sha256.All(Uri.IsHexDigit) ||
                    !current.TryAdd(proof.RelativePath, proof.Sha256))
                {
                    throw new InvalidDataException(
                        "Inventaire actuel des preuves invalide ou dupliqué.");
                }
            }

            HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectGateEvidenceSnapshot frozen in decision.Evidence)
            {
                expected.Add(frozen.RelativePath);
                if (!current.TryGetValue(frozen.RelativePath, out string? hash))
                {
                    findings.Add(new ProjectGateReuseFinding(
                        ProjectGateReuseFindingKind.EvidenceMissing,
                        "Preuve absente : " + frozen.RelativePath, true));
                }
                else if (!string.Equals(hash, frozen.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new ProjectGateReuseFinding(
                        ProjectGateReuseFindingKind.EvidenceChanged,
                        "Preuve modifiée : " + frozen.RelativePath, true));
                }
            }

            foreach (string path in current.Keys)
            {
                if (!expected.Contains(path))
                {
                    findings.Add(new ProjectGateReuseFinding(
                        ProjectGateReuseFindingKind.EvidenceAdded,
                        "Preuve ajoutée hors dossier figé : " + path, true));
                }
            }
        }

        if (decision.Verdict == ProjectGateVerdict.GoWithReservations)
        {
            Dictionary<Guid, ProjectGateReservationEventKind> last = [];
            foreach (ProjectGateReservationEvent item in registry.ReservationEvents)
            {
                if (item.DecisionId == decision.Id)
                {
                    last[item.ReservationId] = item.Kind;
                }
            }

            foreach (ProjectGateReservation reservation in decision.Reservations)
            {
                if (!last.TryGetValue(reservation.Id,
                    out ProjectGateReservationEventKind state) ||
                    state == ProjectGateReservationEventKind.Reopened)
                {
                    findings.Add(new ProjectGateReuseFinding(
                        ProjectGateReuseFindingKind.OpenReservation,
                        "Réserve ouverte : " + reservation.Description, false));
                }
            }
        }

        return new ProjectGateReuseAssessment
        {
            DecisionId = decision.Id,
            HistoricalVerdict = decision.Verdict,
            WithinHistoricalConditions = !findings.Any(item =>
                item.RequiresNewDecision),
            Findings = findings
        };
    }
}
