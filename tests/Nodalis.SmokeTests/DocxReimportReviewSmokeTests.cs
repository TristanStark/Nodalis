using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Persistence;

internal static class DocxReimportReviewSmokeTests
{
    /// <summary>
    /// Exercises the full read-only source/base/local review, including
    /// a changed Word section, local edits, deletion and cross-project refusal.
    /// </summary>
    public static async Task RunAsync(string root)
    {
        string projectDirectory = Path.Combine(root,
            "Review " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);
        Guid projectId = Guid.NewGuid();
        ProjectManifest project = new ProjectManifest
        {
            Id = projectId,
            Name = "Reimport review",
            ApplicationId = Guid.NewGuid(),
            InitialComplexity = ProjectComplexity.Medium
        };
        await File.WriteAllTextAsync(Path.Combine(projectDirectory,
            WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(project, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

        string documents = Path.Combine(projectDirectory, "Tests");
        Directory.CreateDirectory(documents);
        string markdownPath = Path.Combine(documents, "Import - spec.md");
        string original = "## Tests\n\nCas initial";
        string latestWord = "## Tests\n\nCas Word modifié";
        await File.WriteAllTextAsync(markdownPath, original);

        string sourceDirectory = Path.Combine(root, WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);
        Directory.CreateDirectory(sourceDirectory);
        string oldCopy = Path.Combine(sourceDirectory,
            "spec-review-" + Guid.NewGuid().ToString("N") + ".docx");
        string newCopy = Path.Combine(root,
            "new-review-" + Guid.NewGuid().ToString("N") + ".docx");
        await File.WriteAllTextAsync(oldCopy, "Original DOCX package fixture");
        await File.WriteAllTextAsync(newCopy, "Updated DOCX package fixture");

        DocxImportManifestStore store = new DocxImportManifestStore(root);
        DocxImportManifestFragment fragment = new DocxImportManifestFragment
        {
            FragmentId = Guid.NewGuid(),
            SourceSectionIndex = 0,
            HeadingBlockIndex = 0,
            SourceHeading = "Tests",
            SourceParentPath = string.Empty,
            TargetSection = "Tests",
            DestinationRelativePath = store.ToWorkspaceRelativePath(markdownPath),
            BaseMarkdown = original,
            FragmentSha256 = DocxImportManifestStore.ComputeSha256(original),
            DestinationSha256 = DocxImportManifestStore.ComputeSha256(original),
            SelectedBlockIndexes = [1]
        };
        DocxImportManifest previous = new DocxImportManifest
        {
            ImportId = Guid.NewGuid(),
            ProjectId = projectId,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceFileName = "spec.docx",
            SourceSha256 = await DocxImportManifestStore.ComputeFileSha256Async(oldCopy),
            SourceCopyRelativePath = store.ToWorkspaceRelativePath(oldCopy),
            Fragments = [fragment]
        };

        DocxBlock heading = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = "Tests", HeadingLevel = 1 }
        };
        DocxBlock paragraph = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = "Cas Word modifié" }
        };
        DocxImportPreview preview = new DocxImportPreview
        {
            StagedImport = new DocxStagedImport
            {
                OriginalSourcePath = "spec.docx",
                StagedCopyPath = newCopy,
                Document = new ParsedDocxDocument { Blocks = [heading, paragraph] }
            },
            Analysis = new DocxImportAnalysis(),
            Sections =
            [
                new DocxImportSectionPreview
                {
                    Index = 0,
                    HeadingBlockIndex = 0,
                    SourceHeading = "Tests",
                    SuggestedTargetSection = "Tests",
                    BlockCount = 1,
                    MarkdownPreview = latestWord,
                    Blocks =
                    [
                        new DocxImportBlockPreview
                        {
                            BlockIndex = 1,
                            Block = paragraph,
                            KindLabel = "Paragraphe",
                            DisplayText = "Cas Word modifié",
                            MarkdownPreview = "Cas Word modifié"
                        }
                    ]
                }
            ]
        };

