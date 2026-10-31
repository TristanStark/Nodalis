using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Safe, repeatable Complex v2 migration inventory regressions.</summary>
internal static class ComplexMigrationPreviewSmokeTests
{
    /// <summary>Verifies collision detection and zero source mutations.</summary>
    /// <param name="root">Isolated test workspace parent.</param>
    /// <returns>Asynchronous regression suite.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Complex-v2-plan");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("Complex Preview");
        try
        {
            string projectRoot = Path.Combine(workspace, "Applications", "Test", "Projets", "Complex");
            Directory.CreateDirectory(projectRoot);
            ProjectManifest project = new ProjectManifest
            {
                Id = Guid.NewGuid(),
                ApplicationId = Guid.NewGuid(),
                Name = "Complex",
                InitialComplexity = ProjectComplexity.Complex,
                Sections =
                [
                    NewSection("Fonctionnel", "functional"),
                    NewSection("Technique", "technical-complex"),
                    NewSection("Risques", "risks"),
                    NewSection("Dépendances", "dependencies"),
                    NewSection("Déploiement", "deployment"),
                    NewSection("Réunions", null, false),
                    NewSection("Décisions", null, false),
                    NewSection("Jalons", "milestones"),
                    NewSection("Divers perso", null, false)
                ]
            };

            string file = Path.Combine(projectRoot, WorkspaceLayout.ProjectManifestFileName);
            string canonical = JsonSerializer.Serialize(project);
            await File.WriteAllTextAsync(file, canonical);
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "Jalons.md"), "# Jalons\n");

            foreach (string dir in new[]
                { "Fonctionnel", "Technique", "Risques", "Dépendances", "Déploiement",
                  "Réunions", "Décisions", "Divers perso" })
            {
                Directory.CreateDirectory(Path.Combine(projectRoot, dir));
            }

            string collision = Path.Combine(projectRoot, "Analyse", "Risques");
            Directory.CreateDirectory(collision);
            await File.WriteAllTextAsync(Path.Combine(collision, "custom.md"), "never overwrite");
            ComplexProjectMigrationPreviewService service = new(workspace);
            ComplexProjectMigrationPreview preview = await service.PreviewAsync(projectRoot);
            Assert(preview.ProjectId == project.Id &&
                preview.Moves.Count == 7 &&
                preview.Moves.Count(item => item.State == ComplexMigrationMoveState.Candidate) == 6 &&
                preview.Moves.Single(item => item.SectionName == "Risques").State ==
                    ComplexMigrationMoveState.Blocked &&
                preview.Moves.Single(item => item.SectionName == "Technique").To ==
                    "Documentation/Technique" &&
                preview.MissingDirectories.Contains("Documentation/Technique/Architecture"),
                "Preview must classify built-in roles and pre-existing target collisions.");

            ComplexProjectMigrationPreview second = await service.PreviewAsync(projectRoot);
            Assert(preview.Moves.SequenceEqual(second.Moves) &&
                await File.ReadAllTextAsync(file) == canonical &&
                File.Exists(Path.Combine(projectRoot, "Jalons.md")) &&
                File.Exists(Path.Combine(collision, "custom.md")) &&
                !Directory.Exists(Path.Combine(projectRoot, "Management")) &&
                preview.Warnings.Any(item => item.Contains("Divers perso", StringComparison.Ordinal)),
                "Preview must be deterministic and must not modify source files or directories.");

            Directory.Delete(Path.Combine(projectRoot, "Technique"), recursive: true);
            ComplexProjectMigrationPreview missing = await service.PreviewAsync(projectRoot);
            Assert(missing.Moves.Single(item => item.SectionName == "Technique").State ==
                ComplexMigrationMoveState.MissingSource,
                "A missing legacy section must not be automatically recreated.");

            bool rejected = false;
            try
            {
                await service.PreviewAsync(Path.Combine(root, "unrelated"));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected, "Out-of-workspace projects must be rejected.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Creates a v1.9 test section manifest.</summary>
    /// <param name="name">Section name.</param>
    /// <param name="key">Template role, when present.</param>
    /// <param name="singleton">Whether the section expects one file.</param>
    /// <returns>Canonical section metadata.</returns>
    private static SectionManifest NewSection(string name, string? key, bool singleton = true)
    {
        return new SectionManifest
        {
            Id = Guid.NewGuid(),
            Name = name,
            TemplateKey = key,
            IsSingleton = singleton
        };
    }

    /// <summary>Throws when a read-only plan invariant regresses.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
