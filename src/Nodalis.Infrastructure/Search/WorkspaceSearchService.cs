using System.Globalization;
using System.Text.Json;
using Nodalis.Core.Markdown;
using Nodalis.Core.Search;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Search;

/// <summary>
/// Performs dependency-free ranked Markdown search across the local workspace.
/// </summary>
public sealed class WorkspaceSearchService
{
    private const int MaximumMatchesPerFile = 8;

    private static readonly string[] StandardPropertyKeys =
    [
        "status",
        "owner",
        "version",
        "environment",
        "type",
        "tag",
        "tags"
    ];

    /// <summary>
    /// Searches using the historical exact-substring behavior.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="contextPath">The current context.</param>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Results grouped by contextual scope.</returns>
    public Task<SearchResultSet> SearchAsync(
            string workspaceRoot,
            string? contextPath,
            string query,
            CancellationToken cancellationToken = default) =>
        SearchAsync(
            workspaceRoot,
            contextPath,
            query,
            SearchMatchMode.Exact,
            cancellationToken);

    /// <summary>
    /// Searches all searchable Markdown files using exact or fuzzy matching.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="contextPath">The current context.</param>
    /// <param name="query">Free text and optional filters.</param>
    /// <param name="mode">The text matching mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Ranked results grouped by contextual scope.</returns>
    public async Task<SearchResultSet> SearchAsync(
            string workspaceRoot,
            string? contextPath,
            string query,
            SearchMatchMode mode,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            query);

        string root =
            Path.GetFullPath(
                workspaceRoot);

        string contextDirectory =
            ResolveContextDirectory(
                root,
                contextPath);

        string? currentProjectDirectory =
            FindAncestorContaining(
                contextDirectory,
                root,
                WorkspaceLayout.ProjectManifestFileName);

        string? currentApplicationDirectory =
            FindAncestorContaining(
                contextDirectory,
                root,
                WorkspaceLayout.ApplicationManifestFileName);

        SearchQueryPlan plan =
            ParseQuery(
                query);

        SearchResultSet results =
            new SearchResultSet();

        Dictionary<string, string?> projectNameCache =
            new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string file in EnumerateMarkdownFiles(
                     root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string directory =
                Path.GetDirectoryName(
                    file) ??
                root;

            string? fileProjectDirectory =
                FindAncestorContaining(
                    directory,
                    root,
                    WorkspaceLayout.ProjectManifestFileName);

            string? fileApplicationDirectory =
                FindAncestorContaining(
                    directory,
                    root,
                    WorkspaceLayout.ApplicationManifestFileName);

            SearchScopeKind scope =
                ClassifyScope(
                    currentProjectDirectory,
                    currentApplicationDirectory,
                    fileProjectDirectory,
                    fileApplicationDirectory);

            string? projectName =
                GetProjectName(
                    fileProjectDirectory,
                    projectNameCache);

            if (!MatchesProjectFilter(
                    projectName,
                    plan.ProjectFilter) ||
                !MatchesDateFilter(
                    file,
                    plan.ModifiedDateFilter))
            {
                continue;
            }

            IReadOnlyList<SearchResult> fileResults =
                await SearchFileAsync(
                    file,
                    scope,
                    projectName,
                    plan,
                    mode,
                    cancellationToken);

            List<SearchResult> target =
                scope switch
                {
                    SearchScopeKind.Project => results.Project,
                    SearchScopeKind.Application => results.Application,
                    _ => results.Global
                };

            target.AddRange(
                fileResults);
        }

        Sort(
            results.Project);
        Sort(
            results.Application);
        Sort(
            results.Global);

