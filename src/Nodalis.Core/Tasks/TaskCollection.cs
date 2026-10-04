namespace Nodalis.Core.Tasks;

public sealed record TaskCollection
{
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<TaskItem> Tasks { get; init; } = [];

    public IReadOnlyList<TaskItem> OpenTasks =>
        Tasks
            .Where(task => !task.IsCompleted)
            .ToArray();
}
