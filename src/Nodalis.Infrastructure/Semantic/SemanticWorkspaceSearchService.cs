using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Links;
using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.AI;

namespace Nodalis.Infrastructure.Semantic;

/// <summary>A selected canonical Markdown source for an explicit indexing operation.</summary>
public sealed record SemanticSource(Guid DocumentId, string RelativePath);

/// <summary>Progress over approved eligible canonical Markdown sources.</summary>
public sealed record SemanticIndexProgress(int Completed, int Total, int EmbeddedChunks);

/// <summary>Outcome of an explicit indexing operation, not implicit background work.</summary>
public sealed record SemanticIndexSummary(int DocumentsVisited, int DocumentsChanged,
    int EmbeddedChunks, bool WasApproved);

/// <summary>Search result with a fresh source excerpt and numeric similarity.</summary>
public sealed record SemanticSearchResult(Guid DocumentId, string RelativePath,
    string? Heading, int SourceLine, string Excerpt, double Similarity);

/// <summary>
/// Coordinates explicit offline indexing and vector retrieval without ever
/// persisting full source text outside canonical Markdown.
/// </summary>
public sealed class SemanticWorkspaceSearchService
{
    private const long MaxSourceBytes = 8_000_000;
    private readonly string _workspaceRoot;
    private readonly SemanticSearchPreferences _preferences;
    private readonly SemanticChunkingOptions _chunking;
    private readonly EmbeddingExecutionService _execution;
    private readonly SemanticVectorCacheService _cache;
    private readonly string _policyKey;

    /// <summary>Creates an inert service without reading any documents or invoking the provider.</summary>
    /// <param name="workspaceRoot">Existing canonical workspace root.</param>
    /// <param name="preferences">Independent semantic opt-in settings.</param>
    /// <param name="provider">Locally configured or simulated embeddings provider.</param>
    /// <param name="chunking">Canonical Markdown passage policy.</param>
    /// <param name="cacheDirectory">Optional local derived cache folder for tests.</param>
    public SemanticWorkspaceSearchService(string workspaceRoot,
        SemanticSearchPreferences preferences, IEmbeddingProvider provider,
        SemanticChunkingOptions? chunking = null, string? cacheDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(provider);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _preferences = preferences;
        _chunking = chunking ?? new();
        _execution = new EmbeddingExecutionService(preferences, provider);
        _cache = new SemanticVectorCacheService(workspaceRoot, cacheDirectory);
        _policyKey = CreatePolicyKey(_chunking);
    }

    /// <summary>Human-visible location of the entirely disposable derived index.</summary>
    public string CachePath => _cache.CachePath;

    /// <summary>Inspects model compatibility, vector counts, disk size and last update without running embeddings.</summary>
    /// <param name="token">Cancellation of local file reading.</param>
    /// <returns>Read-only derived index metadata for user controls.</returns>
    public Task<SemanticIndexStatus> InspectAsync(CancellationToken token = default) =>
        _cache.InspectAsync(_preferences.ModelId, _preferences.Dimensions, _policyKey, token);

    /// <summary>
    /// Discards vectors for removed canonical documents only after explicit
    /// consent and against the full catalog, not a selected project subset.
    /// </summary>
    /// <param name="allEligibleDocumentIds">Current full set of indexable canonical GUIDs.</param>
    /// <param name="approved">Whether the user approved this indexing operation.</param>
    /// <param name="token">Cancellation request.</param>
    /// <returns>Number of stale vector fragments removed from disk.</returns>
    public Task<int> PruneRemovedDocumentsAsync(IReadOnlySet<Guid> allEligibleDocumentIds,
        bool approved, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(allEligibleDocumentIds);
        if (!_preferences.IsEnabled || !approved) return Task.FromResult(0);
        return _cache.PruneUnlistedDocumentsAsync(_preferences.ModelId,
            _preferences.Dimensions, _policyKey, allEligibleDocumentIds, token);
    }

    /// <summary>
    /// Indexes only the explicitly approved selected documents. Unchanged chunks
    /// reuse their vectors; modified or deleted files replace their own entries.
    /// </summary>
    /// <param name="sources">Exact selected canonical document list.</param>
    /// <param name="approved">Specific user confirmation of indexing scope.</param>
    /// <param name="progress">Optional progress of completed source files.</param>
    /// <param name="token">Cancellation request.</param>
    /// <returns>Counts of visited documents and computed embeddings.</returns>
    public async Task<SemanticIndexSummary> IndexAsync(
        IReadOnlyList<SemanticSource> sources, bool approved,
        IProgress<SemanticIndexProgress>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!_preferences.IsEnabled || !approved)
            return new SemanticIndexSummary(0, 0, 0, false);

        if (_preferences.MaxBatchSize is < 1 or > 32 ||
            _preferences.Dimensions is < 1 or > 4096)
        {
            throw new EmbeddingProviderException(EmbeddingFailureKind.InvalidConfiguration,
                "Le modèle d'embeddings et ses limites doivent être configurés.");
        }

