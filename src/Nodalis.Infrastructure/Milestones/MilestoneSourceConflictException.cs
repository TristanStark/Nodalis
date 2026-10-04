namespace Nodalis.Infrastructure.Milestones;

public sealed class MilestoneSourceConflictException : IOException
{
    /// <summary>
    /// Initializes a new instance of <see cref="MilestoneSourceConflictException"/>.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
public MilestoneSourceConflictException(string path)
        : base($"Le jalon ne peut plus être localisé sans ambiguïté dans '{path}'.")
    {
        Path = path;
    }

    public string Path { get; }
}
