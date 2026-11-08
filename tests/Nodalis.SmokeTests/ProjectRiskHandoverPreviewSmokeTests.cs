using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic RIS A/B review: no read-time creation, no inherited
/// assessment or treatment history, and strict source/target snapshots.
/// </summary>
internal static class ProjectRiskHandoverPreviewSmokeTests
{
    /// <summary>Runs isolated non-mutating RIS handover checks.</summary>
    /// <param name="root">Temporary Nodalis smoke workspace.</param>
    /// <returns>Completed source and target checks.</returns>
    public static async Task RunAsync(string root)
    {
        string application = Path.Combine(root, "Applications",
            "RiskHandoverFixture");
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid applicationId = Guid.NewGuid();
        ProjectManifest originalManifest = new()
        {
            Id = Guid.NewGuid(), ApplicationId = applicationId,
            Name = "Source", InitialComplexity = ProjectComplexity.Complex
        };
        ProjectManifest targetManifest = originalManifest with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(
            Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(originalManifest));
        await File.WriteAllTextAsync(
            Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(targetManifest));

        ProjectRiskRegistryService risks = new(root);
        ProjectRiskEntry original = await risks.CreateAsync(source,
            "Synthetic risk without real business data");
        string file = Path.Combine(source, "Analyse", "Risques",
            original.Card.Reference + ".md");
        await File.AppendAllTextAsync(file,
            "\nHistorique synthétique à ne pas copier : CANARY_RIS_123\n");
        string unchanged = await File.ReadAllTextAsync(file);
        ProjectRiskHandoverDraft.Prepare(
            original.Card with
            {
                Initial = new ProjectRiskRating(3, 3),
                Residual = new ProjectRiskRating(2, 2),
                Qualification = ProjectRiskQualification.Validated,
                TreatmentHeadId = Guid.NewGuid()
            }, targetManifest.Id, "RIS-0002", Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        string freshMarkdown = ProjectRiskHandoverDraft.Prepare(
            original.Card with
            {
                Initial = new ProjectRiskRating(3, 3),
                Residual = new ProjectRiskRating(2, 2),
                Qualification = ProjectRiskQualification.Validated,
                TreatmentHeadId = Guid.NewGuid()
            }, targetManifest.Id, "RIS-0002", Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        ProjectRiskCard fresh = ProjectRiskMarkdownCodec.Parse(freshMarkdown);
        Assert(fresh.ProjectId == targetManifest.Id &&
               fresh.Qualification == ProjectRiskQualification.ToReview &&
               fresh.Lifecycle == ProjectRiskLifecycle.Open &&
               fresh.Initial == new ProjectRiskRating(null, null) &&
               fresh.Residual == new ProjectRiskRating(null, null) &&
               fresh.TreatmentHeadId is null &&
               !freshMarkdown.Contains("CANARY_RIS_123",
                   StringComparison.Ordinal),
            "A B draft must not inherit historic decisions, ratings or notes.");

        bool reusedIdentityRejected = false;
        try
        {
            ProjectRiskHandoverDraft.Prepare(original.Card, targetManifest.Id,
                "RIS-0002", original.Card.Id, DateTimeOffset.UtcNow);
        }
        catch (InvalidDataException)
        {
            reusedIdentityRejected = true;
        }

        Assert(reusedIdentityRejected,
            "A RIS successor must never reuse the original GUID.");

        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverIntentService intents = new(root);
        ProjectHandoverIntentSnapshot initial =
            await intents.ReadAsync(source);
        ProjectHandoverPreview candidate = await preflight.PrepareAsync(
            source, target, original.Card.Id,
            ProjectHandoverMethod.LinkedSuccessor);
        Assert(candidate.CanRecord, "Synthetic RIS B must pass preflight.");
        ProjectHandoverIntentSnapshot recorded =
            await intents.RecordIntentAsync(candidate, initial,
                "Synthetic reviewer", "Traçabilité explicite pour reprise.");
        Guid intentId = recorded.Ledger.Intents[^1].Id;
        ProjectRiskHandoverPreviewService previews = new(root);
        ProjectRiskHandoverPreview preview = await previews.PrepareAsync(
            source, intentId);
        Assert(preview.CanPreview && preview.SuccessorMarkdown is not null &&
               preview.TargetId == candidate.ProposedTargetObjectId &&
               preview.TargetReference == "RIS-0001" &&
               preview.SuccessorMarkdown.Contains(
                   candidate.ProposedTargetObjectId.ToString("D"),
                   StringComparison.Ordinal) &&
               !preview.SuccessorMarkdown.Contains("CANARY_RIS_123",
                   StringComparison.Ordinal) &&
               (await File.ReadAllTextAsync(file)) == unchanged &&
               !Directory.Exists(Path.Combine(target, "Analyse", "Risques")),
            "Preview must be SHA-bound, read-only and exclude source history.");

        ProjectHandoverPreview move = await preflight.PrepareAsync(
            source, target, original.Card.Id,
            ProjectHandoverMethod.MoveOriginal);
        ProjectHandoverIntentSnapshot movedIntent =
            await intents.RecordIntentAsync(move, recorded,
                "Synthetic reviewer", "Déplacement à requalifier.");
        ProjectRiskHandoverPreview movePreview = await previews.PrepareAsync(
            source, movedIntent.Ledger.Intents[^1].Id);
        Assert(movePreview.CanPreview &&
               movePreview.TargetId == original.Card.Id &&
               movePreview.SuccessorMarkdown is null &&
               (await File.ReadAllTextAsync(file)) == unchanged,
            "A preview cannot move the source or create a successor.");

        await File.AppendAllTextAsync(file,
            "\nRévision après préparation de la passation.\n");
        bool staleRejected = false;
        try
        {
            await previews.PrepareAsync(source, intentId);
        }
        catch (InvalidDataException)
        {
            staleRejected = true;
        }

        Assert(staleRejected, "An edited source RIS must invalidate a preview.");
        Directory.Delete(application, recursive: true);
    }

    /// <summary>Stops on a failed synthetic safety invariant.</summary>
    /// <param name="value">Expected property.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