        DocxReimportReviewService service = new DocxReimportReviewService(root);
        DocxReimportReview wordOnly = await service.BuildAsync(
            projectDirectory, previous, preview);
        Assert(wordOnly.Fragments.Count == 1 &&
               wordOnly.Fragments[0].Change?.Kind == DocxReimportChangeKind.WordChanged &&
               wordOnly.Fragments[0].BaseMarkdown == original &&
               wordOnly.Fragments[0].LatestWordMarkdown?.Replace("\r\n", "\n") == latestWord &&
               wordOnly.Fragments[0].CurrentNodalisMarkdown == original &&
               !wordOnly.SourcePackageUnchanged,
            "Read-only review must compare old base, new Word and actual Nodalis text.");

        DocxReimportReview adjudicated = await service.BuildWithOverridesAsync(
            projectDirectory, previous, preview,
            [
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = fragment.FragmentId,
                    CurrentSectionIndex = null
                }
            ]);
        Assert(adjudicated.Fragments[0].Association.Kind ==
                   DocxReimportAssociationKind.ConfirmedMissingFromWord &&
               adjudicated.Fragments[0].Change?.Kind == DocxReimportChangeKind.WordDeleted &&
               File.Exists(markdownPath),
            "Explicitly confirming a missing Word section must not delete local Markdown.");

        DocxReimportProvenanceBuilder provenance =
            new DocxReimportProvenanceBuilder(root);
        DocxReimportResolution useWord = new DocxReimportResolution
        {
            FragmentId = fragment.FragmentId,
            Action = DocxReimportResolutionAction.UseWord
        };
        DocxReimportDecisionPlan appliedWord =
            DocxReimportDecisionPlanner.Build(wordOnly, previous, [useWord]);
        Assert(appliedWord.ReviewComplete && appliedWord.Writes.Count == 1,
            "A changed Word fragment must have one approved projected write.");
        Guid wordOperationId = Guid.NewGuid();
        DocxImportManifest newHistory = await provenance.BuildAsync(
            previous, wordOnly, appliedWord, preview, [useWord],
            wordOperationId,
            "Imports/Sources/reimport-" + wordOperationId.ToString("N") + ".docx");
        Assert(newHistory.PreviousImportId == previous.ImportId &&
               newHistory.ReimportOperationId == wordOperationId &&
               newHistory.ImportId == wordOperationId &&
               newHistory.SourceSha256 ==
                   await DocxImportManifestStore.ComputeFileSha256Async(newCopy) &&
               newHistory.Fragments.Single().FragmentId == fragment.FragmentId &&
               newHistory.Fragments.Single().BaseMarkdown.Replace("\r\n", "\n") == latestWord &&
               newHistory.Fragments.Single().LatestWordMarkdownSnapshot?.Replace("\r\n", "\n") == latestWord &&
               newHistory.Fragments.Single().ReimportResolution == "Word" &&
               newHistory.Fragments.Single().LatestWordSnapshotSha256 ==
                   DocxImportManifestStore.ComputeSha256(
                       newHistory.Fragments.Single().LatestWordMarkdownSnapshot!) &&
               newHistory.Fragments.Single().DestinationSha256 ==
                   appliedWord.Writes.Single().ProjectedSha256 &&
               !File.Exists(store.ResolveWorkspaceRelativePath(
                   newHistory.SourceCopyRelativePath)),
            "Versioned provenance must record approved Word baseline and lineage without writing.");

        bool forgedSnapshotRejected = false;
        try
        {
            store.Validate(newHistory with
            {
                Fragments =
                [
                    newHistory.Fragments.Single() with
                    {
                        LatestWordMarkdownSnapshot = "corrupted observed Word"
                    }
                ]
            });
        }
        catch (InvalidDataException)
        {
            forgedSnapshotRejected = true;
        }

        Assert(forgedSnapshotRejected,
            "Changing the observed Word snapshot without its SHA must fail validation.");

        DocxReimportTransactionService transaction =
            new DocxReimportTransactionService(root);
        DocxReimportProvenancePackage package = new DocxReimportProvenancePackage
        {
            Manifest = newHistory,
            StagedDocxPath = newCopy
        };
        string fingerprint = DocxReimportTransactionService.Fingerprint(appliedWord);
        DocxReimportTransactionResult committed = await transaction.CommitWithProvenanceAsync(
            projectDirectory, appliedWord, wordOperationId, fingerprint, package);
        Assert(!committed.AlreadyApplied && committed.WrittenFiles == 1 &&
               committed.ManifestPath is not null &&
               File.Exists(committed.ManifestPath) &&
               File.Exists(store.ResolveWorkspaceRelativePath(
                   newHistory.SourceCopyRelativePath)) &&
               (await File.ReadAllTextAsync(markdownPath)).Replace("\r\n", "\n") == latestWord &&
               (await store.LoadAsync(committed.ManifestPath)).PreviousImportId ==
                   previous.ImportId,
            "Provenance-aware transaction must commit Markdown, Word copy and lineage together.");

        DocxReimportTransactionResult repeated = await transaction.CommitWithProvenanceAsync(
            projectDirectory, appliedWord, wordOperationId, fingerprint, package);
        Assert(repeated.AlreadyApplied && repeated.WrittenFiles == 1,
            "Repeating a completed provenance operation must be idempotent.");

        // Restore the original fixture before the remaining independent
        // resolution tests. The immutable new history and audit remain on disk.
        await File.WriteAllTextAsync(markdownPath, original);
        Guid interruptedOperation = Guid.NewGuid();
        DocxImportManifest interruptedHistory = await provenance.BuildAsync(
            previous, wordOnly, appliedWord, preview, [useWord], interruptedOperation,
            "Imports/Sources/reimport-" + interruptedOperation.ToString("N") + ".docx");
        DocxReimportTransactionService injectedFailure =
            new DocxReimportTransactionService(root, checkpoint =>
            {
                if (checkpoint == 2)
                {
                    throw new InvalidOperationException("Injected metadata failure");
                }
            });
        bool rolledBackArtifacts = false;
        try
        {
            await injectedFailure.CommitWithProvenanceAsync(
                projectDirectory, appliedWord, interruptedOperation, fingerprint,
                new DocxReimportProvenancePackage
                {
                    Manifest = interruptedHistory,
                    StagedDocxPath = newCopy
                });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Injected", StringComparison.Ordinal))
        {
            rolledBackArtifacts = true;
        }

        Assert(rolledBackArtifacts &&
               await File.ReadAllTextAsync(markdownPath) == original &&
               !File.Exists(store.ResolveWorkspaceRelativePath(
                   interruptedHistory.SourceCopyRelativePath)) &&
               !File.Exists(store.GetManifestPath(projectDirectory,
                   interruptedOperation)),
            "Metadata failure must remove both new artifacts and restore existing Markdown.");

        DocxReimportResolution manual = new DocxReimportResolution
        {
            FragmentId = fragment.FragmentId,
            Action = DocxReimportResolutionAction.ManualMerge,
            ManualMarkdown = "## Tests\n\nRésolution manuelle"
        };
        DocxReimportDecisionPlan mergedPlan =
            DocxReimportDecisionPlanner.Build(wordOnly, previous, [manual]);
        Guid manualOperationId = Guid.NewGuid();
        DocxImportManifest mergedHistory = await provenance.BuildAsync(
            previous, wordOnly, mergedPlan, preview, [manual],
            manualOperationId,
            "Imports/Sources/reimport-" + manualOperationId.ToString("N") + ".docx");
        Assert(mergedHistory.Fragments.Single().BaseMarkdown ==
                   manual.ManualMarkdown &&
               mergedHistory.Fragments.Single().LatestWordMarkdownSnapshot?.Replace("\r\n", "\n") == latestWord &&
               mergedHistory.Fragments.Single().ReimportResolution == "Manual",
            "Manual resolution must preserve both the accepted Markdown and Word source text.");

        DocxReimportResolution keepNodalis = new DocxReimportResolution
        {
            FragmentId = fragment.FragmentId,
            Action = DocxReimportResolutionAction.KeepNodalis
        };
        DocxReimportDecisionPlan keptPlan =
            DocxReimportDecisionPlanner.Build(wordOnly, previous, [keepNodalis]);
        Guid retainedOperationId = Guid.NewGuid();
        DocxImportManifest keptHistory = await provenance.BuildAsync(
            previous, wordOnly, keptPlan, preview, [keepNodalis],
            retainedOperationId,
            "Imports/Sources/reimport-" + retainedOperationId.ToString("N") + ".docx");
        Assert(keptHistory.Fragments.Single().BaseMarkdown == original &&
               keptHistory.Fragments.Single().LatestWordMarkdownSnapshot?.Replace("\r\n", "\n") == latestWord &&
               keptHistory.Fragments.Single().ReimportResolution == "Nodalis" &&
               keptHistory.Fragments.Single().DestinationSha256 ==
                   DocxImportManifestStore.ComputeSha256(original),
            "Keeping Nodalis must retain its baseline and audit the rejected Word update.");

        DocxReimportTransactionResult onlyProvenance =
            await transaction.CommitWithProvenanceAsync(
                projectDirectory, keptPlan, retainedOperationId,
                DocxReimportTransactionService.Fingerprint(keptPlan),
                new DocxReimportProvenancePackage
                {
                    Manifest = keptHistory,
                    StagedDocxPath = newCopy
                });
        Assert(onlyProvenance.WrittenFiles == 0 &&
               !onlyProvenance.AlreadyApplied &&
               onlyProvenance.ManifestPath is not null &&
               File.Exists(onlyProvenance.ManifestPath) &&
               await File.ReadAllTextAsync(markdownPath) == original,
            "Keeping Nodalis must still version the Word provenance without modifying notes.");

        DocxReimportTransactionResult replayKept =
            await transaction.CommitWithProvenanceAsync(
                projectDirectory, keptPlan, retainedOperationId,
                DocxReimportTransactionService.Fingerprint(keptPlan),
                new DocxReimportProvenancePackage
                {
                    Manifest = keptHistory,
                    StagedDocxPath = newCopy
                });
        Assert(replayKept.AlreadyApplied && replayKept.WrittenFiles == 0,
            "A metadata-only reimport must be idempotent.");

        bool mismatchedHistoryRejected = false;
        try
        {
            await provenance.BuildAsync(previous, wordOnly, mergedPlan, preview,
                [manual], previous.ImportId,
                "Imports/Sources/new-" + Guid.NewGuid().ToString("N") + ".docx");
        }
        catch (InvalidDataException)
        {
            mismatchedHistoryRejected = true;
        }

        Assert(mismatchedHistoryRejected,
            "A reimport history must have a new identity distinct from its parent.");

        await File.WriteAllTextAsync(markdownPath, original + "\nAjout local");
        DocxReimportReview both = await service.BuildAsync(
            projectDirectory, previous, preview);
        Assert(both.Fragments[0].Change?.Kind == DocxReimportChangeKind.BothChanged &&
               both.Fragments[0].RequiresExplicitResolution &&
               File.ReadAllText(markdownPath).Contains("Ajout local", StringComparison.Ordinal),
            "A local edit must remain intact and create a three-way conflict.");

        File.Delete(markdownPath);
        DocxReimportReview missing = await service.BuildAsync(
            projectDirectory, previous, preview);
        Assert(missing.Fragments[0].Change?.Kind ==
                   DocxReimportChangeKind.DestinationMissing &&
               !File.Exists(markdownPath),
            "A removed local file must remain removed after inspection.");

        bool refusedOtherProject = false;
        try
        {
            await service.BuildAsync(projectDirectory,
                previous with { ProjectId = Guid.NewGuid() }, preview);
        }
        catch (InvalidDataException)
        {
            refusedOtherProject = true;
        }

        Assert(refusedOtherProject,
            "Reviewing a history from another project must fail closed.");

        bool refusedEscape = false;
        try
        {
            await service.BuildAsync(projectDirectory,
                previous with
                {
                    Fragments =
                    [
                        fragment with { DestinationRelativePath = "../secret.md" }
                    ]
                }, preview);
        }
        catch (InvalidDataException)
        {
            refusedEscape = true;
        }

        Assert(refusedEscape && File.Exists(oldCopy) && File.Exists(newCopy),
            "A malicious relative destination must not touch preserved source packages.");
    }

    /// <summary>Fails the end-to-end review contract.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
