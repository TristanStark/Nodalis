using System.Globalization;
using Nodalis.Core.Decisions;
using Nodalis.Core.Links;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Decisions;

public sealed class WorkspaceDecisionService
{
    public const string DecisionsDirectoryName = "Décisions";

    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;
    private readonly FileSystemTemplateStore _templateStore;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceDecisionService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
public WorkspaceDecisionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
        _templateStore = new FileSystemTemplateStore(_workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>CreateAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <param name="sourceDocumentPath">The <c>sourceDocumentPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<DecisionCreationResult> CreateAsync(
        string? contextPath,
        DecisionDraft draft,
        string? sourceDocumentPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Decision);

        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel) scope = ResolveScope(contextPath, links)
            ?? throw new InvalidOperationException(
                "Sélectionnez une application, un projet ou un document rattaché avant de créer une décision.");

        string scopeDirectory = ResolveWorkspacePath(
            scope.Target.RelativePath);
        string decisionsDirectory = Path.Combine(
            scopeDirectory,
            DecisionsDirectoryName);

        Directory.CreateDirectory(decisionsDirectory);

        global::Nodalis.Core.Links.LinkTargetEntry? source = ResolveSourceTarget(
            sourceDocumentPath,
            links);

        global::System.Guid documentId = Guid.NewGuid();
        global::System.Collections.Generic.Dictionary<string, string> variables = MarkdownTemplateRenderer.CreateStandardVariables(
            draft.Title.Trim(),
            documentId,
            DateTimeOffset.Now,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["context.name"] = scope.Target.DisplayName,
                ["context.kind"] = scope.KindLabel
            });

        variables["date"] =
            draft.Date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

        string content = await _templateStore.RenderAsync(
            "decision",
            variables,
            cancellationToken);

        content = InsertMetadata(
            content,
            scope.KindLabel,
            scope.Target.DisplayName,
            draft.Status,
            source?.QualifiedName);

        content = ReplaceSection(
            content,
            "Décision",
            draft.Decision);
        content = ReplaceSection(
            content,
            "Contexte",
            draft.Context);
        content = ReplaceSection(
            content,
            "Justification",
            draft.Justification);
        content = ReplaceSection(
            content,
            "Impacts",
            draft.Impacts);

        string linksBody = BuildLinksBody(
            draft.Links,
            source);

        content = ReplaceSection(
            content,
            "Sources et liens",
            linksBody);

        string safeTitle = WindowsPathRules.SanitizeSegment(
            draft.Title.Trim());

        string filePath = WindowsPathRules.GetUniqueFilePath(
            decisionsDirectory,
            $"{draft.Date:yyyy-MM-dd} - {safeTitle}.md");

        await AtomicFileWriter.WriteAllTextAsync(
            filePath,
            EnsureTrailingNewline(content),
            cancellationToken);

        return new DecisionCreationResult
        {
            FilePath = filePath,
            ScopeName = scope.Target.DisplayName,
            ScopeKind = scope.KindLabel
        };
    }

    /// <summary>
    /// Performs the <c>GetDecisionsAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel)? scope = ResolveScope(contextPath, links);

        if (scope is null)
        {
            return [];
        }

        string directory = Path.Combine(
            ResolveWorkspacePath(scope.Value.Target.RelativePath),
            DecisionsDirectoryName);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Decisions.DecisionRecord> result = new List<DecisionRecord>();

        foreach (string filePath in Directory.EnumerateFiles(
                     directory,
                     "*.md",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content = await File.ReadAllTextAsync(
                filePath,
                cancellationToken);

            result.Add(
                ParseDecision(
                    filePath,
                    content,
                    scope.Value.KindLabel,
                    scope.Value.Target.DisplayName));
        }

        return result
            .OrderByDescending(item => item.Date ?? DateOnly.MinValue)
            .ThenBy(
                item => item.Title,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>SearchAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<DecisionRecord>> SearchAsync(
        string? contextPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Decisions.DecisionRecord> decisions = await GetDecisionsAsync(
            contextPath,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return decisions;
        }

        string needle = query.Trim();

        return decisions
            .Where(item =>
                Contains(item.Title, needle) ||
                Contains(item.Status, needle) ||
                Contains(item.Decision, needle) ||
                Contains(item.Context, needle) ||
                Contains(item.Justification, needle) ||
                Contains(item.Impacts, needle) ||
                Contains(item.SourceReference, needle) ||
                Contains(item.Links, needle))
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>ExtractDecisionCandidatesAsync</c> operation.
    /// </summary>
    /// <param name="sourceDocumentPath">The <c>sourceDocumentPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<string>> ExtractDecisionCandidatesAsync(
        string sourceDocumentPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocumentPath);

        string fullPath = Path.GetFullPath(sourceDocumentPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Le document source est introuvable.",
                fullPath);
        }

        if (!IsWithinWorkspace(fullPath))
        {
            throw new InvalidDataException(
                "Le document source se trouve hors du workspace.");
        }

        string content = await File.ReadAllTextAsync(
            fullPath,
            cancellationToken);

        string section = ReadSection(
            content,
            "Décisions");

        if (string.IsNullOrWhiteSpace(section))
        {
            return [];
        }

        return NormalizeNewlines(section)
            .Split(
                '\n',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
            .Select(StripBullet)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>ResolveScopeAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<(string ScopeKind, string ScopeName)?> ResolveScopeAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel)? scope = ResolveScope(contextPath, links);

        if (scope is null)
        {
            return null;
        }

        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel) resolved = scope.Value;

        return (
            resolved.KindLabel,
            resolved.Target.DisplayName);
    }

    /// <summary>
    /// Performs the <c>ResolveScope</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
