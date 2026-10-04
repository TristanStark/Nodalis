namespace Nodalis.Infrastructure.Tasks;

public sealed class TaskSourceConflictException : IOException
{
    public TaskSourceConflictException(string path)
        : base($"La tâche ne peut plus être localisée sans ambiguïté dans '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}
