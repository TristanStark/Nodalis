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

    public WorkspaceDecisionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
        _templateStore = new FileSystemTemplateStore(_workspaceRoot);
    }

    public async Task<DecisionCreationResult> CreateAsync(
        string? contextPath,
        DecisionDraft draft,
        string? sourceDocumentPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Decision);

        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var scope = ResolveScope(contextPath, links)
            ?? throw new InvalidOperationException(
                "Sélectionnez une application, un projet ou un document rattaché avant de créer une décision.");

        var scopeDirectory = ResolveWorkspacePath(
            scope.Target.RelativePath);
        var decisionsDirectory = Path.Combine(
            scopeDirectory,
            DecisionsDirectoryName);

        Directory.CreateDirectory(decisionsDirectory);

        var source = ResolveSourceTarget(
            sourceDocumentPath,
            links);

        var documentId = Guid.NewGuid();
        var variables = MarkdownTemplateRenderer.CreateStandardVariables(
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

        var content = await _templateStore.RenderAsync(
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

        var linksBody = BuildLinksBody(
            draft.Links,
            source);

        content = ReplaceSection(
            content,
            "Sources et liens",
            linksBody);

        var safeTitle = WindowsPathRules.SanitizeSegment(
            draft.Title.Trim());

        var filePath = WindowsPathRules.GetUniqueFilePath(
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

    public async Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var scope = ResolveScope(contextPath, links);

        if (scope is null)
        {
            return [];
        }

        var directory = Path.Combine(
            ResolveWorkspacePath(scope.Value.Target.RelativePath),
            DecisionsDirectoryName);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<DecisionRecord>();

        foreach (var filePath in Directory.EnumerateFiles(
                     directory,
                     "*.md",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(
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

    public async Task<IReadOnlyList<DecisionRecord>> SearchAsync(
        string? contextPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        var decisions = await GetDecisionsAsync(
            contextPath,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return decisions;
        }

        var needle = query.Trim();

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

    public async Task<IReadOnlyList<string>> ExtractDecisionCandidatesAsync(
        string sourceDocumentPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocumentPath);

        var fullPath = Path.GetFullPath(sourceDocumentPath);

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

        var content = await File.ReadAllTextAsync(
            fullPath,
            cancellationToken);

        var section = ReadSection(
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

    public async Task<(string ScopeKind, string ScopeName)?> ResolveScopeAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var scope = ResolveScope(contextPath, links);

        if (scope is null)
        {
            return null;
        }

        var resolved = scope.Value;

        return (
            resolved.KindLabel,
            resolved.Target.DisplayName);
    }

    private (LinkTargetEntry Target, string KindLabel)? ResolveScope(
        string? contextPath,
        LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(contextPath);

        if (!IsWithinWorkspace(fullPath))
        {
            throw new InvalidDataException(
                "Le contexte de décision se trouve hors du workspace.");
        }

        var contextDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
            : fullPath;

        var relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                contextDirectory));

        var project = links.Targets
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

        var application = links.Targets
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

    private LinkTargetEntry? ResolveSourceTarget(
        string? sourceDocumentPath,
        LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(sourceDocumentPath);

        if (!File.Exists(fullPath) ||
            !IsWithinWorkspace(fullPath))
        {
            return null;
        }

        var relativePath = NormalizeRelativePath(
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

    private DecisionRecord ParseDecision(
        string filePath,
        string content,
        string scopeKind,
        string scopeName)
    {
        var lines = NormalizeNewlines(content)
            .Split('\n');

        var titleLine = lines.FirstOrDefault(line =>
            line.StartsWith(
                "# ",
                StringComparison.Ordinal));

        var title = titleLine is null
            ? Path.GetFileNameWithoutExtension(filePath)
            : titleLine[2..].Trim();

        if (title.StartsWith(
                "Décision — ",
                StringComparison.CurrentCultureIgnoreCase))
        {
            title = title["Décision — ".Length..].Trim();
        }

        var dateText = ReadMetadata(lines, "Date");
        DateOnly? date = null;

        if (DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
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

    private static string InsertMetadata(
        string content,
        string scopeKind,
        string scopeName,
        string status,
        string? sourceQualifiedName)
    {
        var lines = NormalizeNewlines(content)
            .Split('\n')
            .ToList();

        var dateIndex = lines.FindIndex(line =>
            line.TrimStart().StartsWith(
                "**Date :**",
                StringComparison.CurrentCultureIgnoreCase));

        var insertIndex = dateIndex >= 0
            ? dateIndex + 1
            : Math.Min(
                1,
                lines.Count);

        var metadata = new List<string>
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

    private static string BuildLinksBody(
        string links,
        LinkTargetEntry? source)
    {
        var entries = new List<string>();

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

    private static string ReplaceSection(
        string content,
        string heading,
        string body)
    {
        var normalized = NormalizeNewlines(content);
        var lines = normalized.Split('\n').ToList();
        var expected = $"## {heading}";

        var headingIndex = lines.FindIndex(line =>
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

        var nextHeading = lines.FindIndex(
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

        var replacement = new List<string>
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

    private static string ReadSection(
        string content,
        string heading)
    {
        var lines = NormalizeNewlines(content)
            .Split('\n');

        var expected = $"## {heading}";
        var start = Array.FindIndex(
            lines,
            line => string.Equals(
                line.Trim(),
                expected,
                StringComparison.CurrentCultureIgnoreCase));

        if (start < 0)
        {
            return string.Empty;
        }

        var end = Array.FindIndex(
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

    private static string ReadMetadata(
        IReadOnlyList<string> lines,
        string key)
    {
        var prefix = $"**{key} :**";

        var line = lines.FirstOrDefault(candidate =>
            candidate.TrimStart().StartsWith(
                prefix,
                StringComparison.CurrentCultureIgnoreCase));

        return line is null
            ? string.Empty
            : line.Trim()[
                prefix.Length..]
                .Trim();
    }

    private static string StripBullet(string line)
    {
        var value = line.Trim();

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

    private static bool Contains(
        string value,
        string needle) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(
            needle,
            StringComparison.CurrentCultureIgnoreCase);

    private static string EnsureTrailingNewline(string value) =>
        NormalizeNewlines(value).TrimEnd() + "\n";

    private static string NormalizeNewlines(string value) =>
        (value ?? string.Empty)
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

    private string ResolveWorkspacePath(string relativePath) =>
        Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

    private bool IsWithinWorkspace(string path)
    {
        var root = _workspaceRoot.TrimEnd(
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

    private static bool IsRelativeAncestorOrEqual(
        string candidateParent,
        string child)
    {
        var parent = candidateParent.Trim('/');
        var descendant = child.Trim('/');

        return string.Equals(
                   parent,
                   descendant,
                   StringComparison.OrdinalIgnoreCase) ||
               descendant.StartsWith(
                   parent + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(
            Path.DirectorySeparatorChar,
            '/');
}