        SemanticVectorIndex index = await _cache.LoadAsync(_preferences.ModelId,
            _preferences.Dimensions, _policyKey, token);
        List<SemanticSource> distinct = sources
            .Where(source => source.DocumentId != Guid.Empty)
            .DistinctBy(source => source.DocumentId).ToList();

        int visited = 0, changed = 0, embedded = 0;
        foreach (SemanticSource source in distinct)
        {
            token.ThrowIfCancellationRequested();
            visited++;
            string? path = SafePath(source.RelativePath);
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > MaxSourceBytes)
            {
                index = await _cache.ReplaceDocumentAsync(_preferences.ModelId,
                    _preferences.Dimensions, _policyKey, source.DocumentId, [], token);
                changed++;
                progress?.Report(new SemanticIndexProgress(visited, distinct.Count, embedded));
                continue;
            }

            string markdown = await File.ReadAllTextAsync(path, token);
            IReadOnlyList<SemanticChunk> chunks = await Task.Run(
                () => SemanticMarkdownChunker.Split(
                    source.DocumentId, source.RelativePath, markdown, _chunking), token);
            token.ThrowIfCancellationRequested();
            Dictionary<string, SemanticVectorEntry> old = index.Entries
                .Where(entry => entry.DocumentId == source.DocumentId)
                .ToDictionary(entry => entry.ChunkId, StringComparer.Ordinal);
            // Fail before invoking the model when this approved document cannot fit
            // in the derived index. Earlier fully committed documents remain valid.
            if (index.Entries.Count - old.Count + chunks.Count >
                SemanticVectorCacheService.MaximumEntries)
                throw new InvalidDataException("Limite de 10 000 fragments : réduisez le périmètre.");
            List<SemanticVectorEntry> replacement = [];
            List<SemanticChunk> pending = [];
            foreach (SemanticChunk chunk in chunks)
            {
                if (old.TryGetValue(chunk.ChunkId, out SemanticVectorEntry? entry) &&
                    entry.ContentHash == chunk.ContentHash)
                {
                    replacement.Add(entry with
                    {
                        RelativePath = chunk.RelativePath,
                        Heading = chunk.Heading,
                        SourceLine = chunk.SourceLine
                    });
                }
                else
                {
                    pending.Add(chunk);
                }
            }

            bool equal = pending.Count == 0 &&
                replacement.Count == old.Count &&
                replacement.All(entry =>
                    old.TryGetValue(entry.ChunkId, out SemanticVectorEntry? previous) &&
                    string.Equals(previous.RelativePath, entry.RelativePath,
                        StringComparison.Ordinal) &&
                    previous.SourceLine == entry.SourceLine &&
                    previous.Heading == entry.Heading);
            if (equal)
            {
                progress?.Report(new SemanticIndexProgress(visited, distinct.Count, embedded));
                continue;
            }

            for (int offset = 0; offset < pending.Count; offset += _preferences.MaxBatchSize)
            {
                token.ThrowIfCancellationRequested();
                SemanticChunk[] batch = pending.Skip(offset)
                    .Take(_preferences.MaxBatchSize).ToArray();
                EmbeddingBatch? vectors = await _execution.EmbedApprovedAsync(
                    batch.Select(chunk => chunk.Content).ToArray(), approved, token);
                if (vectors is null || vectors.Vectors.Count != batch.Length)
                {
                    throw new EmbeddingProviderException(EmbeddingFailureKind.InvalidResponse,
                        "Lot de vecteurs local incomplet.");
                }

                for (int i = 0; i < batch.Length; i++)
                {
                    replacement.Add(new SemanticVectorEntry
                    {
                        DocumentId = batch[i].DocumentId,
                        ChunkId = batch[i].ChunkId,
                        ContentHash = batch[i].ContentHash,
                        RelativePath = batch[i].RelativePath,
                        Heading = batch[i].Heading,
                        SourceLine = batch[i].SourceLine,
                        Vector = vectors.Vectors[i]
                    });
                }

                embedded += batch.Length;
            }

