using Nodalis.Core.Domain;
using Nodalis.Core.Search;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Search;

public sealed class WorkspaceSearchService
{
    public async Task<SearchResultSet> SearchAsync(
        string workspaceRoot,
        string? contextPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var root = Path.GetFullPath(workspaceRoot);
        var contextDirectory = ResolveContextDirectory(
            root,
            contextPath);

        var projectDirectory = FindAncestorContaining(
            contextDirectory,
            root,
            WorkspaceLayout.ProjectManifestFileName);

        var applicationDirectory = FindAncestorContaining(
            contextDirectory,
            root,
            WorkspaceLayout.ApplicationManifestFileName);

        var results = new SearchResultSet();

        if (projectDirectory is not null)
        {
            results.Project.AddRange(
                await SearchDirectoryAsync(
                    projectDirectory,
                    query,
                    SearchScopeKind.Project,
                    shouldSkipDirectory: directory =>
                        !string.Equals(
                            directory,
                            projectDirectory,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            Path.GetFileName(directory),
                            WorkspaceLayout.SubProjectsDirectoryName,
                            StringComparison.OrdinalIgnoreCase),
                    cancellationToken));
        }

        if (applicationDirectory is not null)
        {
            results.Application.AddRange(
                await SearchDirectoryAsync(
                    applicationDirectory,
                    query,
                    SearchScopeKind.Application,
                    shouldSkipDirectory: directory =>
                    {
                        var name = Path.GetFileName(directory);

                        return name.Equals(
                                   WorkspaceLayout.ProjectsDirectoryName,
                                   StringComparison.OrdinalIgnoreCase) ||
                               name.Equals(
                                   WorkspaceLayout.SubProjectsDirectoryName,
                                   StringComparison.OrdinalIgnoreCase);
                    },
                    cancellationToken));
        }

        results.Global.AddRange(
            await SearchDirectoryAsync(
                root,
                query,
                SearchScopeKind.Global,
                shouldSkipDirectory: directory =>
                {
                    if (string.Equals(
                            directory,
                            root,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    var name = Path.GetFileName(directory);

                    return name.Equals(
                               WorkspaceLayout.ApplicationsDirectoryName,
                               StringComparison.OrdinalIgnoreCase) ||
                           name.Equals(
                               WorkspaceLayout.TemplatesDirectoryName,
                               StringComparison.OrdinalIgnoreCase) ||
                           name.Equals(
                               WorkspaceLayout.AttachmentsDirectoryName,
                               StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith(
                               ".",
                               StringComparison.Ordinal);
                },
                cancellationToken));

        Sort(results.Project);
        Sort(results.Application);
        Sort(results.Global);

        return results;
    }

    private static async Task<List<SearchResult>> SearchDirectoryAsync(
        string root,
        string query,
        SearchScopeKind scope,
        Func<string, bool> shouldSkipDirectory,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = pending.Pop();

            if (shouldSkipDirectory(directory))
            {
                continue;
            }

            foreach (var child in Directory
                         .EnumerateDirectories(directory)
                         .OrderBy(
                             path => Path.GetFileName(path),
                             StringComparer.CurrentCultureIgnoreCase))
            {
                if (!shouldSkipDirectory(child))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory
                         .EnumerateFiles(
                             directory,
                             "*.md",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(
                             path => Path.GetFileName(path),
                             StringComparer.CurrentCultureIgnoreCase))
            {
                await SearchFileAsync(
                    file,
                    query,
                    scope,
                    results,
                    cancellationToken);
            }
        }

        return results;
    }

    private static async Task SearchFileAsync(
        string file,
        string query,
        SearchScopeKind scope,
        ICollection<SearchResult> results,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(
            file,
            cancellationToken);

        var fileNameMatches = Path
            .GetFileNameWithoutExtension(file)
            .Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase);

        if (fileNameMatches)
        {
            results.Add(new SearchResult
            {
                Scope = scope,
                FilePath = file,
                DisplayName = Path.GetFileNameWithoutExtension(file),
                LineNumber = 1,
                Excerpt = "Correspondance dans le nom du fichier"
            });
        }

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Scope = scope,
                FilePath = file,
                DisplayName = Path.GetFileNameWithoutExtension(file),
                LineNumber = index + 1,
                Excerpt = BuildExcerpt(
                    lines[index],
                    query)
            });
        }
    }

    private static string BuildExcerpt(
        string line,
        string query)
    {
        var trimmed = line.Trim();

        if (trimmed.Length <= 180)
        {
            return trimmed;
        }

        var index = trimmed.IndexOf(
            query,
            StringComparison.CurrentCultureIgnoreCase);

        var start = Math.Max(
            0,
            index - 70);

        var length = Math.Min(
            180,
            trimmed.Length - start);

        var excerpt = trimmed.Substring(
            start,
            length);

        return
            (start > 0 ? "…" : string.Empty) +
            excerpt +
            (start + length < trimmed.Length ? "…" : string.Empty);
    }

    private static string ResolveContextDirectory(
        string workspaceRoot,
        string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return workspaceRoot;
        }

        var fullPath = Path.GetFullPath(contextPath);

        if (File.Exists(fullPath) ||
            Path.HasExtension(fullPath))
        {
            return Path.GetDirectoryName(fullPath)
                ?? workspaceRoot;
        }

        return Directory.Exists(fullPath)
            ? fullPath
            : workspaceRoot;
    }

    private static string? FindAncestorContaining(
        string startDirectory,
        string root,
        string fileName)
    {
        for (var current = startDirectory;
             current is not null && IsInsideOrEqual(current, root);
             current = Directory.GetParent(current)?.FullName)
        {
            if (File.Exists(Path.Combine(
                    current,
                    fileName)))
            {
                return current;
            }

            if (string.Equals(
                    current,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return null;
    }

    private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullCandidate,
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void Sort(
        List<SearchResult> results)
    {
        results.Sort(
            (left, right) =>
            {
                var byFile = StringComparer.CurrentCultureIgnoreCase.Compare(
                    left.DisplayName,
                    right.DisplayName);

                return byFile != 0
                    ? byFile
                    : left.LineNumber.CompareTo(right.LineNumber);
            });
    }
}
