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

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceMeetingService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
public WorkspaceMeetingService(string workspaceRoot)
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
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<MeetingCreationResult> CreateAsync(
        string? contextPath,
        MeetingDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);

        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel) scope = ResolveScope(contextPath, links)
            ?? throw new InvalidOperationException(
                "Sélectionnez une application, un projet ou un document rattaché avant de créer une réunion.");

        string scopeDirectory = ResolveWorkspacePath(scope.Target.RelativePath);
        string meetingsDirectory = Path.Combine(
            scopeDirectory,
            MeetingsDirectoryName);

        Directory.CreateDirectory(meetingsDirectory);

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

        variables["date"] = draft.Date.ToString("yyyy-MM-dd");

        string content = await _templateStore.RenderAsync(
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

        string safeTitle = WindowsPathRules.SanitizeSegment(
            draft.Title.Trim());

        string desiredFileName =
            $"{draft.Date:yyyy-MM-dd} - {safeTitle}.md";

        string filePath = WindowsPathRules.GetUniqueFilePath(
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
                "Le contexte de réunion se trouve hors du workspace.");
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
    /// Performs the <c>InsertScopeMetadata</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="scopeKind">The <c>scopeKind</c> value.</param>
    /// <param name="scopeName">The <c>scopeName</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string InsertScopeMetadata(
        string content,
        string scopeKind,
        string scopeName)
    {
        string normalized = NormalizeNewlines(content);
        global::System.Collections.Generic.List<string> lines = normalized.Split('\n').ToList();

        int dateIndex = lines.FindIndex(line =>
            line.TrimStart().StartsWith(
                "**Date :**",
                StringComparison.CurrentCultureIgnoreCase));

        string metadata = $"**{scopeKind} :** {scopeName}";

        if (dateIndex >= 0)
        {
            lines.Insert(
                dateIndex + 1,
                metadata);
        }
        else
        {
            int titleIndex = lines.FindIndex(line =>
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
            string trimmed = normalized.TrimEnd();
            return trimmed +
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
    /// Performs the <c>NormalizeBullets</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string NormalizeBullets(string value)
    {
        global::System.Collections.Generic.IReadOnlyList<string> lines = NormalizeInputLines(value);
        global::System.Collections.Generic.List<string> result = new List<string>();

        foreach (string line in lines)
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

    /// <summary>
    /// Performs the <c>NormalizeActions</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string NormalizeActions(string value)
    {
        global::System.Collections.Generic.IReadOnlyList<string> lines = NormalizeInputLines(value);
        global::System.Collections.Generic.List<string> result = new List<string>();

        foreach (string line in lines)
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

            string action = line;

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

    /// <summary>
    /// Performs the <c>NormalizeInputLines</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static IReadOnlyList<string> NormalizeInputLines(string value) =>
        NormalizeNewlines(value)
            .Split(
                '\n',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Performs the <c>NormalizeText</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string NormalizeText(string value) =>
        NormalizeNewlines(value).Trim();

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
    /// Performs the <c>EnsureTrailingNewline</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string EnsureTrailingNewline(string value) =>
        NormalizeNewlines(value).TrimEnd() + "\n";

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
