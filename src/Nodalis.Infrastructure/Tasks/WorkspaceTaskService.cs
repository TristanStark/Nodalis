using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Links;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Tasks;

public sealed partial class WorkspaceTaskService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;

    public WorkspaceTaskService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(
            _workspaceRoot);
    }

    public async Task<TaskCollection> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var links = await _linkIndex.RefreshAsync(
            cancellationToken);

        var tasks = new List<TaskItem>();

        foreach (var document in links.Targets.Where(target =>
                     target.Kind == LinkTargetKind.Document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = ResolveWorkspacePath(
                document.RelativePath);

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var lines = await File.ReadAllLinesAsync(
                fullPath,
                cancellationToken);

            var project = links.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Project &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        document.RelativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

            var application = links.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Application &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        document.RelativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

            for (var index = 0;
                 index < lines.Length;
                 index++)
            {
                var match = CheckboxPattern().Match(
                    lines[index]);

                if (!match.Success)
                {
                    continue;
                }

                var body = match.Groups["body"].Value.Trim();
                var metadata = ParseMetadata(
                    body);

                tasks.Add(new TaskItem
                {
                    Id = CreateTaskId(
                        document.Id,
                        index + 1,
                        body),
                    SourceDocumentId = document.Id,
                    SourceRelativePath = document.RelativePath,
                    LineNumber = index + 1,
                    RawLine = lines[index],
                    Text = metadata.Text,
                    IsCompleted =
                        !string.IsNullOrWhiteSpace(
                            match.Groups["checked"].Value),
                    Owner = metadata.Owner,
                    DueDate = metadata.DueDate,
                    ApplicationId = application?.Id,
                    ApplicationName = application?.DisplayName,
                    ProjectId = project?.Id,
                    ProjectName = project?.DisplayName
                });
            }
        }

        return new TaskCollection
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Tasks = tasks
                .OrderBy(task => task.IsCompleted)
                .ThenBy(task => task.DueDate ?? DateOnly.MaxValue)
                .ThenBy(task => task.Text, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(
        string? contextPath,
        bool includeCompleted = false,
        CancellationToken cancellationToken = default)
    {
        var catalog = await RefreshAsync(
            cancellationToken);

        var filtered = catalog.Tasks.AsEnumerable();

        var context = ResolveContext(
            contextPath,
            await _linkIndex.LoadAsync(cancellationToken));

        if (context.ProjectId is Guid projectId)
        {
            filtered = filtered.Where(task =>
                task.ProjectId == projectId);
        }
        else if (context.ApplicationId is Guid applicationId)
        {
            filtered = filtered.Where(task =>
                task.ApplicationId == applicationId);
        }

        if (!includeCompleted)
        {
            filtered = filtered.Where(task =>
                !task.IsCompleted);
        }

        return filtered
            .OrderBy(task => task.DueDate ?? DateOnly.MaxValue)
            .ThenBy(task => task.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task SetCompletedAsync(
        TaskItem task,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var fullPath = ResolveWorkspacePath(
            task.SourceRelativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Le document source de la tâche est introuvable.",
                fullPath);
        }

        var session = await TextDocumentSession.OpenAsync(
            fullPath,
            cancellationToken);

        var newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var normalized = session.Content
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

        var hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        var lines = normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(
                lines.Count - 1);
        }

        var index = task.LineNumber - 1;

        if (index < 0 ||
            index >= lines.Count ||
            !string.Equals(
                lines[index],
                task.RawLine,
                StringComparison.Ordinal))
        {
            var candidates = lines
                .Select((line, lineIndex) =>
                    (line, lineIndex))
                .Where(candidate =>
                    string.Equals(
                        candidate.line,
                        task.RawLine,
                        StringComparison.Ordinal))
                .Select(candidate =>
                    candidate.lineIndex)
                .ToArray();

            if (candidates.Length != 1)
            {
                throw new TaskSourceConflictException(
                    fullPath);
            }

            index = candidates[0];
        }

        var updatedLine = SetCheckboxState(
            lines[index],
            completed);

        if (string.Equals(
                updatedLine,
                lines[index],
                StringComparison.Ordinal))
        {
            return;
        }

        lines[index] = updatedLine;

        var content = string.Join(
            newline,
            lines);

        if (hadTrailingNewline)
        {
            content += newline;
        }

        await session.SaveAsync(
            content,
            cancellationToken);
    }

    private (Guid? ApplicationId, Guid? ProjectId) ResolveContext(
        string? contextPath,
        LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return (null, null);
        }

        var fullPath = Path.GetFullPath(
            contextPath);

        var relativePath = Path.GetRelativePath(
                _workspaceRoot,
                File.Exists(fullPath)
                    ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
                    : fullPath)
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

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
            var application = links.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Application &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        project.RelativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

            return (
                application?.Id,
                project.Id);
        }

        var app = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Application &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relativePath))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        return (
            app?.Id,
            null);
    }

    private string ResolveWorkspacePath(
        string relativePath) =>
        Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

    private static (string Text, string? Owner, DateOnly? DueDate)
        ParseMetadata(string body)
    {
        var matches = MetadataPattern()
            .Matches(body);

        if (matches.Count == 0)
        {
            return (
                body.Trim(),
                null,
                null);
        }

        var text = body[..matches[0].Index]
            .Trim();

        string? owner = null;
        DateOnly? dueDate = null;

        foreach (Match match in matches)
        {
            var key = NormalizeMetadataKey(
                match.Groups["key"].Value);

            var value = match.Groups["value"].Value.Trim();

            if (key is "responsable" or "owner")
            {
                owner = value;
                continue;
            }

            if (key is "echeance" or "due" &&
                DateOnly.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                dueDate = parsed;
            }
        }

        return (
            text,
            owner,
            dueDate);
    }

    private static string NormalizeMetadataKey(
        string value) =>
        value
            .Trim()
            .ToLowerInvariant()
            .Replace(
                "é",
                "e",
                StringComparison.Ordinal)
            .Replace(
                "è",
                "e",
                StringComparison.Ordinal)
            .Replace(
                "ê",
                "e",
                StringComparison.Ordinal);

    private static string SetCheckboxState(
        string line,
        bool completed)
    {
        var match = CheckboxPattern().Match(
            line);

        if (!match.Success)
        {
            throw new InvalidDataException(
                "La ligne source n'est plus une checkbox Markdown.");
        }

        return
            match.Groups["prefix"].Value +
            (completed ? "x" : " ") +
            match.Groups["suffix"].Value;
    }

    private static Guid CreateTaskId(
        Guid sourceDocumentId,
        int lineNumber,
        string body)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{sourceDocumentId:D}|{lineNumber}|{body}"));

        return new Guid(
            bytes.AsSpan(
                0,
                16));
    }

    private static bool IsRelativeAncestor(
        string candidateParent,
        string child) =>
        child.StartsWith(
            candidateParent.TrimEnd('/') + "/",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsRelativeAncestorOrEqual(
        string candidateParent,
        string child) =>
        string.Equals(
            candidateParent.TrimEnd('/'),
            child.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase) ||
        IsRelativeAncestor(
            candidateParent,
            child);

    [GeneratedRegex(
        @"^(?<prefix>\s*[-*+]\s+\[)(?<checked>[ xX])(?<suffix>\]\s+(?<body>.*))$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CheckboxPattern();

    [GeneratedRegex(
        @"\s+\|\s+(?<key>Responsable|Owner|Échéance|Echeance|Due)\s*:\s*(?<value>[^|]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataPattern();
}
