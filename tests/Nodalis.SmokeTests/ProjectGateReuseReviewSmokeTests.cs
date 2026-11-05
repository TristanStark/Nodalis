using Nodalis.Core.Projects;

/// <summary>Deterministic non-mutating review tests for historical gate reuse.</summary>
internal static class ProjectGateReuseReviewSmokeTests
{
    /// <summary>Checks mismatches, reservations, proofs and human-only status.</summary>
    public static void Run()
    {
        Guid gateId = Guid.NewGuid();
        Guid decisionId = Guid.NewGuid();
        Guid reservationId = Guid.NewGuid();
        ProjectGateEvidenceSnapshot proof = new()
        {
            RelativePath = "Tests/Recette.md",
            Sha256 = new string('A', 64)
        };
        ProjectGateDecision decision = new()
        {
            Id = decisionId,
            GateId = gateId,
            GateName = "GO production",
            Verdict = ProjectGateVerdict.Go,
            Actor = "Responsable",
            Scope = "Application A",
            Rationale = "Revue humaine complète et justifiée.",
            Version = "2.0.0",
            Environment = "PROD",
            Assessments =
            [
                new ProjectGateCriterionAssessment
                {
                    CriterionId = Guid.NewGuid(),
                    Name = "Tests qualifiés",
                    Status = ProjectGateCriterionStatus.Conform
                }
            ],
            Evidence = [proof]
        };
        ProjectGateRegistry registry = new()
        {
            ProjectId = Guid.NewGuid(),
            Gates = [new ProjectGateDefinition
            {
                Id = gateId, Name = decision.GateName
            }],
            Decisions = [decision]
        };
        ProjectGateReuseAssessment equal = Inspect(registry, decisionId,
            "2.0.0", "PROD", "Application A", [proof]);
        Check(equal.WithinHistoricalConditions &&
            equal.ExplicitHumanApprovalRequired && equal.Findings.Count == 0,
            "Matching sources must never authorize an operation automatically.");

        Expect(Inspect(registry, decisionId, "2.0.1", "PROD",
            "Application A", [proof]), ProjectGateReuseFindingKind.VersionChanged);
        Expect(Inspect(registry, decisionId, "2.0.0", "PREPROD",
            "Application A", [proof]), ProjectGateReuseFindingKind.EnvironmentChanged);
        Expect(Inspect(registry, decisionId, "2.0.0", "PROD",
            "Application A + B", [proof]), ProjectGateReuseFindingKind.ScopeChanged);
        Expect(Inspect(registry, decisionId, "2.0.0", "PROD",
            "Application A", null), ProjectGateReuseFindingKind.EvidenceNotReviewed);
        Expect(Inspect(registry, decisionId, "2.0.0", "PROD",
            "Application A", []), ProjectGateReuseFindingKind.EvidenceMissing);
        Expect(Inspect(registry, decisionId, "2.0.0", "PROD",
            "Application A", [proof with { Sha256 = new string('B', 64) }]),
            ProjectGateReuseFindingKind.EvidenceChanged);
        Expect(Inspect(registry, decisionId, "2.0.0", "PROD",
            "Application A", [proof, proof with
            { RelativePath = "Tests/Autre.md" }]),
            ProjectGateReuseFindingKind.EvidenceAdded);
        Expect(Inspect(registry, decisionId, null, "PROD",
            "Application A", [proof]),
            ProjectGateReuseFindingKind.CurrentContextMissing);
        Expect(Inspect(registry with
            { Decisions = [decision with { Version = null }] },
            decisionId, "2.0.0", "PROD", "Application A", [proof]),
            ProjectGateReuseFindingKind.HistoricalContextMissing);
        Expect(Inspect(registry with
            { Decisions = [decision with { Verdict = ProjectGateVerdict.NoGo }] },
            decisionId, "2.0.0", "PROD", "Application A", [proof]),
            ProjectGateReuseFindingKind.NonApprovingVerdict);

        ProjectGateDecision reserved = decision with
        {
            Verdict = ProjectGateVerdict.GoWithReservations,
            Reservations = [new ProjectGateReservation
            {
                Id = reservationId, Description = "Surveillance à requalifier"
            }]
        };
        ProjectGateRegistry pending = registry with { Decisions = [reserved] };
        ProjectGateReuseAssessment open = Inspect(pending, decisionId,
            "2.0.0", "PROD", "Application A", [proof]);
        Check(open.WithinHistoricalConditions &&
            open.ExplicitHumanApprovalRequired &&
            open.Findings.Any(item =>
                item.Kind == ProjectGateReuseFindingKind.OpenReservation),
            "Open reservations must stay visible without rewriting the verdict.");

        ProjectGateReservationEvent lift = new()
        {
            Id = Guid.NewGuid(),
            DecisionId = decisionId,
            ReservationId = reservationId,
            Kind = ProjectGateReservationEventKind.Lifted,
            Actor = "Responsable",
            Rationale = "Preuve de levée humaine approuvée.",
            RecordedUtc = reserved.RecordedUtc.AddSeconds(1),
            Evidence = [proof]
        };
        ProjectGateRegistry lifted = pending with { ReservationEvents = [lift] };
        ProjectGateReuseAssessment afterLift = Inspect(lifted, decisionId,
            "2.0.0", "PROD", "Application A", [proof]);
        Check(afterLift.Findings.Count == 0 &&
            afterLift.ExplicitHumanApprovalRequired &&
            reserved.Reservations.Count == 1 &&
            registry.Decisions.Single().Verdict == ProjectGateVerdict.Go,
            "Lifting must preserve frozen verdicts and remain human-reviewed.");
        ProjectGateRegistry reopened = lifted with
        {
            ReservationEvents =
            [
                lift,
                new ProjectGateReservationEvent
                {
                    Id = Guid.NewGuid(),
                    DecisionId = decisionId,
                    ReservationId = reservationId,
                    Kind = ProjectGateReservationEventKind.Reopened,
                    Actor = "Responsable",
                    Rationale = "Nouvelle régression identifiée en recette.",
                    RecordedUtc = reserved.RecordedUtc.AddSeconds(2)
                }
            ]
        };
        ProjectGateReuseAssessment again = Inspect(reopened, decisionId,
            "2.0.0", "PROD", "Application A", [proof]);
        Check(again.Findings.Any(item =>
            item.Kind == ProjectGateReuseFindingKind.OpenReservation),
            "Reopening must restore the need to qualify the reservation.");

        try
        {
            Inspect(registry, decisionId, "2.0.0", "PROD", "Application A",
                [proof, proof]);
            throw new InvalidOperationException("Duplicate proof accepted.");
        }
        catch (InvalidDataException)
        {
            // Duplicate identities cannot be silently disambiguated.
        }
    }

