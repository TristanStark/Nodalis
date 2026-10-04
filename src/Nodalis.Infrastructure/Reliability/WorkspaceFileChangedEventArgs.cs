namespace Nodalis.Infrastructure.Reliability;

public sealed class WorkspaceFileChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceFileChangedEventArgs"/>.
    /// </summary>
    /// <param name="fullPath">The <c>fullPath</c> value.</param>
    /// <param name="changeType">The <c>changeType</c> value.</param>
    /// <param name="oldFullPath">The <c>oldFullPath</c> value.</param>
public WorkspaceFileChangedEventArgs(
        string fullPath,
        WatcherChangeTypes changeType,
        string? oldFullPath)
    {
        FullPath = fullPath;
        ChangeType = changeType;
        OldFullPath = oldFullPath;
    }

    public string FullPath { get; }

    public WatcherChangeTypes ChangeType { get; }

    public string? OldFullPath { get; }
}
