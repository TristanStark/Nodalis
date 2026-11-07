using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Projects;

/// <summary>Read-only A/B preflight and durable, non-executing handover decisions.</summary>
internal static class ProjectHandoverIntentSmokeTests
{
    /// <summary>Exercises exact owners, conflicts, stale sources and archives.</summary>
    /// <param name="root">Disposable temporary workspace root.</param>
    /// <returns>Completed assertions.</returns>
    public static async Task RunAsync(string root)
    {
        string applicationRoot = Path.Combine(root, "Applications",
            "HandoverFixture");
        string source = Path.Combine(applicationRoot, "Projets", "Source");
        string target = Path.Combine(applicationRoot, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid sourceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Guid appId = Guid.NewGuid();
        ProjectManifest sourceManifest = new()
        {
            Id = sourceId, ApplicationId = appId, Name = "Source",
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        ProjectManifest targetManifest = sourceManifest with
        {
            Id = targetId, Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(sourceManifest));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(targetManifest));
        Guid taskId = Guid.NewGuid();
        string sourceTask = "- [ ] Action à transmettre " +
            TaskPersistentIdentityCodec.FormatMarker(taskId) + "\n";
        string sourceDocument = Path.Combine(source, "Notes rapides.md");
        string targetDocument = Path.Combine(target, "Notes rapides.md");
        await File.WriteAllTextAsync(sourceDocument, sourceTask);
        await File.WriteAllTextAsync(targetDocument, "# Projet destinataire\n");

        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverIntentService intentions = new(root);
        ProjectHandoverIntentSnapshot initial = await intentions.ReadAsync(source);
        Assert(initial.SourceSha256 is null &&
               !Directory.Exists(Path.Combine(source, "Management")),
            "Consulting handover plans must not create a Management folder.");

        ProjectHandoverPreview move = await preflight.PrepareAsync(
            source, target, taskId, ProjectHandoverMethod.MoveOriginal);
        ProjectHandoverPreview successor = await preflight.PrepareAsync(
            source, target, taskId, ProjectHandoverMethod.LinkedSuccessor);
        Assert(move.CanRecord && successor.CanRecord &&
               move.ProposedTargetObjectId == taskId &&
               successor.ProposedTargetObjectId != taskId &&
               successor.ProposedTargetObjectId != Guid.Empty &&
               successor.SourceFingerprint == move.SourceFingerprint,
            "A preserves the source identity; B proposes a distinct identity.");

        ProjectHandoverIntentSnapshot recorded =
            await intentions.RecordIntentAsync(successor, initial,
                "Reviewer", "Plan de succession à discuter avec l'équipe.");
        string handovers = Path.Combine(source, "Management", "Passations.md");
        Assert(recorded.Ledger.Intents.Count == 1 &&
               recorded.Ledger.Intents[0].Method ==
                   ProjectHandoverMethod.LinkedSuccessor &&
               recorded.Ledger.Intents[0].ProposedTargetObjectId ==
                   successor.ProposedTargetObjectId &&
               recorded.SourceSha256 is { Length: 64 } &&
               await File.ReadAllTextAsync(sourceDocument) == sourceTask &&
               await File.ReadAllTextAsync(targetDocument) ==
                   "# Projet destinataire\n",
            "Recording B must not copy or move either original business file.");
        Assert(ProjectHandoverIntentCodec.Parse(
                await File.ReadAllTextAsync(handovers))
                    .Intents.Count == 1,
            "A human decision must survive a canonical Markdown round-trip.");

        bool staleReportRejected = false;
        try
        {
            await intentions.RecordIntentAsync(move, initial,
                "Reviewer", "Ancienne révision.");
        }
        catch (InvalidDataException)
        {
            staleReportRejected = true;
        }

        Assert(staleReportRejected,
            "A stale handover ledger must never be appended or overwritten.");

        ProjectHandoverPreview refreshed = await preflight.PrepareAsync(
            source, target, taskId, ProjectHandoverMethod.LinkedSuccessor);
        bool duplicateRejected = false;
        try
        {
            await intentions.RecordIntentAsync(refreshed, recorded,
                "Reviewer", "Même décision répétée.");
        }
        catch (InvalidDataException)
        {
            duplicateRejected = true;
        }

        Assert(duplicateRejected,
            "One source/target/method request may not be silently duplicated.");

        string oldTarget = await File.ReadAllTextAsync(targetDocument);
        await File.WriteAllTextAsync(targetDocument,
            oldTarget + "- [ ] Nouvelle action\n");
        bool targetDriftRejected = false;
        try
        {
            ProjectHandoverPreview staleTarget = await preflight.PrepareAsync(
                source, target, taskId, ProjectHandoverMethod.MoveOriginal);
            await File.AppendAllTextAsync(targetDocument,
                "- [ ] Changement après aperçu\n");
            await intentions.RecordIntentAsync(staleTarget, recorded,
                "Reviewer", "Cible modifiée.");
        }
        catch (InvalidDataException)
        {
            targetDriftRejected = true;
        }

        Assert(targetDriftRejected,
            "Destination changes after preview must block signing.");

        await File.WriteAllTextAsync(targetDocument,
            "- [x] Ancien objet soldé " +
            TaskPersistentIdentityCodec.FormatMarker(taskId) + "\n");
        ProjectHandoverPreview collision = await preflight.PrepareAsync(
            source, target, taskId, ProjectHandoverMethod.MoveOriginal);
        Assert(!collision.CanRecord &&
               collision.Blockers.Any(item =>
                   item.Contains("déjà présent", StringComparison.Ordinal)) &&
               collision.ProposedTargetObjectId == taskId,
            "Even a completed target task must block an A GUID collision.");

        ProjectHandoverPreview linked = await preflight.PrepareAsync(
            source, target, taskId, ProjectHandoverMethod.LinkedSuccessor);
        Assert(linked.CanRecord &&
               linked.ProposedTargetObjectId != taskId,
            "Mode B may prepare a new distinct identity without editing either task.");

        bool sameOwnerRejected = false;
        try
        {
            await preflight.PrepareAsync(source, source, taskId,
                ProjectHandoverMethod.MoveOriginal);
        }
        catch (InvalidDataException)
        {
            sameOwnerRejected = true;
        }

        Assert(sameOwnerRejected, "A project cannot transfer to itself.");
        bool unrelatedOwnerRejected = false;
        try
        {
            await preflight.PrepareAsync(target, source, taskId,
                ProjectHandoverMethod.MoveOriginal);
        }
        catch (InvalidDataException)
        {
            unrelatedOwnerRejected = true;
        }

        Assert(unrelatedOwnerRejected,
            "Selection from another owner must not resolve to a source item.");

        string archive = await new ProjectArchiveService(root)
            .ArchiveAsync(target);
        bool archivedRecipientRejected = false;
        try
        {
            await intentions.RecordIntentAsync(linked, recorded,
                "Reviewer", "Destinataire devenu archive.");
        }
        catch (InvalidDataException)
        {
            archivedRecipientRejected = true;
        }

        Assert(archivedRecipientRejected,
            "An archived destination must not accept a new handover intention.");
        await new ProjectArchiveService(root).RestoreAsync(archive);

        string stored = await File.ReadAllTextAsync(handovers);
        Assert(recorded.Ledger.Intents.Count == 1 &&
               stored.Contains(successor.ProposedTargetObjectId.ToString("D"),
                   StringComparison.Ordinal),
            "Rejected actions must not change the historical handover ledger.");

        string legacy = Path.Combine(applicationRoot, "Projets", "Legacy");
        Directory.CreateDirectory(Path.Combine(legacy, "Management"));
        await File.WriteAllTextAsync(Path.Combine(legacy, ".project.json"),
            JsonSerializer.Serialize(sourceManifest with
            {
                Id = Guid.NewGuid(), Name = "Legacy"
            }));
        string legacyPath = Path.Combine(legacy, "Management", "Passations.md");
        await File.WriteAllTextAsync(legacyPath, "# Notes humaines anciennes\n");
        bool legacyProtected = false;
        try
        {
            await intentions.ReadAsync(legacy);
        }
        catch (InvalidDataException)
        {
            legacyProtected = true;
        }

        Assert(legacyProtected &&
               (await File.ReadAllTextAsync(legacyPath)).Contains(
                   "Notes humaines anciennes", StringComparison.Ordinal),
            "An existing free-text handover document must remain untouched.");
        Directory.Delete(applicationRoot, recursive: true);
    }

    /// <summary>Fails the smoke suite on a broken safety invariant.</summary>
    /// <param name="value">Boolean predicate.</param>
    /// <param name="message">Meaningful smoke failure text.</param>
    private static void Assert(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
