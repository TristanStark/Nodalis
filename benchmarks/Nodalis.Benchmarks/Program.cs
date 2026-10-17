using System.Diagnostics;
using System.Text.Json;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Search;

namespace Nodalis.Benchmarks;

internal static class Program
{
    /// <summary>
    /// Generates a dependency-free synthetic workspace and prints reproducible benchmark measurements as JSON.
    /// </summary>
    /// <param name="args">Optional --documents N argument.</param>
    /// <returns>Zero when the benchmark completes.</returns>
    public static async Task<int> Main(
            string[] args)
    {
        int documentCount = ReadDocumentCount(
            args);
        string root = Path.Combine(
            Path.GetTempPath(),
            "Nodalis-Benchmark-" +
            Guid.NewGuid().ToString(
                "N"));

        try
        {
            FileSystemWorkspaceStore store =
                new FileSystemWorkspaceStore(
                    root);
            await store.InitializeAsync(
                "Nodalis Performance Benchmark");

            await GenerateDocumentsAsync(
                root,
                documentCount);

            WorkspaceNavigationBuilder navigationBuilder =
                new WorkspaceNavigationBuilder();
            Stopwatch navigationWatch = Stopwatch.StartNew();
            await navigationBuilder.BuildAsync(
                root);
            navigationWatch.Stop();

            WorkspaceLinkIndexService indexService =
                new WorkspaceLinkIndexService(
                    root);

            LinkIndexRefreshResult cold =
                await indexService.RefreshWithMetricsAsync();
            LinkIndexRefreshResult warm =
                await indexService.RefreshWithMetricsAsync();

            string changedPath = Path.Combine(
                root,
                "Document 000001.md");

            await File.AppendAllTextAsync(
                changedPath,
                "\nbenchmark single-change needle\n");
            File.SetLastWriteTimeUtc(
                changedPath,
                DateTime.UtcNow.AddSeconds(
                    2));

            LinkIndexRefreshResult singleChange =
                await indexService.RefreshDocumentAsync(
                    changedPath);

            WorkspaceSearchService search =
                new WorkspaceSearchService();
            Stopwatch searchWatch = Stopwatch.StartNew();
            await search.SearchAsync(
                root,
                contextPath: null,
                "needle");
            searchWatch.Stop();

            WorkspaceIntegrityDiagnosticService integrity =
                new WorkspaceIntegrityDiagnosticService(
                    root);
            Stopwatch integrityWatch = Stopwatch.StartNew();
            await integrity.ScanAsync();
            integrityWatch.Stop();

            object report = new
            {
                generatedUtc =
                    DateTimeOffset.UtcNow,
                documents =
                    documentCount,
                navigationMilliseconds =
                    navigationWatch.ElapsedMilliseconds,
                coldIndex =
                    cold.Metrics,
                warmIndex =
                    warm.Metrics,
                singleChangeIndex =
                    singleChange.Metrics,
                searchMilliseconds =
                    searchWatch.ElapsedMilliseconds,
                integrityDiagnosticMilliseconds =
                    integrityWatch.ElapsedMilliseconds
            };

            Console.WriteLine(
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions
                    {
                        WriteIndented =
                            true
                    }));

            return 0;
        }
        finally
        {
            if (Directory.Exists(
                    root))
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
        }
    }

    /// <summary>
    /// Generates deterministic Markdown files with links, tasks and searchable text.
    /// </summary>
    /// <param name="root">The workspace root.</param>
    /// <param name="documentCount">The number of documents to generate.</param>
    private static async Task GenerateDocumentsAsync(
            string root,
            int documentCount)
    {
        for (int index = 0;
             index < documentCount;
             index++)
        {
            string nextName =
                $"Document {(index + 1) % documentCount:D6}";
            string content =
                $"# Document {index:D6}\n\n" +
                $"[[{nextName}]]\n\n" +
                $"- [ ] Tâche benchmark {index} | Échéance: 2030-01-01\n\n" +
                (index % 97 == 0
                    ? "needle benchmark token\n"
                    : "contenu benchmark reproductible\n");

            await File.WriteAllTextAsync(
                Path.Combine(
                    root,
                    $"Document {index:D6}.md"),
                content);
        }
    }

    /// <summary>
    /// Reads the optional document-count argument.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The validated document count.</returns>
    private static int ReadDocumentCount(
            IReadOnlyList<string> args)
    {
        for (int index = 0;
             index < args.Count - 1;
             index++)
        {
            if (string.Equals(
                    args[index],
                    "--documents",
                    StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(
                    args[index + 1],
                    out int parsed) &&
                parsed >= 10 &&
                parsed <= 100000)
            {
                return parsed;
            }
        }

        return 5000;
    }
}
