using System.Globalization;
using System.Text.RegularExpressions;
using Nodalis.Core.Calendar;
using Nodalis.Core.Links;
using Nodalis.Core.Milestones;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Tasks;

namespace Nodalis.Infrastructure.Calendar;

/// <summary>
/// Builds a calendar view directly from dated Markdown workspace data and safely rewrites explicit source dates.
/// </summary>
public sealed partial class WorkspaceCalendarService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceTaskService _taskService;
    private readonly WorkspaceMilestoneService _milestoneService;
    private readonly WorkspaceLinkIndexService _linkIndexService;

    /// <summary>
    /// Initializes a calendar service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceCalendarService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _taskService =
            new WorkspaceTaskService(
                _workspaceRoot);
        _milestoneService =
            new WorkspaceMilestoneService(
                _workspaceRoot);
        _linkIndexService =
            new WorkspaceLinkIndexService(
                _workspaceRoot);
    }

    /// <summary>
    /// Rebuilds the calendar snapshot from existing task, milestone, and meeting sources.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The derived calendar snapshot.</returns>
    public async Task<WorkspaceCalendarSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default)
    {
        TaskCollection taskCatalog =
            await _taskService.RefreshAsync(
                cancellationToken);

        IReadOnlyList<MilestoneItem> milestones =
            await _milestoneService.GetMilestonesAsync(
                _workspaceRoot,
                cancellationToken);

        LinkIndexCatalog links =
            await _linkIndexService.RefreshAsync(
                cancellationToken);

        List<CalendarEventItem> events =
            new List<CalendarEventItem>();

        foreach (TaskItem task in taskCatalog.Tasks.Where(task =>
                     task.DueDate is not null))
        {
            events.Add(
                new CalendarEventItem
                {
                    Kind =
                        CalendarEventKind.Task,
                    Date =
                        task.DueDate!.Value,
                    Title =
                        task.Text,
                    SourceRelativePath =
                        task.SourceRelativePath,
                    LineNumber =
                        task.LineNumber,
                    ApplicationId =
                        task.ApplicationId,
                    ApplicationName =
                        task.ApplicationName,
                    ProjectId =
                        task.ProjectId,
                    ProjectName =
                        task.ProjectName,
                    CanReschedule =
                        true
                });
        }

        foreach (MilestoneItem milestone in milestones.Where(milestone =>
                     milestone.TargetDate is not null))
        {
            (LinkTargetEntry? Application, LinkTargetEntry? Project) scope =
                ResolveScope(
                    milestone.SourceRelativePath,
                    links);

            events.Add(
                new CalendarEventItem
                {
                    Kind =
                        CalendarEventKind.Milestone,
                    Date =
                        milestone.TargetDate!.Value,
                    Title =
                        milestone.Name,
                    SourceRelativePath =
                        milestone.SourceRelativePath,
                    LineNumber =
                        milestone.LineNumber,
                    ApplicationId =
                        scope.Application?.Id,
                    ApplicationName =
                        scope.Application?.DisplayName,
                    ProjectId =
                        milestone.ProjectId,
                    ProjectName =
                        milestone.ProjectName,
                    CanReschedule =
                        true
                });
        }

        foreach (LinkTargetEntry document in links.Targets.Where(target =>
                     target.Kind ==
                         LinkTargetKind.Document &&
                     IsMeetingPath(
                         target.RelativePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullPath =
                ResolveWorkspacePath(
                    document.RelativePath);

            if (!File.Exists(
                    fullPath))
            {
                continue;
            }

            DateOnly? meetingDate =
                await ReadMeetingDateAsync(
                    fullPath,
                    cancellationToken);

            if (meetingDate is not DateOnly date)
            {
                continue;
            }

            (LinkTargetEntry? Application, LinkTargetEntry? Project) scope =
                ResolveScope(
                    document.RelativePath,
                    links);

            events.Add(
                new CalendarEventItem
                {
                    Kind =
                        CalendarEventKind.Meeting,
                    Date =
                        date,
                    Title =
                        ReadMeetingTitle(
                            fullPath),
                    SourceRelativePath =
                        document.RelativePath,
                    ApplicationId =
                        scope.Application?.Id,
                    ApplicationName =
                        scope.Application?.DisplayName,
                    ProjectId =
                        scope.Project?.Id,
                    ProjectName =
                        scope.Project?.DisplayName,
                    CanReschedule =
                        false
                });
        }

        return new WorkspaceCalendarSnapshot
        {
            Events =
                events
                    .OrderBy(item =>
                        item.Date)
                    .ThenBy(item =>
                        item.Kind)
                    .ThenBy(item =>
                        item.Title,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
        };
    }

    /// <summary>
    /// Rewrites an event date only when the source type has an existing explicit and safe update API.
    /// </summary>
    /// <param name="item">The derived calendar item.</param>
    /// <param name="newDate">The new explicit source date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the safe source update.</returns>
    public async Task RescheduleAsync(
            CalendarEventItem item,
            DateOnly newDate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            item);

        if (!item.CanReschedule)
        {
            throw new InvalidOperationException(
                "Ce type d'élément ne dispose pas d'une écriture de date atomique suffisamment sûre pour le déplacement.");
        }

        if (item.Date ==
            newDate)
        {
            return;
        }

        if (item.Kind ==
            CalendarEventKind.Task)
        {
            TaskCollection catalog =
                await _taskService.RefreshAsync(
                    cancellationToken);

            TaskItem task =
                catalog.Tasks.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.SourceRelativePath,
                        item.SourceRelativePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    candidate.LineNumber ==
                        item.LineNumber &&
                    string.Equals(
                        candidate.Text,
                        item.Title,
                        StringComparison.CurrentCulture))
                ?? throw new TaskSourceConflictException(
                    ResolveWorkspacePath(
                        item.SourceRelativePath));

            if (task.DueDate is null)
            {
                throw new InvalidOperationException(
                    "La tâche ne possède plus d'échéance explicite.");
            }

            await _taskService.UpdateMetadataAsync(
                task,
                new TaskMetadataUpdate
                {
                    Owner =
                        task.Owner,
                    DueDate =
                        newDate,
                    Priority =
                        task.Priority,
                    Status =
                        task.Status,
                    Tags =
                        task.Tags
                },
                cancellationToken);

            return;
        }

        if (item.Kind ==
            CalendarEventKind.Milestone)
        {
            IReadOnlyList<MilestoneItem> milestones =
                await _milestoneService.GetMilestonesAsync(
                    _workspaceRoot,
                    cancellationToken);

            MilestoneItem milestone =
                milestones.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.SourceRelativePath,
                        item.SourceRelativePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    candidate.LineNumber ==
                        item.LineNumber &&
                    string.Equals(
                        candidate.Name,
                        item.Title,
                        StringComparison.CurrentCulture))
                ?? throw new MilestoneSourceConflictException(
                    ResolveWorkspacePath(
                        item.SourceRelativePath));

            if (milestone.TargetDate is null)
            {
                throw new InvalidOperationException(
                    "Le jalon ne possède plus de date cible explicite.");
            }

            await _milestoneService.UpdateAsync(
                milestone,
                new MilestoneDraft
                {
                    Name =
                        milestone.Name,
                    TargetDate =
                        newDate,
                    Status =
                        milestone.Status,
                    Description =
                        milestone.Description,
                    Link =
                        milestone.Link,
                    DependencyIds =
                        milestone.DependencyIds
                },
                cancellationToken);

            return;
        }

        throw new InvalidOperationException(
            "Le déplacement de cette source n'est pas pris en charge.");
    }

    /// <summary>
    /// Reads the explicit date from one meeting report, falling back to the dated filename.
    /// </summary>
    /// <param name="fullPath">The absolute meeting path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed meeting date, when available.</returns>
    private static async Task<DateOnly?> ReadMeetingDateAsync(
            string fullPath,
            CancellationToken cancellationToken)
    {
        string content =
            await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);

        Match metadata =
            MeetingDatePattern().Match(
                content);

        if (metadata.Success &&
            DateOnly.TryParseExact(
                metadata.Groups["date"].Value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
        {
            return date;
        }

        Match fileName =
            FileDatePattern().Match(
                Path.GetFileNameWithoutExtension(
                    fullPath));

        return fileName.Success &&
               DateOnly.TryParseExact(
                   fileName.Groups["date"].Value,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out date)
            ? date
            : null;
    }

    /// <summary>
    /// Reads a friendly meeting title from the dated filename.
    /// </summary>
    /// <param name="fullPath">The absolute meeting path.</param>
    /// <returns>The meeting title.</returns>
    private static string ReadMeetingTitle(
            string fullPath)
    {
        string name =
            Path.GetFileNameWithoutExtension(
                fullPath);

        Match match =
            MeetingFileTitlePattern().Match(
                name);

        return match.Success
            ? match.Groups["title"].Value.Trim()
            : name;
    }

    /// <summary>
    /// Resolves application and project ancestors for one relative source path.
    /// </summary>
    /// <param name="relativePath">The source path relative to the workspace.</param>
    /// <param name="links">The current link index.</param>
    /// <returns>The nearest application and project targets.</returns>
    private static (LinkTargetEntry? Application, LinkTargetEntry? Project) ResolveScope(
            string relativePath,
            LinkIndexCatalog links)
    {
        LinkTargetEntry? project =
            links.Targets
                .Where(target =>
                    target.Kind ==
                        LinkTargetKind.Project &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        relativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

        LinkTargetEntry? application =
            links.Targets
                .Where(target =>
                    target.Kind ==
                        LinkTargetKind.Application &&
                    IsRelativeAncestor(
                        target.RelativePath,
                        relativePath))
                .OrderByDescending(target =>
                    target.RelativePath.Length)
                .FirstOrDefault();

        return (
            application,
            project
        );
    }

    /// <summary>
    /// Determines whether one workspace-relative path identifies a meeting report.
    /// </summary>
    /// <param name="relativePath">The workspace-relative path.</param>
    /// <returns>Whether a Réunions path segment exists.</returns>
    private static bool IsMeetingPath(
            string relativePath)
    {
        return NormalizeRelativePath(
                relativePath)
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                string.Equals(
                    segment,
                    "Réunions",
                    StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(
                    segment,
                    "Reunions",
                    StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Determines whether one normalized relative path is an ancestor of another.
    /// </summary>
    /// <param name="ancestor">The candidate ancestor.</param>
    /// <param name="candidate">The candidate descendant.</param>
    /// <returns>Whether the relationship exists.</returns>
    private static bool IsRelativeAncestor(
            string ancestor,
            string candidate)
    {
        string normalizedAncestor =
            NormalizeRelativePath(
                ancestor)
            .TrimEnd(
                '/');
        string normalizedCandidate =
            NormalizeRelativePath(
                candidate)
            .TrimEnd(
                '/');

        return normalizedCandidate.StartsWith(
            normalizedAncestor +
            "/",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves one workspace-relative path to an absolute path.
    /// </summary>
    /// <param name="relativePath">The workspace-relative path.</param>
    /// <returns>The absolute path.</returns>
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

        string workspacePrefix =
            _workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                workspacePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le chemin calendrier se trouve hors du workspace.");
        }

        return fullPath;
    }

    /// <summary>
    /// Normalizes one workspace-relative path.
    /// </summary>
    /// <param name="value">The path to normalize.</param>
    /// <returns>A forward-slash path.</returns>
    private static string NormalizeRelativePath(
            string value)
    {
        return value.Replace(
            '\\',
            '/');
    }

    /// <summary>Gets the meeting metadata date parser.</summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"^\s*\*\*Date\s*:\*\*\s*(?<date>\d{4}-\d{2}-\d{2})\s*$",
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase |
        RegexOptions.Multiline)]
    private static partial Regex MeetingDatePattern();

    /// <summary>Gets the dated meeting filename parser.</summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"^(?<date>\d{4}-\d{2}-\d{2})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex FileDatePattern();

    /// <summary>Gets the meeting title parser for dated filenames.</summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}\s*-\s*(?<title>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MeetingFileTitlePattern();
}
