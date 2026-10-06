using System.Diagnostics;
using System.Security.Cryptography;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Core.Navigation;
using Nodalis.Core.Relations;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Links;

public sealed class WorkspaceLinkIndexService
{
    private readonly string _workspaceRoot;
    private readonly string _indexPath;
    private readonly WorkspaceNavigationBuilder _navigationBuilder = new();

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceLinkIndexService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public WorkspaceLinkIndexService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _indexPath = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.LinkIndexFileName);
    }

    /// <summary>
    /// Performs the <c>LoadAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<LinkIndexCatalog> LoadAsync(
            CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_indexPath))
        {
            return await RefreshAsync(cancellationToken);
        }

        try
        {
            global::Nodalis.Core.Links.LinkIndexCatalog catalog = await AtomicJsonFile.ReadAsync<LinkIndexCatalog>(
                _indexPath,
                cancellationToken);

            if (catalog.SchemaVersion != LinkIndexCatalog.CurrentSchemaVersion)
            {
                return await RefreshAsync(cancellationToken);
            }

            return catalog;
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            System.Text.Json.JsonException)
        {
            return await RefreshAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Performs the <c>RefreshAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<LinkIndexCatalog> RefreshAsync(
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexRefreshResult result =
            await RefreshWithMetricsAsync(
                cancellationToken);

        return result.Catalog;
    }

    /// <summary>
    /// Refreshes the derived link index while reusing unchanged document fingerprints and parsed entries.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refreshed catalog and deterministic work counters.</returns>
    public async Task<LinkIndexRefreshResult> RefreshWithMetricsAsync(
            CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        global::Nodalis.Core.Links.LinkIndexCatalog previous = await LoadExistingUnsafeAsync(
            cancellationToken);

        global::Nodalis.Core.Navigation.WorkspaceNavigationNode navigation = await _navigationBuilder.BuildAsync(
            _workspaceRoot,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkTargetEntry> targets = new List<LinkTargetEntry>();
        global::System.Collections.Generic.HashSet<global::System.Guid> matchedPreviousIds = new HashSet<Guid>();
        RefreshCounters counters = new RefreshCounters();

        await CollectTargetsAsync(
            navigation,
            breadcrumb: [],
            scopeIdentity: $"workspace:{navigation.Id:D}",
            scopeRootPath: _workspaceRoot,
            previous.Targets,
            matchedPreviousIds,
            targets,
            counters,
            cancellationToken);

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Links.LinkTargetEntry> previousDocuments =
            previous.Targets
                .Where(target => target.Kind == LinkTargetKind.Document)
                .ToDictionary(target => target.Id);

        global::System.Collections.Generic.HashSet<global::System.Guid> changedSourceIds =
            targets
                .Where(target => target.Kind == LinkTargetKind.Document)
                .Where(target =>
                    !previousDocuments.TryGetValue(
                        target.Id,
                        out global::Nodalis.Core.Links.LinkTargetEntry? previousTarget) ||
                    !string.Equals(
                        previousTarget.ContentHash,
                        target.ContentHash,
                        StringComparison.Ordinal))
                .Select(target => target.Id)
                .ToHashSet();

        DocumentIndexBuildResult documentIndexes = await BuildDocumentIndexesAsync(
            targets,
            previous,
            changedSourceIds,
            counters,
            cancellationToken);

        global::Nodalis.Core.Links.LinkIndexCatalog catalog = new LinkIndexCatalog
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Targets = targets
                .OrderBy(target => target.QualifiedName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            References = documentIndexes.References,
            Relations = documentIndexes.Relations
        };

        await AtomicJsonFile.WriteAsync(
            _indexPath,
            catalog,
            cancellationToken);

        stopwatch.Stop();

        int documentCount = targets.Count(target =>
            target.Kind == LinkTargetKind.Document);

        return new LinkIndexRefreshResult
        {
            Catalog = catalog,
            Metrics = new LinkIndexRefreshMetrics
            {
                DocumentCount = documentCount,
                ChangedDocuments = changedSourceIds.Count,
                HashedDocuments = counters.HashedDocuments,
                ParsedDocuments = counters.ParsedDocuments,
                ReusedDocuments = Math.Max(
                    0,
                    documentCount - counters.ParsedDocuments),
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            }
        };
    }

    /// <summary>
    /// Refreshes after one document change. Fingerprints ensure only documents whose content changed are rehashed and reparsed.
    /// </summary>
    /// <param name="fullPath">The changed Markdown document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refreshed catalog and deterministic work counters.</returns>
    public Task<LinkIndexRefreshResult> RefreshDocumentAsync(
            string fullPath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            fullPath);

        string candidate = Path.GetFullPath(
            fullPath);
        string relative = Path.GetRelativePath(
            _workspaceRoot,
            candidate);

        if (relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            string.Equals(
                relative,
                "..",
                StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetExtension(candidate),
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "L'invalidation ciblée doit viser un document Markdown du workspace.");
        }

        return RefreshWithMetricsAsync(
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>ResolveAsync</c> operation.
    /// </summary>
    /// <param name="rawTarget">The <c>rawTarget</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<LinkResolution> ResolveAsync(
            string rawTarget,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog catalog = await LoadAsync(cancellationToken);
        return Resolve(catalog, rawTarget);
    }

    /// <summary>
    /// Performs the <c>Resolve</c> operation.
    /// </summary>
    /// <param name="catalog">The <c>catalog</c> value.</param>
    /// <param name="rawTarget">The <c>rawTarget</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static LinkResolution Resolve(
            LinkIndexCatalog catalog,
            string rawTarget)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTarget);

        string target = rawTarget.Trim();

        if (Guid.TryParse(target, out global::System.Guid targetId))
        {
            global::Nodalis.Core.Links.LinkTargetEntry[] byId = catalog.Targets
                .Where(candidate => candidate.Id == targetId)
                .ToArray();

            return BuildResolution(byId);
        }

        global::Nodalis.Core.Links.LinkTargetEntry[] qualified = catalog.Targets
            .Where(candidate => string.Equals(
                candidate.QualifiedName,
                target,
                StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (qualified.Length > 0)
        {
            return BuildResolution(qualified);
        }

        global::Nodalis.Core.Links.LinkTargetEntry[] currentNames = catalog.Targets
            .Where(candidate => string.Equals(
                candidate.DisplayName,
                target,
                StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (currentNames.Length > 0)
        {
            return BuildResolution(currentNames);
        }

        global::Nodalis.Core.Links.LinkTargetEntry[] aliases = catalog.Targets
            .Where(candidate => candidate.Aliases.Any(alias =>
                string.Equals(
                    alias,
                    target,
                    StringComparison.CurrentCultureIgnoreCase)))
            .ToArray();

        return BuildResolution(aliases);
    }

    /// <summary>
    /// Performs the <c>FindByPathAsync</c> operation.
    /// </summary>
    /// <param name="fullPath">The <c>fullPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<LinkTargetEntry?> FindByPathAsync(
            string fullPath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog catalog = await LoadAsync(cancellationToken);
        string relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(fullPath)));

        return catalog.Targets.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the <c>GetSuggestionsAsync</c> operation.
    /// </summary>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<LinkTargetEntry>> GetSuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog catalog = await LoadAsync(cancellationToken);
        string normalized = query.Trim();

        IEnumerable<LinkTargetEntry> candidates = catalog.Targets;

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            candidates = candidates.Where(target =>
                target.DisplayName.Contains(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase) ||
                target.QualifiedName.Contains(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase) ||
                target.Aliases.Any(alias =>
                    alias.Contains(
                        normalized,
                        StringComparison.CurrentCultureIgnoreCase)));
        }

        return candidates
            .OrderBy(target =>
                target.DisplayName.StartsWith(
                    normalized,
                    StringComparison.CurrentCultureIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(target => target.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(target => target.QualifiedName, StringComparer.CurrentCultureIgnoreCase)
            .Take(100)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>GetBacklinksAsync</c> operation.
    /// </summary>
    /// <param name="targetId">The <c>targetId</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<BacklinkEntry>> GetBacklinksAsync(
            Guid targetId,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog catalog = await LoadAsync(cancellationToken);
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Links.LinkTargetEntry> targetsById = catalog.Targets.ToDictionary(target => target.Id);

        return catalog.References
            .Where(reference => reference.TargetId == targetId)
            .Where(reference => targetsById.ContainsKey(reference.SourceId))
            .Select(reference => new BacklinkEntry
            {
                Source = targetsById[reference.SourceId],
                LineNumber = reference.LineNumber,
                Excerpt = reference.Excerpt,
                RawTarget = reference.RawTarget
            })
            .OrderBy(backlink => backlink.Source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(backlink => backlink.LineNumber)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>RegisterRenameAsync</c> operation.
    /// </summary>
    /// <param name="oldFullPath">The <c>oldFullPath</c> value.</param>
    /// <param name="newFullPath">The <c>newFullPath</c> value.</param>
    /// <param name="oldDisplayName">The <c>oldDisplayName</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task RegisterRenameAsync(
            string oldFullPath,
            string newFullPath,
            string oldDisplayName,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog catalog = await LoadAsync(cancellationToken);

        string oldRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(oldFullPath)));

        string newRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(newFullPath)));

        global::Nodalis.Core.Links.LinkTargetEntry? existing = catalog.Targets.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                oldRelativePath,
                StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            await RefreshAsync(cancellationToken);
            return;
        }

        global::System.Collections.Generic.List<string> aliases = MergeAliases(
            existing.Aliases,
            oldDisplayName,
            existing.DisplayName,
            existing.QualifiedName);

        global::Nodalis.Core.Links.LinkTargetEntry updated = existing with
        {
            RelativePath = newRelativePath,
            Aliases = aliases
        };

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkTargetEntry> targets = catalog.Targets
            .Select(target => target.Id == existing.Id ? updated : target)
            .ToList();

        await AtomicJsonFile.WriteAsync(
            _indexPath,
            catalog with
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Targets = targets
            },
            cancellationToken);

        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Performs the <c>LoadExistingUnsafeAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task<LinkIndexCatalog> LoadExistingUnsafeAsync(
            CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath))
        {
            return new LinkIndexCatalog();
        }

        try
        {
            return await AtomicJsonFile.ReadAsync<LinkIndexCatalog>(
                _indexPath,
                cancellationToken);
        }
        catch
        {
            return new LinkIndexCatalog();
        }
    }

    /// <summary>
    /// Performs the <c>CollectTargetsAsync</c> operation.
    /// </summary>
    /// <param name="node">The <c>node</c> value.</param>
    /// <param name="breadcrumb">The <c>breadcrumb</c> value.</param>
    /// <param name="scopeIdentity">The <c>scopeIdentity</c> value.</param>
    /// <param name="scopeRootPath">The <c>scopeRootPath</c> value.</param>
    /// <param name="previousTargets">The <c>previousTargets</c> value.</param>
    /// <param name="matchedPreviousIds">The <c>matchedPreviousIds</c> value.</param>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task CollectTargetsAsync(
            WorkspaceNavigationNode node,
            IReadOnlyList<string> breadcrumb,
            string scopeIdentity,
            string scopeRootPath,
            IReadOnlyList<LinkTargetEntry> previousTargets,
            ISet<Guid> matchedPreviousIds,
            ICollection<LinkTargetEntry> targets,
            RefreshCounters counters,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool includeInBreadcrumb = node.Kind is
            WorkspaceNodeKind.Global or
            WorkspaceNodeKind.Application or
            WorkspaceNodeKind.Module or
            WorkspaceNodeKind.Project or
            WorkspaceNodeKind.Section or
            WorkspaceNodeKind.Folder;

        string[] currentBreadcrumb = includeInBreadcrumb
            ? breadcrumb.Append(node.DisplayName).ToArray()
            : breadcrumb.ToArray();

        string currentScopeIdentity = scopeIdentity;
        string currentScopeRootPath = scopeRootPath;

        if (node.Kind == WorkspaceNodeKind.Application)
        {
            currentScopeIdentity = $"application:{node.Id:D}";
            currentScopeRootPath = node.FullPath;
        }
        else if (node.Kind == WorkspaceNodeKind.Module)
        {
            currentScopeIdentity = $"module:{node.Id:D}";
            currentScopeRootPath = node.FullPath;
        }
        else if (node.Kind == WorkspaceNodeKind.Project)
        {
            currentScopeIdentity = $"project:{node.Id:D}";
            currentScopeRootPath = node.FullPath;
        }

        if (node.Kind is
            WorkspaceNodeKind.Application or
            WorkspaceNodeKind.Module or
            WorkspaceNodeKind.Project)
        {
            string relativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    node.FullPath));

            string qualifiedName = string.Join(
                " / ",
                currentBreadcrumb);

            global::Nodalis.Core.Links.LinkTargetEntry? existing = previousTargets.FirstOrDefault(target =>
                target.Id == node.Id);

            global::System.Collections.Generic.List<string> aliases = existing is null
                ? []
                : MergeAliases(
                    existing.Aliases,
                    existing.DisplayName,
                    existing.QualifiedName);

            targets.Add(new LinkTargetEntry
            {
                Id = node.Id,
                Kind = ToLinkTargetKind(node.Kind),
                DisplayName = node.DisplayName,
                QualifiedName = qualifiedName,
                RelativePath = relativePath,
                ScopeIdentity = currentScopeIdentity,
                LocalRelativePath = ".",
                Aliases = aliases
            });

            if (existing is not null)
            {
                matchedPreviousIds.Add(existing.Id);
            }
        }
        else if (node.Kind == WorkspaceNodeKind.Document)
        {
            string relativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    node.FullPath));

            string localRelativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    currentScopeRootPath,
                    node.FullPath));

            FileInfo fileInfo = new FileInfo(
                node.FullPath);

            global::Nodalis.Core.Links.LinkTargetEntry? existing = MatchDocumentByLocation(
                previousTargets,
                matchedPreviousIds,
                relativePath,
                currentScopeIdentity,
                localRelativePath);

            string contentHash;

            if (existing is not null &&
                existing.ContentLength == fileInfo.Length &&
                existing.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks &&
                !string.IsNullOrWhiteSpace(
                    existing.ContentHash))
            {
                contentHash = existing.ContentHash;
            }
            else
            {
                contentHash = await ComputeHashAsync(
                    node.FullPath,
                    cancellationToken);
                counters.HashedDocuments++;

                existing ??= MatchDocumentByHash(
                    previousTargets,
                    matchedPreviousIds,
                    currentScopeIdentity,
                    contentHash);
            }

            string displayName = node.DisplayName;
            string qualifiedName = string.Join(
                " / ",
                currentBreadcrumb.Append(displayName));

            global::System.Collections.Generic.List<string> aliases = existing is null
                ? []
                : MergeAliases(
                    existing.Aliases,
                    existing.DisplayName,
                    existing.QualifiedName);

            global::System.Guid id = existing?.Id ?? Guid.NewGuid();

            targets.Add(new LinkTargetEntry
            {
                Id = id,
                Kind = LinkTargetKind.Document,
                DisplayName = displayName,
                QualifiedName = qualifiedName,
                RelativePath = relativePath,
                ScopeIdentity = currentScopeIdentity,
                LocalRelativePath = localRelativePath,
                ContentHash = contentHash,
                ContentLength = fileInfo.Length,
                LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                Aliases = aliases
            });

            if (existing is not null)
            {
                matchedPreviousIds.Add(existing.Id);
            }
        }

        foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode child in node.Children)
        {
            await CollectTargetsAsync(
                child,
                currentBreadcrumb,
                currentScopeIdentity,
                currentScopeRootPath,
                previousTargets,
                matchedPreviousIds,
                targets,
                counters,
                cancellationToken);
        }
    }

    /// <summary>
    /// Rebuilds parsed entries only for changed documents and reuses the transparent JSON entries for all others.
    /// </summary>
    /// <param name="targets">The current indexed targets.</param>
    /// <param name="previous">The previous transparent index.</param>
    /// <param name="changedSourceIds">Documents whose content hash changed.</param>
    /// <param name="counters">Refresh work counters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>References and typed relations for the refreshed catalog.</returns>
    private async Task<DocumentIndexBuildResult> BuildDocumentIndexesAsync(
            IReadOnlyList<LinkTargetEntry> targets,
            LinkIndexCatalog previous,
            ISet<Guid> changedSourceIds,
            RefreshCounters counters,
            CancellationToken cancellationToken)
    {
        LinkIndexCatalog resolutionCatalog = new LinkIndexCatalog
        {
            Targets = targets.ToList()
        };

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkReferenceEntry>> previousReferences =
            previous.References
                .GroupBy(reference => reference.SourceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList());

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Collections.Generic.List<global::Nodalis.Core.Relations.TypedRelationEntry>> previousRelations =
            previous.Relations
                .GroupBy(relation => relation.SourceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList());

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkReferenceEntry> references =
            new List<LinkReferenceEntry>();
        global::System.Collections.Generic.List<global::Nodalis.Core.Relations.TypedRelationEntry> relations =
            new List<TypedRelationEntry>();

        foreach (LinkTargetEntry source in targets.Where(target =>
                     target.Kind == LinkTargetKind.Document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!changedSourceIds.Contains(
                    source.Id))
            {
                if (previousReferences.TryGetValue(
                        source.Id,
                        out global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkReferenceEntry>? cachedReferences))
                {
                    foreach (LinkReferenceEntry cached in cachedReferences)
                    {
                        LinkResolution resolution = Resolve(
                            resolutionCatalog,
                            cached.RawTarget);

                        references.Add(
                            cached with
                            {
                                TargetId =
                                    resolution.Status == LinkResolutionStatus.Resolved
                                        ? resolution.Target!.Id
                                        : null
                            });
                    }
                }

                if (previousRelations.TryGetValue(
                        source.Id,
                        out global::System.Collections.Generic.List<global::Nodalis.Core.Relations.TypedRelationEntry>? cachedRelations))
                {
                    relations.AddRange(
                        cachedRelations);
                }

                continue;
            }

            string fullPath = Path.Combine(
                _workspaceRoot,
                source.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            if (!File.Exists(
                    fullPath))
            {
                continue;
            }

            string markdown = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);
            counters.ParsedDocuments++;

            string[] lines = markdown
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

            for (int lineIndex = 0;
                 lineIndex < lines.Length;
                 lineIndex++)
            {
                foreach (MarkdownInline inline in MarkdownInlineParser.Parse(
                             lines[lineIndex]))
                {
                    if (inline.Kind != MarkdownInlineKind.InternalLink ||
                        string.IsNullOrWhiteSpace(
                            inline.Target))
                    {
                        continue;
                    }

                    LinkResolution resolution = Resolve(
                        resolutionCatalog,
                        inline.Target);

                    references.Add(
                        new LinkReferenceEntry
                        {
                            SourceId = source.Id,
                            TargetId =
                                resolution.Status == LinkResolutionStatus.Resolved
                                    ? resolution.Target!.Id
                                    : null,
                            RawTarget = inline.Target.Trim(),
                            LineNumber = lineIndex + 1,
                            Excerpt = BuildExcerpt(
                                lines[lineIndex])
                        });
                }
            }

            foreach (MarkdownRelationEntry relation in MarkdownRelationParser.Parse(
                         markdown))
            {
                relations.Add(
                    new TypedRelationEntry
                    {
                        SourceId = source.Id,
                        TargetId = relation.TargetId,
                        RawTargetId = relation.RawTargetId,
                        RelationType = relation.RelationType,
                        TargetLabel = relation.TargetLabel,
                        LineNumber = relation.LineNumber,
                        Excerpt = BuildExcerpt(
                            relation.RawLine)
                    });
            }
        }

        return new DocumentIndexBuildResult(
            references,
            relations);
    }

    /// <summary>
    /// Matches a document by its stable filesystem location without opening the file.
    /// </summary>
    private static LinkTargetEntry? MatchDocumentByLocation(
            IReadOnlyList<LinkTargetEntry> previousTargets,
            ISet<Guid> matchedPreviousIds,
            string relativePath,
            string scopeIdentity,
            string localRelativePath)
    {
        LinkTargetEntry[] documents = previousTargets
            .Where(target =>
                target.Kind == LinkTargetKind.Document &&
                !matchedPreviousIds.Contains(
                    target.Id))
            .ToArray();

        LinkTargetEntry? byPath = documents.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));

        if (byPath is not null)
        {
            return byPath;
        }

        return documents.FirstOrDefault(target =>
            string.Equals(
                target.ScopeIdentity,
                scopeIdentity,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                target.LocalRelativePath,
                localRelativePath,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Matches a moved document by content hash after a location match was impossible.
    /// </summary>
    private static LinkTargetEntry? MatchDocumentByHash(
            IReadOnlyList<LinkTargetEntry> previousTargets,
            ISet<Guid> matchedPreviousIds,
            string scopeIdentity,
            string contentHash)
    {
        LinkTargetEntry[] documents = previousTargets
            .Where(target =>
                target.Kind == LinkTargetKind.Document &&
                !matchedPreviousIds.Contains(
                    target.Id))
            .ToArray();

        LinkTargetEntry[] sameScopeHash = documents
            .Where(target =>
                string.Equals(
                    target.ScopeIdentity,
                    scopeIdentity,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    target.ContentHash,
                    contentHash,
                    StringComparison.Ordinal))
            .ToArray();

        if (sameScopeHash.Length == 1)
        {
            return sameScopeHash[0];
        }

        LinkTargetEntry[] globalHash = documents
            .Where(target =>
                string.Equals(
                    target.ContentHash,
                    contentHash,
                    StringComparison.Ordinal))
            .ToArray();

        return globalHash.Length == 1
            ? globalHash[0]
            : null;
    }

    /// <summary>
    /// Performs the <c>MergeAliases</c> operation.
    /// </summary>
    /// <param name="existing">The <c>existing</c> value.</param>
    /// <param name="candidates">The <c>candidates</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> MergeAliases(
            IEnumerable<string> existing,
            params string?[] candidates)
    {
        global::System.Collections.Generic.HashSet<string> aliases = new HashSet<string>(
            existing.Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.CurrentCultureIgnoreCase);

        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                aliases.Add(candidate.Trim());
            }
        }

        return aliases
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Performs the <c>BuildResolution</c> operation.
    /// </summary>
    /// <param name="candidates">The <c>candidates</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static LinkResolution BuildResolution(
            IReadOnlyList<LinkTargetEntry> candidates)
    {
        if (candidates.Count == 1)
        {
            return new LinkResolution
            {
                Status = LinkResolutionStatus.Resolved,
                Target = candidates[0],
                Candidates = [candidates[0]]
            };
        }

        return new LinkResolution
        {
            Status = candidates.Count == 0
                ? LinkResolutionStatus.Missing
                : LinkResolutionStatus.Ambiguous,
            Candidates = candidates.ToList()
        };
    }

    /// <summary>
    /// Performs the <c>ToLinkTargetKind</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static LinkTargetKind ToLinkTargetKind(
            WorkspaceNodeKind kind) =>
            kind switch
            {
                WorkspaceNodeKind.Application => LinkTargetKind.Application,
                WorkspaceNodeKind.Module => LinkTargetKind.Module,
                WorkspaceNodeKind.Project => LinkTargetKind.Project,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Unsupported link target kind.")
            };

    /// <summary>
    /// Performs the <c>ComputeHashAsync</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<string> ComputeHashAsync(
            string path,
            CancellationToken cancellationToken)
    {
        await using global::System.IO.FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            useAsync: true);

        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Performs the <c>NormalizeRelativePath</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeRelativePath(string path) =>
            path.Replace(
                Path.DirectorySeparatorChar,
                '/');

    /// <summary>
    /// Performs the <c>BuildExcerpt</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string BuildExcerpt(string line)
    {
        string trimmed = line.Trim();

        return trimmed.Length <= 220
            ? trimmed
            : trimmed[..217] + "…";
    }
    private sealed class RefreshCounters
    {
        public int HashedDocuments { get; set; }

        public int ParsedDocuments { get; set; }
    }

    private sealed record DocumentIndexBuildResult(
        List<LinkReferenceEntry> References,
        List<TypedRelationEntry> Relations);

}
