using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;
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
                cold.Metrics.DocumentCount >= generatedDocuments &&
                cold.Metrics.HashedDocuments >= generatedDocuments &&
                cold.Metrics.ParsedDocuments >= generatedDocuments,
                "A cold index refresh must inspect and parse the synthetic Markdown corpus.");

            LinkIndexRefreshResult warm =
                await service.RefreshWithMetricsAsync();

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
                targeted.Metrics.HashedDocuments == 1 &&
                targeted.Metrics.ParsedDocuments == 1 &&
                targeted.Metrics.ChangedDocuments == 1,
                "Changing one document must invalidate exactly one document payload.");

            Assert(
                targeted.Catalog.References.Count >= generatedDocuments,
                "Incremental refresh must preserve backlinks from unchanged documents.");
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
