using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

/// <summary>Safe, repeatable, revision-checked project closure report regressions.</summary>
internal static class ProjectClosureReportSmokeTests
{
    /// <summary>Checks opt-in, immutable history, stale audits and archived reads.</summary>
    /// <param name="root">Disposable workspace managed by the smoke runner.</param>
    /// <returns>Completed verification.</returns>
    public static async Task RunAsync(string root)
    {
        string baseDirectory = Path.Combine(root, "Applications", "ClosureDemo");
        string directory = Path.Combine(baseDirectory, "Projets", "Démonstration");
        Directory.CreateDirectory(directory);
        Guid projectId = Guid.NewGuid();
        ProjectManifest project = new()
        {
            Id = projectId,
            Name = "Démonstration",
            ApplicationId = Guid.NewGuid(),
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        await File.WriteAllTextAsync(Path.Combine(directory, ".project.json"),
            JsonSerializer.Serialize(project));

        ProjectClosureReportService reports = new(root);
        ProjectClosureAuditService audits = new(root);
        ProjectClosureReportSnapshot empty = await reports.ReadAsync(directory);
        ProjectClosureAuditSnapshot audit = await audits.ReadAsync(directory);
        Assert(empty.SourceSha256 is null && empty.Report.Reviews.Count == 0 &&
               audit.SourceFingerprint.Length == 64 &&
               !Directory.Exists(Path.Combine(directory, "Management")),
            "Read-only consultation must not initialize a closure report.");

        ProjectClosureReportSnapshot first = await reports.RecordAsync(directory,
            empty, audit, ProjectClosureReviewOutcome.ReviewRecorded,
            "Responsable fictif", "Examiner la passation avant archivage.");
        string markdownPath = Path.Combine(directory, "Management",
            "Bilan de clôture.md");
        string markdown = await File.ReadAllTextAsync(markdownPath);
        ProjectClosureReport parsed = ProjectClosureReportCodec.Parse(markdown);
        Assert(first.Report.Reviews.Count == 1 &&
               parsed.Reviews[0].Number == 1 &&
               parsed.Reviews[0].SourceFingerprint == audit.SourceFingerprint &&
               first.SourceSha256 is { Length: 64 },
            "The first signed source snapshot must be canonical and traceable.");

        ProjectClosureReportSnapshot second = await reports.RecordAsync(directory,
            first, audit, ProjectClosureReviewOutcome.Deferred,
            "Responsable fictif", "Attendre la qualification des autres registres.");
        Assert(second.Report.Reviews.Count == 2 &&
               second.Report.Reviews[0] == first.Report.Reviews[0] &&
               second.Report.Reviews[1].Number == 2,
            "New reviews must append instead of rewriting historical decisions.");
        bool staleReportRejected = false;
        try
        {
            await reports.RecordAsync(directory, first, audit,
                ProjectClosureReviewOutcome.ReadyForArchive,
                "Responsable fictif", "Révision du bilan obsolète.");
        }
        catch (InvalidDataException)
        {
            staleReportRejected = true;
        }

        Assert(staleReportRejected, "Concurrent report versions must be refused.");

        await new ProjectRequirementRegistryService(root).CreateAsync(
            directory, "Exigence de démonstration");
        bool staleAuditRejected = false;
        try
        {
            await reports.RecordAsync(directory, second, audit,
                ProjectClosureReviewOutcome.ReadyForArchive,
                "Responsable fictif", "Un fichier EXI a changé.");
        }
        catch (InvalidDataException)
        {
            staleAuditRejected = true;
        }

        Assert(staleAuditRejected,
            "An audit cannot be signed after the underlying EXI source changes.");
        ProjectClosureAuditSnapshot updatedAudit = await audits.ReadAsync(directory);
        Assert(updatedAudit.Items.Count == 1 &&
               updatedAudit.SourceFingerprint != audit.SourceFingerprint,
            "An open EXI must appear in the refreshed, hash-pinned inventory.");
        ProjectClosureReportSnapshot third = await reports.RecordAsync(directory,
            second, updatedAudit, ProjectClosureReviewOutcome.ReadyForArchive,
            "Responsable fictif", "Archivage envisagé avec exigence à suivre.");
        Assert(third.Report.Reviews.Count == 3 &&
               third.Report.Reviews[2].Items.Count == 1 &&
               third.Report.Reviews[0].Items.Count == 0,
            "Each review must freeze its own inventory without rewriting the past.");

        string archived = await new ProjectArchiveService(root)
            .ArchiveAsync(directory);
        ProjectClosureReportSnapshot history = await reports.ReadAsync(archived);
        Assert(history.Report.Reviews.Count == 3,
            "Archived project roots must allow reading all original reviews.");
        bool archivedWriteRejected = false;
        try
        {
            await reports.RecordAsync(archived, history, updatedAudit,
                ProjectClosureReviewOutcome.Deferred,
                "Responsable fictif", "Interdit en archive.");
        }
        catch (InvalidOperationException)
        {
            archivedWriteRejected = true;
        }

        Assert(archivedWriteRejected,
            "Archived reviews must be immutable, even when history is readable.");
        await new ProjectArchiveService(root).RestoreAsync(archived);

        string other = Path.Combine(baseDirectory, "Projets", "Ancien bilan");
        string folder = Path.Combine(other, "Management");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(other, ".project.json"),
            JsonSerializer.Serialize(project with
            {
                Id = Guid.NewGuid(), Name = "Ancien bilan"
            }));
        string incompatible = Path.Combine(folder, "Bilan de clôture.md");
        await File.WriteAllTextAsync(incompatible, "# Ancien bilan libre\nTexte original.");
        bool originalPreserved = false;
        try
        {
            await reports.ReadAsync(other);
        }
        catch (InvalidDataException)
        {
            originalPreserved = true;
        }

        Assert(originalPreserved &&
               (await File.ReadAllTextAsync(incompatible))
                   .Contains("Texte original.", StringComparison.Ordinal),
            "Legacy user-authored closure documents must never be replaced.");
        Directory.Delete(baseDirectory, recursive: true);
    }

    /// <summary>Raises a useful smoke failure for a violated invariant.</summary>
    /// <param name="condition">Result of the assertion.</param>
    /// <param name="message">Diagnostic to display on CI.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
