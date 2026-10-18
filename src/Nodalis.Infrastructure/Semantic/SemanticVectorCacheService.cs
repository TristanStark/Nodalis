using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Semantic;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Semantic;

/// <summary>Read-only local index metadata; never includes document content or vectors.</summary>
/// <param name="FileExists">Whether an index file is present.</param>
/// <param name="IsCompatible">Whether its schema, model, policy and vectors are valid.</param>
/// <param name="CachePath">Derived machine-local index location.</param>
/// <param name="ModelId">Configured embedding model for this inspection.</param>
/// <param name="Dimensions">Configured embedding dimensions.</param>
/// <param name="DocumentCount">Number of unique indexed documents.</param>
/// <param name="ChunkCount">Number of validated stored vector fragments.</param>
/// <param name="OnDiskBytes">Local derived cache size in bytes.</param>
/// <param name="VectorBytes">Uncompressed in-memory vector payload estimate in bytes.</param>
/// <param name="UpdatedAtUtc">Observed file modification timestamp.</param>
/// <param name="IndexedPaths">Unique workspace-relative indexed paths, not document contents.</param>
public sealed record SemanticIndexStatus(bool FileExists, bool IsCompatible, string CachePath,
    string ModelId, int Dimensions, int DocumentCount, int ChunkCount,
    long OnDiskBytes, long VectorBytes, DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<string> IndexedPaths);

