using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxFirstImportRecoverySmokeTests
{
    /// <summary>
    /// Verifies write-ahead recovery following simulated process termination,
    /// foreign edits, and a complete import stopped before its commit marker.
    /// </summary>
    /// <param name="workspace">Disposable smoke-test workspace.</param>
    /// <returns>Asynchronous regression suite.</returns>
    public static async Task RunAsync(string workspace)
    {
        DocxFirstImportRecoveryService service =
            new DocxFirstImportRecoveryService(workspace);

        string existingProject = Path.Combine(workspace,
            "initial-import-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(existingProject);
        string projectManifest = Path.Combine(existingProject, ".project.json");
        const string original = "{\"project\":\"original\"}\n";
        const string updated = "{\"project\":\"import-edit\"}\n";
        await File.WriteAllTextAsync(projectManifest, original);
        string unrelated = Path.Combine(existingProject, "UserNote.md");
        await File.WriteAllTextAsync(unrelated, "This file already belongs to the user.");
        string created = Path.Combine(existingProject, "Imported.md");
        string createdRelative = Path.GetRelativePath(workspace, created).Replace('\\', '/');
        string projectRelative = Path.GetRelativePath(workspace, existingProject)
            .Replace('\\', '/');
        DocxImportPlan plan = Plan(projectRelative);
        Guid firstId = Guid.NewGuid();

        // Crash after updating an existing project's manifest and creating a
        // document. The exact original bytes must be restored on next launch.
        using (DocxFirstImportSession session = await service.BeginAsync(
                   plan, firstId, Guid.NewGuid(),
                   [File(createdRelative, "First imported paragraph")]))
        {
            await service.ExpectProjectManifestAsync(session,
                DocxImportManifestStore.ComputeSha256(updated));
            await File.WriteAllTextAsync(projectManifest, updated);
            await File.WriteAllTextAsync(created, "First imported paragraph");
            // A normal process exit would dispose the OS lock without setting
            // Completed or invoking the exception rollback path.
        }

        Assert((await service.ListPendingProjectsAsync()).Single() == existingProject,
            "Prepared first-import journal must expose its impacted project.");
        Assert(await service.RecoverPendingAsync() == 1 &&
               await File.ReadAllTextAsync(projectManifest) == original &&
               !System.IO.File.Exists(created) &&
               await File.ReadAllTextAsync(unrelated) ==
                   "This file already belongs to the user.",
            "Crash recovery must restore the original project manifest and remove owned files only.");

        // Unknown edits force manual action. The first pass must preserve the
        // writer's bytes and the immutable manifest backup without partial rollback.
        Guid conflictId = Guid.NewGuid();
        using (DocxFirstImportSession session = await service.BeginAsync(
                   plan, conflictId, Guid.NewGuid(),
                   [File(createdRelative, "Expected file bytes")]))
        {
            await File.WriteAllTextAsync(created, "Third-party edit");
        }

        bool conflictBlocked = false;
        try
        {
            await service.RecoverPendingAsync();
        }
        catch (InvalidDataException)
        {
            conflictBlocked = true;
        }

        Assert(conflictBlocked &&
               await File.ReadAllTextAsync(created) == "Third-party edit",
            "Crash recovery must not destroy third-party modifications.");
        string preserved = Path.Combine(workspace,
            "preserved-" + Guid.NewGuid().ToString("N") + ".md");
        System.IO.File.Move(created, preserved);
        Assert(await service.RecoverPendingAsync() == 1 &&
               await File.ReadAllTextAsync(preserved) == "Third-party edit",
            "Manual conflict resolution must permit subsequent safe recovery.");

        // All bytes were committed before a simulated crash, but the final
        // Completed journal state was not persisted. Recovery must keep the
        // valid import instead of rolling it back.
        Guid completedId = Guid.NewGuid();
        string completedDocument = Path.Combine(existingProject,
            "Complete-" + completedId.ToString("N") + ".md");
        string completedRelative = Path.GetRelativePath(workspace, completedDocument)
            .Replace('\\', '/');
        string source = Path.Combine(workspace, "Imports", "Sources",
            "complete-" + completedId.ToString("N") + ".docx");
        string sourceRelative = Path.GetRelativePath(workspace, source)
            .Replace('\\', '/');
        const string docxData = "Synthetic Word package bytes";
        const string markdown = "Accepted Word section";
        Guid targetId = Guid.NewGuid();
        using (DocxFirstImportSession session = await service.BeginAsync(
                   plan, completedId, targetId,
                   [File(completedRelative, markdown), File(sourceRelative, docxData)]))
        {
            DocxImportManifest provenance = new DocxImportManifest
            {
                ImportId = completedId,
                ProjectId = targetId,
                ImportedAtUtc = DateTimeOffset.UtcNow,
                SourceFileName = "complete.docx",
                SourceSha256 = DocxImportManifestStore.ComputeSha256(docxData),
                SourceCopyRelativePath = sourceRelative,
                Fragments =
                [
                    new DocxImportManifestFragment
                    {
                        FragmentId = Guid.NewGuid(),
                        SourceSectionIndex = 0,
                        SourceHeading = "Accepted Word section",
                        TargetSection = "Technique",
                        DestinationRelativePath = completedRelative,
                        FragmentSha256 =
                            DocxImportManifestStore.ComputeSha256(markdown),
                        BaseMarkdown = markdown,
                        DestinationSha256 =
                            DocxImportManifestStore.ComputeSha256(markdown)
                    }
                ]
            };

            await service.ExpectProvenanceAsync(session, provenance);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllTextAsync(source, docxData);
            await File.WriteAllTextAsync(completedDocument, markdown);
            DocxImportManifestStore store = new DocxImportManifestStore(workspace);
            await store.SaveNewAsync(existingProject, provenance);
            // Simulated hard termination before CompleteAsync.
        }

        Assert(await service.RecoverPendingAsync() == 1 &&
               await File.ReadAllTextAsync(completedDocument) == markdown &&
               System.IO.File.Exists(source),
            "A complete provenance-verified import must be finalized after a crash.");
        Assert(await service.RecoverPendingAsync() == 0,
            "Completed initial imports must be idempotent on subsequent launches.");

        // An incomplete new project's scaffolding may already be edited by a
        // person: remove only checksum-verified import outputs, never its folder.
        Guid newId = Guid.NewGuid();
        string newlyCreated = Path.Combine(workspace,
            "new-first-import-" + newId.ToString("N"));
        string newRelative = Path.GetRelativePath(workspace, newlyCreated)
            .Replace('\\', '/');
        string generated = Path.Combine(newlyCreated, "Tests", "Import.md");
        string generatedRelative = Path.GetRelativePath(workspace, generated)
            .Replace('\\', '/');
        using (DocxFirstImportSession session = await service.BeginAsync(
                   Plan(newRelative), newId, null,
                   [File(generatedRelative, "DOCX content")]))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(generated)!);
            await File.WriteAllTextAsync(generated, "DOCX content");
            await File.WriteAllTextAsync(
                Path.Combine(newlyCreated, "Fiche de Maintenance.md"),
                "Manual notes retained after interruption.");
        }

        Assert(await service.RecoverPendingAsync() == 1 &&
               !System.IO.File.Exists(generated) &&
               await File.ReadAllTextAsync(
                   Path.Combine(newlyCreated, "Fiche de Maintenance.md")) ==
                   "Manual notes retained after interruption.",
            "Recovery must preserve a new project's initial scaffold and unknown edits.");
    }

    /// <summary>Creates a minimal stable, approved transaction plan.</summary>
    /// <param name="relative">Workspace-relative project directory.</param>
    /// <returns>Valid plan for isolated journal tests.</returns>
    private static DocxImportPlan Plan(string relative) => new DocxImportPlan
    {
        TargetProjectDisplayName = "Crash smoke",
        TargetProjectRelativePath = relative,
        Fingerprint = new string('a', 64)
    };

    /// <summary>Calculates one expected UTF-8 file checksum.</summary>
    /// <param name="relative">Workspace-relative output path.</param>
    /// <param name="content">Exact bytes represented as UTF-8 text.</param>
    /// <returns>Expected created file descriptor.</returns>
    private static DocxFirstImportFile File(string relative, string content) =>
        new DocxFirstImportFile
        {
            RelativePath = relative,
            Sha256 = DocxImportManifestStore.ComputeSha256(content)
        };

    /// <summary>Fails the suite on data loss, unexpected mutation or missing recovery.</summary>
    /// <param name="condition">Required behavior.</param>
    /// <param name="message">Failure reason.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
