using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Infrastructure.Links;
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
        await VerifyDocumentIdentityAfterMoveAsync(root);

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

            // Unrelated external links and stable relative paths stay eligible.
            // These links must not make the entire workspace un-migratable.
            string originalPresentation = "# Projet\n\nTexte original.\n";
            await File.WriteAllTextAsync(presentationPath,
                originalPresentation + "[Site](https://example.org/page)\n" +
                "![Aide](Glossaire.md)\n");
            ComplexProjectMigrationPlan safeLinks = await planner.PrepareAsync(
                projectRoot);
            Assert(safeLinks.CanExecute,
                "External URLs and relative references to unmoved files must stay safe.");
            await File.WriteAllTextAsync(presentationPath, originalPresentation);

            string movedDoc = Path.Combine(projectRoot, "Technique", "Technique.md");
            string originalMovedDoc = await File.ReadAllTextAsync(movedDoc);
            await File.WriteAllTextAsync(Path.Combine(projectRoot,
                "Technique", "capture.png"), "fixture");
            await File.WriteAllTextAsync(movedDoc,
                originalMovedDoc + "\n![Image](capture.png)\n");
            ComplexProjectMigrationPlan together = await planner.PrepareAsync(
                projectRoot);
            Assert(together.CanExecute,
                "Relative links between files moved together must remain valid.");
            await File.WriteAllTextAsync(movedDoc, originalMovedDoc);
            File.Delete(Path.Combine(projectRoot, "Technique", "capture.png"));

            await File.WriteAllTextAsync(presentationPath,
                originalPresentation + "[Technique](Technique/Technique.md)\n");
            ComplexProjectMigrationPlan affected = await planner.PrepareAsync(
                projectRoot);
            Assert(affected.CanExecute &&
                affected.LinkRewrites.Count == 1 &&
                affected.LinkRewrites[0].ReplacementTarget ==
                    "Documentation/Technique/Technique.md",
                "A resolvable local target move must propose exactly one safe rewrite.");
            await File.WriteAllTextAsync(presentationPath, originalPresentation);

            await File.WriteAllTextAsync(movedDoc,
                originalMovedDoc + "\n![Glossaire](../Glossaire.md)\n");
            ComplexProjectMigrationPlan movingSource = await planner.PrepareAsync(
                projectRoot);
            Assert(movingSource.CanExecute &&
                movingSource.LinkRewrites.Count == 1 &&
                movingSource.LinkRewrites[0].ReplacementTarget == "../../Glossaire.md",
                "A moved source linking to an unmoved asset must be adjusted.");
            await File.WriteAllTextAsync(movedDoc, originalMovedDoc);

            string backlink = Path.Combine(workspace, "Backlink.md");
            await File.WriteAllTextAsync(backlink,
                "[Technique](<Applications/Test/Projets/Complexe historique/Technique/Technique.md>)");
            ComplexProjectMigrationPlan externalBacklink = await planner.PrepareAsync(
                projectRoot);
            Assert(externalBacklink.CanExecute &&
                externalBacklink.ExternalLinkRewrites.Count == 1 &&
                externalBacklink.ExternalLinkRewrites[0].ReplacementTarget ==
                    "<Applications/Test/Projets/Complexe historique/Documentation/Technique/Technique.md>",
                "Cross-project backlink must be planned as an explicit, exact replacement.");
            File.Delete(backlink);

            // Explicit Markdown reference definitions (including image links)
            // are rewritten at the definition, never at their usage sites.
            // Both project-local and external backlinks remain byte-reversible.
            string referenceImage = Path.Combine(projectRoot,
                "Technique", "capture.png");
            await File.WriteAllTextAsync(referenceImage, "reference image");
            string referenceContent = originalPresentation +
                "[Documentation][tech]\n![Capture][screen]\n\n" +
                "[tech]: <Technique/Technique.md> \"Documentation\"\n" +
                "[screen]: <Technique/capture.png>\n";
            string referenceExternal = Path.Combine(workspace,
                "Reference externe.md");
            string referenceExternalContent = "\uFEFF[Ouvrir][tech]\r\n\r\n" +
                "[tech]: <Applications/Test/Projets/Complexe historique/" +
                "Technique/Technique.md> \"Source\"\r\n";
            await File.WriteAllTextAsync(presentationPath, referenceContent);
            await File.WriteAllTextAsync(referenceExternal, referenceExternalContent);
            byte[] originalReferenceBytes = await File.ReadAllBytesAsync(
                referenceExternal);
            ComplexProjectMigrationPlan referencePlan = await planner.PrepareAsync(
                projectRoot);
            Assert(referencePlan.CanExecute &&
                referencePlan.LinkRewrites.Count == 2 &&
                referencePlan.ExternalLinkRewrites.Count == 1 &&
                referencePlan.LinkRewrites.All(item =>
                    item.OriginalTarget.StartsWith('<') &&
                    item.ReplacementTarget.Contains("Documentation/Technique/",
                        StringComparison.Ordinal)),
                "Simple reference definitions must be qualified, including images " +
                "and a backlink in another document.");

            ComplexProjectMigrationTransactionService referenceExecutor = new(
                workspace);
            await referenceExecutor.ApplyAsync(referencePlan,
                referencePlan.SourceFingerprint, approved: true);
            byte[] publishedReferenceBytes = await File.ReadAllBytesAsync(
                referenceExternal);
            Assert((await File.ReadAllTextAsync(presentationPath)).Contains(
                    "[tech]: <Documentation/Technique/Technique.md> \"Documentation\"",
                    StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(presentationPath)).Contains(
                    "[screen]: <Documentation/Technique/capture.png>",
                    StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(referenceExternal)).Contains(
                    "/Documentation/Technique/Technique.md> \"Source\"",
                    StringComparison.Ordinal) &&
                File.Exists(Path.Combine(projectRoot,
                    "Documentation", "Technique", "capture.png")) &&
                publishedReferenceBytes.Length > 3 &&
                publishedReferenceBytes[0] == 0xEF &&
                publishedReferenceBytes[1] == 0xBB &&
                publishedReferenceBytes[2] == 0xBF &&
                (await File.ReadAllTextAsync(referenceExternal)).Contains(
                    "\r\n[tech]:", StringComparison.Ordinal),
                "Approved reference rewrites must preserve the definition label, " +
                "optional title, image and external backlink.");

            ComplexProjectMigrationJournal referenceJournal =
                (await referenceExecutor.GetRollbackCandidatesAsync(
                    projectRoot)).Single();
            await referenceExecutor.RollbackAsync(referenceJournal.TransactionId,
                approved: true);
            Assert(await File.ReadAllTextAsync(presentationPath) == referenceContent &&
                (await File.ReadAllBytesAsync(referenceExternal))
                    .SequenceEqual(originalReferenceBytes) &&
                await File.ReadAllTextAsync(referenceImage) == "reference image",
                "Reference-style migration must restore the precise originals.");
            File.Delete(referenceExternal);
            File.Delete(referenceImage);

            // Ambiguous/duplicate definitions and unknown labels are still
            // blockers; a guess must never become an automatic rewrite.
            await File.WriteAllTextAsync(presentationPath,
                "[Guide][missing]\n");
            Assert(!(await planner.PrepareAsync(projectRoot)).CanExecute,
                "Undefined Markdown references must fail closed.");
            await File.WriteAllTextAsync(presentationPath,
                "[Guide][tech]\n[tech]: <Technique/Technique.md>\n" +
                "[TECH]: <Technique/Technique.md>\n");
            Assert(!(await planner.PrepareAsync(projectRoot)).CanExecute,
                "Case-insensitive duplicate definitions must fail closed.");
            await File.WriteAllTextAsync(presentationPath, originalPresentation);

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

            // Transactionally rewrite several links in two project documents.
            // A BOM and CRLF fixture proves that rewriting changes only the
            // intended target spans and preserves original byte conventions.
            string linkedProject = Path.Combine(workspace,
                "Applications", "Test", "Projets", "Linked Complex");
            Directory.CreateDirectory(linkedProject);
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         applied.BackupDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(applied.BackupDirectory, entry);
                string copy = Path.Combine(linkedProject, relative);
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

            string linkedPresentation = Path.Combine(linkedProject, "Présentation.md");
            string linkedTechnique = Path.Combine(linkedProject,
                "Technique", "Technique.md");
            string presentationText = "# Projet\r\n" +
                "[Technique](<Technique/Technique.md#intro>) et " +
                "![Jalons](Jalons.md)\r\n" +
                "<a href=\"Jalons.md\">jalons</a>\r\n";
            await File.WriteAllTextAsync(linkedPresentation, presentationText,
                new UTF8Encoding(true));
            await File.AppendAllTextAsync(linkedTechnique,
                "\n[Glossaire](../Glossaire.md) et [Jalons](../Jalons.md)\n");
            // Backlinks from both workspace root and another project require
            // individually recoverable snapshots, not silent in-place changes.
            string externalRoot = Path.Combine(workspace, "External Root.md");
            string externalOriginal = "[Technique](<Applications/Test/Projets/" +
                "Linked Complex/Technique/Technique.md#intro>)\r\n";
            await File.WriteAllTextAsync(externalRoot, externalOriginal,
                new UTF8Encoding(true));
            byte[] externalRootBytes = await File.ReadAllBytesAsync(externalRoot);
            string siblingRoot = Path.Combine(workspace,
                "Applications", "Test", "Projets", "Other Project");
            Directory.CreateDirectory(siblingRoot);
            string externalSibling = Path.Combine(siblingRoot, "Source.md");
            string siblingOriginal =
                "![Jalons](<../Linked Complex/Jalons.md>)\n";
            await File.WriteAllTextAsync(externalSibling, siblingOriginal);
            string linkOriginalHash =
                await ComplexProjectMigrationPlanService.FingerprintAsync(linkedProject);
            ComplexProjectMigrationPlan linkedPlan = await planner.PrepareAsync(
                linkedProject);
            Assert(linkedPlan.CanExecute &&
                linkedPlan.LinkRewrites.Count == 5 &&
                linkedPlan.ExternalLinkRewrites.Count == 2 &&
                linkedPlan.ExternalLinkRewrites.All(item =>
                    item.SourceHash.Length == 64),
                "Preflight must preview internal and external byte-protected edits.");

            // The approved operation cannot silently absorb a new external
            // backlink that appeared after the preview.
            string lateBacklink = Path.Combine(workspace, "late-backlink.md");
            await File.WriteAllTextAsync(lateBacklink,
                "[Source](<Applications/Test/Projets/Linked Complex/Technique/Technique.md>)");
            await RejectAsync(() => executor.ApplyAsync(linkedPlan,
                    linkedPlan.SourceFingerprint, approved: true),
                "An external backlink created after planning must invalidate approval.");
            File.Delete(lateBacklink);
            Assert(await ComplexProjectMigrationPlanService.FingerprintAsync(
                    linkedProject) == linkOriginalHash,
                "Rejected approval must not modify the source project.");

            linkedPlan = await planner.PrepareAsync(linkedProject);
            ComplexProjectMigrationResult linkedResult = await executor.ApplyAsync(
                linkedPlan, linkedPlan.SourceFingerprint, approved: true);
            byte[] externalPublished = await File.ReadAllBytesAsync(externalRoot);
            string siblingPublished = await File.ReadAllTextAsync(externalSibling);
            Assert(externalPublished.Length > 3 &&
                externalPublished[0] == 0xEF &&
                externalPublished[1] == 0xBB &&
                externalPublished[2] == 0xBF &&
                (await File.ReadAllTextAsync(externalRoot)).Contains(
                    "Linked Complex/Documentation/Technique/Technique.md#intro",
                    StringComparison.Ordinal) &&
                siblingPublished.Contains(
                    "../Linked Complex/Management/Jalons.md",
                    StringComparison.Ordinal) &&
                (await executor.ListPendingProjectsAsync()).Count == 0,
                "Approved migration must publish every backlink and retain BOM.");

            byte[] publishedBytes = await File.ReadAllBytesAsync(linkedPresentation);
            string publishedPresentation = await File.ReadAllTextAsync(
                linkedPresentation);
            string publishedTechnique = await File.ReadAllTextAsync(
                Path.Combine(linkedProject, "Documentation", "Technique", "Technique.md"));
            Assert(publishedBytes.Length > 3 &&
                publishedBytes[0] == 0xEF && publishedBytes[1] == 0xBB &&
                publishedBytes[2] == 0xBF &&
                publishedPresentation.Contains(
                    "[Technique](<Documentation/Technique/Technique.md#intro>)",
                    StringComparison.Ordinal) &&
                publishedPresentation.Contains(
                    "![Jalons](Management/Jalons.md)",
                    StringComparison.Ordinal) &&
                publishedPresentation.Contains(
                    "href=\"Management/Jalons.md\"", StringComparison.Ordinal) &&
                publishedPresentation.Contains("\r\n", StringComparison.Ordinal) &&
                publishedTechnique.Contains(
                    "[Glossaire](../../Glossaire.md)", StringComparison.Ordinal) &&
                publishedTechnique.Contains(
                    "[Jalons](../../Management/Jalons.md)", StringComparison.Ordinal) &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    linkedResult.BackupDirectory) == linkOriginalHash,
                "Published link repairs must preserve anchors, HTML, BOM, CRLF and backup.");

            IReadOnlyList<ComplexProjectMigrationJournal> linkedRollback =
                await executor.GetRollbackCandidatesAsync(linkedProject);
            Assert(linkedRollback.Count == 1 &&
                linkedRollback[0].ExternalLinkFiles.Count == 2,
                "A rewritten migration must retain exact external archives.");

            await File.AppendAllTextAsync(externalSibling, "Concurrent edit\n");
            await RejectAsync(() => executor.RollbackAsync(
                    linkedRollback[0].TransactionId, approved: true),
                "Edited external backlinks must block rollback before modifying the project.");
            Assert(File.Exists(Path.Combine(linkedProject,
                    "Documentation", "Technique", "Technique.md")),
                "A blocked rollback must leave the migrated project intact.");
            await File.WriteAllTextAsync(externalSibling, siblingPublished);

            await executor.RollbackAsync(
                linkedRollback[0].TransactionId, approved: true);
            Assert(await ComplexProjectMigrationPlanService.FingerprintAsync(
                    linkedProject) == linkOriginalHash &&
                File.Exists(Path.Combine(linkedProject, "Technique", "Technique.md")) &&
                (await File.ReadAllBytesAsync(externalRoot))
                    .SequenceEqual(externalRootBytes) &&
                await File.ReadAllTextAsync(externalSibling) == siblingOriginal,
                "Rollback must restore project and external Markdown byte for byte.");

            // Crash after publishing the first external file must never
            // make the journal appear complete; explicit recovery continues
            // the remaining replacements and makes rollback possible.
            string recoverRoot = Path.Combine(workspace, "Applications", "Test",
                "Projets", "Recover Backlinks");
            Directory.CreateDirectory(recoverRoot);
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         linkedProject, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(linkedProject, entry);
                string copy = Path.Combine(recoverRoot, relative);
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

            string recoveryA = Path.Combine(workspace, "Recovery A.md");
            string recoveryB = Path.Combine(workspace, "Recovery B.md");
            string initialA = "[T](<Applications/Test/Projets/Recover Backlinks/Technique/Technique.md>)\n";
            string initialB = "[J](<Applications/Test/Projets/Recover Backlinks/Jalons.md>)\n";
            await File.WriteAllTextAsync(recoveryA, initialA);
            await File.WriteAllTextAsync(recoveryB, initialB);
            ComplexProjectMigrationPlan recoveryPlan = await planner.PrepareAsync(
                recoverRoot);
            Assert(recoveryPlan.CanExecute &&
                recoveryPlan.ExternalLinkRewrites.Count == 2,
                "Recovery fixture must require exactly two external publications.");

            // An editor can change an external backlink after the staging
            // snapshots are written. This must fail before project publication,
            // leave the human text untouched, and not create a pending journal.
            ComplexProjectMigrationTransactionService lateEditor = new(
                workspace, checkpoint: name =>
                {
                    if (name == "prepared")
                    {
                        File.AppendAllText(recoveryB, "Human change at staging\\n");
                    }
                });
            string recoveryOriginalHash =
                await ComplexProjectMigrationPlanService.FingerprintAsync(recoverRoot);
            await RejectAsync(() => lateEditor.ApplyAsync(recoveryPlan,
                    recoveryPlan.SourceFingerprint, approved: true),
                "A late external edit must stop the migration before any rename.");
            Assert((await File.ReadAllTextAsync(recoveryB)).Contains(
                    "Human change at staging", StringComparison.Ordinal) &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    recoverRoot) == recoveryOriginalHash &&
                (await executor.ListPendingProjectsAsync()).Count == 0,
                "Refused prepublication must preserve human text and original project.");
            await File.WriteAllTextAsync(recoveryB, initialB);

            ComplexProjectMigrationTransactionService fault = new(
                workspace, checkpoint: name =>
                {
                    if (name == "external-published")
                    {
                        throw new InvalidOperationException(
                            "Simulated hard stop after publishing one backlink.");
                    }
                });
            await RejectAsync(() => fault.ApplyAsync(recoveryPlan,
                    recoveryPlan.SourceFingerprint, approved: true),
                "Interrupted cross-project publication must retain recovery state.");
            Assert((await executor.ListPendingProjectsAsync()).Single() ==
                    recoverRoot &&
                (await File.ReadAllTextAsync(recoveryA)).Contains(
                    "Documentation/Technique/Technique.md", StringComparison.Ordinal) &&
                await File.ReadAllTextAsync(recoveryB) == initialB,
                "Crash must leave one applied and one pending backlink, journaled.");
            await RejectAsync(() => executor.RecoverPendingAsync(approved: false),
                "Completing partial backlink publication requires a human decision.");
            await File.AppendAllTextAsync(recoveryB, "Human edit\n");
            await RejectAsync(() => executor.RecoverPendingAsync(approved: true),
                "Recovery must refuse to overwrite an externally edited backlink.");
            Assert((await File.ReadAllTextAsync(recoveryB)).Contains(
                    "Human edit", StringComparison.Ordinal) &&
                (await executor.ListPendingProjectsAsync()).Single() == recoverRoot,
                "Blocked recovery must retain human edits and the pending journal.");
            await File.WriteAllTextAsync(recoveryB, initialB);
            Assert(await executor.RecoverPendingAsync(approved: true) == 1 &&
                (await File.ReadAllTextAsync(recoveryB)).Contains(
                    "Management/Jalons.md", StringComparison.Ordinal) &&
                (await executor.ListPendingProjectsAsync()).Count == 0,
                "Recovery must idempotently complete the second backlink.");

            ComplexProjectMigrationJournal recoveryJournal =
                (await executor.GetRollbackCandidatesAsync(recoverRoot)).Single();
            ComplexProjectMigrationTransactionService interruptedRollback = new(
                workspace, checkpoint: name =>
                {
                    if (name == "external-rolled-back")
                    {
                        throw new InvalidOperationException(
                            "Simulated stop after restoring one external backlink.");
                    }
                });
            await RejectAsync(() => interruptedRollback.RollbackAsync(
                    recoveryJournal.TransactionId, approved: true),
                "An interrupted rollback must surface an error and restore all backlinks.");
            Assert((await executor.ListPendingProjectsAsync()).Count == 0,
                "Handled rollback interruption must restore a coherent terminal state.");
            Assert(await File.ReadAllTextAsync(recoveryA) == initialA &&
                await File.ReadAllTextAsync(recoveryB) == initialB &&
                File.Exists(Path.Combine(recoverRoot, "Technique", "Technique.md")),
                "Recovery followed by rollback must restore every cross-project byte.");

            // A later backlink must be checked BEFORE rewriting the first one.
            // Inject a human edit after the project directory has been published,
            // then prove an external multi-file replay never starts partially.
            ComplexProjectMigrationPlan preflight = await planner.PrepareAsync(
                recoverRoot);
            Assert(preflight.CanExecute &&
                preflight.ExternalLinkRewrites.Count == 2,
                "Restored recovery fixture should remain migratable.");
            ComplexProjectMigrationTransactionService latePublishedEdit = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "published")
                    {
                        File.AppendAllText(recoveryB, "\nHuman change after project publish\n");
                    }
                });
            await RejectAsync(() => latePublishedEdit.ApplyAsync(preflight,
                    preflight.SourceFingerprint, approved: true),
                "A later external edit after project publication must fail before " +
                "the first external rewrite.");
            Assert(await File.ReadAllTextAsync(recoveryA) == initialA &&
                (await File.ReadAllTextAsync(recoveryB)).Contains(
                    "Human change after project publish", StringComparison.Ordinal) &&
                (await executor.ListPendingProjectsAsync()).Single() == recoverRoot,
                "An invalid later backlink must block the whole external replay.");
            await File.WriteAllTextAsync(recoveryB, initialB);
            Assert(await executor.RecoverPendingAsync(approved: true) == 1 &&
                (await File.ReadAllTextAsync(recoveryA)).Contains(
                    "Documentation/Technique/Technique.md",
                    StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(recoveryB)).Contains(
                    "Management/Jalons.md", StringComparison.Ordinal),
                "Approved recovery must apply both backlink replacements.");
            ComplexProjectMigrationJournal retried =
                (await executor.GetRollbackCandidatesAsync(recoverRoot)).Single();
            await executor.RollbackAsync(retried.TransactionId, approved: true);
            Assert(await File.ReadAllTextAsync(recoveryA) == initialA &&
                await File.ReadAllTextAsync(recoveryB) == initialB,
                "A second rollback must restore all preflight-protected backlinks.");

            // Under Windows the final checksum and File.Replace must share
            // a held read handle that rejects concurrent in-place writers.
            ComplexProjectMigrationPlan lockPlan = await planner.PrepareAsync(
                recoverRoot);
            bool attemptedWrite = false;
            bool concurrentWriteRejected = false;
            ComplexProjectMigrationTransactionService lockProtected = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "external-locked" && !attemptedWrite)
                    {
                        attemptedWrite = true;
                        try
                        {
                            File.AppendAllText(recoveryA, "Unapproved concurrent edit");
                        }
                        catch (IOException)
                        {
                            concurrentWriteRejected = true;
                        }
                    }
                });
            await lockProtected.ApplyAsync(lockPlan,
                lockPlan.SourceFingerprint, approved: true);
            Assert(attemptedWrite && concurrentWriteRejected &&
                !(await File.ReadAllTextAsync(recoveryA)).Contains(
                    "Unapproved concurrent edit", StringComparison.Ordinal) &&
                (await File.ReadAllTextAsync(recoveryB)).Contains(
                    "Management/Jalons.md", StringComparison.Ordinal),
                "A concurrent in-place writer must be denied during the last " +
                "hash comparison and File.Replace.");
            ComplexProjectMigrationJournal lockJournal =
                (await executor.GetRollbackCandidatesAsync(recoverRoot)).Single();
            await executor.RollbackAsync(lockJournal.TransactionId, approved: true);
            Assert(await File.ReadAllTextAsync(recoveryA) == initialA &&
                await File.ReadAllTextAsync(recoveryB) == initialB,
                "Conditional publication must retain byte-exact rollback.");

            // A Windows editor can atomically replace the *path* without
            // writing through our protected handle. Even identical bytes must
            // not conceal that the file itself has changed.
            ComplexProjectMigrationPlan swappedPlan =
                await planner.PrepareAsync(recoverRoot);
            bool replacedPath = false;
            string relocated = recoveryA + ".editor-backup";
            ComplexProjectMigrationTransactionService pathSwapper = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "external-locked" && !replacedPath)
                    {
                        replacedPath = true;
                        File.Move(recoveryA, relocated);
                        File.Copy(relocated, recoveryA);
                    }
                });
            await RejectAsync(() => pathSwapper.ApplyAsync(swappedPlan,
                    swappedPlan.SourceFingerprint, approved: true),
                "A same-content atomic path swap must be detected by file ID.");
            Assert(replacedPath &&
                File.Exists(relocated) &&
                await File.ReadAllTextAsync(recoveryA) == initialA &&
                await File.ReadAllTextAsync(recoveryB) == initialB &&
                (await executor.ListPendingProjectsAsync()).Single() == recoverRoot,
                "A path swap must not overwrite a newly installed file, " +
                "even if it contains the same bytes.");
            File.Delete(relocated);
            Assert(await executor.RecoverPendingAsync(approved: true) == 1 &&
                (await File.ReadAllTextAsync(recoveryA)).Contains(
                    "Documentation/Technique/Technique.md",
                    StringComparison.Ordinal),
                "The journal must remain explicitly recoverable after a path swap.");
            ComplexProjectMigrationJournal swappedJournal =
                (await executor.GetRollbackCandidatesAsync(recoverRoot)).Single();
            await executor.RollbackAsync(swappedJournal.TransactionId,
                approved: true);
            Assert(await File.ReadAllTextAsync(recoveryA) == initialA &&
                await File.ReadAllTextAsync(recoveryB) == initialB,
                "Recovery after path swap must retain exact backlinks.");

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

            // An explicit post-success rollback requires that both sides
            // still match their journal, so a human edit is never overwritten.
            IReadOnlyList<ComplexProjectMigrationJournal> rollbackChoices =
                await executor.GetRollbackCandidatesAsync(projectRoot);
            Assert(rollbackChoices.Count == 1 &&
                rollbackChoices[0].OriginalFingerprint == originalHash,
                "A completed migration must expose its exact retained original.");

            await RejectAsync(() => executor.RollbackAsync(
                    rollbackChoices[0].TransactionId, approved: false),
                "Rolling back without approval must never move any project.");

            string activeMilestones = Path.Combine(projectRoot,
                "Management", "Jalons.md");
            byte[] beforeEdit = await File.ReadAllBytesAsync(activeMilestones);
            await File.AppendAllTextAsync(activeMilestones, "\nExternal edit\n");
            await RejectAsync(() => executor.RollbackAsync(
                    rollbackChoices[0].TransactionId, approved: true),
                "A modified published project must be protected from rollback.");
            Assert((await File.ReadAllTextAsync(activeMilestones))
                .Contains("External edit", StringComparison.Ordinal),
                "Denied rollback must retain all human edits.");
            await File.WriteAllBytesAsync(activeMilestones, beforeEdit);

            string restored = await executor.RollbackAsync(
                rollbackChoices[0].TransactionId, approved: true);
            Assert(restored == projectRoot &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(
                    projectRoot) == originalHash &&
                File.Exists(Path.Combine(projectRoot, "Jalons.md")) &&
                !File.Exists(activeMilestones) &&
                (await executor.GetRollbackCandidatesAsync(projectRoot)).Count == 0,
                "Approved rollback must recover exact original file bytes, paths and manifest.");

            // A failure between the two rollback renames must also restore
            // the original immediately without deleting the published copy.
            IReadOnlyList<ComplexProjectMigrationJournal> secondChoices =
                await executor.GetRollbackCandidatesAsync(second);
            Assert(secondChoices.Count == 1,
                "Recovered published migrations must retain rollback candidates.");

            ComplexProjectMigrationTransactionService failedRollback = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "rollback-source-moved")
                    {
                        throw new InvalidOperationException(
                            "Simulated hard stop before restoring original.");
                    }
                });
            await RejectAsync(() => failedRollback.RollbackAsync(
                    secondChoices[0].TransactionId, approved: true),
                "A rollback interruption must surface an explicit error.");
            Assert(Directory.Exists(second) &&
                File.Exists(Path.Combine(second, "Jalons.md")) &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(second) ==
                    secondChoices[0].OriginalFingerprint &&
                (await executor.ListPendingProjectsAsync()).Count == 0,
                "Rollback fault must restore the exact original and leave no pending operation.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>
    /// Qualifies GUID continuity with real Nodalis navigation, link rewrites,
    /// an interrupted publication, later index refresh, and explicit rollback.
    /// </summary>
    /// <param name="root">Isolated smoke test parent directory.</param>
    private static async Task VerifyDocumentIdentityAfterMoveAsync(string root)
    {
        string workspace = Path.Combine(root, "Complex-v2-identity-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Identités de migration");
        try
        {
            JsonSerializerOptions serializer = new(JsonSerializerDefaults.Web);
            serializer.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            Guid applicationId = Guid.NewGuid();
            string application = Path.Combine(workspace, "Applications", "Produit");
            Directory.CreateDirectory(application);
            await File.WriteAllTextAsync(Path.Combine(application,
                WorkspaceLayout.ApplicationManifestFileName),
                JsonSerializer.Serialize(
                    new ApplicationManifest { Id = applicationId, Name = "Produit" },
                    serializer));

            string projectRoot = Path.Combine(application, "Projets",
                "Complexe identités");
            Directory.CreateDirectory(projectRoot);
            ProjectManifest manifest = new()
            {
                Id = Guid.NewGuid(),
                Name = "Complexe identités",
                ApplicationId = applicationId,
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
            await File.WriteAllTextAsync(Path.Combine(projectRoot,
                WorkspaceLayout.ProjectManifestFileName),
                JsonSerializer.Serialize(manifest, serializer));

            foreach (string name in new[]
                     { "Documentation", "Fonctionnel", "Technique", "Risques",
                       "Dépendances", "Exploitation", "Déploiement", "Réunions",
                       "Décisions", "Tests" })
            {
                string directory = Path.Combine(projectRoot, name);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(
                    Path.Combine(directory, name + ".md"),
                    name is "Risques" or "Dépendances"
                        ? "# Identical text\n"
                        : "# " + name + "\n");
            }

            Guid blockId = Guid.NewGuid();
            string oldTechnique = Path.Combine(projectRoot, "Technique", "Technique.md");
            await File.WriteAllTextAsync(oldTechnique,
                "# Technique\n## Critère\n" +
                MarkdownBlockIdentityCodec.FormatMarker(blockId) +
                "\n[Glossaire](../Glossaire.md)\n");
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "Jalons.md"),
                "# Jalons\n");
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "Glossaire.md"),
                "# Glossaire\n");
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "Présentation.md"),
                "# Présentation\n[Technique](Technique/Technique.md)\n");

            WorkspaceLinkIndexService links = new(workspace);
            LinkIndexCatalog before = await links.RefreshAsync();
            string projectRelative = Path.GetRelativePath(workspace,
                projectRoot).Replace('\\', '/');
            string oldTechniquePath = projectRelative + "/Technique/Technique.md";
            string oldRiskPath = projectRelative + "/Risques/Risques.md";
            string oldDependencyPath = projectRelative + "/Dépendances/Dépendances.md";
            Guid techniqueId = before.Targets.Single(item =>
                item.RelativePath == oldTechniquePath).Id;
            Guid riskId = before.Targets.Single(item =>
                item.RelativePath == oldRiskPath).Id;
            Guid dependencyId = before.Targets.Single(item =>
                item.RelativePath == oldDependencyPath).Id;
            Assert(riskId != dependencyId && techniqueId != Guid.Empty,
                "Cold index must assign distinct identities even to duplicate content.");
            Assert(BlockIndexQuery.Resolve(before, blockId).Document?.Id == techniqueId,
                "Criterion must initially be owned by its original document ID.");

            ComplexProjectMigrationPlanService planner = new(workspace);
            ComplexProjectMigrationPlan plan = await planner.PrepareAsync(projectRoot);
            Assert(plan.CanExecute && plan.LinkRewrites.Count == 2,
                "Migration should preview both a moved document edit and root backlink.");

            ComplexProjectMigrationTransactionService interrupted = new(
                workspace, checkpoint: phase =>
                {
                    if (phase == "published")
                    {
                        throw new InvalidOperationException(
                            "Simulated stop before the index can be refreshed.");
                    }
                });
            await RejectAsync(() => interrupted.ApplyAsync(
                    plan, plan.SourceFingerprint, approved: true),
                "Simulated interruption after publication must retain the journal.");

            ComplexProjectMigrationTransactionService executor = new(workspace);
            Assert((await executor.ListPendingProjectsAsync()).Single() == projectRoot,
                "Published but unconfirmed migration must remain pending.");
            Assert(await executor.RecoverPendingAsync(approved: true) == 1,
                "Explicit recovery must complete the already published transaction.");

            string newTechniquePath = projectRelative +
                "/Documentation/Technique/Technique.md";
            string newRiskPath = projectRelative + "/Analyse/Risques/Risques.md";
            string newDependencyPath = projectRelative +
                "/Analyse/Dépendances/Dépendances.md";
            LinkIndexCatalog migrated = await new WorkspaceLinkIndexService(workspace)
                .RefreshAsync();
            Assert(migrated.Targets.Single(item =>
                    item.RelativePath == newTechniquePath).Id == techniqueId &&
                migrated.Targets.Single(item =>
                    item.RelativePath == newRiskPath).Id == riskId &&
                migrated.Targets.Single(item =>
                    item.RelativePath == newDependencyPath).Id == dependencyId &&
                BlockIndexQuery.Resolve(migrated, blockId).Document?.Id == techniqueId,
                "Rebuilt index must preserve IDs for moved-and-edited documents, " +
                "duplicate-content documents and their stable Markdown blocks.");

            LinkIndexCatalog repeated = await links.RefreshAsync();
            Assert(repeated.Targets.Single(item =>
                    item.RelativePath == newTechniquePath).Id == techniqueId,
                "Repeated refresh must not create a second document identity.");

            IReadOnlyList<ComplexProjectMigrationJournal> rollback =
                await executor.GetRollbackCandidatesAsync(projectRoot);
            Assert(rollback.Count == 1 &&
                rollback[0].DocumentIdentities.Any(item =>
                    item.DocumentId == techniqueId &&
                    item.OriginalRelativePath == oldTechniquePath &&
                    item.PublishedRelativePath == newTechniquePath),
                "Durable journal must retain enough lineage for a future recovery.");

            await executor.RollbackAsync(rollback[0].TransactionId, approved: true);
            LinkIndexCatalog reverted = await new WorkspaceLinkIndexService(workspace)
                .RefreshAsync();
            Assert(reverted.Targets.Single(item =>
                    item.RelativePath == oldTechniquePath).Id == techniqueId &&
                reverted.Targets.Single(item =>
                    item.RelativePath == oldRiskPath).Id == riskId &&
                reverted.Targets.Single(item =>
                    item.RelativePath == oldDependencyPath).Id == dependencyId &&
                BlockIndexQuery.Resolve(reverted, blockId).Document?.Id == techniqueId,
                "A rolled-back journal must restore original document and block GUIDs.");
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
