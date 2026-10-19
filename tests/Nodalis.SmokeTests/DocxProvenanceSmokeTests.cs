using System.IO.Compression;
using System.Text;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

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
