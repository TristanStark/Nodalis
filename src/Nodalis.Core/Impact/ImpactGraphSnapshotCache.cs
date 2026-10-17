using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Links;
using Nodalis.Core.Relations;

namespace Nodalis.Core.Impact;

/// <summary>
/// Reuses a read-only graph snapshot while the indexed identity and relation data
/// are unchanged. Revisions are computed from the content of the derived index,
/// not from timestamps or source-file side effects.
/// </summary>
public sealed class ImpactGraphSnapshotCache
{
    private readonly object _gate = new();
    private string? _revision;
    private ImpactGraphIndex? _snapshot;
    private Dictionary<Guid, string> _sourceRevisions = [];

    /// <summary>Gets the number of full graph reconstructions.</summary>
    public int RebuildCount { get; private set; }

    /// <summary>Gets the number of analyses that reused the current graph.</summary>
    public int CacheHitCount { get; private set; }

    /// <summary>Gets the number of updates that reused unchanged source edges.</summary>
    public int PartialRebuildCount { get; private set; }

    /// <summary>Gets the number of source documents reparsed on the last update.</summary>
    public int LastReindexedSourceCount { get; private set; }

    /// <summary>
    /// Gets an equivalent read-only graph, rebuilding after changes to a target,
    /// link, relation or its provenance. Failure/cancellation never replaces
    /// a previously complete snapshot.
    /// </summary>
    /// <param name="catalog">Current, derived workspace link index.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>The reusable, fully reconstructed snapshot.</returns>
    public ImpactGraphIndex GetOrBuild(
        LinkIndexCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();
        string revision = ComputeRevision(catalog, cancellationToken);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (revision == _revision && _snapshot is not null)
            {
                CacheHitCount++;
                return _snapshot;
            }
        }

        Dictionary<Guid, string> nextSources = ComputeSourceRevisions(
            catalog, cancellationToken);
        ImpactGraphIndex? previous;
        Dictionary<Guid, string> previousSources;
        lock (_gate)
        {
            previous = _snapshot;
            previousSources = new Dictionary<Guid, string>(_sourceRevisions);
        }

        HashSet<Guid> dirty = [];
        foreach (KeyValuePair<Guid, string> entry in nextSources)
        {
            if (!previousSources.TryGetValue(entry.Key, out string? old) ||
                !string.Equals(entry.Value, old, StringComparison.Ordinal))
            {
                dirty.Add(entry.Key);
            }
        }

        foreach (Guid removed in previousSources.Keys.Except(nextSources.Keys))
        {
            dirty.Add(removed);
        }

