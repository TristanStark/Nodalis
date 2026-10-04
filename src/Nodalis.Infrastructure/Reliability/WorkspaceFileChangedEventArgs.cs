namespace Nodalis.Infrastructure.Reliability;

public sealed class WorkspaceFileChangedEventArgs : EventArgs
{
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
