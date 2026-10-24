using Nodalis.Core.Links;

namespace Nodalis.Infrastructure.Links;

/// <summary>
/// Lookup tables for previous targets, built once per refresh.
/// Candidate lists preserve the ordering of the original catalog.
/// </summary>
internal sealed class PreviousLinkTargetLookup
{
    private readonly Dictionary<Guid, LinkTargetEntry> _byId = new();
    private readonly Dictionary<string, List<LinkTargetEntry>> _paths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<LinkTargetEntry>>> _scopedPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<LinkTargetEntry>>> _scopedHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LinkTargetEntry>> _hashes =
        new(StringComparer.Ordinal);

    /// <summary>Indexes all the previous targets with one traversal.</summary>
    internal PreviousLinkTargetLookup(IReadOnlyList<LinkTargetEntry> targets)
    {
        foreach (LinkTargetEntry target in targets)
        {
            _byId.TryAdd(target.Id, target);
            if (target.Kind != LinkTargetKind.Document)
            {
                continue;
            }

            Add(_paths, target.RelativePath, target);
            if (target.ScopeIdentity is not null && target.LocalRelativePath is not null)
            {
                AddScoped(
                    _scopedPaths,
                    target.ScopeIdentity,
                    target.LocalRelativePath,
                    target,
                    StringComparer.OrdinalIgnoreCase);
            }

            if (target.ContentHash is null)
            {
                continue;
            }

            Add(_hashes, target.ContentHash, target);
            if (target.ScopeIdentity is not null)
            {
                AddScoped(
                    _scopedHashes,
                    target.ScopeIdentity,
                    target.ContentHash,
                    target,
                    StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Returns the earliest entry with this identity.</summary>
    internal LinkTargetEntry? FindById(Guid id) =>
        _byId.TryGetValue(id, out LinkTargetEntry? target) ? target : null;

    /// <summary>Finds by global path first, then scope/local path.</summary>
    internal LinkTargetEntry? MatchByLocation(
        ISet<Guid> matchedIds,
        string relativePath,
        string scopeIdentity,
        string localRelativePath)
    {
        if (_paths.TryGetValue(relativePath, out List<LinkTargetEntry>? candidates))
        {
            LinkTargetEntry? match = FirstUnmatched(candidates, matchedIds);
            if (match is not null)
            {
                return match;
            }
        }

        return _scopedPaths.TryGetValue(
                   scopeIdentity,
                   out Dictionary<string, List<LinkTargetEntry>>? scoped) &&
               scoped.TryGetValue(localRelativePath, out List<LinkTargetEntry>? local)
            ? FirstUnmatched(local, matchedIds)
            : null;
    }

    /// <summary>Finds a unique hash within the scope before trying the full workspace.</summary>
    internal LinkTargetEntry? MatchByHash(
        ISet<Guid> matchedIds,
        string scopeIdentity,
        string contentHash)
    {
        if (_scopedHashes.TryGetValue(
                scopeIdentity,
                out Dictionary<string, List<LinkTargetEntry>>? scoped) &&
            scoped.TryGetValue(contentHash, out List<LinkTargetEntry>? local))
        {
            LinkTargetEntry? match = UniqueUnmatched(local, matchedIds);
            if (match is not null)
            {
                return match;
            }
        }

        return _hashes.TryGetValue(contentHash, out List<LinkTargetEntry>? global)
            ? UniqueUnmatched(global, matchedIds)
            : null;
    }

    /// <summary>Returns the earliest document not claimed by another navigation node.</summary>
    private static LinkTargetEntry? FirstUnmatched(
        IEnumerable<LinkTargetEntry> candidates,
        ISet<Guid> matchedIds)
    {
        foreach (LinkTargetEntry candidate in candidates)
        {
            if (!matchedIds.Contains(candidate.Id))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Returns the only remaining candidate, or null for collisions.</summary>
    private static LinkTargetEntry? UniqueUnmatched(
        IEnumerable<LinkTargetEntry> candidates,
        ISet<Guid> matchedIds)
    {
        LinkTargetEntry? match = null;
        foreach (LinkTargetEntry candidate in candidates)
        {
            if (matchedIds.Contains(candidate.Id))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = candidate;
        }

        return match;
    }

    /// <summary>Appends to a bucket while retaining source iteration order.</summary>
    private static void Add(
        Dictionary<string, List<LinkTargetEntry>> map,
        string key,
        LinkTargetEntry target)
    {
        if (!map.TryGetValue(key, out List<LinkTargetEntry>? values))
        {
            values = [];
            map.Add(key, values);
        }

        values.Add(target);
    }

    /// <summary>Appends to a two-level scope/key lookup.</summary>
    private static void AddScoped(
        Dictionary<string, Dictionary<string, List<LinkTargetEntry>>> map,
        string scope,
        string key,
        LinkTargetEntry target,
        StringComparer comparer)
    {
        if (!map.TryGetValue(scope, out Dictionary<string, List<LinkTargetEntry>>? values))
        {
            values = new Dictionary<string, List<LinkTargetEntry>>(comparer);
            map.Add(scope, values);
        }

        Add(values, key, target);
    }
}

/// <summary>
/// Per-refresh resolver retaining GUID/qualified/display/alias precedence and ambiguity.
/// Index and memoized results never escape the lifetime of their target snapshot.
/// </summary>
internal sealed class LinkTargetResolutionLookup
{
    private readonly Dictionary<Guid, List<LinkTargetEntry>> _ids = new();
    private readonly Dictionary<string, List<LinkTargetEntry>> _qualified =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, List<LinkTargetEntry>> _display =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, List<LinkTargetEntry>> _aliases =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, LinkResolution> _cache =
        new(StringComparer.Ordinal);

    /// <summary>Constructs the resolution index with one target traversal.</summary>
    internal LinkTargetResolutionLookup(IReadOnlyList<LinkTargetEntry> targets)
    {
        foreach (LinkTargetEntry target in targets)
        {
            if (!_ids.TryGetValue(target.Id, out List<LinkTargetEntry>? byId))
            {
                byId = [];
                _ids.Add(target.Id, byId);
            }

            byId.Add(target);
            Add(_qualified, target.QualifiedName, target);
            Add(_display, target.DisplayName, target);

            HashSet<string> uniqueAliases =
                new(target.Aliases, StringComparer.CurrentCultureIgnoreCase);
            foreach (string alias in uniqueAliases)
            {
                Add(_aliases, alias, target);
            }
        }
    }

    /// <summary>Resolves the target with the exact historical precedence.</summary>
    internal LinkResolution Resolve(string rawTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTarget);
        string target = rawTarget.Trim();
        if (_cache.TryGetValue(target, out LinkResolution? hit))
        {
            return hit;
        }

        IReadOnlyList<LinkTargetEntry> matches;
        if (Guid.TryParse(target, out Guid id))
        {
            matches = _ids.TryGetValue(id, out List<LinkTargetEntry>? ids) ? ids : [];
        }
        else if (_qualified.TryGetValue(target, out List<LinkTargetEntry>? qualified))
        {
            matches = qualified;
        }
        else if (_display.TryGetValue(target, out List<LinkTargetEntry>? display))
        {
            matches = display;
        }
        else
        {
            matches = _aliases.TryGetValue(target, out List<LinkTargetEntry>? aliases)
                ? aliases
                : [];
        }

        LinkResolution resolution = matches.Count == 1
            ? new LinkResolution
            {
                Status = LinkResolutionStatus.Resolved,
                Target = matches[0],
                Candidates = [matches[0]]
            }
            : new LinkResolution
            {
                Status = matches.Count == 0
                    ? LinkResolutionStatus.Missing
                    : LinkResolutionStatus.Ambiguous,
                Candidates = matches.ToList()
            };

        _cache.Add(target, resolution);
        return resolution;
    }

    /// <summary>Appends a resolution candidate to an ordered name bucket.</summary>
    private static void Add(
        Dictionary<string, List<LinkTargetEntry>> map,
        string key,
        LinkTargetEntry target)
    {
        if (!map.TryGetValue(key, out List<LinkTargetEntry>? values))
        {
            values = [];
            map.Add(key, values);
        }

        values.Add(target);
    }
}