        if (previous is not null)
        {
            HashSet<Guid> membershipChanged = previous.Targets.Keys.ToHashSet();
            membershipChanged.SymmetricExceptWith(catalog.Targets.Select(item => item.Id));
            dirty.UnionWith(membershipChanged);
            foreach (TypedRelationEntry relation in catalog.Relations)
            {
                if (relation.TargetId is Guid id && membershipChanged.Contains(id))
                {
                    dirty.Add(relation.SourceId);
                }
            }

            foreach (LinkReferenceEntry reference in catalog.References)
            {
                if (reference.TargetId is Guid id && membershipChanged.Contains(id))
                {
                    dirty.Add(reference.SourceId);
                }
            }

            foreach (ImpactGraphEdge edge in previous.Edges)
            {
                if (membershipChanged.Contains(edge.TargetId))
                {
                    dirty.Add(edge.SourceId);
                }
            }

            foreach (ImpactGraphDiagnostic diagnostic in previous.Diagnostics)
            {
                if (Guid.TryParse(diagnostic.RawTarget, out Guid id) &&
                    membershipChanged.Contains(id))
                {
                    dirty.Add(diagnostic.SourceId);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ImpactGraphIndex rebuilt = previous is null
            ? ImpactGraphIndex.Build(catalog, cancellationToken)
            : ImpactGraphIndex.RebuildSources(catalog, previous, dirty, cancellationToken);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (revision == _revision && _snapshot is not null)
            {
                CacheHitCount++;
                return _snapshot;
            }

            _revision = revision;
            _snapshot = rebuilt;
            _sourceRevisions = nextSources;
            RebuildCount++;
            if (previous is not null)
            {
                PartialRebuildCount++;
            }

            LastReindexedSourceCount = previous is null ? nextSources.Count : dirty.Count;
            return rebuilt;
        }
    }

    /// <summary>Releases the derived snapshot without touching any workspace data.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _revision = null;
            _snapshot = null;
            _sourceRevisions.Clear();
            LastReindexedSourceCount = 0;
        }
    }

    /// <summary>
    /// Computes a deterministic content fingerprint over identities, document
    /// references and typed relations without reading the filesystem.
    /// </summary>
    /// <param name="catalog">Indexed source snapshot.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>SHA-256 fingerprint, independent of list insertion order.</returns>
    private static string ComputeRevision(
        LinkIndexCatalog catalog, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (LinkTargetEntry target in catalog.Targets.OrderBy(item => item.Id)
                     .ThenBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(hash, "target", target.Id.ToString("D"), target.Kind.ToString(),
                target.DisplayName, target.QualifiedName, target.RelativePath,
                target.ScopeIdentity ?? "", target.ContentHash ?? "",
                string.Join("|", target.Aliases.OrderBy(x => x, StringComparer.Ordinal)));
        }

        foreach (TypedRelationEntry relation in catalog.Relations
                     .OrderBy(item => item.SourceId).ThenBy(item => item.LineNumber)
                     .ThenBy(item => item.RawTargetId, StringComparer.Ordinal)
                     .ThenBy(item => item.RelationType, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(hash, "relation", relation.SourceId.ToString("D"),
                relation.TargetId?.ToString("D") ?? "", relation.RawTargetId,
                relation.RelationType, relation.TargetLabel ?? "",
                relation.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                relation.Excerpt);
        }

        foreach (LinkReferenceEntry reference in catalog.References
                     .OrderBy(item => item.SourceId).ThenBy(item => item.LineNumber)
                     .ThenBy(item => item.RawTarget, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Add(hash, "link", reference.SourceId.ToString("D"),
                reference.TargetId?.ToString("D") ?? "", reference.RawTarget,
                reference.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                reference.Excerpt);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }


    /// <summary>
    /// Computes per-source fingerprints to invalidate only sources whose links
    /// or typed relations were actually modified. Missing/restored targets are
    /// handled independently by the membership check.
    /// </summary>
    /// <param name="catalog">Current index.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Revisions keyed by their stable source document GUID.</returns>
    private static Dictionary<Guid, string> ComputeSourceRevisions(
        LinkIndexCatalog catalog, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> revisions = [];
        Dictionary<Guid, List<TypedRelationEntry>> relations = catalog.Relations
            .GroupBy(item => item.SourceId)
            .ToDictionary(group => group.Key, group => group.ToList());
        Dictionary<Guid, List<LinkReferenceEntry>> links = catalog.References
            .GroupBy(item => item.SourceId)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (Guid source in relations.Keys.Concat(links.Keys).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (relations.TryGetValue(source, out List<TypedRelationEntry>? rows))
            {
                foreach (TypedRelationEntry item in rows.OrderBy(item => item.LineNumber)
                             .ThenBy(item => item.RelationType, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.RawTargetId, StringComparer.Ordinal))
                {
                    Add(hash, "relation", item.SourceId.ToString("D"),
                        item.TargetId?.ToString("D") ?? "", item.RawTargetId,
                        item.RelationType, item.TargetLabel ?? "",
                        item.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        item.Excerpt);
                }
            }

            if (links.TryGetValue(source, out List<LinkReferenceEntry>? references))
            {
                foreach (LinkReferenceEntry item in references.OrderBy(item => item.LineNumber)
                             .ThenBy(item => item.RawTarget, StringComparer.Ordinal))
                {
                    Add(hash, "link", item.SourceId.ToString("D"),
                        item.TargetId?.ToString("D") ?? "", item.RawTarget,
                        item.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        item.Excerpt);
                }
            }

            revisions.Add(source, Convert.ToHexString(hash.GetHashAndReset()));
        }

        return revisions;
    }

    /// <summary>Appends length-prefixed text fields to avoid ambiguous delimiters.</summary>
    /// <param name="hash">Incremental fingerprint algorithm.</param>
    /// <param name="parts">Fields forming one indexed entry.</param>
    private static void Add(IncrementalHash hash, params string[] parts)
    {
        foreach (string part in parts)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(part);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
    }
}
