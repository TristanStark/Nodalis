using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Projects;
using System.Text.Json;

/// <summary>Regressions for reversible project closure and navigation.</summary>
internal static class ProjectArchivesSmokeTests
{
    /// <summary>Verifies move, stable IDs, restoration, collisions, and intact subprojects.</summary>
    /// <param name="root">Disposable smoke workspace.</param>
    /// <returns>Asynchronous verification.</returns>
    public static async Task RunAsync(string root)
    {
        string projects = Path.Combine(root, "Applications", "ArchiveDemo", "Projets");
        string project = Path.Combine(projects, "Projet clos");
        string nested = Path.Combine(project, "Sous-projets", "Sous-projet");
        Directory.CreateDirectory(nested);
        ProjectManifest original = new ProjectManifest
        {
            Id = Guid.NewGuid(),
            Name = "Projet clos",
            ApplicationId = Guid.NewGuid(),
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        await File.WriteAllTextAsync(Path.Combine(project, ".project.json"),
            JsonSerializer.Serialize(original));
        ProjectManifest child = original with { Id = Guid.NewGuid(), Name = "Sous-projet",
            ParentProjectId = original.Id };
        await File.WriteAllTextAsync(Path.Combine(nested, ".project.json"),
            JsonSerializer.Serialize(child));
        string notes = Path.Combine(project, "Notes rapides.md");
        await File.WriteAllTextAsync(notes, "# Notes originales\n\nNe pas modifier.\n");

        ProjectArchiveService service = new ProjectArchiveService(root);
        // Closing a project with unfinished DOCX writes would orphan their
        // journal because every destination still points to the active tree.
        DocxFirstImportRecoveryService recovery =
            new DocxFirstImportRecoveryService(root);
        string expected = Path.Combine(project, "Tests", "Import - incident.md");
        DocxImportPlan plan = new DocxImportPlan
        {
            TargetProjectDisplayName = original.Name,
            TargetProjectRelativePath = Path.GetRelativePath(root, project)
                .Replace('\\', '/'),
            Fingerprint = new string('a', 64)
        };
        using (DocxFirstImportSession pending = await recovery.BeginAsync(
                   plan, Guid.NewGuid(), original.Id,
                   [
                       new DocxFirstImportFile
                       {
                           RelativePath = Path.GetRelativePath(root, expected)
                               .Replace('\\', '/'),
                           Sha256 = DocxImportManifestStore.ComputeSha256("expected")
                       }
                   ]))
        {
            // Persist the unfinished journal but release its workspace lock.
        }

        bool pendingBlocked = false;
        try
        {
            await service.ArchiveAsync(project);
        }
        catch (InvalidOperationException)
        {
            pendingBlocked = true;
        }

        Assert(pendingBlocked && Directory.Exists(project),
            "Project archive must refuse to orphan an interrupted DOCX import journal.");
        Assert(await recovery.RecoverPendingAsync() == 1,
            "The interrupted import must be safely reconciled before closure.");
        string archived = await service.ArchiveAsync(project);
        Assert(!Directory.Exists(project) && Directory.Exists(archived),
            "A closed project must leave the active application tree.");
        Assert(File.Exists(Path.Combine(archived, "Sous-projets", "Sous-projet", ".project.json")) &&
               (await File.ReadAllTextAsync(Path.Combine(archived, "Notes rapides.md")))
                   .Contains("Ne pas modifier.", StringComparison.Ordinal),
            "The complete project and nested projects must survive closure.");

        WorkspaceNavigationBuilder navigation = new WorkspaceNavigationBuilder();
        WorkspaceNavigationNode tree = await navigation.BuildAsync(root);
        WorkspaceNavigationNode archiveRoot = tree.Children.Single(item =>
            item.DisplayName == "Archives");
        Assert(archiveRoot.Children.Count == 1 &&
               archiveRoot.Children[0].Id == original.Id &&
               archiveRoot.Children[0].Children.Any(item => item.DisplayName == "Sous-projets"),
            "Archived projects and their IDs must remain visible in navigation.");

        Directory.CreateDirectory(project);
        bool rejectedCollision = false;
        try
        {
            await service.RestoreAsync(archived);
        }
        catch (IOException)
        {
            rejectedCollision = true;
        }

        Assert(rejectedCollision && Directory.Exists(archived),
            "Restoring must never overwrite another directory.");
        Directory.Delete(project);
        string restored = await service.RestoreAsync(archived);
        Assert(restored == project && !Directory.Exists(archived) &&
               File.Exists(Path.Combine(project, "Notes rapides.md")),
            "Reopening must restore the project to its exact source location.");
        ProjectArchiveService again = new ProjectArchiveService(root);
        string second = await again.ArchiveAsync(restored);
        Assert(Directory.Exists(second), "Re-archiving a restored project must work.");
        await again.RestoreAsync(second);
        Directory.Delete(Path.Combine(root, "Applications", "ArchiveDemo"), recursive: true);
    }

    /// <summary>Fails a regression with a readable error message.</summary>
    /// <param name="condition">Assertion result.</param>
    /// <param name="message">Failure context.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
