using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Projects;

/// <summary>Source-scoped coverage for the expanded closure inventory.</summary>
internal static class ProjectClosureAdditionalSmokeTests
{
    /// <summary>
    /// Checks task, milestone, REC and incident inclusion and exclusion with
    /// an independently owned nested project and no hidden write.
    /// </summary>
    /// <param name="root">Disposable workspace.</param>
    /// <returns>Completed regression.</returns>
    public static async Task RunAsync(string root)
    {
        string parent = Path.Combine(root, "Applications", "ClosureExtra");
        string project = Path.Combine(parent, "Projets", "Owner");
        string child = Path.Combine(project, "Sous-projets", "Other");
        Directory.CreateDirectory(child);
        Guid projectId = Guid.NewGuid();
        ProjectManifest manifest = new()
        {
            Id = projectId, Name = "Owner", ApplicationId = Guid.NewGuid(),
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        await File.WriteAllTextAsync(Path.Combine(project, ".project.json"),
            JsonSerializer.Serialize(manifest));
        await File.WriteAllTextAsync(Path.Combine(child, ".project.json"),
            JsonSerializer.Serialize(manifest with
            {
                Id = Guid.NewGuid(), Name = "Other",
                ParentProjectId = projectId
            }));

        Guid taskId = Guid.NewGuid();
        Guid childTaskId = Guid.NewGuid();
        string notePath = Path.Combine(project, "Notes rapides.md");
        string tasks = "# Notes\n- [ ] Action principale " +
            TaskPersistentIdentityCodec.FormatMarker(taskId) +
            "\n- [ ] Ancienne action sans GUID\n" +
            "- [x] Action soldée " +
            TaskPersistentIdentityCodec.FormatMarker(Guid.NewGuid()) +
            "\n~~~md\n- [ ] Checkbox d'exemple\n~~~\n";
        await File.WriteAllTextAsync(notePath, tasks);
        await File.WriteAllTextAsync(Path.Combine(child, "Notes rapides.md"),
            "- [ ] Action d'un sous-projet " +
            TaskPersistentIdentityCodec.FormatMarker(childTaskId));

        Guid milestoneId = Guid.NewGuid();
        string milestones = "# Jalons\n\n" +
            "| Jalon | Date cible | Statut | Description | Lien | Id | Dépend de |\n" +
            "| --- | --- | --- | --- | --- | --- | --- |\n" +
            "| Validation métier | 10/11/2026 | À faire | Relecture | | " +
            milestoneId.ToString("D") + " | |\n" +
            "| Archive prête | | Terminée | | | " + Guid.NewGuid().ToString("D") +
            " | |\n";
        await File.WriteAllTextAsync(Path.Combine(project, "Jalons.md"),
            milestones);

        string incidentId = MaintenanceMarkdownCodec.NewId(
            MaintenanceRecordKind.Incident);
        string incidentMarkdown = MaintenanceMarkdownCodec.Append(
            "# Fiche de Maintenance\n", new MaintenanceRecord
            {
                Kind = MaintenanceRecordKind.Incident,
                Id = incidentId,
                Date = new DateOnly(2026, 11, 7),
                Summary = "Incident en revue"
            });
        await File.WriteAllTextAsync(Path.Combine(project,
            "Fiche de Maintenance.md"), incidentMarkdown);

        ProjectTestCampaignService campaignService = new(root);
        ProjectTestCampaignCatalog empty = await campaignService.ListAsync(
            project);
        ProjectTestCampaignEntry created = await campaignService.CreateAsync(
            project, "REC à qualifier", empty.NextReference!);
        ProjectClosureAuditService closure = new(root);
        ProjectClosureAuditSnapshot audit = await closure.ReadAsync(project);
        Assert(audit.Items.Any(item => item.Id == taskId &&
                item.Kind == "Tâches ouvertes"),
            "A persistent unchecked project task must appear.");
        Assert(!audit.Items.Any(item => item.Id == childTaskId),
            "Nested projects must never be scanned as the parent owner.");
        Assert(audit.Items.Any(item => item.Id == milestoneId &&
                item.Kind == "Jalons à suivre"),
            "An outstanding explicit milestone must appear.");
        Assert(audit.Items.Any(item => item.Id == created.Campaign.Id &&
                item.Kind == "Campagnes REC"),
            "A draft REC campaign must appear without inferring test outcomes.");
        Assert(audit.Items.Any(item => item.Reference == incidentId &&
                item.Kind == "Incidents à vérifier"),
            "A canonical incident must request human status review.");
        Assert(audit.Diagnostics.Any(item =>
                item.Contains("sans ID persistant", StringComparison.Ordinal)),
            "Legacy unnumbered open checkboxes must be diagnosed.");
        Assert((await File.ReadAllTextAsync(notePath)) == tasks &&
               (await File.ReadAllTextAsync(Path.Combine(project, "Jalons.md")))
                   == milestones &&
               (await File.ReadAllTextAsync(Path.Combine(project,
                   "Fiche de Maintenance.md"))) == incidentMarkdown,
            "The audit must not rewrite existing Markdown.");

        string completed = tasks.Replace("- [ ] Action principale",
            "- [x] Action principale", StringComparison.Ordinal);
        await File.WriteAllTextAsync(notePath, completed);
        ProjectClosureAuditSnapshot changed = await closure.ReadAsync(project);
        Assert(!changed.Items.Any(item => item.Id == taskId) &&
               changed.SourceFingerprint != audit.SourceFingerprint,
            "Checking a task must update the fingerprint and inventory.");

        Guid repeated = Guid.NewGuid();
        string duplicate = "- [ ] Répétée " +
            TaskPersistentIdentityCodec.FormatMarker(repeated);
        await File.AppendAllTextAsync(notePath, duplicate + "\n");
        await File.WriteAllTextAsync(Path.Combine(project, "Autre.md"),
            duplicate + "\n");
        ProjectClosureAuditSnapshot ambiguous = await closure.ReadAsync(project);
        Assert(!ambiguous.Items.Any(item => item.Id == repeated) &&
               ambiguous.Diagnostics.Any(item =>
                   item.Contains("dupliqué", StringComparison.OrdinalIgnoreCase)),
            "Cross-document duplicate task identities must not be transferred.");

        // Moving the source to another project after the preview must not
        // allow a historical closure review to be signed against stale data.
        ProjectClosureReportService reports = new(root);
        ProjectClosureReportSnapshot original = await reports.ReadAsync(project);
        bool stale = false;
        try
        {
            await reports.RecordAsync(project, original, audit,
                Nodalis.Core.Projects.ProjectClosureReviewOutcome.ReviewRecorded,
                "Responsable fictif", "Une tâche a changé entre les revues.");
        }
        catch (InvalidDataException)
        {
            stale = true;
        }

        Assert(stale && original.Report.Reviews.Count == 0 &&
               !File.Exists(Path.Combine(project, "Management",
                   "Bilan de clôture.md")),
            "A stale extended inventory must be rejected without writes.");

        Directory.Delete(parent, recursive: true);
    }

    /// <summary>Throws a clear regression failure on a violated invariant.</summary>
    /// <param name="condition">Assertion.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
