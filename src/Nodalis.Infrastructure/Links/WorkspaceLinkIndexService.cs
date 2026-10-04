using System.Security.Cryptography;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Links;

public sealed class WorkspaceLinkIndexService
{
    private readonly string _workspaceRoot;
    private readonly string _indexPath;
    private readonly WorkspaceNavigationBuilder _navigationBuilder = new();

    public WorkspaceLinkIndexService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _indexPath = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.LinkIndexFileName);
    }

    public async Task<LinkIndexCatalog> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_indexPath))
        {
            return await RefreshAsync(cancellationToken);
        }

        try
        {
            var catalog = await AtomicJsonFile.ReadAsync<LinkIndexCatalog>(
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

    public async Task<LinkIndexCatalog> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var previous = await LoadExistingUnsafeAsync(
            cancellationToken);

        var navigation = await _navigationBuilder.BuildAsync(
            _workspaceRoot,
            cancellationToken);

        var targets = new List<LinkTargetEntry>();
        var matchedPreviousIds = new HashSet<Guid>();

        await CollectTargetsAsync(
            navigation,
            breadcrumb: [],
            scopeIdentity: $"workspace:{navigation.Id:D}",
            scopeRootPath: _workspaceRoot,
            previous.Targets,
            matchedPreviousIds,
            targets,
            cancellationToken);

        var references = await BuildReferencesAsync(
            targets,
            cancellationToken);

        var catalog = new LinkIndexCatalog
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Targets = targets
                .OrderBy(target => target.QualifiedName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            References = references
        };

        await AtomicJsonFile.WriteAsync(
            _indexPath,
            catalog,
            cancellationToken);

        return catalog;
    }

    public async Task<LinkResolution> ResolveAsync(
        string rawTarget,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(cancellationToken);
        return Resolve(catalog, rawTarget);
    }

    public static LinkResolution Resolve(
        LinkIndexCatalog catalog,
        string rawTarget)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTarget);

        var target = rawTarget.Trim();

        if (Guid.TryParse(target, out var targetId))
        {
            var byId = catalog.Targets
                .Where(candidate => candidate.Id == targetId)
                .ToArray();

            return BuildResolution(byId);
        }

        var qualified = catalog.Targets
            .Where(candidate => string.Equals(
                candidate.QualifiedName,
                target,
                StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (qualified.Length > 0)
        {
            return BuildResolution(qualified);
        }

        var currentNames = catalog.Targets
            .Where(candidate => string.Equals(
                candidate.DisplayName,
                target,
                StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (currentNames.Length > 0)
        {
            return BuildResolution(currentNames);
        }

        var aliases = catalog.Targets
            .Where(candidate => candidate.Aliases.Any(alias =>
                string.Equals(
                    alias,
                    target,
                    StringComparison.CurrentCultureIgnoreCase)))
            .ToArray();

        return BuildResolution(aliases);
    }

    public async Task<LinkTargetEntry?> FindByPathAsync(
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(cancellationToken);
        var relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(fullPath)));

        return catalog.Targets.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<LinkTargetEntry>> GetSuggestionsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(cancellationToken);
        var normalized = query.Trim();

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

    public async Task<IReadOnlyList<BacklinkEntry>> GetBacklinksAsync(
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(cancellationToken);
        var targetsById = catalog.Targets.ToDictionary(target => target.Id);

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

    public async Task RegisterRenameAsync(
        string oldFullPath,
        string newFullPath,
        string oldDisplayName,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(cancellationToken);

        var oldRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(oldFullPath)));

        var newRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(newFullPath)));

        var existing = catalog.Targets.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                oldRelativePath,
                StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            await RefreshAsync(cancellationToken);
            return;
        }

        var aliases = MergeAliases(
            existing.Aliases,
            oldDisplayName,
            existing.DisplayName,
            existing.QualifiedName);

        var updated = existing with
        {
            RelativePath = newRelativePath,
            Aliases = aliases
        };

        var targets = catalog.Targets
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

    private async Task CollectTargetsAsync(
        WorkspaceNavigationNode node,
        IReadOnlyList<string> breadcrumb,
        string scopeIdentity,
        string scopeRootPath,
        IReadOnlyList<LinkTargetEntry> previousTargets,
        ISet<Guid> matchedPreviousIds,
        ICollection<LinkTargetEntry> targets,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var includeInBreadcrumb = node.Kind is
            WorkspaceNodeKind.Global or
            WorkspaceNodeKind.Application or
            WorkspaceNodeKind.Module or
            WorkspaceNodeKind.Project or
            WorkspaceNodeKind.Section or
            WorkspaceNodeKind.Folder;

        var currentBreadcrumb = includeInBreadcrumb
            ? breadcrumb.Append(node.DisplayName).ToArray()
            : breadcrumb.ToArray();

        var currentScopeIdentity = scopeIdentity;
        var currentScopeRootPath = scopeRootPath;

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
            var relativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    node.FullPath));

            var qualifiedName = string.Join(
                " / ",
                currentBreadcrumb);

            var existing = previousTargets.FirstOrDefault(target =>
                target.Id == node.Id);

            var aliases = existing is null
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
            var relativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    node.FullPath));

            var localRelativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    currentScopeRootPath,
                    node.FullPath));

            var contentHash = await ComputeHashAsync(
                node.FullPath,
                cancellationToken);

            var existing = MatchDocument(
                previousTargets,
                matchedPreviousIds,
                relativePath,
                currentScopeIdentity,
                localRelativePath,
                contentHash);

            var displayName = node.DisplayName;
            var qualifiedName = string.Join(
                " / ",
                currentBreadcrumb.Append(displayName));

            var aliases = existing is null
                ? []
                : MergeAliases(
                    existing.Aliases,
                    existing.DisplayName,
                    existing.QualifiedName);

            var id = existing?.Id ?? Guid.NewGuid();

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
                Aliases = aliases
            });

            if (existing is not null)
            {
                matchedPreviousIds.Add(existing.Id);
            }
        }

        foreach (var child in node.Children)
        {
            await CollectTargetsAsync(
                child,
                currentBreadcrumb,
                currentScopeIdentity,
                currentScopeRootPath,
                previousTargets,
                matchedPreviousIds,
                targets,
                cancellationToken);
        }
    }

    private async Task<List<LinkReferenceEntry>> BuildReferencesAsync(
        IReadOnlyList<LinkTargetEntry> targets,
        CancellationToken cancellationToken)
    {
        var catalog = new LinkIndexCatalog
        {
            Targets = targets.ToList()
        };

        var references = new List<LinkReferenceEntry>();

        foreach (var source in targets.Where(target =>
                     target.Kind == LinkTargetKind.Document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(
                _workspaceRoot,
                source.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var lines = await File.ReadAllLinesAsync(
                fullPath,
                cancellationToken);

            for (var lineIndex = 0;
                 lineIndex < lines.Length;
                 lineIndex++)
            {
                foreach (var inline in MarkdownInlineParser.Parse(
                             lines[lineIndex]))
                {
                    if (inline.Kind != MarkdownInlineKind.InternalLink ||
                        string.IsNullOrWhiteSpace(inline.Target))
                    {
                        continue;
                    }

                    var resolution = Resolve(
                        catalog,
                        inline.Target);

                    references.Add(new LinkReferenceEntry
                    {
                        SourceId = source.Id,
                        TargetId = resolution.Status == LinkResolutionStatus.Resolved
                            ? resolution.Target!.Id
                            : null,
                        RawTarget = inline.Target.Trim(),
                        LineNumber = lineIndex + 1,
                        Excerpt = BuildExcerpt(lines[lineIndex])
                    });
                }
            }
        }

        return references;
    }

    private static LinkTargetEntry? MatchDocument(
        IReadOnlyList<LinkTargetEntry> previousTargets,
        ISet<Guid> matchedPreviousIds,
        string relativePath,
        string scopeIdentity,
        string localRelativePath,
        string contentHash)
    {
        var documents = previousTargets
            .Where(target =>
                target.Kind == LinkTargetKind.Document &&
                !matchedPreviousIds.Contains(target.Id))
            .ToArray();

        var byPath = documents.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));

        if (byPath is not null)
        {
            return byPath;
        }

        var byStableScope = documents.FirstOrDefault(target =>
            string.Equals(
                target.ScopeIdentity,
                scopeIdentity,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                target.LocalRelativePath,
                localRelativePath,
                StringComparison.OrdinalIgnoreCase));

        if (byStableScope is not null)
        {
            return byStableScope;
        }

        var sameScopeHash = documents
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

        var globalHash = documents
            .Where(target => string.Equals(
                target.ContentHash,
                contentHash,
                StringComparison.Ordinal))
            .ToArray();

        return globalHash.Length == 1
            ? globalHash[0]
            : null;
    }

    private static List<string> MergeAliases(
        IEnumerable<string> existing,
        params string?[] candidates)
    {
        var aliases = new HashSet<string>(
            existing.Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.CurrentCultureIgnoreCase);

        foreach (var candidate in candidates)
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

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(
            Path.DirectorySeparatorChar,
            '/');

    private static string BuildExcerpt(string line)
    {
        var trimmed = line.Trim();

        return trimmed.Length <= 220
            ? trimmed
            : trimmed[..217] + "…";
    }
}
