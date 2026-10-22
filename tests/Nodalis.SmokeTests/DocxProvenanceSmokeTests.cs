using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Persistence;

internal static class DocxProvenanceSmokeTests
{
    /// <summary>
    /// Checks portable provenance, homonymous sources, snapshot tampering,
    /// defensive reads and the actual preview-to-commit DOCX workflow.
    /// </summary>
    public static async Task RunAsync(string root)
    {
        string legacyProject = Path.Combine(root, "docx-provenance-legacy");
        Directory.CreateDirectory(legacyProject);
        DocxImportManifestStore store = new DocxImportManifestStore(root);
        string firstHash = DocxImportManifestStore.ComputeSha256("word version A");
        string secondHash = DocxImportManifestStore.ComputeSha256("word version B");

        IReadOnlyList<DocxImportManifestMatch> noHistory =
            await store.FindCandidatesAsync(legacyProject, "spec.docx", firstHash);
        Assert(noHistory.Count == 0 &&
               !Directory.Exists(Path.Combine(legacyProject, DocxImportManifestStore.DirectoryName)),
            "Legacy projects must remain readable without a provenance migration.");

        DocxImportManifest first = MakeManifest(store, root, firstHash, 0, "Tests");
        DocxImportManifest second = MakeManifest(store, root, secondHash, 1, "Tests");
        string firstPath = await store.SaveNewAsync(legacyProject, first);
        string secondPath = await store.SaveNewAsync(legacyProject, second);

        Assert(File.Exists(firstPath) && File.Exists(secondPath),
            "Two identically named but different DOCX sources require separate manifests.");
        DocxImportManifest loaded = await store.LoadAsync(firstPath);
        Assert(loaded.ImportId == first.ImportId &&
               loaded.Fragments[0].BaseMarkdown == first.Fragments[0].BaseMarkdown &&
               loaded.AppliedMappingProfile?.Rules.Count == 1,
            "The atomic JSON round-trip must preserve source and fragment identities.");

        IReadOnlyList<DocxImportManifestMatch> candidates =
            await store.FindCandidatesAsync(legacyProject, "spec.docx", secondHash);
        Assert(candidates.Count == 2 &&
               candidates.Count(candidate => candidate.ExactSourceHash) == 1 &&
               candidates[0].Manifest.ImportId == second.ImportId,
            "A source name collision must remain ambiguous; exact package hash ranks first.");

        IReadOnlyList<DocxImportManifestMatch> noSameSource =
            await store.FindCandidatesAsync(legacyProject, "renamed.docx",
                DocxImportManifestStore.ComputeSha256("different source"));
        IReadOnlyList<DocxImportManifest> fullProjectHistory =
            await store.ListProjectHistoryAsync(legacyProject);
        Assert(noSameSource.Count == 0 &&
               fullProjectHistory.Count == 2 &&
               fullProjectHistory.Any(item => item.ImportId == first.ImportId) &&
               fullProjectHistory.Any(item => item.ImportId == second.ImportId),
            "Renamed and modified Word files must expose only explicitly selected project history.");

        bool duplicateRejected = false;
        try
        {
            await store.SaveNewAsync(legacyProject, first);
        }
        catch (IOException)
        {
            duplicateRejected = true;
        }

        Assert(duplicateRejected, "A provenance save must never replace existing history.");

        DocxImportManifest raced = first with { ImportId = Guid.NewGuid() };
        DocxImportManifest alternate = raced with { SourceSha256 = secondHash };
        Task<string>[] simultaneous =
        [
            store.SaveNewAsync(legacyProject, raced),
            store.SaveNewAsync(legacyProject, alternate)
        ];
        try
        {
            await Task.WhenAll(simultaneous);
        }
        catch (IOException)
        {
            // Exactly one atomic creation must win the contention.
        }

        string racePath = store.GetManifestPath(legacyProject, raced.ImportId);
        DocxImportManifest surviving = await store.LoadAsync(racePath);
        Assert(simultaneous.Count(task => task.IsCompletedSuccessfully) == 1 &&
               simultaneous.Count(task => task.IsFaulted) == 1 &&
               (surviving.SourceSha256 == firstHash ||
                surviving.SourceSha256 == secondHash) &&
               Directory.EnumerateFiles(
                   Path.Combine(legacyProject, DocxImportManifestStore.DirectoryName),
                   "*.tmp").Count() == 0,
            "Concurrent immutable manifest creation must preserve exactly one writer and clean temp files.");

        bool traversalRejected = false;
        try
        {
            store.Validate(first with
            {
                SourceCopyRelativePath = "../outside.docx"
            });
        }
        catch (InvalidDataException)
        {
            traversalRejected = true;
        }

        Assert(traversalRejected, "Paths outside the workspace must be rejected.");

        bool forgedSourceBlockCountRejected = false;
        try
        {
            store.Validate(first with
            {
                Fragments =
                [
                    first.Fragments[0] with
                    {
                        OriginalSourceBlockCount = 0
                    }
                ]
            });
        }
        catch (InvalidDataException)
        {
            forgedSourceBlockCountRejected = true;
        }

        Assert(forgedSourceBlockCountRejected,
            "DOCX provenance cannot claim fewer source blocks than were selected.");

        bool tamperingRejected = false;
        try
        {
            store.Validate(first with
            {
                Fragments =
                [
                    first.Fragments[0] with { BaseMarkdown = "Modified without hashing" }
                ]
            });
        }
        catch (InvalidDataException)
        {
            tamperingRejected = true;
        }

        Assert(tamperingRejected, "An altered base snapshot must not validate.");

        await File.WriteAllTextAsync(firstPath, "{ broken json");
        bool corruptionRejected = false;
        try
        {
            await store.LoadAsync(firstPath);
        }
        catch (InvalidDataException)
        {
            corruptionRejected = true;
        }

        Assert(corruptionRejected && File.Exists(secondPath),
            "Corrupted history must fail visibly without damaging other manifests.");

        // A real import checks that provenance is created only after Markdown and
        // the preserved DOCX copy exist, and that it appears in the approved plan.
        string sourcePath = Path.Combine(root,
            "docx-provenance-" + Guid.NewGuid().ToString("N") + ".docx");
        using (ZipArchive archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create))
        {
            WriteXml(archive, "word/styles.xml", """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:style w:type="paragraph" w:styleId="Heading1">
                <w:name w:val="Titre 1"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr>
                </w:style></w:styles>
                """);
            WriteXml(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Tests</w:t></w:r></w:p>
                <w:p><w:r><w:t>Cas de test original</w:t></w:r></w:p>
                </w:body></w:document>
                """);
        }

        WorkspaceDocxImportService service = new WorkspaceDocxImportService(root);
        DocxImportPreview preview = await service.PreparePreviewAsync(sourcePath);
        Assert(preview.Applications.Count > 0 && preview.Sections.Count > 0,
            "A DOCX preview must offer an application and at least one source section.");

        DocxImportCommitRequest request = new DocxImportCommitRequest
        {
            ApplicationId = preview.Applications[0].Id,
            NewProjectName = "Provenance " + Guid.NewGuid().ToString("N"),
            AppliedMappingProfile = first.AppliedMappingProfile,
            Sections = preview.Sections.Select(section => new DocxImportSectionSelection
            {
                SectionIndex = section.Index,
                Include = true,
                TargetSection = "Tests"
            }).ToList()
        };
        // A source edited after the preview cannot silently replace its
        // analyzed content, even when the earlier plan fingerprint is approved.
        DocxImportPreview changedPreview = await service.PreparePreviewAsync(sourcePath);
        DocxImportPlan previouslyApproved = await service.BuildPlanAsync(changedPreview, request);
        await File.AppendAllTextAsync(changedPreview.StagedImport.StagedCopyPath,
            "modified after preview");
        bool changedPlanRejected = false;
        bool changedCommitRejected = false;
        try
        {
            await service.BuildPlanAsync(changedPreview, request);
        }
        catch (InvalidDataException)
        {
            changedPlanRejected = true;
        }

        try
        {
            await service.CommitAsync(changedPreview, request with
            {
                ExpectedPlanFingerprint = previouslyApproved.Fingerprint
            });
        }
        catch (InvalidDataException)
        {
            changedCommitRejected = true;
        }

        Assert(changedPlanRejected && changedCommitRejected &&
               !Directory.Exists(Path.Combine(root,
                   previouslyApproved.TargetProjectRelativePath)),
            "Tampered staged DOCX must not create a project or pass an approved plan.");
        service.DiscardStagedCopy(changedPreview.StagedImport);

        string occupied = Path.Combine(root, "docx-safety-collision.md");
        await File.WriteAllTextAsync(occupied, "Existing user content");
        bool newFileCollisionRejected = false;
        try
        {
            await AtomicFileWriter.WriteNewAllTextAsync(occupied, "Overwrite attempt");
        }
        catch (IOException)
        {
            newFileCollisionRejected = true;
        }

        Assert(newFileCollisionRejected &&
               await File.ReadAllTextAsync(occupied) == "Existing user content" &&
               !Directory.EnumerateFiles(root, ".docx-safety-collision.md.*.tmp").Any(),
            "Concurrent new-document collision must preserve existing notes and clean staging.");

        DocxImportPlan plan = await service.BuildPlanAsync(preview, request);
        string expectedManifestPath = plan.Changes.Single(change =>
            change.RelativePath.EndsWith(preview.ImportId.ToString("N") + ".json",
                StringComparison.OrdinalIgnoreCase)).RelativePath;
        Assert(plan.MappingProfileSha256.Length == 64 &&
               expectedManifestPath.Contains(DocxImportManifestStore.DirectoryName,
                StringComparison.Ordinal),
            "An approved dry-run must include the provenance destination.");

        DocxImportCommitResult committed = await service.CommitAsync(
            preview, request with { ExpectedPlanFingerprint = plan.Fingerprint });
        Assert(committed.ManifestPath is not null &&
               File.Exists(committed.ManifestPath) &&
               File.Exists(committed.SourceCopyPath) &&
               committed.GeneratedFiles.Any(File.Exists),
            "The commit must materialize Markdown, copied DOCX and provenance.");

        DocxImportManifest actual = await store.LoadAsync(committed.ManifestPath!);
        Assert(actual.ImportId == preview.ImportId &&
               actual.ProjectId == committed.ProjectId &&
               actual.Fragments.Count == preview.Sections.Count &&
               actual.AppliedMappingProfile?.Id == request.AppliedMappingProfile?.Id &&
               actual.SourceSha256 == await DocxImportManifestStore.ComputeFileSha256Async(
                   committed.SourceCopyPath) &&
               actual.Fragments.All(fragment =>
                   fragment.TargetSectionId is Guid sectionId && sectionId != Guid.Empty &&
                   File.Exists(Path.Combine(root, fragment.DestinationRelativePath)) &&
                   DocxImportManifestStore.ComputeSha256(
                       File.ReadAllText(Path.Combine(root, fragment.DestinationRelativePath))) ==
                   fragment.DestinationSha256),
            "The manifest must identify every selected Word section and exact target file.");

        Assert(File.Exists(sourcePath),
            "Provenance creation must not modify or remove the original DOCX.");

        // A genuinely new project with a user-defined Word section must be
        // able to update its just-created profile without a null baseline.
        DocxImportPreview customPreview = await service.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest customRequest = request with
        {
            NewProjectName = "DOCX custom section " + Guid.NewGuid().ToString("N"),
            Sections = customPreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = "Section DOCX personnalisée"
                }).ToList()
        };
        DocxImportPlan customPlan = await service.BuildPlanAsync(
            customPreview, customRequest);
        DocxImportCommitResult customCommitted = await service.CommitAsync(
            customPreview, customRequest with
            {
                ExpectedPlanFingerprint = customPlan.Fingerprint
            });
        using JsonDocument customManifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(customCommitted.ProjectDirectory,
                WorkspaceLayout.ProjectManifestFileName)));
        bool hasCustomSection = customManifest.RootElement.GetProperty("sections")
            .EnumerateArray().Any(section =>
                section.GetProperty("name").GetString() ==
                "Section DOCX personnalisée");
        Assert(customCommitted.GeneratedFiles.Count == 1 &&
               File.Exists(customCommitted.GeneratedFiles[0]) &&
               hasCustomSection,
            "New project imports must support custom DOCX section destinations.");

        // Race after plan confirmation and project-manifest update: an
        // external writer reserves the exact Markdown path. Import must NOT
        // pick a new name or erase that writer's file.
        string raceSection = "Race import DOCX";
        string raceDestination = string.Empty;
        WorkspaceDocxImportService racing = new WorkspaceDocxImportService(root,
            afterProjectManifestCheckpoint: _ =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(raceDestination)!);
                File.WriteAllText(raceDestination, "Concurrent writer owns this file");
            });
        DocxImportPreview racePreview = await racing.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest raceRequest = request with
        {
            ProjectId = committed.ProjectId,
            NewProjectName = null,
            Sections = racePreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = raceSection
                }).ToList()
        };
        DocxImportPlan racePlan = await racing.BuildPlanAsync(racePreview, raceRequest);
        raceDestination = Path.Combine(root, racePlan.Changes.Single(change =>
            change.Action == "Créer" &&
            change.Description.Contains("section Nodalis « " + raceSection + " »",
                StringComparison.Ordinal)).RelativePath);
        byte[] raceManifestBefore = await File.ReadAllBytesAsync(
            Path.Combine(committed.ProjectDirectory, WorkspaceLayout.ProjectManifestFileName));
        bool collisionRejected = false;
        try
        {
            await racing.CommitAsync(racePreview, raceRequest with
            {
                ExpectedPlanFingerprint = racePlan.Fingerprint
            });
        }
        catch (IOException)
        {
            collisionRejected = true;
        }

        Assert(collisionRejected &&
               await File.ReadAllTextAsync(raceDestination) == "Concurrent writer owns this file" &&
               (await File.ReadAllBytesAsync(Path.Combine(committed.ProjectDirectory,
                   WorkspaceLayout.ProjectManifestFileName))).SequenceEqual(raceManifestBefore),
            "An interprocess collision after approval must not rename the import or discard user data.");

        // A second race reserves the approved DOCX copy after Markdown creation.
        // Rollback must preserve that other process's source copy.
        string plannedSourceCollision = string.Empty;
        WorkspaceDocxImportService sourceRacing = new WorkspaceDocxImportService(root,
            afterMarkdownCreatedCheckpoint: (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plannedSourceCollision)!);
                File.WriteAllText(plannedSourceCollision, "External source reservation");
            });
        DocxImportPreview sourceRacePreview = await sourceRacing.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest sourceRaceRequest = request with
        {
            ProjectId = committed.ProjectId,
            NewProjectName = null,
            Sections = sourceRacePreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = "Tests"
                }).ToList()
        };
        DocxImportPlan sourceRacePlan = await sourceRacing.BuildPlanAsync(
            sourceRacePreview, sourceRaceRequest);
        plannedSourceCollision = Path.Combine(root, sourceRacePlan.Changes.Single(change =>
            change.Action == "Copier").RelativePath);
        bool sourceCollisionRejected = false;
        try
        {
            await sourceRacing.CommitAsync(sourceRacePreview, sourceRaceRequest with
            {
                ExpectedPlanFingerprint = sourceRacePlan.Fingerprint
            });
        }
        catch (IOException)
        {
            sourceCollisionRejected = true;
        }

        Assert(sourceCollisionRejected &&
               await File.ReadAllTextAsync(plannedSourceCollision) == "External source reservation",
            "A concurrent DOCX source reservation cannot be silently renamed or overwritten.");

        // The dry-run must include the actual initial files created by each
        // project profile, including the two-file Ultra-simple contract.
        DocxImportPreview ultraPreview = await service.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest ultraRequest = request with
        {
            NewProjectName = "DOCX Ultra " + Guid.NewGuid().ToString("N"),
            NewProjectComplexity = ProjectComplexity.UltraSimple,
            Sections = ultraPreview.Sections.Select(section => new DocxImportSectionSelection
            {
                SectionIndex = section.Index,
                Include = true,
                TargetSection = "Tests"
            }).ToList()
        };
        DocxImportPlan ultraPlan = await service.BuildPlanAsync(ultraPreview, ultraRequest);
        Assert(ultraPlan.Changes.Any(change =>
                change.Action == "Créer" &&
                change.RelativePath.EndsWith("Fiche de Maintenance.md", StringComparison.Ordinal)) &&
               !ultraPlan.Changes.Any(change =>
                change.RelativePath.EndsWith("Présentation.md", StringComparison.Ordinal)),
            "Ultra-simple DOCX dry-runs must describe the maintenance sheet instead of a nonexistent presentation.");
        DocxImportCommitResult ultraCommitted = await service.CommitAsync(ultraPreview,
            ultraRequest with { ExpectedPlanFingerprint = ultraPlan.Fingerprint });
        Assert(File.Exists(Path.Combine(ultraCommitted.ProjectDirectory, "Fiche de Maintenance.md")) &&
               !File.Exists(Path.Combine(ultraCommitted.ProjectDirectory, "Présentation.md")),
            "Ultra-simple committed output must match the initial files advertised in the preview.");

        DocxImportPreview ordinaryPreview = await service.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest ordinaryRequest = request with
        {
            NewProjectName = "DOCX Standard " + Guid.NewGuid().ToString("N"),
            NewProjectComplexity = ProjectComplexity.Medium,
            Sections = ordinaryPreview.Sections.Select(section => new DocxImportSectionSelection
            {
                SectionIndex = section.Index,
                Include = true,
                TargetSection = "Technique"
            }).ToList()
        };
        DocxImportPlan ordinaryPlan = await service.BuildPlanAsync(
            ordinaryPreview, ordinaryRequest);
        DocxImportCommitResult ordinaryCommitted = await service.CommitAsync(
            ordinaryPreview, ordinaryRequest with
            {
                ExpectedPlanFingerprint = ordinaryPlan.Fingerprint
            });
        foreach (DocxImportPlannedChange filePlan in ordinaryPlan.Changes
                     .Where(change => change.Action == "Créer" &&
                         change.Description.StartsWith("Document initial de la section ",
                             StringComparison.Ordinal)))
        {
            Assert(File.Exists(Path.Combine(root, filePlan.RelativePath)),
                "Every default profile document predicted in the dry-run must exist at the exact destination.");
        }

        // Test interruption immediately after creating a Markdown document:
        // automatic rollback must remove only our generated file and restore
        // the exact project manifest, never touching pre-existing content.
        string projectManifestPath = Path.Combine(committed.ProjectDirectory,
            WorkspaceLayout.ProjectManifestFileName);
        byte[] originalProjectBytes = await File.ReadAllBytesAsync(projectManifestPath);
        string[] beforeDocuments = Directory.GetFiles(committed.ProjectDirectory,
            "*.md", SearchOption.AllDirectories);
        WorkspaceDocxImportService partial = new WorkspaceDocxImportService(root,
            afterMarkdownCreatedCheckpoint: (_, _) =>
                throw new InvalidOperationException("Simulated Markdown interruption"));
        DocxImportPreview partialPreview = await partial.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest partialRequest = request with
        {
            ProjectId = committed.ProjectId,
            NewProjectName = null,
            Sections = partialPreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = "Rollback après création"
                }).ToList()
        };
        DocxImportPlan partialPlan = await partial.BuildPlanAsync(
            partialPreview, partialRequest);
        bool partialObserved = false;
        try
        {
            await partial.CommitAsync(partialPreview, partialRequest with
            {
                ExpectedPlanFingerprint = partialPlan.Fingerprint
            });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Simulated", StringComparison.Ordinal))
        {
            partialObserved = true;
        }

        Assert(partialObserved &&
               (await File.ReadAllBytesAsync(projectManifestPath))
                   .SequenceEqual(originalProjectBytes) &&
               Directory.GetFiles(committed.ProjectDirectory, "*.md",
                   SearchOption.AllDirectories)
                   .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                   .SequenceEqual(beforeDocuments.OrderBy(path => path,
                       StringComparer.OrdinalIgnoreCase)),
            "Interrupted new Markdown creation must not leave orphan documents.");

        // If a concurrent editor modifies a newly generated document,
        // rollback must preserve that user's content rather than delete it.
        const string userEdit = "# Manual changes during interrupted import";
        string? preservedMarkdownPath = null;
        WorkspaceDocxImportService userEdited = new WorkspaceDocxImportService(root,
            afterMarkdownCreatedCheckpoint: (_, destination) =>
            {
                preservedMarkdownPath = destination;
                File.WriteAllText(destination, userEdit);
                throw new InvalidOperationException("Simulated external Markdown edit");
            });
        DocxImportPreview editedPreview = await userEdited.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest editedRequest = partialRequest with
        {
            Sections = editedPreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = "Édition externe préservée"
                }).ToList()
        };
        DocxImportPlan editedPlan = await userEdited.BuildPlanAsync(
            editedPreview, editedRequest);
        bool editedObserved = false;
        try
        {
            await userEdited.CommitAsync(editedPreview, editedRequest with
            {
                ExpectedPlanFingerprint = editedPlan.Fingerprint
            });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Simulated", StringComparison.Ordinal))
        {
            editedObserved = true;
        }

        Assert(editedObserved && preservedMarkdownPath is not null &&
               File.Exists(preservedMarkdownPath) &&
               await File.ReadAllTextAsync(preservedMarkdownPath) == userEdit &&
               (await File.ReadAllBytesAsync(projectManifestPath))
                   .SequenceEqual(originalProjectBytes),
            "Rollback must keep externally edited Markdown while restoring project metadata.");

        // Force an interruption immediately after an existing project's
        // manifest was changed, before writing any generated Word document.
        WorkspaceDocxImportService interrupted = new WorkspaceDocxImportService(root,
            _ => throw new InvalidOperationException("Simulated DOCX import interruption"));
        DocxImportPreview interruptedPreview =
            await interrupted.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest reuse = request with
        {
            ProjectId = committed.ProjectId,
            NewProjectName = null,
            Sections = interruptedPreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = "Rollback DOCX unique section"
                }).ToList()
        };
        DocxImportPlan interruptedPlan =
            await interrupted.BuildPlanAsync(interruptedPreview, reuse);
        bool interruptedObserved = false;
        try
        {
            await interrupted.CommitAsync(interruptedPreview, reuse with
            {
                ExpectedPlanFingerprint = interruptedPlan.Fingerprint
            });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Simulated", StringComparison.Ordinal))
        {
            interruptedObserved = true;
        }

        Assert(interruptedObserved &&
               (await File.ReadAllBytesAsync(projectManifestPath))
                   .SequenceEqual(originalProjectBytes) &&
               !Directory.Exists(Path.Combine(committed.ProjectDirectory,
                   "Rollback DOCX unique section")),
            "Failed DOCX import must restore original project manifest bytes exactly.");

        // An editor racing with a failed import must win: rollback may only
        // restore a manifest still matching the bytes written by the import.
        string externalEditedJson = Encoding.UTF8.GetString(originalProjectBytes) +
                                    Environment.NewLine + "   ";
        WorkspaceDocxImportService externalEditor = new WorkspaceDocxImportService(
            root, destination =>
            {
                File.WriteAllText(destination, externalEditedJson);
                throw new InvalidOperationException("Simulated concurrent project edit");
            });
        DocxImportPreview concurrentPreview =
            await externalEditor.PreparePreviewAsync(sourcePath);
        DocxImportCommitRequest concurrentRequest = reuse with
        {
            Sections = concurrentPreview.Sections.Select(section =>
                new DocxImportSectionSelection
                {
                    SectionIndex = section.Index,
                    Include = true,
                    TargetSection = "Concurrent DOCX unique section"
                }).ToList()
        };
        DocxImportPlan concurrentPlan =
            await externalEditor.BuildPlanAsync(concurrentPreview, concurrentRequest);
        bool concurrentObserved = false;
        try
        {
            await externalEditor.CommitAsync(concurrentPreview, concurrentRequest with
            {
                ExpectedPlanFingerprint = concurrentPlan.Fingerprint
            });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Simulated", StringComparison.Ordinal))
        {
            concurrentObserved = true;
        }

        Assert(concurrentObserved &&
               await File.ReadAllTextAsync(projectManifestPath) == externalEditedJson &&
               !Directory.Exists(Path.Combine(committed.ProjectDirectory,
                   "Concurrent DOCX unique section")),
            "Rollback must never overwrite a concurrent external project edit.");
    }

    /// <summary>Creates representative input without requiring Word or a network connection.</summary>
    private static DocxImportManifest MakeManifest(
        DocxImportManifestStore store, string root, string sourceHash,
        int sectionIndex, string heading)
    {
        string snapshot = "## " + heading + "\n\nImported base";
        return new DocxImportManifest
        {
            ImportId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceFileName = "spec.docx",
            SourceSha256 = sourceHash,
            SourceCopyRelativePath = "Imports/Sources/spec.docx",
            AppliedMappingProfile = new DocxMappingProfile
            {
                Id = Guid.NewGuid(),
                Name = "Profil des spécifications",
                Rules =
                [
                    new DocxMappingProfileRule
                    {
                        Id = Guid.NewGuid(),
                        HeadingText = "Tests",
                        Action = DocxMappingAction.Map,
                        TargetSection = "Tests"
                    }
                ]
            },
            ExcludedSectionIndexes = [sectionIndex + 2],
            Fragments =
            [
                new DocxImportManifestFragment
                {
                    FragmentId = Guid.NewGuid(),
                    SourceSectionIndex = sectionIndex,
                    HeadingBlockIndex = sectionIndex,
                    SourceHeading = heading,
                    TargetSection = "Tests",
                    DestinationRelativePath = "docx-provenance-legacy/Tests/Import-spec.md",
                    SelectedBlockIndexes = [sectionIndex],
                    BaseMarkdown = snapshot,
                    FragmentSha256 = DocxImportManifestStore.ComputeSha256(snapshot),
                    DestinationSha256 = DocxImportManifestStore.ComputeSha256(snapshot)
                }
            ]
        };
    }

    /// <summary>Writes a simple XML part into a synthetic OOXML archive.</summary>
    private static void WriteXml(ZipArchive archive, string name, string xml)
    {
        using StreamWriter writer = new StreamWriter(archive.CreateEntry(name).Open(),
            new UTF8Encoding(false));
        writer.Write(xml);
    }

    /// <summary>Fails this smoke scenario on any contract regression.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
