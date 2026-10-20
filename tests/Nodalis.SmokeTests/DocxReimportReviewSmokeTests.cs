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

        // The approval coordinator deliberately reopens immutable provenance
        // from disk rather than trusting a history object passed by the UI.
        await store.SaveNewAsync(projectDirectory, previous);

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

        // A filtered initial DOCX import must not silently pull originally
        // excluded paragraphs back in when the entire new Word is rendered.
        DocxImportManifest partial = previous with
        {
            Fragments =
            [
                fragment with { OriginalSourceBlockCount = 3 }
            ]
        };
        DocxReimportReview partialReview = await service.BuildAsync(
            projectDirectory, partial, preview);
        DocxReimportDecisionPlan partialPlan = DocxReimportDecisionPlanner.Build(
            partialReview, partial, [new DocxReimportResolution
            {
                FragmentId = fragment.FragmentId,
                Action = DocxReimportResolutionAction.KeepNodalis
            }]);
        Assert(partialReview.Warnings.Any(warning =>
                   warning.Contains("partiellement", StringComparison.Ordinal)) &&
               !partialPlan.ReviewComplete &&
               partialPlan.Writes.Count == 0,
            "A historical partial import must block writes until selection is reviewed.");

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

        // Complete approval workflow: fresh persisted history, immutable
        // approval, stale Markdown refusal and explicit committed provenance.
        DocxReimportApprovalService approvals =
            new DocxReimportApprovalService(root);
        DocxReimportApproval approved = await approvals.PrepareAsync(
            projectDirectory, previous.ImportId, preview, [], [useWord], []);
        Assert(approved.Plan.ReviewComplete &&
               approved.Plan.Writes.Count == 1 &&
               approved.PreviousImportId == previous.ImportId &&
               approved.ApprovedPlanFingerprint ==
                   DocxReimportTransactionService.Fingerprint(approved.Plan) &&
               await File.ReadAllTextAsync(markdownPath) == original,
            "Preparing a DOCX approval must be a no-write operation.");

        await File.WriteAllTextAsync(markdownPath,
            original + "\n\nModification concurrente Nodalis");
        bool staleApprovalRejected = false;
        try
        {
            await approvals.ApplyAsync(approved, preview);
        }
        catch (InvalidDataException)
        {
            staleApprovalRejected = true;
        }

        Assert(staleApprovalRejected &&
               (await File.ReadAllTextAsync(markdownPath)).Contains(
                   "Modification concurrente Nodalis", StringComparison.Ordinal) &&
               !File.Exists(store.GetManifestPath(projectDirectory,
                   approved.OperationId)),
            "Any Nodalis edit after approval must reject the write and preserve local notes.");

        await File.WriteAllTextAsync(markdownPath, original);
        DocxReimportTransactionResult appliedFromApproval =
            await approvals.ApplyAsync(approved, preview);
        Assert(!appliedFromApproval.AlreadyApplied &&
               appliedFromApproval.ManifestPath is not null &&
               File.Exists(appliedFromApproval.ManifestPath) &&
               (await File.ReadAllTextAsync(markdownPath)).Replace(
                   "\r\n", "\n") == latestWord &&
               (await store.LoadAsync(appliedFromApproval.ManifestPath))
                   .PreviousImportId == previous.ImportId,
            "A freshly revalidated approval must commit Word provenance and Markdown.");

        DocxReimportTransactionResult approvalReplay =
            await approvals.ApplyAsync(approved, preview);
        Assert(approvalReplay.AlreadyApplied &&
               approvalReplay.WrittenFiles == 1,
            "A repeated approved operation must be idempotent.");

        await File.WriteAllTextAsync(markdownPath, original);
        DocxReimportApproval stagedApproval = await approvals.PrepareAsync(
            projectDirectory, previous.ImportId, preview, [],
            [useWord], []);
        await File.AppendAllTextAsync(newCopy, " modified after approval");
        bool sourceChangedRejected = false;
        try
        {
            await approvals.ApplyAsync(stagedApproval, preview);
        }
        catch (InvalidDataException)
        {
            sourceChangedRejected = true;
        }

        Assert(sourceChangedRejected &&
               await File.ReadAllTextAsync(markdownPath) == original &&
               !File.Exists(store.GetManifestPath(projectDirectory,
                   stagedApproval.OperationId)),
            "A staged Word modified after explicit approval must block the reimport.");
        await File.WriteAllTextAsync(newCopy, "Updated DOCX package fixture");

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

        // A newly introduced Word section can be explicitly assigned to
        // an existing project folder and created without any overwrite.
        DocxBlock recipeHeading = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = "Recette", HeadingLevel = 1 }
        };
        DocxBlock recipeParagraph = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = "Vérifier la recette" }
        };
        DocxImportPreview withRecipe = preview with
        {
            StagedImport = preview.StagedImport with
            {
                Document = new ParsedDocxDocument
                {
                    Blocks = [heading, paragraph, recipeHeading, recipeParagraph]
                }
            },
            Sections =
            [
                preview.Sections[0],
                new DocxImportSectionPreview
                {
                    Index = 1,
                    HeadingBlockIndex = 2,
                    SourceHeading = "Recette",
                    SuggestedTargetSection = "Recette",
                    BlockCount = 1,
                    MarkdownPreview = "## Recette\\n\\nVérifier la recette",
                    Blocks =
                    [
                        new DocxImportBlockPreview
                        {
                            BlockIndex = 3,
                            Block = recipeParagraph,
                            KindLabel = "Paragraphe",
                            DisplayText = "Vérifier la recette",
                            MarkdownPreview = "Vérifier la recette"
                        }
                    ]
                }
            ]
        };
        string recipeFolder = Path.Combine(projectDirectory, "Recette");
        Directory.CreateDirectory(recipeFolder);
        DocxReimportNewSectionAssignment recipeChoice =
            new DocxReimportNewSectionAssignment
            {
                SectionIndex = 1,
                TargetFolderRelativePath = store.ToWorkspaceRelativePath(recipeFolder)
            };
        DocxReimportReview recipeReview = await service.BuildAsync(
            projectDirectory, previous, withRecipe);
        DocxReimportDecisionPlan recipePlan = DocxReimportDecisionPlanner.Build(
            recipeReview, previous, [useWord], [], withRecipe, [recipeChoice]);
        Assert(recipePlan.ReviewComplete &&
               recipePlan.NewSectionCreations.Count == 1 &&
               recipePlan.NewSectionCreations[0].SectionIndex == 1 &&
               recipePlan.UnmatchedNewSections.Count == 0 &&
               !File.Exists(store.ResolveWorkspaceRelativePath(
                   recipePlan.NewSectionCreations[0].DestinationRelativePath)),
            "An explicitly assigned Word section must be planned as a new file, without writes.");

        bool unsafePlacementRejected = false;
        try
        {
            DocxReimportDecisionPlanner.Build(recipeReview, previous,
                [useWord], [], withRecipe,
                [
                    recipeChoice with
                    {
                        TargetFolderRelativePath = "../hors-workspace"
                    }
                ]);
        }
        catch (InvalidDataException)
        {
            unsafePlacementRejected = true;
        }

        bool duplicatePlacementRejected = false;
        try
        {
            DocxReimportDecisionPlanner.Build(recipeReview, previous,
                [useWord], [], withRecipe, [recipeChoice, recipeChoice]);
        }
        catch (InvalidDataException)
        {
            duplicatePlacementRejected = true;
        }

        bool excludedAndAssignedRejected = false;
        try
        {
            DocxReimportDecisionPlanner.Build(recipeReview, previous,
                [useWord], [1], withRecipe, [recipeChoice]);
        }
        catch (InvalidDataException)
        {
            excludedAndAssignedRejected = true;
        }

        Assert(unsafePlacementRejected && duplicatePlacementRejected &&
               excludedAndAssignedRejected,
            "New Word sections cannot escape the project, repeat an assignment or be both imported and excluded.");

        DocxReimportApproval recipeApproval = await approvals.PrepareAsync(
            projectDirectory, previous.ImportId, withRecipe,
            [], [useWord], [], newSectionAssignments: [recipeChoice]);
        string createdRecipePath = store.ResolveWorkspaceRelativePath(
            recipeApproval.Plan.NewSectionCreations.Single().DestinationRelativePath);
        Assert(!File.Exists(createdRecipePath) &&
               recipeApproval.Plan.NewSectionCreations.Count == 1,
            "A prepared new-section approval must not create a Markdown file.");

        // An unrelated writer occupying the approved new destination must
        // block the transaction; the new file must NEVER be overwritten.
        await File.WriteAllTextAsync(createdRecipePath,
            "Recette créée entre préparation et validation");
        bool collidedCreationRejected = false;
        try
        {
            await approvals.ApplyAsync(recipeApproval, withRecipe);
        }
        catch (InvalidDataException)
        {
            collidedCreationRejected = true;
        }

        Assert(collidedCreationRejected &&
               await File.ReadAllTextAsync(createdRecipePath) ==
                   "Recette créée entre préparation et validation" &&
               await File.ReadAllTextAsync(markdownPath) == original,
            "A new Word destination occupied after approval must fail closed.");
        File.Delete(createdRecipePath);

        DocxReimportTransactionResult recipeCommit =
            await approvals.ApplyAsync(recipeApproval, withRecipe);
        Assert(recipeCommit.ManifestPath is not null &&
               File.Exists(createdRecipePath) &&
               (await File.ReadAllTextAsync(createdRecipePath)).Contains(
                   "Vérifier la recette", StringComparison.Ordinal) &&
               (await store.LoadAsync(recipeCommit.ManifestPath)).Fragments.Count == 2 &&
               (await store.LoadAsync(recipeCommit.ManifestPath)).Fragments.Any(item =>
                   item.SourceSectionIndex == 1 &&
                   item.DestinationRelativePath ==
                       recipeApproval.Plan.NewSectionCreations[0].DestinationRelativePath),
            "Committing an assigned Word section must create a tracked Markdown and provenance entry.");
        DocxImportManifest recipeManifest =
            await store.LoadAsync(recipeCommit.ManifestPath!);
        DocxReimportReview nextGeneration = await service.BuildAsync(
            projectDirectory, recipeManifest, withRecipe);
        Assert(nextGeneration.Fragments.Count == 2 &&
               nextGeneration.Fragments.All(item =>
                   item.Change?.Kind == DocxReimportChangeKind.Unchanged),
            "The newly imported Word section must be unchanged in the next generation's diff.");

        DocxReimportTransactionResult recipeReplay =
            await approvals.ApplyAsync(recipeApproval, withRecipe);
        Assert(recipeReplay.AlreadyApplied,
            "A new-section creation transaction must replay idempotently.");

        await File.WriteAllTextAsync(markdownPath, original);
        DocxReimportNewSectionAssignment rootChoice =
            new DocxReimportNewSectionAssignment
            {
                SectionIndex = 1,
                TargetFolderRelativePath =
                    store.ToWorkspaceRelativePath(projectDirectory)
            };
        DocxReimportApproval rollbackApproval = await approvals.PrepareAsync(
            projectDirectory, previous.ImportId, withRecipe,
            [], [useWord], [], newSectionAssignments: [rootChoice]);
        DocxReimportProvenanceBuilder rollbackProvenance =
            new DocxReimportProvenanceBuilder(root);
        DocxReimportReview rollbackReview = await service.BuildAsync(
            projectDirectory, previous, withRecipe);
        DocxImportManifest rollbackHistory = await rollbackProvenance.BuildAsync(
            previous, rollbackReview, rollbackApproval.Plan, withRecipe, [useWord],
            rollbackApproval.OperationId,
            "Imports/Sources/reimport-" +
            rollbackApproval.OperationId.ToString("N") + ".docx");
        DocxReimportTransactionService failsAfterNewSection =
            new DocxReimportTransactionService(root, checkpoint =>
            {
                if (checkpoint == 2)
                {
                    throw new InvalidOperationException(
                        "Injected failure after creating the new Word section");
                }
            });
        bool newSectionRolledBack = false;
        try
        {
            await failsAfterNewSection.CommitWithProvenanceAsync(
                projectDirectory, rollbackApproval.Plan,
                rollbackApproval.OperationId,
                rollbackApproval.ApprovedPlanFingerprint,
                new DocxReimportProvenancePackage
                {
                    Manifest = rollbackHistory,
                    StagedDocxPath = newCopy
                });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Injected", StringComparison.Ordinal))
        {
            newSectionRolledBack = true;
        }

        Assert(newSectionRolledBack &&
               await File.ReadAllTextAsync(markdownPath) == original &&
               !File.Exists(store.ResolveWorkspaceRelativePath(
                   rollbackApproval.Plan.NewSectionCreations[0].DestinationRelativePath)) &&
               !File.Exists(store.ResolveWorkspaceRelativePath(
                   rollbackHistory.SourceCopyRelativePath)) &&
               !File.Exists(store.GetManifestPath(projectDirectory,
                   rollbackApproval.OperationId)),
            "A failed new-section transaction must remove the created Markdown and Word provenance.");

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
