namespace Nodalis.Infrastructure.Tasks;

public sealed class TaskSourceConflictException : IOException
{
    /// <summary>
    /// Initializes a new instance of <see cref="TaskSourceConflictException"/>.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    public TaskSourceConflictException(string path)
            : base($"La tâche ne peut plus être localisée sans ambiguïté dans '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}
