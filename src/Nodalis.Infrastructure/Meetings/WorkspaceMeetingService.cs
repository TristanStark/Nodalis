using Nodalis.Core.Links;
using Nodalis.Core.Meetings;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Meetings;

public sealed class WorkspaceMeetingService
{
    public const string MeetingsDirectoryName = "Réunions";

    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;
    private readonly FileSystemTemplateStore _templateStore;

    public WorkspaceMeetingService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
        _templateStore = new FileSystemTemplateStore(_workspaceRoot);
    }

    public async Task<MeetingCreationResult> CreateAsync(
        string? contextPath,
        MeetingDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);

        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var scope = ResolveScope(contextPath, links)
            ?? throw new InvalidOperationException(
                "Sélectionnez une application, un projet ou un document rattaché avant de créer une réunion.");

        var scopeDirectory = ResolveWorkspacePath(scope.Target.RelativePath);
        var meetingsDirectory = Path.Combine(
            scopeDirectory,
            MeetingsDirectoryName);

        Directory.CreateDirectory(meetingsDirectory);

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

        variables["date"] = draft.Date.ToString("yyyy-MM-dd");

        var content = await _templateStore.RenderAsync(
            "meeting",
            variables,
            cancellationToken);

        content = InsertScopeMetadata(
            content,
            scope.KindLabel,
            scope.Target.DisplayName);

        content = ReplaceSection(
            content,
            "Participants",
            NormalizeBullets(draft.Participants));
        content = ReplaceSection(
            content,
            "Contexte",
            NormalizeText(draft.Context));
        content = ReplaceSection(
            content,
            "Ordre du jour",
            NormalizeBullets(draft.Agenda));
        content = ReplaceSection(
            content,
            "Notes",
            NormalizeText(draft.Notes));
        content = ReplaceSection(
            content,
            "Décisions",
            NormalizeBullets(draft.Decisions));
        content = ReplaceSection(
            content,
            "Actions",
            NormalizeActions(draft.Actions));
        content = ReplaceSection(
            content,
            "Transcription IA",
            NormalizeText(draft.AiTranscript));
        content = ReplaceSection(
            content,
            "Résumé IA",
            NormalizeText(draft.AiSummary));
        content = ReplaceSection(
            content,
            "Contenu Outlook",
            NormalizeText(draft.OutlookContent));

        var safeTitle = WindowsPathRules.SanitizeSegment(
            draft.Title.Trim());

        var desiredFileName =
            $"{draft.Date:yyyy-MM-dd} - {safeTitle}.md";

        var filePath = WindowsPathRules.GetUniqueFilePath(
            meetingsDirectory,
            desiredFileName);

        await AtomicFileWriter.WriteAllTextAsync(
            filePath,
            EnsureTrailingNewline(content),
            cancellationToken);

        return new MeetingCreationResult
        {
            FilePath = filePath,
            ScopeName = scope.Target.DisplayName,
            ScopeKind = scope.KindLabel,
            DocumentId = documentId
        };
    }

    public async Task<(string ScopeKind, string ScopeName)?> ResolveScopeAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var scope = ResolveScope(contextPath, links);

        return scope is null
            ? null
            : (scope.KindLabel, scope.Target.DisplayName);
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
                "Le contexte de réunion se trouve hors du workspace.");
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

    private static string InsertScopeMetadata(
        string content,
        string scopeKind,
        string scopeName)
    {
        var normalized = NormalizeNewlines(content);
        var lines = normalized.Split('\n').ToList();

        var dateIndex = lines.FindIndex(line =>
            line.TrimStart().StartsWith(
                "**Date :**",
                StringComparison.CurrentCultureIgnoreCase));

        var metadata = $"**{scopeKind} :** {scopeName}";

        if (dateIndex >= 0)
        {
            lines.Insert(
                dateIndex + 1,
                metadata);
        }
        else
        {
            var titleIndex = lines.FindIndex(line =>
                line.StartsWith(
                    "# ",
                    StringComparison.Ordinal));

            lines.Insert(
                titleIndex >= 0
                    ? titleIndex + 1
                    : 0,
                metadata);
        }

        return string.Join(
            "\n",
            lines);
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
            var trimmed = normalized.TrimEnd();
            return trimmed +
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

    private static string NormalizeBullets(string value)
    {
        var lines = NormalizeInputLines(value);
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith(
                    "- ",
                    StringComparison.Ordinal) ||
                line.StartsWith(
                    "* ",
                    StringComparison.Ordinal))
            {
                result.Add(line);
            }
            else
            {
                result.Add($"- {line}");
            }
        }

        return string.Join(
            "\n",
            result);
    }

    private static string NormalizeActions(string value)
    {
        var lines = NormalizeInputLines(value);
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith(
                    "- [ ] ",
                    StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(
                    "- [x] ",
                    StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(
                    "* [ ] ",
                    StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(
                    "* [x] ",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(line);
                continue;
            }

            var action = line;

            if (action.StartsWith(
                    "- ",
                    StringComparison.Ordinal) ||
                action.StartsWith(
                    "* ",
                    StringComparison.Ordinal))
            {
                action = action[2..].Trim();
            }

            result.Add($"- [ ] {action}");
        }

        return string.Join(
            "\n",
            result);
    }

    private static IReadOnlyList<string> NormalizeInputLines(string value) =>
        NormalizeNewlines(value)
            .Split(
                '\n',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeText(string value) =>
        NormalizeNewlines(value).Trim();

    private static string NormalizeNewlines(string value) =>
        (value ?? string.Empty)
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

    private static string EnsureTrailingNewline(string value) =>
        NormalizeNewlines(value).TrimEnd() + "\n";

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
