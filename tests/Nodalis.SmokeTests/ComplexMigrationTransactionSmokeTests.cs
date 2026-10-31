using System.Text.Json;
using System.Text.Json.Serialization;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Exercises fingerprinted Complex v2 migration, durable backup and simulated
/// process stops without modifying any user's real workspace.
/// </summary>
internal static class ComplexMigrationTransactionSmokeTests
{
    /// <summary>
    /// Qualifies read-only planning, conflicts, approval, rollback after fault,
    /// successful publication and recovery after interrupted publication.
    /// </summary>
    /// <param name="root">Isolated smoke root.</param>
    /// <returns>Transactional migration regression run.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Complex-v2-transaction-smoke");
        string projectRoot = Path.Combine(workspace,
            "Applications", "Test", "Projets", "Complexe historique");
        Directory.CreateDirectory(projectRoot);
        try
        {
            ProjectManifest project = new()
            {
                Id = Guid.NewGuid(),
                ApplicationId = Guid.NewGuid(),
                Name = "Complexe historique",
                InitialComplexity = ProjectComplexity.Complex,
                Sections =
                [
                    Section("Jalons", "milestones", true),
                    Section("Documentation", "documentation", true),
                    Section("Fonctionnel", "functional", true),
                    Section("Technique", "technical-complex", true),
                    Section("Risques", "risks", true),
                    Section("Dépendances", "dependencies", true),
                    Section("Exploitation", "operations", true),
                    Section("Déploiement", "deployment", true),
                    Section("Réunions", null, false),
                    Section("Décisions", null, false),
                    Section("Tests", "tests-complex", true),
                    Section("Glossaire", "glossary", true)
                ]
            };

            string manifestPath = Path.Combine(projectRoot,
                WorkspaceLayout.ProjectManifestFileName);
            JsonSerializerOptions json = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            await File.WriteAllTextAsync(manifestPath,
                JsonSerializer.Serialize(project, json));
            await File.WriteAllTextAsync(Path.Combine(projectRoot,
                "Jalons.md"), "# Jalons\n\n| Jalon | Date cible |\n| --- | --- |\n");
            await File.WriteAllTextAsync(Path.Combine(projectRoot,
                "Glossaire.md"), "# Glossaire\n");
            await File.WriteAllTextAsync(Path.Combine(projectRoot,
                "Présentation.md"), "# Projet\n\nTexte original.\n");
            foreach (string name in new[]
                     { "Documentation", "Fonctionnel", "Technique", "Risques",
                       "Dépendances", "Exploitation", "Déploiement", "Réunions",
                       "Décisions", "Tests" })
            {
                string directory = Path.Combine(projectRoot, name);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory,
                    name + ".md"), "# " + name + "\n\nContenu conservé.\n");
            }

            string originalHash =
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot);
            ComplexProjectMigrationPlanService planner = new(workspace);
            ComplexProjectMigrationPlan plan = await planner.PrepareAsync(
                projectRoot);
            Assert(plan.CanExecute &&
                plan.ProjectId == project.Id &&
                plan.Moves.Count(move =>
                    move.State == ComplexMigrationMoveState.Candidate) == 8 &&
                plan.SourceFingerprint == originalHash &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot) == originalHash,
                "Preflight must be read-only, deterministic and executable for a link-free legacy project.");

            ComplexProjectMigrationTransactionService executor = new(workspace);
            await RejectAsync(() => executor.ApplyAsync(plan,
                    plan.SourceFingerprint, approved: false),
                "An unapproved migration must refuse all writes.");
            Assert(await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot) == originalHash,
                "Refused migration must leave the original project unchanged.");

            await RejectAsync(() => executor.ApplyAsync(plan,
                    new string('0', 64), approved: true),
                "An incorrect approval fingerprint must be rejected.");

            string presentationPath = Path.Combine(projectRoot,
                "Présentation.md");
            await File.AppendAllTextAsync(presentationPath, "\nChangement humain.\n");
            await RejectAsync(() => executor.ApplyAsync(plan,
                    plan.SourceFingerprint, approved: true),
                "An edit after preflight must invalidate the plan.");
            Assert((await File.ReadAllTextAsync(presentationPath))
                    .Contains("Changement humain.", StringComparison.Ordinal),
                "Optimistic concurrency failure must preserve newer edits.");
            await File.WriteAllTextAsync(presentationPath, "# Projet\n\nTexte original.\n");
            Assert(await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot) == originalHash,
                "The fixture must restore its exact content before retry.");

            await File.AppendAllTextAsync(presentationPath,
                "\n[[Technique]]\n");
            ComplexProjectMigrationPlan blocked = await planner.PrepareAsync(
                projectRoot);
            Assert(!blocked.CanExecute &&
                blocked.Blockers.Any(item =>
                    item.Contains("Références Markdown", StringComparison.Ordinal)),
                "Unrewritten wiki, image and Markdown references must block execution.");
            await File.WriteAllTextAsync(presentationPath, "# Projet\n\nTexte original.\n");

            ComplexProjectMigrationTransactionService interrupted = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "original-moved")
                    {
                        throw new InvalidOperationException("Simulated fail before publish.");
                    }
                });
            await RejectAsync(() => interrupted.ApplyAsync(
                    plan, plan.SourceFingerprint, approved: true),
                "A failure between the two renames must trigger immediate restoration.");
            Assert(Directory.Exists(projectRoot) &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot) == originalHash &&
                (await executor.ListPendingProjectsAsync()).Count == 0,
                "Original bytes and paths must be restored after a failed publish.");

            plan = await planner.PrepareAsync(projectRoot);
            ComplexProjectMigrationResult applied = await executor.ApplyAsync(
                plan, plan.SourceFingerprint, approved: true);
            ProjectManifest after = JsonSerializer.Deserialize<ProjectManifest>(
                await File.ReadAllTextAsync(manifestPath), json)!;
            SectionManifest[] oldSections = project.Sections.ToArray();

            Assert(after.Id == project.Id &&
                oldSections.All(previous => after.Sections.Any(current =>
                    current.Id == previous.Id && current.Name == previous.Name)) &&
                after.Sections.Single(item => item.TemplateKey == "milestones")
                    .RelativeDirectory == "Management" &&
                after.Sections.Single(item => item.Name == "Technique")
                    .RelativeDirectory == "Documentation/Technique" &&
                after.Sections.Any(item => item.Name == "Architecture" &&
                    item.RelativeDirectory == "Documentation/Technique/Architecture") &&
                File.Exists(Path.Combine(projectRoot, "Management", "Jalons.md")) &&
                !File.Exists(Path.Combine(projectRoot, "Jalons.md")) &&
                File.Exists(Path.Combine(projectRoot,
                    "Documentation", "Technique", "Technique.md")) &&
                !Directory.Exists(Path.Combine(projectRoot, "Technique")) &&
                Directory.Exists(applied.BackupDirectory) &&
                File.Exists(Path.Combine(applied.BackupDirectory, "Jalons.md")) &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    applied.BackupDirectory) == originalHash &&
                (await executor.ListPendingProjectsAsync()).Count == 0,
                "Approved publish must preserve GUIDs, all content and an exact original backup.");

            // A second independent legacy project simulates a crash immediately
            // after publication but before the durable published checkpoint.
            string second = Path.Combine(workspace,
                "Applications", "Test", "Projets", "Crash replica");
            Directory.CreateDirectory(second);
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         applied.BackupDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(applied.BackupDirectory, entry);
                string copy = Path.Combine(second, relative);
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(copy);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(entry, copy);
                }
            }

            ComplexProjectMigrationPlan retry = await planner.PrepareAsync(second);
            Assert(retry.CanExecute,
                "A second unmodified legacy fixture must be independently migratable.");

            ComplexProjectMigrationTransactionService interruptedPublish = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "published")
                    {
                        throw new InvalidOperationException(
                            "Simulated hard stop after directory publication.");
                    }
                });

            await RejectAsync(() => interruptedPublish.ApplyAsync(
                    retry, retry.SourceFingerprint, approved: true),
                "A failure after publication must preserve recovery metadata.");

            Assert((await executor.ListPendingProjectsAsync()).Single() == second,
                "Post-publish interruption must remain pending, not silently complete.");

            await RejectAsync(() => executor.RecoverPendingAsync(approved: false),
                "Recovery without explicit user approval must fail.");
            Assert(await executor.RecoverPendingAsync(approved: true) == 1 &&
                (await executor.ListPendingProjectsAsync()).Count == 0 &&
                File.Exists(Path.Combine(second, "Management", "Jalons.md")),
                "Fingerprint-checked recovery must finalize a valid published project.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Creates a legacy manifest section with a stable GUID.</summary>
    /// <param name="name">Legacy section name.</param>
    /// <param name="key">Built-in template key, if any.</param>
    /// <param name="singleton">Single document role.</param>
    /// <returns>Legacy section definition.</returns>
    private static SectionManifest Section(
        string name, string? key, bool singleton) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        TemplateKey = key,
        IsSingleton = singleton
    };

    /// <summary>Asserts that an explicitly rejected operation fails.</summary>
    /// <param name="action">Operation expected to reject its input.</param>
    /// <param name="message">Failure explanation.</param>
    /// <returns>Async assertion task.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception error) when (error is InvalidDataException or
            InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Throws on any broken migration invariant.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Failure details.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
