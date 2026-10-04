namespace Nodalis.Infrastructure.Milestones;

public sealed class MilestoneSourceConflictException : IOException
{
    public MilestoneSourceConflictException(string path)
        : base($"Le jalon ne peut plus être localisé sans ambiguïté dans '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}
