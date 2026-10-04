using Nodalis.Core.Domain;
using Nodalis.Core.Search;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Search;

public sealed class WorkspaceSearchService
{
    /// <summary>
    /// Performs the <c>SearchAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<SearchResultSet> SearchAsync(
        string workspaceRoot,
        string? contextPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        string root = Path.GetFullPath(workspaceRoot);
        string contextDirectory = ResolveContextDirectory(
            root,
            contextPath);

        string? projectDirectory = FindAncestorContaining(
            contextDirectory,
            root,
            WorkspaceLayout.ProjectManifestFileName);

        string? applicationDirectory = FindAncestorContaining(
            contextDirectory,
            root,
            WorkspaceLayout.ApplicationManifestFileName);

        global::Nodalis.Core.Search.SearchResultSet results = new SearchResultSet();

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
                        string name = Path.GetFileName(directory);

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

                    string name = Path.GetFileName(directory);

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

    /// <summary>
    /// Performs the <c>SearchDirectoryAsync</c> operation.
    /// </summary>
    /// <param name="root">The <c>root</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="shouldSkipDirectory">The <c>shouldSkipDirectory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static async Task<List<SearchResult>> SearchDirectoryAsync(
        string root,
        string query,
        SearchScopeKind scope,
        Func<string, bool> shouldSkipDirectory,
        CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Search.SearchResult> results = new List<SearchResult>();
        global::System.Collections.Generic.Stack<string> pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directory = pending.Pop();

            if (shouldSkipDirectory(directory))
            {
                continue;
            }

            foreach (string child in Directory
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

            foreach (string file in Directory
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

    /// <summary>
    /// Performs the <c>SearchFileAsync</c> operation.
    /// </summary>
    /// <param name="file">The <c>file</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="results">The <c>results</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static async Task SearchFileAsync(
        string file,
        string query,
        SearchScopeKind scope,
        ICollection<SearchResult> results,
        CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(
            file,
            cancellationToken);

        bool fileNameMatches = Path
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

        for (int index = 0; index < lines.Length; index++)
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

    /// <summary>
    /// Performs the <c>BuildExcerpt</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string BuildExcerpt(
        string line,
        string query)
    {
        string trimmed = line.Trim();

        if (trimmed.Length <= 180)
        {
            return trimmed;
        }

        int index = trimmed.IndexOf(
            query,
            StringComparison.CurrentCultureIgnoreCase);

        int start = Math.Max(
            0,
            index - 70);

        int length = Math.Min(
            180,
            trimmed.Length - start);

        string excerpt = trimmed.Substring(
            start,
            length);

        return
            (start > 0 ? "…" : string.Empty) +
            excerpt +
            (start + length < trimmed.Length ? "…" : string.Empty);
    }

    /// <summary>
    /// Performs the <c>ResolveContextDirectory</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ResolveContextDirectory(
        string workspaceRoot,
        string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return workspaceRoot;
        }

        string fullPath = Path.GetFullPath(contextPath);

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

    /// <summary>
    /// Performs the <c>FindAncestorContaining</c> operation.
    /// </summary>
    /// <param name="startDirectory">The <c>startDirectory</c> value.</param>
    /// <param name="root">The <c>root</c> value.</param>
    /// <param name="fileName">The <c>fileName</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string? FindAncestorContaining(
        string startDirectory,
        string root,
        string fileName)
    {
        for (string? current = startDirectory;
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

    /// <summary>
    /// Performs the <c>IsInsideOrEqual</c> operation.
    /// </summary>
    /// <param name="candidate">The <c>candidate</c> value.</param>
    /// <param name="root">The <c>root</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        string fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string fullRoot = Path.GetFullPath(root)
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

    /// <summary>
    /// Performs the <c>Sort</c> operation.
    /// </summary>
    /// <param name="results">The <c>results</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static void Sort(
        List<SearchResult> results)
    {
        results.Sort(
            (left, right) =>
            {
                int byFile = StringComparer.CurrentCultureIgnoreCase.Compare(
                    left.DisplayName,
                    right.DisplayName);

                return byFile != 0
                    ? byFile
                    : left.LineNumber.CompareTo(right.LineNumber);
            });
    }
}