        return results;
    }

    /// <summary>
    /// Searches one Markdown file after path-level filters have matched.
    /// </summary>
    /// <param name="file">The source file.</param>
    /// <param name="scope">The contextual scope.</param>
    /// <param name="projectName">The containing project name.</param>
    /// <param name="plan">The parsed query.</param>
    /// <param name="mode">The text matching mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The best occurrences from this file.</returns>
    private static async Task<IReadOnlyList<SearchResult>> SearchFileAsync(
            string file,
            SearchScopeKind scope,
            string? projectName,
            SearchQueryPlan plan,
            SearchMatchMode mode,
            CancellationToken cancellationToken)
    {
        string markdown =
            await File.ReadAllTextAsync(
                file,
                cancellationToken);

        MarkdownFrontMatterDocument metadata =
            MarkdownFrontMatterParser.Parse(
                markdown);

        if (!MatchesPropertyFilters(
                metadata.Properties,
                plan.PropertyFilters))
        {
            return [];
        }

        string[] lines =
            markdown
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

        if (plan.TextQuery.Length == 0)
        {
            int lineNumber =
                plan.PropertyFilters.Count > 0
                    ? FindPropertyLine(
                        lines,
                        plan.PropertyFilters[0].Key)
                    : 1;

            return
            [
                new SearchResult
                {
                    Scope = scope,
                    FilePath = file,
                    DisplayName = Path.GetFileNameWithoutExtension(file),
                    LineNumber = lineNumber,
                    Excerpt = BuildFilterExcerpt(
                        plan,
                        projectName),
                    Score = 150 + GetScopeBonus(scope),
                    MatchKind = SearchMatchKind.Metadata
                }
            ];
        }

        List<SearchResult> candidates =
            new List<SearchResult>();

        string displayName =
            Path.GetFileNameWithoutExtension(
                file);

        if (SearchTextScorer.TryScore(
                plan.TextQuery,
                displayName,
                mode,
                out double titleQuality))
        {
            candidates.Add(
                CreateResult(
                    scope,
                    file,
                    displayName,
                    1,
                    $"Titre du document · {displayName}",
                    SearchMatchKind.FileName,
                    titleQuality));
        }

        int bodyStart =
            FindBodyStartLineIndex(
                lines);

        bool inFence =
            false;

        for (int index = bodyStart;
             index < lines.Length;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line =
                lines[index];
            string trimmed =
                line.TrimStart();

            bool fenceMarker =
                trimmed.StartsWith(
                    new string(
                        (char)96,
                        3),
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "~~~",
                    StringComparison.Ordinal);

            bool heading =
                !inFence &&
                IsMarkdownHeading(
                    trimmed);

            if (SearchTextScorer.TryScore(
                    plan.TextQuery,
                    line,
                    mode,
                    out double quality))
            {
                SearchMatchKind kind =
                    heading
                        ? SearchMatchKind.Heading
                        : SearchMatchKind.Body;

                candidates.Add(
                    CreateResult(
                        scope,
                        file,
                        displayName,
                        index + 1,
                        BuildExcerpt(
                            line,
                            plan.TextQuery),
                        kind,
                        quality));
            }

            if (fenceMarker)
            {
                inFence =
                    !inFence;
            }
        }

        return candidates
            .OrderByDescending(result =>
                result.Score)
            .ThenBy(result =>
                result.LineNumber)
            .Take(
                MaximumMatchesPerFile)
            .ToArray();
    }

    /// <summary>
    /// Creates a ranked result with fixed title, heading and body weights.
    /// </summary>
    /// <param name="scope">The result scope.</param>
    /// <param name="file">The source file.</param>
    /// <param name="displayName">The document name.</param>
    /// <param name="lineNumber">The source line.</param>
    /// <param name="excerpt">The excerpt.</param>
    /// <param name="kind">The match location.</param>
    /// <param name="quality">The normalized textual quality.</param>
    /// <returns>The ranked result.</returns>
    private static SearchResult CreateResult(
            SearchScopeKind scope,
            string file,
            string displayName,
            int lineNumber,
            string excerpt,
            SearchMatchKind kind,
            double quality)
    {
        double locationWeight =
            kind switch
            {
                SearchMatchKind.FileName => 300,
                SearchMatchKind.Heading => 200,
                SearchMatchKind.Metadata => 150,
                _ => 100
            };

        return new SearchResult
        {
            Scope = scope,
            FilePath = file,
            DisplayName = displayName,
            LineNumber = lineNumber,
            Excerpt = excerpt,
            MatchKind = kind,
            Score =
                locationWeight +
                (quality * 100) +
                GetScopeBonus(
                    scope)
        };
    }

    /// <summary>
    /// Parses free text plus metadata, date and project filters.
    /// </summary>
    /// <param name="query">The raw query.</param>
    /// <returns>The query plan.</returns>
    private static SearchQueryPlan ParseQuery(
            string query)
    {
        List<string> textTerms =
            new List<string>();
        List<SearchPropertyFilter> propertyFilters =
            new List<SearchPropertyFilter>();

        DateOnly? modifiedDate =
            null;
        string? projectFilter =
            null;

        foreach (string token in TokenizeQuery(
                     query))
        {
            int separator =
                token.IndexOf(
                    ':');

            if (separator <= 0 ||
                separator >= token.Length - 1)
            {
                textTerms.Add(
                    token);
                continue;
            }

            string rawKey =
                token[..separator]
                    .Trim();
            string value =
                token[(separator + 1)..]
                    .Trim();

            if (value.Length == 0)
            {
                textTerms.Add(
                    token);
                continue;
            }

            if (string.Equals(
                    rawKey,
                    "date",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (DateOnly.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly parsedDate))
                {
                    modifiedDate =
                        parsedDate;
                    continue;
                }

                textTerms.Add(
                    token);
                continue;
            }

            if (string.Equals(
                    rawKey,
                    "project",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    rawKey,
                    "projet",
                    StringComparison.OrdinalIgnoreCase))
            {
                projectFilter =
                    value;
                continue;
            }

            bool custom =
                rawKey.StartsWith(
                    '@');

            string propertyKey =
                custom
                    ? rawKey[1..]
                        .Trim()
                    : rawKey;

            bool validKey =
                propertyKey.Length > 0 &&
                propertyKey.All(character =>
                    char.IsLetterOrDigit(
                        character) ||
                    character is
                        '-' or
                        '_' or
                        '.');

            bool standard =
                StandardPropertyKeys.Contains(
                    propertyKey,
                    StringComparer.OrdinalIgnoreCase);

            if (validKey &&
                (custom ||
                 standard))
            {
                propertyFilters.Add(
                    new SearchPropertyFilter(
                        propertyKey,
                        value));
                continue;
            }

            textTerms.Add(
                token);
        }

        return new SearchQueryPlan
        {
            TextQuery =
                string.Join(
                    " ",
                    textTerms)
                    .Trim(),
            PropertyFilters =
                propertyFilters,
            ModifiedDateFilter =
                modifiedDate,
            ProjectFilter =
                projectFilter
        };
    }

    /// <summary>
    /// Tokenizes a query while keeping spaces inside double quotes.
    /// </summary>
    /// <param name="query">The raw query.</param>
    /// <returns>Tokens without quote characters.</returns>
    private static IReadOnlyList<string> TokenizeQuery(
            string query)
    {
        List<string> tokens =
            new List<string>();
        global::System.Text.StringBuilder current =
            new global::System.Text.StringBuilder();

        bool quoted =
            false;

        foreach (char character in query)
        {
            if (character == '"')
            {
                quoted =
                    !quoted;
                continue;
            }

            if (char.IsWhiteSpace(
                    character) &&
                !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(
                        current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(
                character);
        }

        if (current.Length > 0)
        {
            tokens.Add(
                current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Applies all front matter filters.
    /// </summary>
    /// <param name="properties">The document properties.</param>
    /// <param name="filters">The requested filters.</param>
    /// <returns><see langword="true"/> when every filter matches.</returns>
    private static bool MatchesPropertyFilters(
            IReadOnlyDictionary<string, string> properties,
            IReadOnlyList<SearchPropertyFilter> filters)
    {
        foreach (SearchPropertyFilter filter in filters)
        {
            bool tagFilter =
                string.Equals(
                    filter.Key,
                    "tag",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    filter.Key,
                    "tags",
                    StringComparison.OrdinalIgnoreCase);

            if (tagFilter)
            {
                if (!properties.TryGetValue(
                        "tags",
                        out string? rawTags) ||
                    !MarkdownFrontMatterParser.ParseTags(
                            rawTags)
                        .Any(tag =>
                            string.Equals(
                                tag,
                                filter.Value,
                                StringComparison.CurrentCultureIgnoreCase)))
                {
                    return false;
                }

                continue;
            }

            if (!properties.TryGetValue(
                    filter.Key,
                    out string? rawValue) ||
                !rawValue.Contains(
                    filter.Value,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies the optional containing-project filter.
    /// </summary>
    /// <param name="projectName">The file project.</param>
    /// <param name="filter">The requested project value.</param>
    /// <returns><see langword="true"/> when the filter matches or is absent.</returns>
    private static bool MatchesProjectFilter(
            string? projectName,
            string? filter) =>
        string.IsNullOrWhiteSpace(
            filter) ||
        (!string.IsNullOrWhiteSpace(
             projectName) &&
         projectName.Contains(
             filter,
             StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Applies the optional UTC filesystem modification-date filter.
    /// </summary>
    /// <param name="file">The source file.</param>
    /// <param name="filter">The requested date.</param>
    /// <returns><see langword="true"/> when the filter matches or is absent.</returns>
    private static bool MatchesDateFilter(
            string file,
            DateOnly? filter)
    {
        if (filter is not DateOnly requestedDate)
        {
            return true;
        }

        DateOnly modifiedDate =
            DateOnly.FromDateTime(
                File.GetLastWriteTimeUtc(
                    file));

        return modifiedDate == requestedDate;
    }

    /// <summary>
    /// Enumerates all searchable Markdown files without entering internal storage directories.
    /// </summary>
    /// <param name="root">The workspace root.</param>
    /// <returns>The Markdown files.</returns>
    private static IEnumerable<string> EnumerateMarkdownFiles(
            string root)
    {
        Stack<string> pending =
            new Stack<string>();
        pending.Push(
            root);

        while (pending.Count > 0)
        {
            string directory =
                pending.Pop();

            string[] children =
                Directory
                    .EnumerateDirectories(
                        directory)
                    .Where(child =>
                        !ShouldSkipDirectory(
                            child))
                    .OrderByDescending(
                        child => child,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

            foreach (string child in children)
            {
                pending.Push(
                    child);
            }

            foreach (string file in Directory
                         .EnumerateFiles(
                             directory,
                             "*.md",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(
                             file => file,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Identifies internal subtrees excluded from search.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns><see langword="true"/> when the subtree must be skipped.</returns>
    private static bool ShouldSkipDirectory(
            string directory)
    {
        string name =
            Path.GetFileName(
                directory);

        return name.StartsWith(
                   ".",
                   StringComparison.Ordinal) ||
               string.Equals(
                   name,
                   WorkspaceLayout.TrashDirectoryName,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   name,
                   WorkspaceLayout.TemplatesDirectoryName,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   name,
                   WorkspaceLayout.AttachmentsDirectoryName,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   name,
                   "Imports",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Classifies a file relative to the active project and application.
    /// </summary>
    /// <param name="currentProjectDirectory">The active project.</param>
    /// <param name="currentApplicationDirectory">The active application.</param>
    /// <param name="fileProjectDirectory">The file project.</param>
    /// <param name="fileApplicationDirectory">The file application.</param>
    /// <returns>The contextual scope.</returns>
    private static SearchScopeKind ClassifyScope(
            string? currentProjectDirectory,
            string? currentApplicationDirectory,
            string? fileProjectDirectory,
            string? fileApplicationDirectory)
    {
        if (SamePath(
                currentProjectDirectory,
                fileProjectDirectory))
        {
            return SearchScopeKind.Project;
        }

        if (SamePath(
                currentApplicationDirectory,
                fileApplicationDirectory))
        {
            return SearchScopeKind.Application;
        }

        return SearchScopeKind.Global;
    }

    /// <summary>
    /// Compares two optional paths.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> when both paths identify the same directory.</returns>
    private static bool SamePath(
            string? left,
            string? right) =>
        left is not null &&
        right is not null &&
        string.Equals(
            Path.GetFullPath(
                left)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            Path.GetFullPath(
                right)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads and caches the project display name from its manifest.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="cache">The search-local cache.</param>
    /// <returns>The project name or <see langword="null"/>.</returns>
    private static string? GetProjectName(
            string? projectDirectory,
            IDictionary<string, string?> cache)
    {
        if (projectDirectory is null)
        {
            return null;
        }

        if (cache.TryGetValue(
                projectDirectory,
                out string? cached))
        {
            return cached;
        }

        string fallback =
            Path.GetFileName(
                projectDirectory);

        string manifestPath =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

        string? name =
            fallback;

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    File.ReadAllText(
                        manifestPath));

            if (document.RootElement.TryGetProperty(
                    "name",
                    out JsonElement nameElement))
            {
                string? manifestName =
                    nameElement.GetString();

                if (!string.IsNullOrWhiteSpace(
                        manifestName))
                {
                    name =
                        manifestName;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            name =
                fallback;
        }

        cache[projectDirectory] =
            name;
        return name;
    }

    /// <summary>
    /// Finds the first body line after optional front matter.
    /// </summary>
    /// <param name="lines">The source lines.</param>
    /// <returns>The zero-based body start line.</returns>
    private static int FindBodyStartLineIndex(
            IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 ||
            !string.Equals(
                lines[0].Trim(),
                "---",
                StringComparison.Ordinal))
        {
            return 0;
        }

        for (int index = 1;
             index < lines.Count;
             index++)
        {
            if (string.Equals(
                    lines[index].Trim(),
                    "---",
                    StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Detects an ATX H1-H6 Markdown heading.
    /// </summary>
    /// <param name="trimmedLine">The source line without leading whitespace.</param>
    /// <returns><see langword="true"/> when the line is a heading.</returns>
    private static bool IsMarkdownHeading(
            string trimmedLine)
    {
        int hashes =
            0;

        while (hashes < trimmedLine.Length &&
               hashes < 6 &&
               trimmedLine[hashes] == '#')
        {
            hashes++;
        }

        return hashes > 0 &&
               hashes < trimmedLine.Length &&
               char.IsWhiteSpace(
                   trimmedLine[hashes]);
    }

    /// <summary>
    /// Locates a front matter property for navigation.
    /// </summary>
    /// <param name="lines">The source lines.</param>
    /// <param name="propertyKey">The property key.</param>
    /// <returns>The one-based source line.</returns>
    private static int FindPropertyLine(
            IReadOnlyList<string> lines,
            string propertyKey)
    {
        string key =
            string.Equals(
                    propertyKey,
                    "tag",
                    StringComparison.OrdinalIgnoreCase)
                ? "tags"
                : propertyKey;

        for (int index = 0;
             index < lines.Count;
             index++)
        {
            if (lines[index]
                .TrimStart()
                .StartsWith(
                    key + ":",
                    StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return 1;
    }

    /// <summary>
    /// Builds the description for a filter-only result.
    /// </summary>
    /// <param name="plan">The query plan.</param>
    /// <param name="projectName">The containing project.</param>
    /// <returns>The filter description.</returns>
    private static string BuildFilterExcerpt(
            SearchQueryPlan plan,
            string? projectName)
    {
        List<string> parts =
            new List<string>();

        parts.AddRange(
            plan.PropertyFilters.Select(filter =>
                $"{filter.Key}: {filter.Value}"));

        if (plan.ModifiedDateFilter is DateOnly date)
        {
            parts.Add(
                $"date: {date:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(
                plan.ProjectFilter))
        {
            parts.Add(
                $"projet: {projectName}");
        }

        return parts.Count > 0
            ? "Filtres · " +
              string.Join(
                  " · ",
                  parts)
            : "Document correspondant";
    }

    /// <summary>
    /// Builds a compact source excerpt around a literal occurrence when available.
    /// </summary>
    /// <param name="line">The source line.</param>
    /// <param name="query">The free-text query.</param>
    /// <returns>The excerpt.</returns>
    private static string BuildExcerpt(
            string line,
            string query)
    {
        string trimmed =
            line.Trim();

        if (trimmed.Length <= 180)
        {
            return trimmed;
        }

        int index =
            trimmed.IndexOf(
                query,
                StringComparison.CurrentCultureIgnoreCase);

        if (index < 0)
        {
            string firstTerm =
                query.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .FirstOrDefault() ??
                string.Empty;

            index =
                firstTerm.Length > 0
                    ? trimmed.IndexOf(
                        firstTerm,
                        StringComparison.CurrentCultureIgnoreCase)
                    : 0;
        }

        if (index < 0)
        {
            index =
                0;
        }

        int start =
            Math.Max(
                0,
                index - 70);

        int length =
            Math.Min(
                180,
                trimmed.Length - start);

        string excerpt =
            trimmed.Substring(
                start,
                length);

        return
            (start > 0
                ? "…"
                : string.Empty) +
            excerpt +
            (start + length < trimmed.Length
                ? "…"
                : string.Empty);
    }

    /// <summary>
    /// Resolves a file or navigation context to a directory.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="contextPath">The context path.</param>
    /// <returns>The context directory.</returns>
    private static string ResolveContextDirectory(
            string workspaceRoot,
            string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(
                contextPath))
        {
            return workspaceRoot;
        }

        string fullPath =
            Path.GetFullPath(
                contextPath);

        if (File.Exists(
                fullPath) ||
            Path.HasExtension(
                fullPath))
        {
            return Path.GetDirectoryName(
                       fullPath) ??
                   workspaceRoot;
        }

        return Directory.Exists(
                fullPath)
            ? fullPath
            : workspaceRoot;
    }

    /// <summary>
    /// Finds the nearest ancestor containing the specified manifest.
    /// </summary>
    /// <param name="startDirectory">The starting directory.</param>
    /// <param name="root">The workspace boundary.</param>
    /// <param name="fileName">The manifest filename.</param>
    /// <returns>The matching ancestor or <see langword="null"/>.</returns>
    private static string? FindAncestorContaining(
            string startDirectory,
            string root,
            string fileName)
    {
        for (string? current = startDirectory;
             current is not null &&
             IsInsideOrEqual(
                 current,
                 root);
             current = Directory.GetParent(
                 current)?.FullName)
        {
            if (File.Exists(
                    Path.Combine(
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
    /// Tests whether a path is the workspace root or one of its descendants.
    /// </summary>
    /// <param name="candidate">The candidate path.</param>
    /// <param name="root">The root boundary.</param>
    /// <returns><see langword="true"/> when the path is inside the workspace.</returns>
    private static bool IsInsideOrEqual(
            string candidate,
            string root)
    {
        string fullCandidate =
            Path.GetFullPath(
                candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string fullRoot =
            Path.GetFullPath(
                root)
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
    /// Returns the fixed project, application or global scope bonus.
    /// </summary>
    /// <param name="scope">The result scope.</param>
    /// <returns>The relevance bonus.</returns>
    private static double GetScopeBonus(
            SearchScopeKind scope) =>
        scope switch
        {
            SearchScopeKind.Project => 30,
            SearchScopeKind.Application => 15,
            _ => 0
        };

    /// <summary>
    /// Sorts results by score, document name and line number.
    /// </summary>
    /// <param name="results">The result list.</param>
    private static void Sort(
            List<SearchResult> results)
    {
        results.Sort(
            (left, right) =>
            {
                int byScore =
                    right.Score.CompareTo(
                        left.Score);

                if (byScore != 0)
                {
                    return byScore;
                }

                int byFile =
                    StringComparer.CurrentCultureIgnoreCase.Compare(
                        left.DisplayName,
                        right.DisplayName);

                return byFile != 0
                    ? byFile
                    : left.LineNumber.CompareTo(
                        right.LineNumber);
            });
    }

    /// <summary>
    /// Represents one front matter filter.
    /// </summary>
    /// <param name="Key">The property key.</param>
    /// <param name="Value">The requested value.</param>
    private sealed record SearchPropertyFilter(
        string Key,
        string Value);

    /// <summary>
    /// Represents a parsed search query.
    /// </summary>
    private sealed record SearchQueryPlan
    {
        /// <summary>
        /// Gets free text remaining after filters are extracted.
        /// </summary>
        public required string TextQuery { get; init; }

        /// <summary>
        /// Gets front matter filters.
        /// </summary>
        public IReadOnlyList<SearchPropertyFilter> PropertyFilters { get; init; } = [];

        /// <summary>
        /// Gets the optional UTC modification date.
        /// </summary>
        public DateOnly? ModifiedDateFilter { get; init; }

        /// <summary>
        /// Gets the optional project name filter.
        /// </summary>
        public string? ProjectFilter { get; init; }
    }
}