private (LinkTargetEntry Target, string KindLabel)? ResolveScope(
        string? contextPath,
        LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(contextPath);

        if (!IsWithinWorkspace(fullPath))
        {
            throw new InvalidDataException(
                "Le contexte de décision se trouve hors du workspace.");
        }

        string contextDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
            : fullPath;

        string relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                contextDirectory));

        global::Nodalis.Core.Links.LinkTargetEntry? project = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Project &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relativePath))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        if (project is not null)
        {
            return (project, "Projet");
        }

        global::Nodalis.Core.Links.LinkTargetEntry? application = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Application &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relativePath))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        return application is null
            ? null
            : (application, "Application");
    }

    /// <summary>
    /// Performs the <c>ResolveSourceTarget</c> operation.
    /// </summary>
    /// <param name="sourceDocumentPath">The <c>sourceDocumentPath</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
private LinkTargetEntry? ResolveSourceTarget(
        string? sourceDocumentPath,
        LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentPath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(sourceDocumentPath);

        if (!File.Exists(fullPath) ||
            !IsWithinWorkspace(fullPath))
        {
            return null;
        }

        string relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath));

        return links.Targets.FirstOrDefault(target =>
            target.Kind == LinkTargetKind.Document &&
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the <c>ParseDecision</c> operation.
    /// </summary>
    /// <param name="filePath">The <c>filePath</c> value.</param>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="scopeKind">The <c>scopeKind</c> value.</param>
    /// <param name="scopeName">The <c>scopeName</c> value.</param>
    /// <returns>The result of the operation.</returns>
private DecisionRecord ParseDecision(
        string filePath,
        string content,
        string scopeKind,
        string scopeName)
    {
        string[] lines = NormalizeNewlines(content)
            .Split('\n');

        string? titleLine = lines.FirstOrDefault(line =>
            line.StartsWith(
                "# ",
                StringComparison.Ordinal));

        string title = titleLine is null
            ? Path.GetFileNameWithoutExtension(filePath)
            : titleLine[2..].Trim();

        if (title.StartsWith(
                "Décision — ",
                StringComparison.CurrentCultureIgnoreCase))
        {
            title = title["Décision — ".Length..].Trim();
        }

        string dateText = ReadMetadata(lines, "Date");
        DateOnly? date = null;

        if (DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out global::System.DateOnly parsedDate))
        {
            date = parsedDate;
        }

        return new DecisionRecord
        {
            SourceRelativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    filePath)),
            Title = title,
            Date = date,
            Status = ReadMetadata(lines, "Statut"),
            ScopeName = scopeName,
            ScopeKind = scopeKind,
            Decision = ReadSection(content, "Décision"),
            Context = ReadSection(content, "Contexte"),
            Justification = ReadSection(content, "Justification"),
            Impacts = ReadSection(content, "Impacts"),
            SourceReference = ReadMetadata(lines, "Source"),
            Links = ReadSection(content, "Sources et liens")
        };
    }

    /// <summary>
    /// Performs the <c>InsertMetadata</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="scopeKind">The <c>scopeKind</c> value.</param>
    /// <param name="scopeName">The <c>scopeName</c> value.</param>
    /// <param name="status">The <c>status</c> value.</param>
    /// <param name="sourceQualifiedName">The <c>sourceQualifiedName</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string InsertMetadata(
        string content,
        string scopeKind,
        string scopeName,
        string status,
        string? sourceQualifiedName)
    {
        global::System.Collections.Generic.List<string> lines = NormalizeNewlines(content)
            .Split('\n')
            .ToList();

        int dateIndex = lines.FindIndex(line =>
            line.TrimStart().StartsWith(
                "**Date :**",
                StringComparison.CurrentCultureIgnoreCase));

        int insertIndex = dateIndex >= 0
            ? dateIndex + 1
            : Math.Min(
                1,
                lines.Count);

        global::System.Collections.Generic.List<string> metadata = new List<string>
        {
            $"**{scopeKind} :** {scopeName}"
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            metadata.Add(
                $"**Statut :** {status.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(sourceQualifiedName))
        {
            metadata.Add(
                $"**Source :** [[{sourceQualifiedName}]]");
        }

        lines.InsertRange(
            insertIndex,
            metadata);

        return string.Join(
            "\n",
            lines);
    }

    /// <summary>
    /// Performs the <c>BuildLinksBody</c> operation.
    /// </summary>
    /// <param name="links">The <c>links</c> value.</param>
    /// <param name="source">The <c>source</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string BuildLinksBody(
        string links,
        LinkTargetEntry? source)
    {
        global::System.Collections.Generic.List<string> entries = new List<string>();

        if (source is not null)
        {
            entries.Add(
                $"- Source : [[{source.QualifiedName}]]");
        }

        entries.AddRange(
            NormalizeNewlines(links)
                .Split(
                    '\n',
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                    line.StartsWith(
                        "- ",
                        StringComparison.Ordinal)
                        ? line
                        : $"- {line}"));

        return string.Join(
            "\n",
            entries);
    }

    /// <summary>
    /// Performs the <c>ReplaceSection</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="heading">The <c>heading</c> value.</param>
    /// <param name="body">The <c>body</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ReplaceSection(
        string content,
        string heading,
        string body)
    {
        string normalized = NormalizeNewlines(content);
        global::System.Collections.Generic.List<string> lines = normalized.Split('\n').ToList();
        string expected = $"## {heading}";

        int headingIndex = lines.FindIndex(line =>
            string.Equals(
                line.Trim(),
                expected,
                StringComparison.CurrentCultureIgnoreCase));

        if (headingIndex < 0)
        {
            return normalized.TrimEnd() +
                   "\n\n" +
                   expected +
                   "\n\n" +
                   body.Trim() +
                   "\n";
        }

        int nextHeading = lines.FindIndex(
            headingIndex + 1,
            line => line.TrimStart().StartsWith(
                "## ",
                StringComparison.Ordinal));

        if (nextHeading < 0)
        {
            nextHeading = lines.Count;
        }

        lines.RemoveRange(
            headingIndex + 1,
            nextHeading - headingIndex - 1);

        global::System.Collections.Generic.List<string> replacement = new List<string>
        {
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(body))
        {
            replacement.AddRange(
                NormalizeNewlines(body)
                    .Trim()
                    .Split('\n'));
            replacement.Add(
                string.Empty);
        }

        lines.InsertRange(
            headingIndex + 1,
            replacement);

        return string.Join(
            "\n",
            lines);
    }

    /// <summary>
    /// Performs the <c>ReadSection</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="heading">The <c>heading</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ReadSection(
        string content,
        string heading)
    {
        string[] lines = NormalizeNewlines(content)
            .Split('\n');

        string expected = $"## {heading}";
        int start = Array.FindIndex(
            lines,
            line => string.Equals(
                line.Trim(),
                expected,
                StringComparison.CurrentCultureIgnoreCase));

        if (start < 0)
        {
            return string.Empty;
        }

        int end = Array.FindIndex(
            lines,
            start + 1,
            line => line.TrimStart().StartsWith(
                "## ",
                StringComparison.Ordinal));

        if (end < 0)
        {
            end = lines.Length;
        }

        return string.Join(
                "\n",
                lines[(start + 1)..end])
            .Trim();
    }

    /// <summary>
    /// Performs the <c>ReadMetadata</c> operation.
    /// </summary>
    /// <param name="lines">The <c>lines</c> value.</param>
    /// <param name="key">The <c>key</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ReadMetadata(
        IReadOnlyList<string> lines,
        string key)
    {
        string prefix = $"**{key} :**";

        string? line = lines.FirstOrDefault(candidate =>
            candidate.TrimStart().StartsWith(
                prefix,
                StringComparison.CurrentCultureIgnoreCase));

        return line is null
            ? string.Empty
            : line.Trim()[
                prefix.Length..]
                .Trim();
    }

    /// <summary>
    /// Performs the <c>StripBullet</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string StripBullet(string line)
    {
        string value = line.Trim();

        if (value.StartsWith(
                "- ",
                StringComparison.Ordinal) ||
            value.StartsWith(
                "* ",
                StringComparison.Ordinal))
        {
            return value[2..].Trim();
        }

        return value;
    }

    /// <summary>
    /// Performs the <c>Contains</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <param name="needle">The <c>needle</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool Contains(
        string value,
        string needle) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            needle,
            StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Performs the <c>EnsureTrailingNewline</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string EnsureTrailingNewline(string value) =>
        NormalizeNewlines(value).TrimEnd() + "\n";

    /// <summary>
    /// Performs the <c>NormalizeNewlines</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string NormalizeNewlines(string value) =>
        (value ?? string.Empty)
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

    /// <summary>
    /// Performs the <c>ResolveWorkspacePath</c> operation.
    /// </summary>
    /// <param name="relativePath">The <c>relativePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
private string ResolveWorkspacePath(string relativePath) =>
        Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

    /// <summary>
    /// Performs the <c>IsWithinWorkspace</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
private bool IsWithinWorkspace(string path)
    {
        string root = _workspaceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return string.Equals(
                   path,
                   root,
                   StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(
                   root + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>IsRelativeAncestorOrEqual</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool IsRelativeAncestorOrEqual(
        string candidateParent,
        string child)
    {
        string parent = candidateParent.Trim('/');
        string descendant = child.Trim('/');

        return string.Equals(
                   parent,
                   descendant,
                   StringComparison.OrdinalIgnoreCase) ||
               descendant.StartsWith(
                   parent + "/",
                   StringComparison.OrdinalIgnoreCase);
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
}