    /// <summary>Forwards one context to the pure decision inspector.</summary>
    /// <param name="registry">Source of historical truth.</param>
    /// <param name="id">Frozen decision identifier.</param>
    /// <param name="version">Target version.</param>
    /// <param name="environment">Target environment.</param>
    /// <param name="scope">Target scope.</param>
    /// <param name="proofs">Fresh fingerprint inventory.</param>
    /// <returns>Read-only review result.</returns>
    private static ProjectGateReuseAssessment Inspect(
        ProjectGateRegistry registry, Guid id, string? version,
        string? environment, string? scope,
        IReadOnlyList<ProjectGateEvidenceSnapshot>? proofs) =>
        ProjectGateReuseReview.Assess(
            registry, id, version, environment, scope, proofs);

    /// <summary>Checks that one difference requires a new human decision.</summary>
    /// <param name="result">Computed findings.</param>
    /// <param name="kind">Expected issue.</param>
    private static void Expect(ProjectGateReuseAssessment result,
        ProjectGateReuseFindingKind kind)
    {
        Check(!result.WithinHistoricalConditions &&
            result.Findings.Any(item =>
                item.Kind == kind && item.RequiresNewDecision),
            "Expected mandatory redecision: " + kind);
    }

    /// <summary>Fails a deterministic core regression.</summary>
    /// <param name="ok">Condition to assert.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Check(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException(message);
        }
    }
}
