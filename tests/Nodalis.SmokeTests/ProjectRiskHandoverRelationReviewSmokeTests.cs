using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Pure RIS A/B relation checklist regression using synthetic source snapshots.
/// No task/Gate relations or verdicts are ever persisted or migrated.
/// </summary>
internal static class ProjectRiskHandoverRelationReviewSmokeTests
{
    /// <summary>Runs pinned-source and bounded relationship audit checks.</summary>
    /// <param name="root">Temporary smoke workspace.</param>
    /// <returns>Validated actionable, non-authorizing checklist.</returns>
    public static async Task RunAsync(string root)
    {
        string application = Path.Combine(root, "Applications",
            "RiskRelationsFixture");
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid applicationId = Guid.NewGuid();
        ProjectManifest owner = new()
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId,
            Name = "Source", InitialComplexity = ProjectComplexity.Complex
        };
        ProjectManifest recipient = owner with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(owner));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(recipient));
        ProjectRiskRegistryService service = new(root);
        ProjectRiskEntry created = await service.CreateAsync(source,
            "Synthetic requalification risk");
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverIntentService intentions = new(root);
        ProjectHandoverPreview prepared = await preflight.PrepareAsync(
            source, target, created.Card.Id,
            ProjectHandoverMethod.LinkedSuccessor);
        ProjectHandoverIntentSnapshot recorded =
            await intentions.RecordIntentAsync(prepared,
                await intentions.ReadAsync(source),
                "Synthetic owner", "Review without transferring historical proof.");
        ProjectRiskHandoverPreview preview =
            await new ProjectRiskHandoverPreviewService(root).PrepareAsync(
                source, recorded.Ledger.Intents[^1].Id);
        ProjectRiskCatalog risks = await service.ListAsync(source);
        ProjectRiskEntry baseEntry = risks.Risks.Single(item =>
            item.Card.Id == created.Card.Id);
        Guid treatmentId = Guid.NewGuid();
        Guid oldTreatmentId = Guid.NewGuid();
        Guid currentTaskId = Guid.NewGuid();
        Guid historicTaskId = Guid.NewGuid();
        Guid unlinkedTaskId = Guid.NewGuid();
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        ProjectRiskTreatmentEvent treatment = new()
        {
            Id = treatmentId, RiskId = created.Card.Id,
            Actor = "Synthetic reviewer",
            Rationale = "Synthetic treatment for relation qualification.",
            RecordedUtc = utc,
            Plan = new ProjectRiskTreatmentPlan(
                ProjectRiskTreatmentStrategy.Reduce, "Synthetic owner",
                "Review prevention task.", "Review mitigation.",
                "Review contingency.")
        };
        ProjectRiskTaskLinkEvent linked = Link(
            created.Card.Id, treatmentId, currentTaskId, utc);
        ProjectRiskTaskLinkEvent old = Link(
            created.Card.Id, oldTreatmentId, historicTaskId, utc);
        ProjectRiskTaskLinkEvent removed = Link(
            created.Card.Id, treatmentId, unlinkedTaskId, utc);
        ProjectRiskTaskLinkEvent unlink = removed with
        {
            Id = Guid.NewGuid(), PreviousId = removed.Id,
            Action = ProjectRiskTaskLinkAction.Unlink,
            RecordedUtc = utc.AddMinutes(1)
        };
        ProjectRiskEntry selected = baseEntry with
        {
            Treatments = [treatment],
            TaskLinks = [old, linked, removed, unlink]
        };
        ProjectRiskGateBacklink activeGate = new(
            Guid.NewGuid(), "Gate QA", Guid.NewGuid(), Guid.NewGuid(),
            "Synthetic reservation still open",
            ProjectGateVerdict.GoWithReservations, true, true, utc);
        ProjectRiskGateBacklink historyGate = new(
            activeGate.GateId, "Gate QA", activeGate.DecisionId,
            Guid.NewGuid(), "Synthetic lifted reservation",
            ProjectGateVerdict.GoWithReservations, false, false,
            utc.AddMinutes(2));
        ProjectRiskGateBacklinkCatalog gateCatalog = new(
            created.Card.Id, [historyGate, activeGate], []);
        ProjectRiskHandoverRelationReview review =
            ProjectRiskHandoverRelationReviewBuilder.Build(
                preview, new ProjectRiskCatalog([selected], [], "RIS-0002"),
                gateCatalog);
        Assert(review.IsComplete && review.Items.Count == 5 &&
               review.ActionCount == 2 &&
               review.Items.Any(item =>
                   item.Identity == currentTaskId && item.RequiresDecision) &&
               review.Items.Any(item =>
                   item.Identity == historicTaskId &&
                   !item.RequiresDecision) &&
               review.Items.Any(item =>
                   item.Identity == unlinkedTaskId &&
                   !item.RequiresDecision) &&
               review.Items.Any(item =>
                   item.Identity == activeGate.ReservationId &&
                   item.RequiresDecision) &&
               review.Items.Any(item =>
                   item.Identity == historyGate.ReservationId &&
                   !item.RequiresDecision) &&
               review.FormatChecklist().Contains(
                   "aucun lien, état de tâche ou verdict",
                   StringComparison.OrdinalIgnoreCase),
            "A/B checklist must distinguish current/historical task and " +
            "frozen Gate links without approving them.");
        // One task per group, plus one current + one historical Gate.
        Assert(review.Items.Count(item =>
                   item.Kind == ProjectRiskHandoverRelationKind.Task) == 3 &&
               review.Items.Count(item =>
                   item.Kind ==
                   ProjectRiskHandoverRelationKind.GateReservation) == 2,
            "Every task and Gate relationship needs exactly one action row.");

        ProjectRiskEntry stale = selected with
        {
            SourceSha256 = new string('0', 64)
        };
        ProjectRiskHandoverRelationReview rejected =
            ProjectRiskHandoverRelationReviewBuilder.Build(
                preview, new ProjectRiskCatalog([stale], [], "RIS-0002"),
                gateCatalog);
        Assert(!rejected.IsComplete && rejected.Items.Count == 0,
            "Stale source SHA must not produce a trustworthy checklist.");

        ProjectRiskHandoverRelationReview wrongGate =
            ProjectRiskHandoverRelationReviewBuilder.Build(
                preview, new ProjectRiskCatalog([selected], [], "RIS-0002"),
                gateCatalog with { RiskId = Guid.NewGuid() });
        Assert(!wrongGate.IsComplete,
            "Wrong Gate/RIS identity must be diagnosed.");

        ProjectRiskTaskLinkEvent[] oversized = Enumerable.Range(0, 1010)
            .Select(index => Link(created.Card.Id, treatmentId,
                Guid.NewGuid(), utc.AddSeconds(index))).ToArray();
        ProjectRiskHandoverRelationReview capped =
            ProjectRiskHandoverRelationReviewBuilder.Build(
                preview,
                new ProjectRiskCatalog(
                    [selected with { TaskLinks = oversized }],
                    [], "RIS-0002"),
                gateCatalog);
        Assert(capped.Items.Count == 1000 && !capped.IsComplete &&
               capped.Diagnostics.Any(d => d.Contains("tronquée",
                   StringComparison.Ordinal)),
            "Large RIS relation audit must fail closed at 1000 rows.");

        Directory.Delete(application, recursive: true);
    }

    /// <summary>Creates one synthetic stable task association.</summary>
    /// <param name="riskId">Risk identity.</param>
    /// <param name="treatmentId">Human plan version.</param>
    /// <param name="taskId">Stable task identity.</param>
    /// <param name="time">Event time.</param>
    /// <returns>Unpersisted human-link model.</returns>
    private static ProjectRiskTaskLinkEvent Link(
        Guid riskId, Guid treatmentId, Guid taskId, DateTimeOffset time) =>
        new()
        {
            Id = Guid.NewGuid(), RiskId = riskId,
            TreatmentId = treatmentId,
            Kind = ProjectRiskMeasureKind.Prevention,
            TaskId = taskId,
            Action = ProjectRiskTaskLinkAction.Link,
            Actor = "Synthetic reviewer",
            Rationale = "Review task owner before risk handover.",
            RecordedUtc = time
        };

    /// <summary>Fails visibly when a conservative audit invariant breaks.</summary>
    /// <param name="value">Expected condition.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
