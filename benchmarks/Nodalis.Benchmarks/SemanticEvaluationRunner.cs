using System.Diagnostics;
using System.Text.Json;
using Nodalis.Core.Search;
using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Search;
using Nodalis.Infrastructure.Semantic;

namespace Nodalis.Benchmarks;

/// <summary>
/// Reproducible French relevance benchmark. Runs only on a disposable corpus.
/// A real local embedding process is never started without explicit CLI approval.
/// </summary>
internal static class SemanticEvaluationRunner
{
    private sealed record Corpus
    {
        public string Description { get; init; } = "";
        public List<CorpusDocument> Documents { get; init; } = [];
        public List<CorpusQuestion> Questions { get; init; } = [];
    }

    private sealed record CorpusDocument
    {
        public Guid Id { get; init; }
        public string RelativePath { get; init; } = "";
        public string Markdown { get; init; } = "";
    }

    private sealed record CorpusQuestion
    {
        public string Name { get; init; } = "";
        public string Query { get; init; } = "";
        public List<Guid> RelevantDocumentIds { get; init; } = [];
    }

    /// <summary>
    /// Copies a selected JSON corpus to a disposable workspace, runs historical
    /// fuzzy search, and optionally runs real local vector + hybrid evaluation.
    /// </summary>
    /// <param name="args">--semantic-eval with optional --corpus-file and explicit local-model options.</param>
    /// <returns>Exit code; no private workspace data is emitted in the report.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        string? corpusArgument = GetOption(args, "--corpus-file");
        string corpusPath = corpusArgument ?? Path.Combine(
            AppContext.BaseDirectory, "semantic-fr-corpus.json");
        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine("Semantic benchmark: missing JSON corpus.");
            return 2;
        }

        Corpus corpus;
        try
        {
            string json = await File.ReadAllTextAsync(corpusPath);
            corpus = JsonSerializer.Deserialize<Corpus>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing corpus data.");
            ValidateCorpus(corpus);
        }
        catch (Exception error) when (error is JsonException or
            InvalidDataException or ArgumentException or IOException)
        {
            Console.Error.WriteLine("Semantic benchmark corpus rejected: " + error.Message);
            return 2;
        }

        bool approved = args.Contains("--approve-local", StringComparer.OrdinalIgnoreCase);
        string? adapter = GetOption(args, "--adapter");
        string? model = GetOption(args, "--model");
        string? rawDimensions = GetOption(args, "--dimensions");
        bool configured = adapter is not null || model is not null ||
            rawDimensions is not null || approved;
        if (configured && (!approved || string.IsNullOrWhiteSpace(adapter) ||
            string.IsNullOrWhiteSpace(model) ||
            !int.TryParse(rawDimensions, out int dimensions) ||
            dimensions is < 1 or > 4096))
        {
            Console.Error.WriteLine(
                "Real semantic evaluation requires --approve-local --adapter ABSOLUTE_EXE " +
                "--model EMBEDDING_MODEL --dimensions N. Otherwise run lexical-only.");
            return 2;
        }

        string root = Path.Combine(Path.GetTempPath(),
            "Nodalis-SemanticEvaluation-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (CorpusDocument document in corpus.Documents)
            {
                string file = Path.Combine(root, document.RelativePath.Replace('/',
                    Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, document.Markdown);
            }

            Dictionary<string, CorpusDocument> paths = corpus.Documents.ToDictionary(
                document => document.RelativePath, StringComparer.OrdinalIgnoreCase);
            WorkspaceSearchService lexicalService = new();
            SemanticWorkspaceSearchService? semanticService = null;
            SemanticIndexSummary? firstIndex = null;
            SemanticIndexSummary? secondIndex = null;
            SemanticIndexStatus? status = null;
            double coldIndexMs = 0;
            double warmIndexMs = 0;
            if (configured)
            {
                int dimensions = int.Parse(rawDimensions!,
                    System.Globalization.CultureInfo.InvariantCulture);
                SemanticSearchPreferences preferences = new()
                {
                    IsEnabled = true,
                    ExecutablePath = Path.GetFullPath(adapter!),
                    ModelId = model!,
                    Dimensions = dimensions,
                    MaxBatchSize = 16,
                    TimeoutSeconds = 120
                };
                semanticService = new SemanticWorkspaceSearchService(root,
                    preferences, new ProcessEmbeddingProvider(preferences),
                    cacheDirectory: Path.Combine(root, ".derived-cache"));
                SemanticSource[] sources = corpus.Documents.Select(document =>
                    new SemanticSource(document.Id, document.RelativePath)).ToArray();
                Stopwatch timer = Stopwatch.StartNew();
                firstIndex = await semanticService.IndexAsync(sources, approved: true);
                timer.Stop();
                coldIndexMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                secondIndex = await semanticService.IndexAsync(sources, approved: true);
                timer.Stop();
                warmIndexMs = timer.Elapsed.TotalMilliseconds;
                status = await semanticService.InspectAsync();
                if (secondIndex.EmbeddedChunks != 0)
                    throw new InvalidDataException(
                        "Warm index must not recompute unchanged corpus passages.");
            }

            List<SearchEvaluationCase> lexicalCases = [];
            List<SearchEvaluationCase> vectorCases = [];
            List<SearchEvaluationCase> hybridCases = [];
            foreach (CorpusQuestion question in corpus.Questions)
            {
                Stopwatch timer = Stopwatch.StartNew();
                SearchResultSet sourceResults = await lexicalService.SearchAsync(root,
                    null, question.Query, SearchMatchMode.Fuzzy);
                HybridSearchCandidate[] lexical = sourceResults.Project
                    .Concat(sourceResults.Application).Concat(sourceResults.Global)
                    .OrderByDescending(hit => hit.Score)
                    .ThenBy(hit => hit.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Where(hit => paths.ContainsKey(Path.GetRelativePath(root,
                        hit.FilePath).Replace('\\', '/')))
                    .DistinctBy(hit => paths[Path.GetRelativePath(root,
                        hit.FilePath).Replace('\\', '/')].Id)
                    .Select(hit =>
                    {
                        string relative = Path.GetRelativePath(root, hit.FilePath)
                            .Replace('\\', '/');
                        bool exact = hit.MatchKind == SearchMatchKind.FileName &&
                            SearchTextScorer.TryScore(question.Query, hit.DisplayName,
                                SearchMatchMode.Exact, out double _);
                        return new HybridSearchCandidate(paths[relative].Id, relative,
                            hit.LineNumber, null, hit.Excerpt, IsExactIdentifier: exact);
                    }).ToArray();
                timer.Stop();
                double lexicalMilliseconds = timer.Elapsed.TotalMilliseconds;
                lexicalCases.Add(new SearchEvaluationCase(question.Name,
                    question.RelevantDocumentIds,
                    lexical.Select(hit => hit.DocumentId).ToArray(),
                    (long)Math.Ceiling(lexicalMilliseconds)));

                if (semanticService is null) continue;
                timer.Restart();
                IReadOnlyList<SemanticSearchResult> found =
                    await semanticService.SearchAsync(question.Query, approved: true);
                timer.Stop();
                double semanticMilliseconds = timer.Elapsed.TotalMilliseconds;
                HybridSearchCandidate[] semantic = found.Select(hit =>
                    new HybridSearchCandidate(hit.DocumentId, hit.RelativePath,
                        hit.SourceLine, hit.Heading, hit.Excerpt,
                        Similarity: hit.Similarity)).ToArray();
                vectorCases.Add(new SearchEvaluationCase(question.Name,
                    question.RelevantDocumentIds, found.Select(hit => hit.DocumentId).ToArray(),
                    (long)Math.Ceiling(semanticMilliseconds)));
                timer.Restart();
                IReadOnlyList<HybridRankedHit> fused =
                    HybridSearchRanker.Fuse(lexical, semantic);
                timer.Stop();
                hybridCases.Add(new SearchEvaluationCase(question.Name,
                    question.RelevantDocumentIds,
                    fused.Select(hit => hit.Candidate.DocumentId).ToArray(),
                    (long)Math.Ceiling(lexicalMilliseconds +
                        semanticMilliseconds + timer.Elapsed.TotalMilliseconds)));
            }

            object output = new
            {
                benchmark = "Nodalis 1.8 search modes",
                corpus = Path.GetFileName(corpusPath),
                description = corpus.Description,
                documents = corpus.Documents.Count,
                queries = corpus.Questions.Count,
                model = configured ? model : null,
                realEmbeddingProvider = configured,
                lexical = SearchEvaluationMetrics.Evaluate(lexicalCases),
                semantic = semanticService is null ? null :
                    SearchEvaluationMetrics.Evaluate(vectorCases),
                hybrid = semanticService is null ? null :
                    SearchEvaluationMetrics.Evaluate(hybridCases),
                indexing = new
                {
                    coldElapsedMs = coldIndexMs,
                    warmElapsedMs = warmIndexMs,
                    cold = firstIndex,
                    warm = secondIndex,
                    localIndex = status
                },
                memory = new
                {
                    processWorkingSetBytes = Environment.WorkingSet,
                    processPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                    managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
                    externalModelMemoryMeasured = false
                }
            };
            Console.WriteLine(JsonSerializer.Serialize(output,
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Reads an optional string argument without interpreting any network endpoint.</summary>
    /// <param name="args">Raw CLI arguments.</param>
    /// <param name="name">Argument switch including leading dashes.</param>
    /// <returns>Value following the switch, or null.</returns>
    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (int index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    /// <summary>Rejects unsafe paths, duplicate identities and unannotated datasets.</summary>
    /// <param name="corpus">Deserialized synthetic benchmark corpus.</param>
    private static void ValidateCorpus(Corpus corpus)
    {
        if (corpus.Documents.Count is < 2 or > 1000 ||
            corpus.Questions.Count is < 2 or > 200 ||
            corpus.Documents.Any(document => document.Id == Guid.Empty ||
                !SemanticMarkdownChunker.IsEligiblePath(document.RelativePath) ||
                document.Markdown.Length is < 1 or > 100_000) ||
            corpus.Documents.DistinctBy(document => document.Id).Count() !=
                corpus.Documents.Count ||
            corpus.Documents.DistinctBy(document => document.RelativePath,
                StringComparer.OrdinalIgnoreCase).Count() != corpus.Documents.Count)
            throw new InvalidDataException("Duplicate/unsafe or excessive corpus documents.");

        HashSet<Guid> ids = corpus.Documents.Select(document => document.Id).ToHashSet();
        if (corpus.Questions.DistinctBy(question => question.Name,
                StringComparer.OrdinalIgnoreCase).Count() != corpus.Questions.Count ||
            corpus.Questions.Any(question => string.IsNullOrWhiteSpace(question.Name) ||
                string.IsNullOrWhiteSpace(question.Query) || question.Query.Length > 8192 ||
                question.RelevantDocumentIds.Count == 0 ||
                question.RelevantDocumentIds.Any(id => !ids.Contains(id))))
            throw new InvalidDataException("Queries must have distinct names and annotated GUIDs.");
    }
}
