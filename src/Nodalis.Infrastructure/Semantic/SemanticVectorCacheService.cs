using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Semantic;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Semantic;

/// <summary>A derived vector without copied Markdown content.</summary>
public sealed record SemanticVectorEntry
{
    /// <summary>Stable canonical document identity.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Stable source chunk identity.</summary>
    public string ChunkId { get; init; } = "";

    /// <summary>Source text fingerprint for targeted invalidation.</summary>
    public string ContentHash { get; init; } = "";

    /// <summary>Workspace relative Markdown document location.</summary>
    public string RelativePath { get; init; } = "";

    /// <summary>Optional source heading, not duplicated source text.</summary>
    public string? Heading { get; init; }

    /// <summary>One-based source position.</summary>
    public int SourceLine { get; init; }

    /// <summary>Embedding vector from the selected local model.</summary>
    public float[] Vector { get; init; } = [];
}

/// <summary>Disposable, versioned local vector index; never a canonical business file.</summary>
public sealed record SemanticVectorIndex
{
    /// <summary>Index format version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Index format discriminator.</summary>
    public int SchemaVersion { get; init; } = CurrentVersion;

    /// <summary>Exact configured embedding model.</summary>
    public string ModelId { get; init; } = "";

    /// <summary>Validated dimensions.</summary>
    public int Dimensions { get; init; }

    /// <summary>Chunker settings fingerprint.</summary>
    public string SegmentationKey { get; init; } = "";

    /// <summary>Derived vectors, omitting all full source text.</summary>
    public List<SemanticVectorEntry> Entries { get; init; } = [];
}

/// <summary>Versioned local cache, safe to purge or rebuild from canonical Markdown.</summary>
public sealed class SemanticVectorCacheService
{
    private const int MaxEntries = 10_000;
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
            entries.Count > MaxEntries || entries.Any(entry =>
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
            if (merged.Count > MaxEntries)
            {
                throw new InvalidDataException("Quota d'index dépassé (10 000 fragments).");
            }

            SemanticVectorIndex updated = current with { Entries = merged };
            await AtomicJsonFile.WriteAsync(_filePath, updated, cancellationToken);
            return updated;
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
            ModelId = modelId, Dimensions = dimensions, SegmentationKey = segmentationKey
        };
        if (!File.Exists(_filePath)) return empty;
        try
        {
            FileInfo info = new(_filePath);
            if (info.Length > MaxCacheFileBytes) return empty;
            SemanticVectorIndex loaded = await AtomicJsonFile.ReadAsync<SemanticVectorIndex>(
                _filePath, token);
            if (loaded.SchemaVersion != SemanticVectorIndex.CurrentVersion ||
                !string.Equals(loaded.ModelId, modelId, StringComparison.Ordinal) ||
                loaded.Dimensions != dimensions ||
                loaded.SegmentationKey != segmentationKey ||
                loaded.Entries is null || loaded.Entries.Count > MaxEntries ||
                loaded.Entries.Any(entry => !IsValid(entry, dimensions)))
                return empty;
            return loaded;
        }
        catch (Exception error) when (error is IOException or JsonException or
            InvalidDataException or UnauthorizedAccessException)
        {
            return empty;
        }
    }

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
