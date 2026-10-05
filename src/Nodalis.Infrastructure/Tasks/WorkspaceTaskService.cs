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

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceTaskService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public WorkspaceTaskService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(
            _workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>RefreshAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<TaskCollection> RefreshAsync(
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Tasks.TaskItem> tasks = new List<TaskItem>();

        foreach (global::Nodalis.Core.Links.LinkTargetEntry document in links.Targets.Where(target =>
                     target.Kind == LinkTargetKind.Document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath = ResolveWorkspacePath(
                document.RelativePath);

            if (!File.Exists(fullPath))
            {
                continue;
            }

            string[] lines = await File.ReadAllLinesAsync(
                fullPath,
                cancellationToken);

            global::Nodalis.Core.Links.LinkTargetEntry? project = links.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Project &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        document.RelativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

            global::Nodalis.Core.Links.LinkTargetEntry? application = links.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Application &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        document.RelativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

            for (int index = 0;
                 index < lines.Length;
                 index++)
            {
                global::System.Text.RegularExpressions.Match match = CheckboxPattern().Match(
                    lines[index]);

                if (!match.Success)
                {
                    continue;
                }

                if (index + 1 < lines.Length &&
                    TryParsePromotionMarker(
                        lines[index + 1],
                        MeetingActionSourceMarkerPrefix,
                        out global::System.Guid _))
                {
                    continue;
                }

                string body = match.Groups["body"].Value.Trim();
                (
                    string Text,
                    string? Owner,
                    global::System.DateOnly? DueDate,
                    string? Priority,
                    string? Status,
                    global::System.Collections.Generic.IReadOnlyList<string> Tags
                ) metadata = ParseMetadata(
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
                    Priority = metadata.Priority,
                    Status = metadata.Status,
                    Tags = metadata.Tags,
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
                .ThenBy(task => GetPriorityRank(task.Priority))
                .ThenBy(task => task.Text, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// Performs the <c>GetTasksAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="includeCompleted">The <c>includeCompleted</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(
            string? contextPath,
            bool includeCompleted = false,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Tasks.TaskCollection catalog = await RefreshAsync(
            cancellationToken);

        global::System.Collections.Generic.IEnumerable<global::Nodalis.Core.Tasks.TaskItem> filtered = catalog.Tasks.AsEnumerable();

        (global::System.Guid? ApplicationId, global::System.Guid? ProjectId) context = ResolveContext(
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
            .ThenBy(task => GetPriorityRank(task.Priority))
            .ThenBy(task => task.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Promotes one project meeting action into the project's dedicated task document while retaining bidirectional links.
    /// </summary>
    /// <param name="task">The meeting checkbox action to promote.</param>
    /// <param name="metadata">The metadata to apply to both the meeting action and promoted task.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deterministic promotion result.</returns>
    public async Task<MeetingActionPromotionResult> PromoteMeetingActionAsync(
            TaskItem task,
            TaskMetadataUpdate metadata,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            task);
        ArgumentNullException.ThrowIfNull(
            metadata);

        if (!task.CanPromoteMeetingAction ||
            task.ProjectId is not Guid projectId)
        {
            throw new InvalidOperationException(
                "Seules les actions d'une réunion rattachée à un projet peuvent être promues.");
        }

        Guid promotionId = CreateMeetingActionPromotionId(
            task);

        LinkIndexCatalog links = await _linkIndex.RefreshAsync(
            cancellationToken);

        LinkTargetEntry sourceTarget = links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Document &&
                target.Id == task.SourceDocumentId)
            ?? links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Document &&
                string.Equals(
                    target.RelativePath,
                    task.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "Le compte-rendu source n'est plus indexé dans le workspace.");

        LinkTargetEntry projectTarget = links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Project &&
                target.Id == projectId)
            ?? throw new InvalidDataException(
                "Le projet de l'action de réunion n'est plus indexé dans le workspace.");

        string sourcePath = ResolveWorkspacePath(
            sourceTarget.RelativePath);
        string projectDirectory = ResolveWorkspacePath(
            projectTarget.RelativePath);

        if (!File.Exists(
                sourcePath))
        {
            throw new FileNotFoundException(
                "Le compte-rendu source de l'action est introuvable.",
                sourcePath);
        }

        if (!Directory.Exists(
                projectDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Le dossier projet est introuvable : {projectDirectory}");
        }

        TextDocumentSession sourceSession = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        List<string> sourceLines = SplitDocumentLines(
            sourceSession.Content,
            out string sourceNewline,
            out bool sourceHadTrailingNewline);

        int sourceMarkerIndex = FindPromotionMarkerIndex(
            sourceLines,
            MeetingActionSourceMarkerPrefix,
            promotionId);

        int sourceLineIndex;

        if (sourceMarkerIndex > 0)
        {
            sourceLineIndex = sourceMarkerIndex - 1;

            if (!CheckboxPattern().IsMatch(
                    sourceLines[sourceLineIndex]))
            {
                throw new InvalidDataException(
                    "Le marqueur de promotion du compte-rendu n'est plus associé à une checkbox Markdown.");
            }
        }
        else
        {
            sourceLineIndex = ResolveTaskLineIndex(
                sourceLines,
                task,
                sourcePath);
        }

        string owner = NormalizeMetadataValue(
            metadata.Owner,
            "responsable");
        string priority = NormalizeMetadataValue(
            metadata.Priority,
            "priorité");
        string status = NormalizeMetadataValue(
            metadata.Status,
            "statut");
        IReadOnlyList<string> tags = NormalizeTags(
            metadata.Tags);

        string body = BuildTaskBody(
            task.Text,
            owner,
            metadata.DueDate,
            priority,
            status,
            tags);

        Match sourceMatch = CheckboxPattern().Match(
            sourceLines[sourceLineIndex]);

        if (!sourceMatch.Success)
        {
            throw new InvalidDataException(
                "La ligne source n'est plus une checkbox Markdown.");
        }

        sourceLines[sourceLineIndex] =
            sourceMatch.Groups["prefix"].Value +
            sourceMatch.Groups["checked"].Value +
            "] " +
            body;

        string taskDocumentPath = Path.Combine(
            projectDirectory,
            "Tâches.md");

        if (!File.Exists(
                taskDocumentPath))
        {
            await AtomicFileWriter.WriteAllTextAsync(
                taskDocumentPath,
                "# Tâches\n\n## Actions de réunion\n\n",
                cancellationToken);
        }

        links = await _linkIndex.RefreshAsync(
            cancellationToken);

        sourceTarget = links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Document &&
                target.Id == sourceTarget.Id)
            ?? links.Targets.First(target =>
                target.Kind == LinkTargetKind.Document &&
                string.Equals(
                    target.RelativePath,
                    task.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase));

        string taskRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                taskDocumentPath));

        LinkTargetEntry taskTarget = links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Document &&
                string.Equals(
                    target.RelativePath,
                    taskRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "Le document Tâches.md n'a pas pu être indexé après sa création.");

        TextDocumentSession taskSession = await TextDocumentSession.OpenAsync(
            taskDocumentPath,
            cancellationToken);

        List<string> taskLines = SplitDocumentLines(
            taskSession.Content,
            out string taskNewline,
            out bool taskHadTrailingNewline);

        int taskMarkerIndex = FindPromotionMarkerIndex(
            taskLines,
            MeetingActionTaskMarkerPrefix,
            promotionId);

        int promotedTaskLineIndex;
        bool created;

        if (taskMarkerIndex > 0)
        {
            promotedTaskLineIndex = taskMarkerIndex - 1;
            Match existingTaskMatch = CheckboxPattern().Match(
                taskLines[promotedTaskLineIndex]);

            if (!existingTaskMatch.Success)
            {
                throw new InvalidDataException(
                    "Le marqueur de tâche promue n'est plus associé à une checkbox Markdown.");
            }

            taskLines[promotedTaskLineIndex] =
                existingTaskMatch.Groups["prefix"].Value +
                existingTaskMatch.Groups["checked"].Value +
                "] " +
                body;

            taskLines[taskMarkerIndex] =
                "  " + BuildPromotionMarker(
                    MeetingActionTaskMarkerPrefix,
                    promotionId);

            UpsertLinkLine(
                taskLines,
                taskMarkerIndex + 1,
                "Source",
                sourceTarget.QualifiedName);

            created = false;
        }
        else
        {
            EnsureMeetingActionSection(
                taskLines);

            if (taskLines.Count > 0 &&
                taskLines[^1].Length > 0)
            {
                taskLines.Add(
                    string.Empty);
            }

            promotedTaskLineIndex = taskLines.Count;
            taskLines.Add(
                $"- [{(task.IsCompleted ? "x" : " ")}] {body}");
            taskLines.Add(
                "  " + BuildPromotionMarker(
                    MeetingActionTaskMarkerPrefix,
                    promotionId));
            taskLines.Add(
                $"  - Source : [[{sourceTarget.QualifiedName}]]");

            created = true;
        }

        await taskSession.SaveAsync(
            JoinDocumentLines(
                taskLines,
                taskNewline,
                taskHadTrailingNewline),
            cancellationToken);

        if (sourceMarkerIndex > 0)
        {
            sourceLines[sourceMarkerIndex] =
                "  " + BuildPromotionMarker(
                    MeetingActionSourceMarkerPrefix,
                    promotionId);

            UpsertLinkLine(
                sourceLines,
                sourceMarkerIndex + 1,
                "Tâche projet",
                taskTarget.QualifiedName);
        }
        else
        {
            sourceLines.Insert(
                sourceLineIndex + 1,
                "  " + BuildPromotionMarker(
                    MeetingActionSourceMarkerPrefix,
                    promotionId));
            sourceLines.Insert(
                sourceLineIndex + 2,
                $"  - Tâche projet : [[{taskTarget.QualifiedName}]]");
        }

        await sourceSession.SaveAsync(
            JoinDocumentLines(
                sourceLines,
                sourceNewline,
                sourceHadTrailingNewline),
            cancellationToken);

        await _linkIndex.RefreshAsync(
            cancellationToken);

        return new MeetingActionPromotionResult
        {
            PromotionId = promotionId,
            ProjectTaskRelativePath = taskRelativePath,
            ProjectTaskLineNumber = promotedTaskLineIndex + 1,
            Created = created
        };
    }

    /// <summary>
    /// Performs the <c>SetCompletedAsync</c> operation.
    /// </summary>
    /// <param name="task">The <c>task</c> value.</param>
    /// <param name="completed">The <c>completed</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task SetCompletedAsync(
            TaskItem task,
            bool completed,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        string fullPath = ResolveWorkspacePath(
            task.SourceRelativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Le document source de la tâche est introuvable.",
                fullPath);
        }

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            fullPath,
            cancellationToken);

        string newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        string normalized = session.Content
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

        bool hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        global::System.Collections.Generic.List<string> lines = normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(
                lines.Count - 1);
        }

        int index = ResolveTaskLineIndex(
            lines,
            task,
            fullPath);

        string updatedLine = SetCheckboxState(
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

        string content = string.Join(
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

    /// <summary>
    /// Rewrites the readable metadata segments of one Markdown checkbox task.
    /// </summary>
    /// <param name="task">The source task to update.</param>
    /// <param name="metadata">The complete metadata set to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the local Markdown update.</returns>
    public async Task UpdateMetadataAsync(
            TaskItem task,
            TaskMetadataUpdate metadata,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            task);
        ArgumentNullException.ThrowIfNull(
            metadata);

        string fullPath = ResolveWorkspacePath(
            task.SourceRelativePath);

        if (!File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "Le document source de la tâche est introuvable.",
                fullPath);
        }

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session =
            await TextDocumentSession.OpenAsync(
                fullPath,
                cancellationToken);

        string newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        string normalized = session.Content
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

        bool hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        global::System.Collections.Generic.List<string> lines =
            normalized.Split(
                '\n')
            .ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(
                lines.Count - 1);
        }

        int index = ResolveTaskLineIndex(
            lines,
            task,
            fullPath);

        string owner = NormalizeMetadataValue(
            metadata.Owner,
            "responsable");
        string priority = NormalizeMetadataValue(
            metadata.Priority,
            "priorité");
        string status = NormalizeMetadataValue(
            metadata.Status,
            "statut");

        global::System.Collections.Generic.IReadOnlyList<string> tags =
            NormalizeTags(
                metadata.Tags);

        string body = BuildTaskBody(
            task.Text,
            owner,
            metadata.DueDate,
            priority,
            status,
            tags);

        global::System.Text.RegularExpressions.Match match =
            CheckboxPattern().Match(
                lines[index]);

        if (!match.Success)
        {
            throw new InvalidDataException(
                "La ligne source n'est plus une checkbox Markdown.");
        }

        string updatedLine =
            match.Groups["prefix"].Value +
            match.Groups["checked"].Value +
            "] " +
            body;

        if (string.Equals(
                updatedLine,
                lines[index],
                StringComparison.Ordinal))
        {
            return;
        }

        lines[index] =
            updatedLine;

        string content = string.Join(
            newline,
            lines);

        if (hadTrailingNewline)
        {
            content +=
                newline;
        }

        await session.SaveAsync(
            content,
            cancellationToken);
    }

    private const string MeetingActionSourceMarkerPrefix = "<!-- nodalis:meeting-action-source:";
    private const string MeetingActionTaskMarkerPrefix = "<!-- nodalis:meeting-action-task:";
    private const string PromotionMarkerSuffix = " -->";

    /// <summary>
    /// Creates a stable promotion identifier from the source meeting action identity.
    /// </summary>
    /// <param name="task">The source meeting action.</param>
    /// <returns>A deterministic identifier for repeated promotion attempts.</returns>
    private static Guid CreateMeetingActionPromotionId(
            TaskItem task)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{task.SourceDocumentId:D}|{task.LineNumber}|{task.Text.Trim()}"));

        return new Guid(
            bytes.AsSpan(
                0,
                16));
    }

    /// <summary>
    /// Splits a text document into editable lines while remembering its newline convention and trailing newline state.
    /// </summary>
    /// <param name="content">The complete document content.</param>
    /// <param name="newline">Receives the original newline convention.</param>
    /// <param name="hadTrailingNewline">Receives whether the original document ended with a newline.</param>
    /// <returns>The normalized document lines without the synthetic trailing empty line.</returns>
    private static List<string> SplitDocumentLines(
            string content,
            out string newline,
            out bool hadTrailingNewline)
    {
        newline = content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        string normalized = content
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

        hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        List<string> lines = normalized.Split(
                '\n')
            .ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(
                lines.Count - 1);
        }

        return lines;
    }

    /// <summary>
    /// Rebuilds a text document using its original newline convention and trailing newline state.
    /// </summary>
    /// <param name="lines">The document lines.</param>
    /// <param name="newline">The newline convention to use.</param>
    /// <param name="hadTrailingNewline">Whether to restore a trailing newline.</param>
    /// <returns>The rebuilt document content.</returns>
    private static string JoinDocumentLines(
            IReadOnlyList<string> lines,
            string newline,
            bool hadTrailingNewline)
    {
        string content = string.Join(
            newline,
            lines);

        if (hadTrailingNewline)
        {
            content += newline;
        }

        return content;
    }

    /// <summary>
    /// Finds one exact promotion marker in a document.
    /// </summary>
    /// <param name="lines">The document lines.</param>
    /// <param name="prefix">The marker prefix.</param>
    /// <param name="promotionId">The promotion identifier to locate.</param>
    /// <returns>The zero-based marker line index, or -1 when absent.</returns>
    private static int FindPromotionMarkerIndex(
            IReadOnlyList<string> lines,
            string prefix,
            Guid promotionId)
    {
        for (int index = 0;
             index < lines.Count;
             index++)
        {
            if (TryParsePromotionMarker(
                    lines[index],
                    prefix,
                    out Guid candidateId) &&
                candidateId == promotionId)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Parses a hidden Nodalis promotion marker.
    /// </summary>
    /// <param name="line">The Markdown line to inspect.</param>
    /// <param name="prefix">The expected marker prefix.</param>
    /// <param name="promotionId">Receives the marker identifier when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the line contains a valid marker.</returns>
    private static bool TryParsePromotionMarker(
            string line,
            string prefix,
            out Guid promotionId)
    {
        promotionId = Guid.Empty;
        string trimmed = line.Trim();

        if (!trimmed.StartsWith(
                prefix,
                StringComparison.Ordinal) ||
            !trimmed.EndsWith(
                PromotionMarkerSuffix,
                StringComparison.Ordinal))
        {
            return false;
        }

        int valueLength =
            trimmed.Length -
            prefix.Length -
            PromotionMarkerSuffix.Length;

        if (valueLength <= 0)
        {
            return false;
        }

        string value = trimmed.Substring(
            prefix.Length,
            valueLength);

        return Guid.TryParse(
            value,
            out promotionId);
    }

    /// <summary>
    /// Builds the canonical hidden Markdown marker for a promoted meeting action.
    /// </summary>
    /// <param name="prefix">The marker prefix.</param>
    /// <param name="promotionId">The promotion identifier.</param>
    /// <returns>The marker line without indentation.</returns>
    private static string BuildPromotionMarker(
            string prefix,
            Guid promotionId) =>
            $"{prefix}{promotionId:D}{PromotionMarkerSuffix}";

    /// <summary>
    /// Adds the dedicated meeting-action section to a project task document when it is missing.
    /// </summary>
    /// <param name="lines">The editable task document lines.</param>
    private static void EnsureMeetingActionSection(
            List<string> lines)
    {
        if (lines.Any(line =>
                string.Equals(
                    line.Trim(),
                    "## Actions de réunion",
                    StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        if (lines.Count > 0 &&
            lines[^1].Length > 0)
        {
            lines.Add(
                string.Empty);
        }

        lines.Add(
            "## Actions de réunion");
        lines.Add(
            string.Empty);
    }

    /// <summary>
    /// Updates or inserts the readable internal-link line following a promotion marker.
    /// </summary>
    /// <param name="lines">The editable document lines.</param>
    /// <param name="lineIndex">The expected zero-based link line position.</param>
    /// <param name="label">The localized link label.</param>
    /// <param name="qualifiedTarget">The indexed target qualified name.</param>
    private static void UpsertLinkLine(
            List<string> lines,
            int lineIndex,
            string label,
            string qualifiedTarget)
    {
        string linkLine =
            $"  - {label} : [[{qualifiedTarget}]]";

        if (lineIndex < lines.Count &&
            lines[lineIndex].TrimStart().StartsWith(
                "- " + label + " :",
                StringComparison.CurrentCultureIgnoreCase))
        {
            lines[lineIndex] =
                linkLine;
            return;
        }

        lines.Insert(
            Math.Min(
                lineIndex,
                lines.Count),
            linkLine);
    }

    /// <summary>
    /// Normalizes one workspace-relative path to forward slashes.
    /// </summary>
    /// <param name="relativePath">The relative path to normalize.</param>
    /// <returns>The portable workspace-relative path.</returns>
    private static string NormalizeRelativePath(
            string relativePath) =>
            relativePath.Replace(
                Path.DirectorySeparatorChar,
                '/');

    /// <summary>
    /// Performs the <c>ResolveContext</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private (Guid? ApplicationId, Guid? ProjectId) ResolveContext(
            string? contextPath,
            LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return (null, null);
        }

        string fullPath = Path.GetFullPath(
            contextPath);

        string relativePath = Path.GetRelativePath(
                _workspaceRoot,
                File.Exists(fullPath)
                    ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
                    : fullPath)
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

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
            global::Nodalis.Core.Links.LinkTargetEntry? application = links.Targets
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

        global::Nodalis.Core.Links.LinkTargetEntry? app = links.Targets
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

    /// <summary>
    /// Performs the <c>ResolveWorkspacePath</c> operation.
    /// </summary>
    /// <param name="relativePath">The <c>relativePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private string ResolveWorkspacePath(
            string relativePath) =>
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

    /// <summary>
    /// Parses readable pipe-delimited task metadata while preserving unknown text segments.
    /// </summary>
    /// <param name="body">The checkbox body without its marker.</param>
    /// <returns>The task text and recognized metadata.</returns>
    private static (
            string Text,
            string? Owner,
            DateOnly? DueDate,
            string? Priority,
            string? Status,
            IReadOnlyList<string> Tags)
            ParseMetadata(
                string body)
    {
        string[] segments = body
            .Split(
                '|',
                StringSplitOptions.TrimEntries)
            .ToArray();

        global::System.Collections.Generic.List<string> textSegments =
            new List<string>();

        string? owner =
            null;
        DateOnly? dueDate =
            null;
        string? priority =
            null;
        string? status =
            null;
        global::System.Collections.Generic.List<string> tags =
            new List<string>();

        foreach (string segment in segments)
        {
            int separatorIndex =
                segment.IndexOf(
                    ':');

            if (separatorIndex <= 0)
            {
                if (!string.IsNullOrWhiteSpace(
                        segment))
                {
                    textSegments.Add(
                        segment.Trim());
                }

                continue;
            }

            string key = NormalizeMetadataKey(
                segment[..separatorIndex]);

            string value = segment[(separatorIndex + 1)..]
                .Trim();

            bool recognized =
                key is
                    "responsable" or
                    "owner" or
                    "echeance" or
                    "due" or
                    "priorite" or
                    "priority" or
                    "statut" or
                    "status" or
                    "tag" or
                    "tags";

            if (!recognized)
            {
                textSegments.Add(
                    segment.Trim());
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                continue;
            }

            if (key is "responsable" or "owner")
            {
                owner =
                    value;
                continue;
            }

            if (key is "echeance" or "due")
            {
                if (DateOnly.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out global::System.DateOnly parsed))
                {
                    dueDate =
                        parsed;
                }

                continue;
            }

            if (key is "priorite" or "priority")
            {
                priority =
                    value;
                continue;
            }

            if (key is "statut" or "status")
            {
                status =
                    value;
                continue;
            }

            tags.AddRange(
                ParseTags(
                    value));
        }

        string text =
            string.Join(
                " | ",
                textSegments);

        return (
            text,
            owner,
            dueDate,
            priority,
            status,
            tags
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
    }

    /// <summary>
    /// Performs the <c>NormalizeMetadataKey</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Parses a comma-separated task tag list.
    /// </summary>
    /// <param name="value">The raw tag value.</param>
    /// <returns>Normalized unique tags.</returns>
    private static IReadOnlyList<string> ParseTags(
            string value) =>
            value
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(tag =>
                    tag.Trim()
                        .TrimStart(
                            '#'))
                .Where(tag =>
                    tag.Length > 0)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

    /// <summary>
    /// Normalizes tags before writing them back to Markdown.
    /// </summary>
    /// <param name="tags">The edited tags.</param>
    /// <returns>Normalized unique tags.</returns>
    private static IReadOnlyList<string> NormalizeTags(
            IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(
            tags);

        global::System.Collections.Generic.List<string> normalized =
            new List<string>();

        foreach (string tag in tags)
        {
            string value =
                tag.Trim()
                    .TrimStart(
                        '#');

            if (value.Length == 0)
            {
                continue;
            }

            if (value.Contains(
                    '|') ||
                value.Contains(
                    ',') ||
                value.Contains(
                    '\r') ||
                value.Contains(
                    '\n'))
            {
                throw new InvalidDataException(
                    "Un tag de tâche ne peut pas contenir « | », une virgule ou un retour à la ligne.");
            }

            if (!normalized.Contains(
                    value,
                    StringComparer.CurrentCultureIgnoreCase))
            {
                normalized.Add(
                    value);
            }
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes one optional single-line metadata value.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <param name="displayName">The metadata field name for validation messages.</param>
    /// <returns>The trimmed value or an empty string.</returns>
    private static string NormalizeMetadataValue(
            string? value,
            string displayName)
    {
        string normalized =
            value?.Trim() ??
            string.Empty;

        if (normalized.Contains(
                '|') ||
            normalized.Contains(
                '\r') ||
            normalized.Contains(
                '\n'))
        {
            throw new InvalidDataException(
                $"La valeur « {displayName} » doit tenir sur une ligne et ne peut pas contenir « | ».");
        }

        return normalized;
    }

    /// <summary>
    /// Builds the canonical readable checkbox body from task text and metadata.
    /// </summary>
    /// <param name="text">The task text.</param>
    /// <param name="owner">The owner.</param>
    /// <param name="dueDate">The due date.</param>
    /// <param name="priority">The priority.</param>
    /// <param name="status">The status.</param>
    /// <param name="tags">The tags.</param>
    /// <returns>The Markdown checkbox body.</returns>
    private static string BuildTaskBody(
            string text,
            string owner,
            DateOnly? dueDate,
            string priority,
            string status,
            IReadOnlyList<string> tags)
    {
        global::System.Collections.Generic.List<string> segments =
            new List<string>
            {
                text.Trim()
            };

        if (owner.Length > 0)
        {
            segments.Add(
                $"Responsable: {owner}");
        }

        if (dueDate is DateOnly date)
        {
            segments.Add(
                $"Échéance: {date:yyyy-MM-dd}");
        }

        if (priority.Length > 0)
        {
            segments.Add(
                $"Priorité: {priority}");
        }

        if (status.Length > 0)
        {
            segments.Add(
                $"Statut: {status}");
        }

        if (tags.Count > 0)
        {
            segments.Add(
                $"Tags: {string.Join(", ", tags)}");
        }

        return string.Join(
            " | ",
            segments);
    }

    /// <summary>
    /// Resolves the current source line for a task, including safe relocation after inserted lines.
    /// </summary>
    /// <param name="lines">The current source lines.</param>
    /// <param name="task">The indexed task.</param>
    /// <param name="fullPath">The source path used in conflict diagnostics.</param>
    /// <returns>The zero-based current source line index.</returns>
    private static int ResolveTaskLineIndex(
            IReadOnlyList<string> lines,
            TaskItem task,
            string fullPath)
    {
        int index =
            task.LineNumber -
            1;

        if (index >= 0 &&
            index < lines.Count &&
            string.Equals(
                lines[index],
                task.RawLine,
                StringComparison.Ordinal))
        {
            return index;
        }

        int[] candidates = lines
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

        return candidates[0];
    }

    /// <summary>
    /// Returns the stable sort rank for common priority labels.
    /// </summary>
    /// <param name="priority">The optional priority label.</param>
    /// <returns>A lower number for a more important priority.</returns>
    public static int GetPriorityRank(
            string? priority)
    {
        string normalized = NormalizeMetadataKey(
            priority ??
            string.Empty);

        return normalized switch
        {
            "critique" or "critical" or "urgent" => 0,
            "haute" or "high" => 1,
            "normale" or "normal" or "moyenne" or "medium" => 2,
            "basse" or "low" => 3,
            _ => 4
        };
    }

    /// <summary>
    /// Performs the <c>SetCheckboxState</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <param name="completed">The <c>completed</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string SetCheckboxState(
            string line,
            bool completed)
    {
        global::System.Text.RegularExpressions.Match match = CheckboxPattern().Match(
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

    /// <summary>
    /// Performs the <c>CreateTaskId</c> operation.
    /// </summary>
    /// <param name="sourceDocumentId">The <c>sourceDocumentId</c> value.</param>
    /// <param name="lineNumber">The <c>lineNumber</c> value.</param>
    /// <param name="body">The <c>body</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Guid CreateTaskId(
            Guid sourceDocumentId,
            int lineNumber,
            string body)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{sourceDocumentId:D}|{lineNumber}|{body}"));

        return new Guid(
            bytes.AsSpan(
                0,
                16));
    }

    /// <summary>
    /// Performs the <c>IsRelativeAncestor</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsRelativeAncestor(
            string candidateParent,
            string child) =>
            child.StartsWith(
                candidateParent.TrimEnd('/') + "/",
                StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the <c>IsRelativeAncestorOrEqual</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>CheckboxPattern</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    [GeneratedRegex(
            @"^(?<prefix>\s*[-*+]\s+\[)(?<checked>[ xX])(?<suffix>\]\s+(?<body>.*))$",
            RegexOptions.CultureInvariant)]
    private static partial Regex CheckboxPattern();

}