            index = await _cache.ReplaceDocumentAsync(_preferences.ModelId,
                _preferences.Dimensions, _policyKey, source.DocumentId, replacement, token);
            changed++;
            progress?.Report(new SemanticIndexProgress(visited, distinct.Count, embedded));
        }

        return new SemanticIndexSummary(visited, changed, embedded, true);
    }

    /// <summary>
    /// Searches real vector matches and rehydrates excerpts from current canonical
    /// Markdown only when their source hash still matches the cached embedding.
    /// </summary>
    /// <param name="query">User-entered natural-language query.</param>
    /// <param name="approved">Explicit opt-in to sending this query to local provider.</param>
    /// <param name="scope">Optional workspace-relative directory prefix.</param>
    /// <param name="token">Cancellation request.</param>
    /// <returns>Deduplicated, traceable source matches with cosine similarity.</returns>
    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        string query, bool approved, string? scope = null,
        CancellationToken token = default)
    {
        if (!_preferences.IsEnabled || !approved ||
            string.IsNullOrWhiteSpace(query))
            return [];

        if (query.Length > 8192)
            throw new ArgumentOutOfRangeException(nameof(query));

        SemanticVectorIndex index = await _cache.LoadAsync(
            _preferences.ModelId, _preferences.Dimensions, _policyKey, token);
        if (index.Entries.Count == 0) return [];

        EmbeddingBatch? embedding = await _execution.EmbedApprovedAsync([query],
            approved, token);
        if (embedding is null || embedding.Vectors.Count != 1) return [];

        // Validate every possible canonical fragment BEFORE deduplicating documents.
        // Limiting to the top 100 raw passages lets a modified long document
        // hide a valid document entirely. Maximum cache capacity is 10 000.
        IReadOnlyList<SemanticRankedHit> hits = await Task.Run(
            () => SemanticVectorRanker.Rank(index,
                embedding.Vectors[0], topK: SemanticVectorCacheService.MaximumEntries,
                threshold: 0.2, scope: scope,
                deduplicateDocuments: false), token);
        token.ThrowIfCancellationRequested();
        List<SemanticSearchResult> output = [];
        HashSet<Guid> delivered = [];
        // Each document may have hundreds of ranked chunks. Rehydrate its
        // canonical source at most once per query, even for stale candidates.
        Dictionary<Guid, IReadOnlyDictionary<string, SemanticChunk>?> sourceCache = [];
        foreach (SemanticRankedHit hit in hits)
        {
            if (delivered.Contains(hit.DocumentId)) continue;
            token.ThrowIfCancellationRequested();
            if (!sourceCache.TryGetValue(hit.DocumentId, out
                IReadOnlyDictionary<string, SemanticChunk>? chunks))
            {
                chunks = null;
                string? file = SafePath(hit.RelativePath);
                if (file is not null && File.Exists(file) &&
                    new FileInfo(file).Length <= MaxSourceBytes)
                {
                    string content = await File.ReadAllTextAsync(file, token);
                    IReadOnlyList<SemanticChunk> source = await Task.Run(
                        () => SemanticMarkdownChunker.Split(
                            hit.DocumentId, hit.RelativePath, content, _chunking), token);
                    chunks = source.ToDictionary(
                        candidate => candidate.ChunkId, StringComparer.Ordinal);
                }

                sourceCache.Add(hit.DocumentId, chunks);
            }

            if (chunks is null || !chunks.TryGetValue(hit.ChunkId,
                out SemanticChunk? chunk))
                continue;
            token.ThrowIfCancellationRequested();
            delivered.Add(hit.DocumentId);

            output.Add(new SemanticSearchResult(hit.DocumentId, hit.RelativePath,
                hit.Heading, chunk.SourceLine,
                chunk.Content.Length <= 250 ? chunk.Content :
                    chunk.Content[..250] + "…",
                hit.Similarity));
            if (output.Count == 20) break;
        }

        return output;
    }

    /// <summary>Purges only the local vector cache after explicit user action.</summary>
    /// <param name="token">Cancellation request.</param>
    /// <returns>Completed cache deletion.</returns>
    public Task PurgeAsync(CancellationToken token = default) =>
        _cache.PurgeAsync(token);

    /// <summary>Maps safe workspace-relative Markdown paths, rejecting directory links.</summary>
    /// <param name="relativePath">Indexed path to validate.</param>
    /// <returns>Full path or null for unsafe destinations.</returns>
    private string? SafePath(string relativePath)
    {
        if (!SemanticMarkdownChunker.IsEligiblePath(relativePath, _chunking))
            return null;

        try
        {
            string root = Path.TrimEndingDirectorySeparator(_workspaceRoot) +
                Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(_workspaceRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return null;

            DirectoryInfo? directory = Directory.GetParent(full);
            while (directory is not null &&
                directory.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                if (directory.Exists &&
                    (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return null;
                directory = directory.Parent;
            }

            if (File.Exists(full) &&
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                return null;
            return full;
        }
        catch (Exception error) when (error is ArgumentException or
            IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Hashes versioned chunk settings so any setting change invalidates old vectors.</summary>
    /// <param name="settings">Canonical segmentation options.</param>
    /// <returns>Stable fingerprint for this indexing policy.</returns>
    public static string CreatePolicyKey(SemanticChunkingOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string canonical = "chunker-v1|" + Math.Clamp(settings.MaxCharacters, 160, 8192) +
            "|" + Math.Clamp(settings.OverlapCharacters, 0,
                Math.Min(512, Math.Clamp(settings.MaxCharacters, 160, 8192) / 4)) +
            "|" + settings.IncludeCode + "|" + string.Join(";",
                settings.ExcludedPathPatterns.Order(StringComparer.OrdinalIgnoreCase));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
