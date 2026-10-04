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
        global::Nodalis.Core.Links.LinkIndexCatalog previous = await LoadExistingUnsafeAsync(
            cancellationToken);

        global::Nodalis.Core.Navigation.WorkspaceNavigationNode navigation = await _navigationBuilder.BuildAsync(
            _workspaceRoot,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkTargetEntry> targets = new List<LinkTargetEntry>();
        global::System.Collections.Generic.HashSet<global::System.Guid> matchedPreviousIds = new HashSet<Guid>();

        await CollectTargetsAsync(
            navigation,
            breadcrumb: [],
            scopeIdentity: $"workspace:{navigation.Id:D}",
            scopeRootPath: _workspaceRoot,
            previous.Targets,
            matchedPreviousIds,
            targets,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkReferenceEntry> references = await BuildReferencesAsync(
            targets,
            cancellationToken);

        global::Nodalis.Core.Links.LinkIndexCatalog catalog = new LinkIndexCatalog
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

            string contentHash = await ComputeHashAsync(
                node.FullPath,
                cancellationToken);

            global::Nodalis.Core.Links.LinkTargetEntry? existing = MatchDocument(
                previousTargets,
                matchedPreviousIds,
                relativePath,
                currentScopeIdentity,
                localRelativePath,
                contentHash);

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
                cancellationToken);
        }
    }

    /// <summary>
    /// Performs the <c>BuildReferencesAsync</c> operation.
    /// </summary>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
private async Task<List<LinkReferenceEntry>> BuildReferencesAsync(
        IReadOnlyList<LinkTargetEntry> targets,
        CancellationToken cancellationToken)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog catalog = new LinkIndexCatalog
        {
            Targets = targets.ToList()
        };

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.LinkReferenceEntry> references = new List<LinkReferenceEntry>();

        foreach (global::Nodalis.Core.Links.LinkTargetEntry source in targets.Where(target =>
                     target.Kind == LinkTargetKind.Document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = Path.Combine(
                _workspaceRoot,
                source.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                continue;
            }

            string[] lines = await File.ReadAllLinesAsync(
                fullPath,
                cancellationToken);

            for (int lineIndex = 0;
                 lineIndex < lines.Length;
                 lineIndex++)
            {
                foreach (global::Nodalis.Core.Markdown.MarkdownInline inline in MarkdownInlineParser.Parse(
                             lines[lineIndex]))
                {
                    if (inline.Kind != MarkdownInlineKind.InternalLink ||
                        string.IsNullOrWhiteSpace(inline.Target))
                    {
                        continue;
                    }

                    global::Nodalis.Core.Links.LinkResolution resolution = Resolve(
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

    /// <summary>
    /// Performs the <c>MatchDocument</c> operation.
    /// </summary>
    /// <param name="previousTargets">The <c>previousTargets</c> value.</param>
    /// <param name="matchedPreviousIds">The <c>matchedPreviousIds</c> value.</param>
    /// <param name="relativePath">The <c>relativePath</c> value.</param>
    /// <param name="scopeIdentity">The <c>scopeIdentity</c> value.</param>
    /// <param name="localRelativePath">The <c>localRelativePath</c> value.</param>
    /// <param name="contentHash">The <c>contentHash</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static LinkTargetEntry? MatchDocument(
        IReadOnlyList<LinkTargetEntry> previousTargets,
        ISet<Guid> matchedPreviousIds,
        string relativePath,
        string scopeIdentity,
        string localRelativePath,
        string contentHash)
    {
        global::Nodalis.Core.Links.LinkTargetEntry[] documents = previousTargets
            .Where(target =>
                target.Kind == LinkTargetKind.Document &&
                !matchedPreviousIds.Contains(target.Id))
            .ToArray();

        global::Nodalis.Core.Links.LinkTargetEntry? byPath = documents.FirstOrDefault(target =>
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));

        if (byPath is not null)
        {
            return byPath;
        }

        global::Nodalis.Core.Links.LinkTargetEntry? byStableScope = documents.FirstOrDefault(target =>
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

        global::Nodalis.Core.Links.LinkTargetEntry[] sameScopeHash = documents
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

        global::Nodalis.Core.Links.LinkTargetEntry[] globalHash = documents
            .Where(target => string.Equals(
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
}
