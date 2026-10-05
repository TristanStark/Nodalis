namespace Nodalis.Infrastructure.Milestones;

public sealed class MilestoneDependencyConflictException : InvalidDataException
{
    /// <summary>
    /// Initializes a new instance of <see cref="MilestoneDependencyConflictException"/>.
    /// </summary>
    /// <param name="message">The dependency conflict description.</param>
    public MilestoneDependencyConflictException(string message)
        : base(message)
    {
    }
}
