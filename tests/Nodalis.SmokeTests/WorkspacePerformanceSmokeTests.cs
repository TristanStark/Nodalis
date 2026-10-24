using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Tasks;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Persistence;

internal static class WorkspacePerformanceSmokeTests
{
    /// <summary>
    /// Verifies algorithmic performance guarantees without relying on fragile wall-clock thresholds.
    /// </summary>
    /// <param name="root">The main smoke-test root used to derive an isolated performance workspace.</param>
    public static async Task RunAsync(
            string root)
    {
        string performanceRoot =
            root + "-IncrementalPerformance";

        try
        {
            FileSystemWorkspaceStore store =
                new FileSystemWorkspaceStore(
                    performanceRoot);
            await store.InitializeAsync(
                "Incremental Performance");

            const int generatedDocuments = 48;

            for (int index = 0;
                 index < generatedDocuments;
                 index++)
            {
                string path = Path.Combine(
                    performanceRoot,
                    $"Document {index:D4}.md");
                string nextName =
                    $"Document {(index + 1) % generatedDocuments:D4}";

                await File.WriteAllTextAsync(
                    path,
                    $"# Document {index:D4}\n\n[[{nextName}]]\n\n- [ ] Tâche {index}\n");
            }

            WorkspaceLinkIndexService service =
                new WorkspaceLinkIndexService(
                    performanceRoot);

            LinkIndexRefreshResult cold =
                await service.RefreshWithMetricsAsync();

            Assert(
                cold.Metrics.IndexFileWritten,
                "Cold index creation must persist a derived JSON snapshot.");

            Assert(
                cold.Metrics.DocumentCount >= generatedDocuments &&
                cold.Metrics.HashedDocuments >= generatedDocuments &&
                cold.Metrics.ParsedDocuments >= generatedDocuments,
                "A cold index refresh must inspect and parse the synthetic Markdown corpus.");

            // Multiple service instances must serialize their atomic derived JSON writes.
            WorkspaceLinkIndexService secondService =
                new WorkspaceLinkIndexService(performanceRoot);
            Task<LinkIndexRefreshResult>[] concurrentRefreshes =
            [
                service.RefreshWithMetricsAsync(),
                secondService.RefreshWithMetricsAsync(),
                service.RefreshWithMetricsAsync(),
                secondService.RefreshWithMetricsAsync()
            ];
            LinkIndexRefreshResult[] sharedResults = await Task.WhenAll(concurrentRefreshes);
            Assert(
                sharedResults.All(result =>
                    result.Metrics.HashedDocuments == 0 &&
                    result.Metrics.ParsedDocuments == 0 &&
                    !result.Metrics.IndexFileWritten),
                "Parallel warm refreshes must not rewrite the same workspace index.");

            using CancellationTokenSource canceled = new CancellationTokenSource();
            canceled.Cancel();
            bool cancellationObserved = false;
            try
            {
                await secondService.RefreshWithMetricsAsync(canceled.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved = true;
            }

            Assert(
                cancellationObserved,
                "Canceled refresh callers must stop without writing the index.");

            // A supplied snapshot must be consumed without a redundant filesystem
            // traversal, even when a new Markdown file appears in the meantime.
            WorkspaceTaskService sharedTasks =
                new WorkspaceTaskService(performanceRoot);
            WorkspaceMilestoneService sharedMilestones =
                new WorkspaceMilestoneService(performanceRoot);
            string lateDocument = Path.Combine(performanceRoot, "Late Addition.md");
            await File.WriteAllTextAsync(lateDocument, "- [ ] Late task\\n");
            IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> snapshotTasks =
                await sharedTasks.GetTasksAsync(
                    null,
                    includeCompleted: true,
                    indexSnapshot: cold.Catalog);
            IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> snapshotMilestones =
                await sharedMilestones.GetMilestonesAsync(
                    null,
                    indexSnapshot: cold.Catalog);
            Assert(
                !snapshotTasks.Any(task => task.Text.Contains("Late task", StringComparison.Ordinal)) &&
                snapshotMilestones.Count == 0,
                "Shared services must use the supplied index snapshot instead of rescanning.");
            File.Delete(lateDocument);

            LinkIndexRefreshResult warm =
                await service.RefreshWithMetricsAsync();

            Assert(
                !warm.Metrics.IndexFileWritten,
                "No-change refresh must not rewrite the derived JSON.");

            Assert(
                warm.Metrics.HashedDocuments == 0 &&
                warm.Metrics.ParsedDocuments == 0 &&
                warm.Metrics.ChangedDocuments == 0,
                "A warm no-change refresh must reuse every document without file hashing or Markdown parsing.");

            string changedPath = Path.Combine(
                performanceRoot,
                "Document 0007.md");

            await File.AppendAllTextAsync(
                changedPath,
                "\nModification ciblée.\n");

            File.SetLastWriteTimeUtc(
                changedPath,
                DateTime.UtcNow.AddSeconds(
                    2));

            LinkIndexRefreshResult targeted =
                await service.RefreshDocumentAsync(
                    changedPath);

            Assert(
                targeted.Metrics.IndexFileWritten,
                "A changed source must persist the updated JSON.");

            Assert(
                targeted.Metrics.HashedDocuments == 1 &&
                targeted.Metrics.ParsedDocuments == 1 &&
                targeted.Metrics.ChangedDocuments == 1,
                "Changing one document must invalidate exactly one document payload.");

            Assert(
                targeted.Catalog.References.Count >= generatedDocuments,
                "Incremental refresh must preserve backlinks from unchanged documents.");

            LinkTargetEntry original = targeted.Catalog.Targets.Single(target =>
                target.RelativePath == "Document 0008.md");
            File.Move(
                Path.Combine(performanceRoot, "Document 0008.md"),
                Path.Combine(performanceRoot, "Renamed Document 0008.md"));
            LinkIndexRefreshResult moved = await service.RefreshWithMetricsAsync();
            LinkTargetEntry afterMove = moved.Catalog.Targets.Single(target =>
                target.RelativePath == "Renamed Document 0008.md");
            Assert(
                original.Id == afterMove.Id &&
                afterMove.Aliases.Contains("Document 0008"),
                "Unique hash lookup must preserve GUID and historic alias across a move.");

            string folderA = Path.Combine(performanceRoot, "Collision A");
            string folderB = Path.Combine(performanceRoot, "Collision B");
            Directory.CreateDirectory(folderA);
            Directory.CreateDirectory(folderB);
            await File.WriteAllTextAsync(Path.Combine(folderA, "Collision.md"), "# A\n");
            await File.WriteAllTextAsync(Path.Combine(folderB, "Collision.md"), "# B\n");
            await File.WriteAllTextAsync(
                Path.Combine(performanceRoot, "Ambiguity.md"),
                "[[Collision]] [[Document 0008]] [[missing-target]]\n");
            LinkIndexRefreshResult collisions = await service.RefreshWithMetricsAsync();
            foreach (LinkReferenceEntry reference in collisions.Catalog.References)
            {
                LinkResolution expectedResolution = WorkspaceLinkIndexService.Resolve(
                    collisions.Catalog,
                    reference.RawTarget);
                Guid? expectedId = expectedResolution.Status == LinkResolutionStatus.Resolved
                    ? expectedResolution.Target!.Id
                    : null;
                Assert(
                    reference.TargetId == expectedId,
                    "Batch resolver must match legacy resolver for cached and parsed backlinks.");
            }

            LinkTargetEntry ambiguity = collisions.Catalog.Targets.Single(target =>
                target.RelativePath == "Ambiguity.md");
            Assert(
                collisions.Catalog.References.Any(reference =>
                    reference.SourceId == ambiguity.Id &&
                    reference.RawTarget == "Collision" &&
                    reference.TargetId is null),
                "Duplicate display names must remain ambiguous.");

        }
        finally
        {
            if (Directory.Exists(
                    performanceRoot))
            {
                Directory.Delete(
                    performanceRoot,
                    recursive: true);
            }
        }
    }

    /// <summary>
    /// Throws when a deterministic performance property is violated.
    /// </summary>
    /// <param name="condition">The property to verify.</param>
    /// <param name="message">The failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
