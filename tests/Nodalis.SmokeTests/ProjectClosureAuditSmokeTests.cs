using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

/// <summary>Read-only closure inventory regressions for roadmap #204.</summary>
internal static class ProjectClosureAuditSmokeTests
{
    /// <summary>Checks human statuses, scoped GUIDs and side-effect-free reads.</summary>
    /// <param name="root">Temporary test workspace root.</param>
    /// <returns>Completed verification.</returns>
    public static async Task RunAsync(string root)
    {
        Guid projectId = Guid.NewGuid();
        Guid riskId = Guid.NewGuid();
        Guid requirementId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        Guid reservationId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-5);

        ProjectRiskCard risk = new()
        {
            Id = riskId, ProjectId = projectId, Reference = "RIS-0001",
            Title = "Risque à transmettre", CreatedUtc = created
        };
        ProjectRequirementCard requirement = new()
        {
            Id = requirementId, ProjectId = projectId, Reference = "EXI-0001",
            Title = "Exigence non vérifiée", CreatedUtc = created
        };
        ProjectDeploymentOperation operation = new()
        {
            Id = operationId, ProjectId = projectId, Reference = "MEP-0001",
            Title = "Déploiement à examiner", CreatedUtc = created
        };
        ProjectDeploymentReservation reservation = new()
        {
            Id = reservationId, Reference = "RES-0001",
            OperationId = operationId,
            OriginKind = ProjectDeploymentReservationOrigin.Closure,
            OriginId = Guid.NewGuid(), OriginSha256 = new string('A', 64),
            OriginSummary = "Revue d'exploitation",
            Title = "Réserve opérationnelle",
            Description = "Passation à valider",
            CreatedBy = "Qualificateur", InitialOwner = "Exploitation",
            CreatedUtc = created
        };
        ProjectRiskCatalog risks = new(
            [new ProjectRiskEntry(risk, "Analyse/Risques/RIS-0001.md")],
            [], null);
        ProjectRequirementCatalog requirements = new(
            [new ProjectRequirementEntry(requirement,
                "Documentation/Exigences/EXI-0001.md", new string('B', 64))],
            [], null) { ProjectId = projectId };
        ProjectDeploymentCatalog deployments = new(
            [new ProjectDeploymentEntry(operation,
                "Exploitation/Déploiement/Opérations/MEP-0001.md",
                new string('C', 64))], [], null);
        ProjectDeploymentReservationLedger ledger = new()
        {
            ProjectId = projectId,
            Items = [reservation]
        };
        ProjectDeploymentReservationSnapshot reservations = new(
            ledger, "Exploitation/Déploiement/Suivi/Reservations-MEP.md",
            new string('D', 64));

        ProjectClosureAuditSnapshot open = ProjectClosureAuditProjection.Build(
            projectId, risks, requirements, deployments, reservations);
        Assert(open.Items.Count == 4, "The four canonical open statuses must be visible.");
        Assert(open.Items.All(item => item.Id != Guid.Empty),
            "Only persistent source GUIDs may identify closure items.");
        Assert(open.FormatPreview().Contains("tâches, jalons, incidents",
                StringComparison.OrdinalIgnoreCase),
            "The preview must explicitly disclose its incomplete coverage.");

        ProjectDeploymentReservationEvent resolved = new()
        {
            Id = Guid.NewGuid(), ReservationId = reservationId,
            Action = ProjectDeploymentReservationAction.Resolve,
            Actor = "Qualificateur", Rationale = "Suivi vérifié",
            RecordedUtc = created.AddMinutes(1)
        };
        ProjectDeploymentReservationSnapshot closedReservations =
            reservations with { Ledger = ledger with { Events = [resolved] } };
        ProjectRiskCatalog closedRisks = risks with
        {
            Risks = [new ProjectRiskEntry(risk with
            {
                Lifecycle = ProjectRiskLifecycle.Closed,
                Qualification = ProjectRiskQualification.Validated
            }, "Analyse/Risques/RIS-0001.md")]
        };
        ProjectRequirementCatalog verifiedRequirements = requirements with
        {
            Requirements = [new ProjectRequirementEntry(requirement with
            {
                Validation = ProjectRequirementValidation.Approved,
                Verification = ProjectRequirementVerification.Verified
            }, "Documentation/Exigences/EXI-0001.md", new string('B', 64))]
        };
        ProjectDeploymentCatalog closedDeployments = deployments with
        {
            Operations = [new ProjectDeploymentEntry(operation with
            {
                Closure = new ProjectDeploymentClosure
                {
                    Id = Guid.NewGuid(),
                    Outcome = ProjectDeploymentOutcome.Cancelled,
                    RecordedUtc = created.AddMinutes(2),
                    Actor = "Qualificateur",
                    Rationale = "Opération annulée"
                }
            }, "Exploitation/Déploiement/Opérations/MEP-0001.md",
                new string('C', 64))]
        };
        ProjectClosureAuditSnapshot closed = ProjectClosureAuditProjection.Build(
            projectId, closedRisks, verifiedRequirements,
            closedDeployments, closedReservations);
        Assert(closed.Items.Count == 0,
            "A settled human status must not be silently reopened by the projection.");
        Assert(open.Items.Count == 4 && ledger.Events.Count == 0,
            "The projection must not mutate the original sources or event log.");

        bool wrongOwnerRejected = false;
        try
        {
            ProjectClosureAuditProjection.Build(Guid.NewGuid(), risks,
                requirements, deployments, reservations);
        }
        catch (InvalidDataException)
        {
            wrongOwnerRejected = true;
        }

        Assert(wrongOwnerRejected, "A different project must never inherit another audit.");

        string projectDirectory = Path.Combine(root, "Applications", "AuditDemo",
            "Projets", "Projet à contrôler");
        Directory.CreateDirectory(projectDirectory);
        ProjectManifest manifest = new()
        {
            Id = projectId, ApplicationId = Guid.NewGuid(),
            Name = "Projet à contrôler",
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, ".project.json"),
            JsonSerializer.Serialize(manifest));
        string[] before = Directory.GetFiles(projectDirectory, "*",
            SearchOption.AllDirectories);
        ProjectClosureAuditSnapshot read = await new ProjectClosureAuditService(root)
            .ReadAsync(projectDirectory);
        string[] after = Directory.GetFiles(projectDirectory, "*",
            SearchOption.AllDirectories);
        Assert(read.ProjectId == projectId && read.Items.Count == 0 &&
               before.SequenceEqual(after),
            "Reading an existing legacy project must not initialize v2 sources.");
        Directory.Delete(Path.Combine(root, "Applications", "AuditDemo"), true);
    }

    /// <summary>Fails the smoke suite when an expected invariant is violated.</summary>
    /// <param name="condition">Assertion outcome.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
