using System.IO;

namespace Nodalis.Infrastructure.Trash;

public sealed class TrashRestoreCollisionException : IOException
{
    /// <summary>
    /// Initializes a new instance of <see cref="TrashRestoreCollisionException"/>.
    /// </summary>
    /// <param name="destinationPath">The occupied restore destination.</param>
    public TrashRestoreCollisionException(string destinationPath)
        : base($"The original location '{destinationPath}' is already occupied.")
    {
        DestinationPath = destinationPath;
    }

    public string DestinationPath { get; }
}
