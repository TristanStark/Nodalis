using System.IO;

namespace Nodalis.Infrastructure.Backups;

public sealed class BackupRestoreCollisionException : IOException
{
    /// <summary>
    /// Initializes a new instance of <see cref="BackupRestoreCollisionException"/>.
    /// </summary>
    /// <param name="destinationPath">The occupied restore destination.</param>
    public BackupRestoreCollisionException(string destinationPath)
        : base($"The restore destination '{destinationPath}' is not empty.")
    {
        DestinationPath = destinationPath;
    }

    public string DestinationPath { get; }
}