/// <summary>Versioned local cache, safe to purge or rebuild from canonical Markdown.</summary>
public sealed class SemanticVectorCacheService
{
    /// <summary>Hard upper bound for local vector fragments across a workspace.</summary>
    public const int MaximumEntries = 10_000;
    private const long MaxCacheFileBytes = 64_000_000;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Builds a machine-local cache key for one workspace, not a workspace file.</summary>
    /// <param name="workspaceRoot">Absolute root of the canonical workspace.</param>
    /// <param name="cacheDirectory">Optional disposable cache location for tests.</param>
    public SemanticVectorCacheService(string workspaceRoot, string? cacheDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        string root = Path.GetFullPath(workspaceRoot);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            root.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).ToLowerInvariant();
        string directory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nodalis", "SemanticIndexes");
        _filePath = Path.Combine(Path.GetFullPath(directory), key + ".json");
    }

    /// <summary>Path to the derived cache, for user-visible purge information.</summary>
    public string CachePath => _filePath;

    /// <summary>Reads index size, age and coverage without starting a provider or modifying documents.</summary>
    /// <param name="modelId">Configured embedding model.</param>
    /// <param name="dimensions">Configured vector dimensions.</param>
    /// <param name="segmentationKey">Current chunker policy identifier.</param>
    /// <param name="token">Cancellation for local cache inspection.</param>
    /// <returns>A validated index summary or an explicit absent/incompatible status.</returns>
    public async Task<SemanticIndexStatus> InspectAsync(string modelId, int dimensions,
        string segmentationKey, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(_filePath))
                return new SemanticIndexStatus(false, false, _filePath, modelId,
                    dimensions, 0, 0, 0, 0, null, []);
            FileInfo info = new(_filePath);
            if (info.Length > MaxCacheFileBytes)
                return new SemanticIndexStatus(true, false, _filePath, modelId,
                    dimensions, 0, 0, info.Length, 0, info.LastWriteTimeUtc, []);

            try
            {
                SemanticVectorIndex loaded = await AtomicJsonFile.ReadAsync<SemanticVectorIndex>(
                    _filePath, token);
                if (!IsCompatible(loaded, modelId, dimensions, segmentationKey))
                    return new SemanticIndexStatus(true, false, _filePath, modelId,
                        dimensions, 0, 0, info.Length, 0, info.LastWriteTimeUtc, []);

                string[] paths = loaded.Entries.Select(entry => entry.RelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
                long vectorBytes = loaded.Entries.Sum(entry => (long)entry.Vector.Length * sizeof(float));
                return new SemanticIndexStatus(true, true, _filePath, modelId,
                    dimensions, loaded.Entries.Select(entry => entry.DocumentId).Distinct().Count(),
                    loaded.Entries.Count, info.Length, vectorBytes,
                    info.LastWriteTimeUtc, paths);
            }
            catch (Exception error) when (error is IOException or JsonException or
                InvalidDataException or UnauthorizedAccessException)
            {
                return new SemanticIndexStatus(true, false, _filePath, modelId,
                    dimensions, 0, 0, info.Length, 0, info.LastWriteTimeUtc, []);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads only matching schema/model/dimensions/chunk-policy data; corrupt data acts as empty.</summary>
    /// <param name="modelId">Requested local embedding model.</param>
    /// <param name="dimensions">Expected vector dimensions.</param>
    /// <param name="segmentationKey">Current chunker policy identifier.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Compatible index or empty rebuildable cache.</returns>
    public async Task<SemanticVectorIndex> LoadAsync(string modelId, int dimensions,
        string segmentationKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(modelId, dimensions, segmentationKey,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Atomically replaces one document's vectors, preserving other valid sources.</summary>
    /// <param name="modelId">Exact source model.</param>
    /// <param name="dimensions">Expected vector size.</param>
    /// <param name="segmentationKey">Current segmentation policy.</param>
    /// <param name="documentId">The source being refreshed or removed.</param>
    /// <param name="entries">All newly generated vectors for that document.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Updated cache snapshot.</returns>
    public async Task<SemanticVectorIndex> ReplaceDocumentAsync(
        string modelId, int dimensions, string segmentationKey, Guid documentId,
        IReadOnlyList<SemanticVectorEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (documentId == Guid.Empty || dimensions is < 1 or > 4096 ||
            string.IsNullOrWhiteSpace(modelId) ||
            string.IsNullOrWhiteSpace(segmentationKey) ||
            entries.Count > MaximumEntries || entries.Any(entry =>
                entry.DocumentId != documentId ||
                !IsValid(entry, dimensions)))
        {
            throw new InvalidDataException("Vecteurs d'index non compatibles ou mal formés.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            SemanticVectorIndex current = await ReadUnsafeAsync(
                modelId, dimensions, segmentationKey, cancellationToken);
            List<SemanticVectorEntry> merged = current.Entries
                .Where(entry => entry.DocumentId != documentId).ToList();
            merged.AddRange(entries);
            if (merged.Count > MaximumEntries)
            {
                throw new InvalidDataException("Quota d'index dépassé (10 000 fragments).");
            }

            SemanticVectorIndex updated = current with { Entries = merged };
            // Reject an over-budget index BEFORE touching the existing atomic cache.
            // This also prevents a successful write that could not be loaded again.
            int encodedSize = JsonSerializer.SerializeToUtf8Bytes(updated).Length;
            if (encodedSize > MaxCacheFileBytes - 1024)
                throw new InvalidDataException("Cache vectoriel trop volumineux (64 Mo maximum).");
            cancellationToken.ThrowIfCancellationRequested();
            await AtomicJsonFile.WriteAsync(_filePath, updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reconciles the disposable index with the full canonical workspace catalog.
    /// This is called only after explicit user approval of a new indexing session.
    /// </summary>
    /// <param name="modelId">Configured local model.</param>
    /// <param name="dimensions">Configured embedding dimensions.</param>
    /// <param name="segmentationKey">Current chunker policy.</param>
    /// <param name="knownDocuments">All currently eligible canonical document GUIDs.</param>
    /// <param name="token">Cancellation for local reconciliation.</param>
    /// <returns>Number of removed obsolete vector fragments.</returns>
    public async Task<int> PruneUnlistedDocumentsAsync(string modelId, int dimensions,
        string segmentationKey, IReadOnlySet<Guid> knownDocuments,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(knownDocuments);
        await _gate.WaitAsync(token);
        try
        {
            if (!File.Exists(_filePath)) return 0;
            SemanticVectorIndex current = await ReadUnsafeAsync(
                modelId, dimensions, segmentationKey, token);
            if (current.Entries.Count == 0) return 0;
            List<SemanticVectorEntry> kept = current.Entries
                .Where(entry => knownDocuments.Contains(entry.DocumentId)).ToList();
            int removed = current.Entries.Count - kept.Count;
            if (removed == 0) return 0;
            token.ThrowIfCancellationRequested();
            await AtomicJsonFile.WriteAsync(_filePath,
                current with { Entries = kept }, token);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes the derived cache without accessing a workspace source.</summary>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Completed local purge.</returns>
    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns an index only if it is compatible and numerically sound.</summary>
    /// <param name="modelId">Current model identifier.</param>
    /// <param name="dimensions">Current dimensions.</param>
    /// <param name="segmentationKey">Current segmentation policy.</param>
    /// <param name="token">Cancellation request.</param>
    /// <returns>Compatible cache or an empty cache.</returns>
    private async Task<SemanticVectorIndex> ReadUnsafeAsync(
        string modelId, int dimensions, string segmentationKey, CancellationToken token)
    {
        SemanticVectorIndex empty = new()
        {
            ModelId = modelId,
            Dimensions = dimensions,
            SegmentationKey = segmentationKey
        };
        if (!File.Exists(_filePath)) return empty;
        try
        {
            FileInfo info = new(_filePath);
            if (info.Length > MaxCacheFileBytes) return empty;
            SemanticVectorIndex loaded = await AtomicJsonFile.ReadAsync<SemanticVectorIndex>(
                _filePath, token);
            if (!IsCompatible(loaded, modelId, dimensions, segmentationKey))
                return empty;
            return loaded;
        }
        catch (Exception error) when (error is IOException or JsonException or
            InvalidDataException or UnauthorizedAccessException)
        {
            return empty;
        }
    }

    /// <summary>Checks schema, provider identity, metadata bounds and every stored vector.</summary>
    /// <param name="index">Deserialized local cache.</param>
    /// <param name="modelId">Current model.</param>
    /// <param name="dimensions">Current dimension.</param>
    /// <param name="segmentationKey">Current segmentation policy.</param>
    /// <returns>Whether the cache can safely be used or reported as valid.</returns>
    private static bool IsCompatible(SemanticVectorIndex index, string modelId,
        int dimensions, string segmentationKey) =>
        index.SchemaVersion == SemanticVectorIndex.CurrentVersion &&
        string.Equals(index.ModelId, modelId, StringComparison.Ordinal) &&
        index.Dimensions == dimensions &&
        index.SegmentationKey == segmentationKey &&
        index.Entries is not null && index.Entries.Count <= MaximumEntries &&
        index.Entries.All(entry => IsValid(entry, dimensions));

    /// <summary>Validates one safe canonical source reference and its finite vector.</summary>
    /// <param name="entry">Candidate vector record.</param>
    /// <param name="dimension">Model dimensions.</param>
    /// <returns>Whether this index entry can be used for retrieval.</returns>
    private static bool IsValid(SemanticVectorEntry entry, int dimension) =>
        entry.DocumentId != Guid.Empty &&
        entry.ChunkId.Length is > 0 and <= 128 &&
        entry.ContentHash.Length is > 0 and <= 128 &&
        SemanticMarkdownChunker.IsEligiblePath(entry.RelativePath) &&
        entry.SourceLine > 0 && entry.Vector is not null &&
        entry.Vector.Length == dimension &&
        entry.Vector.All(float.IsFinite) &&
        entry.Vector.Any(value => value != 0);
}
