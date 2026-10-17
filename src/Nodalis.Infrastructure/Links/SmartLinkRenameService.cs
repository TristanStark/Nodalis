using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Links;

/// <summary>
/// Plans and applies explicit textual internal-link rewrites around an element rename.
/// Stable IDs and aliases remain responsible for link correctness when rewriting is declined.
/// </summary>
public sealed class SmartLinkRenameService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;

    /// <summary>
    /// Initializes the smart rename service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    public SmartLinkRenameService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _linkIndex =
            new WorkspaceLinkIndexService(
                _workspaceRoot);
    }

    /// <summary>
    /// Detects source documents that explicitly use the previous display name as an internal-link target.
    /// </summary>
    /// <param name="targetId">The stable target identifier.</param>
    /// <param name="oldDisplayName">The previous display name.</param>
    /// <param name="newDisplayName">The proposed display name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A previewable, source-hash-protected rewrite plan.</returns>
    public async Task<RenameLinkRewritePlan> AnalyzeAsync(
            Guid targetId,
            string oldDisplayName,
            string newDisplayName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            oldDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            newDisplayName);

        LinkIndexCatalog catalog =
            await _linkIndex.RefreshAsync(
                cancellationToken);

        LinkTargetEntry target =
            catalog.Targets.FirstOrDefault(candidate =>
                candidate.Id == targetId) ??
            throw new InvalidOperationException(
                "The rename target is no longer indexed.");

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Links.LinkTargetEntry> sources =
            catalog.Targets
                .Where(candidate =>
                    candidate.Kind == LinkTargetKind.Document)
                .ToDictionary(candidate =>
                    candidate.Id);

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.RenameLinkFilePlan> files =
            new List<RenameLinkFilePlan>();

        foreach (IGrouping<Guid, LinkReferenceEntry> group in catalog.References
                     .Where(reference =>
                         reference.TargetId == target.Id &&
                         string.Equals(
                             reference.RawTarget.Trim(),
                             oldDisplayName.Trim(),
                             StringComparison.CurrentCultureIgnoreCase))
                     .GroupBy(reference =>
                         reference.SourceId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!sources.TryGetValue(
                    group.Key,
                    out LinkTargetEntry? source))
            {
                continue;
            }

            string fullPath =
                ResolveWorkspacePath(
                    source.RelativePath);

            if (!File.Exists(
                    fullPath))
            {
                continue;
            }

            string[] lines =
                await File.ReadAllLinesAsync(
                    fullPath,
                    cancellationToken);

            global::System.Collections.Generic.List<global::Nodalis.Core.Links.RenameLinkChange> changes =
                new List<RenameLinkChange>();

            foreach (int lineNumber in group
                         .Select(reference =>
                             reference.LineNumber)
                         .Distinct()
                         .OrderBy(value =>
                             value))
            {
                int index =
                    lineNumber - 1;

                if (index < 0 ||
                    index >= lines.Length)
                {
                    continue;
                }

                string before =
                    lines[index];
                string after =
                    RewriteInternalTargets(
                        before,
                        oldDisplayName,
                        newDisplayName);

                if (string.Equals(
                        before,
                        after,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                changes.Add(
                    new RenameLinkChange
                    {
                        LineNumber =
                            lineNumber,
                        Before =
                            before,
                        After =
                            after
                    });
            }

            if (changes.Count == 0)
            {
                continue;
            }

            files.Add(
                new RenameLinkFilePlan
                {
                    SourceId =
                        source.Id,
                    SourceDisplayName =
                        source.DisplayName,
                    SourceRelativePath =
                        source.RelativePath,
                    SourceContentHash =
                        source.ContentHash ??
                        await ComputeHashAsync(
                            fullPath,
                            cancellationToken),
                    Changes =
                        changes
                });
        }

        return new RenameLinkRewritePlan
        {
            TargetId =
                targetId,
            OldDisplayName =
                oldDisplayName.Trim(),
            NewDisplayName =
                newDisplayName.Trim(),
            Files =
                files
                    .OrderBy(
                        file => file.SourceRelativePath,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
        };
    }

    /// <summary>
    /// Applies selected file rewrites as an all-or-rollback operation.
    /// Current source IDs are resolved again so parent-folder moves do not invalidate the plan.
    /// </summary>
    /// <param name="plan">The plan created before the rename.</param>
    /// <param name="selectedSourceIds">The source documents explicitly selected by the user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rewritten files.</returns>
    public async Task<int> ApplyAsync(
            RenameLinkRewritePlan plan,
            IReadOnlyCollection<Guid> selectedSourceIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            plan);
        ArgumentNullException.ThrowIfNull(
            selectedSourceIds);

        if (selectedSourceIds.Count == 0)
        {
            return 0;
        }

        global::System.Collections.Generic.HashSet<global::System.Guid> selected =
            new HashSet<Guid>(
                selectedSourceIds);

        LinkIndexCatalog current =
            await _linkIndex.RefreshAsync(
                cancellationToken);

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Links.LinkTargetEntry> currentTargets =
            current.Targets.ToDictionary(target =>
                target.Id);

        global::System.Collections.Generic.List<(string Path, string Original, string Updated)> prepared =
            new List<(string Path, string Original, string Updated)>();

        foreach (RenameLinkFilePlan filePlan in plan.Files.Where(file =>
                     selected.Contains(
                         file.SourceId)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!currentTargets.TryGetValue(
                    filePlan.SourceId,
                    out LinkTargetEntry? source))
            {
                throw new InvalidOperationException(
                    $"La source '{filePlan.SourceDisplayName}' n'existe plus.");
            }

            string fullPath =
                ResolveWorkspacePath(
                    source.RelativePath);

            if (!File.Exists(
                    fullPath))
            {
                throw new FileNotFoundException(
                    "A source selected for smart rename no longer exists.",
                    fullPath);
            }

            string currentHash =
                await ComputeHashAsync(
                    fullPath,
                    cancellationToken);

            if (!string.Equals(
                    currentHash,
                    filePlan.SourceContentHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Le fichier '{source.DisplayName}' a changé depuis la prévisualisation. " +
                    "La réécriture est annulée.");
            }

            string original =
                await File.ReadAllTextAsync(
                    fullPath,
                    cancellationToken);

            string updated =
                RewriteInternalTargets(
                    original,
                    plan.OldDisplayName,
                    plan.NewDisplayName);

            if (string.Equals(
                    original,
                    updated,
                    StringComparison.Ordinal))
            {
                continue;
            }

            prepared.Add(
                (
                    fullPath,
                    original,
                    updated
                ));
        }

        global::System.Collections.Generic.List<(string Path, string Original)> written =
            new List<(string Path, string Original)>();

        try
        {
            foreach ((string path, string original, string updated) in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await AtomicFileWriter.WriteAllTextAsync(
                    path,
                    updated,
                    cancellationToken);

                written.Add(
                    (
                        path,
                        original
                    ));
            }
        }
        catch
        {
            global::System.Collections.Generic.List<Exception> rollbackErrors =
                new List<Exception>();

            foreach ((string path, string original) in written.AsEnumerable().Reverse())
            {
                try
                {
                    await AtomicFileWriter.WriteAllTextAsync(
                        path,
                        original,
                        CancellationToken.None);
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException)
                {
                    rollbackErrors.Add(
                        exception);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    "La réécriture a échoué et au moins un rollback n'a pas pu être appliqué.",
                    rollbackErrors);
            }

            throw;
        }

        if (prepared.Count > 0)
        {
            await _linkIndex.RefreshAsync(
                cancellationToken);
        }

        return prepared.Count;
    }

    /// <summary>
    /// Rewrites only internal-link target names while preserving optional aliases and surrounding text.
    /// </summary>
    /// <param name="text">The Markdown text or source line.</param>
    /// <param name="oldDisplayName">The previous target name.</param>
    /// <param name="newDisplayName">The new target name.</param>
    /// <returns>The rewritten text.</returns>
    public static string RewriteInternalTargets(
            string text,
            string oldDisplayName,
            string newDisplayName)
    {
        ArgumentNullException.ThrowIfNull(
            text);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            oldDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            newDisplayName);

        return Regex.Replace(
            text,
            @"\[\[(?<target>[^\]\|\r\n]+)(?<alias>\|[^\]\r\n]+)?\]\]",
            match =>
            {
                string rawTarget =
                    match.Groups["target"].Value;

                if (!string.Equals(
                        rawTarget.Trim(),
                        oldDisplayName.Trim(),
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    return match.Value;
                }

                string alias =
                    match.Groups["alias"].Success
                        ? match.Groups["alias"].Value
                        : string.Empty;

                return
                    $"[[{newDisplayName.Trim()}{alias}]]";
            },
            RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Resolves a workspace-relative path while preventing path traversal.
    /// </summary>
    /// <param name="relativePath">The normalized relative path.</param>
    /// <returns>The absolute workspace-contained path.</returns>
    private string ResolveWorkspacePath(
            string relativePath)
    {
        string fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        string check =
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath);

        if (check.Equals(
                "..",
                StringComparison.Ordinal) ||
            check.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A rename rewrite path escapes the workspace.");
        }

        return fullPath;
    }

    /// <summary>
    /// Computes the SHA-256 hash used by the derived link index to detect concurrent source changes.
    /// </summary>
    /// <param name="path">The source file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The uppercase SHA-256 hash.</returns>
    private static async Task<string> ComputeHashAsync(
            string path,
            CancellationToken cancellationToken)
    {
        await using global::System.IO.FileStream stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                useAsync: true);

        byte[] hash =
            await SHA256.HashDataAsync(
                stream,
                cancellationToken);

        return Convert.ToHexString(
            hash);
    }
}
