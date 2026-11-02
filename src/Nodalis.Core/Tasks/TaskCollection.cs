namespace Nodalis.Core.Tasks;

public sealed record TaskCollection
{
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<TaskItem> Tasks { get; init; } = [];

    /// <summary>
    /// Read-only malformed or duplicate identity warnings. No automatic
    /// migration or correction is attempted during workspace refresh.
    /// </summary>
    public IReadOnlyList<string> IdentityDiagnostics { get; init; } = [];

    /// <summary>
    /// Typed GUID collisions across or within documents. Tasks with these
    /// markers deliberately have no linkable PersistentId, but previously
    /// linked RIS references must be diagnosed as ambiguous, not missing.
    /// </summary>
    public IReadOnlySet<Guid> ConflictingPersistentIds { get; init; } =
        new HashSet<Guid>();

    public IReadOnlyList<TaskItem> OpenTasks =>
        Tasks
            .Where(task => !task.IsCompleted)
            .ToArray();
}
