using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Tasks;

namespace Nodalis.Infrastructure.Kanban;

/// <summary>
/// Projects Markdown tasks onto a simple four-state board and safely persists board moves to task metadata.
/// </summary>
public sealed class WorkspaceKanbanService
{
    /// <summary>Canonical to-do status.</summary>
    public const string TodoStatus = "À faire";

    /// <summary>Canonical in-progress status.</summary>
    public const string InProgressStatus = "En cours";

    /// <summary>Canonical blocked status.</summary>
    public const string BlockedStatus = "Bloqué";

    /// <summary>Canonical completed status.</summary>
    public const string DoneStatus = "Terminé";

    private readonly WorkspaceTaskService _taskService;

    /// <summary>
    /// Initializes a Kanban service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceKanbanService(
            string workspaceRoot)
    {
        _taskService =
            new WorkspaceTaskService(
                workspaceRoot);
    }

    /// <summary>
    /// Reads all tasks from their Markdown sources without creating board-specific storage.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All current tasks, including completed tasks.</returns>
    public async Task<IReadOnlyList<TaskItem>> RefreshAsync(
            CancellationToken cancellationToken = default)
    {
        TaskCollection catalog =
            await _taskService.RefreshAsync(
                cancellationToken);

        return catalog.Tasks;
    }

    /// <summary>
    /// Moves one task to a canonical board status and updates both readable status metadata and completion checkbox safely.
    /// </summary>
    /// <param name="task">The source task.</param>
    /// <param name="targetStatus">One of the four canonical board statuses.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the Markdown update.</returns>
    public async Task MoveAsync(
            TaskItem task,
            string targetStatus,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        string normalizedStatus =
            NormalizeTargetStatus(
                targetStatus);

        bool shouldBeCompleted =
            string.Equals(
                normalizedStatus,
                DoneStatus,
                StringComparison.CurrentCulture);

        TaskItem current =
            await ResolveCurrentTaskAsync(
                task,
                cancellationToken);

        if (current.IsCompleted !=
            shouldBeCompleted)
        {
            await _taskService.SetCompletedAsync(
                current,
                shouldBeCompleted,
                cancellationToken);

            current =
                await ResolveCurrentTaskAsync(
                    task,
                    cancellationToken);
        }

        if (!string.Equals(
                current.Status,
                normalizedStatus,
                StringComparison.CurrentCulture))
        {
            await _taskService.UpdateMetadataAsync(
                current,
                new TaskMetadataUpdate
                {
                    Owner =
                        current.Owner,
                    DueDate =
                        current.DueDate,
                    Priority =
                        current.Priority,
                    Status =
                        normalizedStatus,
                    Tags =
                        current.Tags
                },
                cancellationToken);
        }
    }

    /// <summary>
    /// Maps one task to the canonical board column without altering its source.
    /// </summary>
    /// <param name="task">The source task.</param>
    /// <returns>The canonical board status.</returns>
    public static string GetColumnStatus(
            TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        if (task.IsCompleted ||
            IsStatus(
                task.Status,
                DoneStatus,
                "Done",
                "Completed"))
        {
            return DoneStatus;
        }

        if (IsStatus(
                task.Status,
                BlockedStatus,
                "Bloque",
                "Blocked",
                "Blocker"))
        {
            return BlockedStatus;
        }

        if (IsStatus(
                task.Status,
                InProgressStatus,
                "En cours",
                "Doing",
                "In progress",
                "InProgress"))
        {
            return InProgressStatus;
        }

        return TodoStatus;
    }

    /// <summary>
    /// Resolves the current task after a previous source rewrite using stable source coordinates and title.
    /// </summary>
    /// <param name="original">The original task projection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refreshed source task.</returns>
    private async Task<TaskItem> ResolveCurrentTaskAsync(
            TaskItem original,
            CancellationToken cancellationToken)
    {
        TaskCollection catalog =
            await _taskService.RefreshAsync(
                cancellationToken);

        return catalog.Tasks.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.SourceRelativePath,
                    original.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase) &&
                candidate.LineNumber ==
                    original.LineNumber &&
                string.Equals(
                    candidate.Text,
                    original.Text,
                    StringComparison.CurrentCulture))
            ?? throw new TaskSourceConflictException(
                original.SourceRelativePath);
    }

    /// <summary>
    /// Validates and normalizes a requested target column.
    /// </summary>
    /// <param name="targetStatus">Requested target status.</param>
    /// <returns>The canonical status.</returns>
    private static string NormalizeTargetStatus(
            string targetStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            targetStatus);

        if (string.Equals(
                targetStatus,
                TodoStatus,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return TodoStatus;
        }

        if (string.Equals(
                targetStatus,
                InProgressStatus,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return InProgressStatus;
        }

        if (string.Equals(
                targetStatus,
                BlockedStatus,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return BlockedStatus;
        }

        if (string.Equals(
                targetStatus,
                DoneStatus,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return DoneStatus;
        }

        throw new ArgumentOutOfRangeException(
            nameof(targetStatus),
            "Le Kanban accepte uniquement À faire, En cours, Bloqué ou Terminé.");
    }

    /// <summary>
    /// Compares one task status against accepted aliases.
    /// </summary>
    /// <param name="value">The source status.</param>
    /// <param name="aliases">Accepted aliases.</param>
    /// <returns>Whether one alias matches.</returns>
    private static bool IsStatus(
            string? value,
            params string[] aliases)
    {
        return !string.IsNullOrWhiteSpace(
                   value) &&
               aliases.Any(alias =>
                   string.Equals(
                       value.Trim(),
                       alias,
                       StringComparison.CurrentCultureIgnoreCase));
    }
}
