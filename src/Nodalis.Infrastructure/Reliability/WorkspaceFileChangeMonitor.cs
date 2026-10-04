namespace Nodalis.Infrastructure.Reliability;

public sealed class WorkspaceFileChangeMonitor : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceFileChangeMonitor"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
public WorkspaceFileChangeMonitor(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string fullPath = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(fullPath);

        _watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    public event EventHandler<WorkspaceFileChangedEventArgs>? FileChanged;

    /// <summary>
    /// Performs the <c>Dispose</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
    }

    /// <summary>
    /// Performs the <c>OnChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="eventArgs">The <c>eventArgs</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (ShouldIgnore(eventArgs.FullPath))
        {
            return;
        }

        FileChanged?.Invoke(
            this,
            new WorkspaceFileChangedEventArgs(
                eventArgs.FullPath,
                eventArgs.ChangeType,
                oldFullPath: null));
    }

    /// <summary>
    /// Performs the <c>OnRenamed</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="eventArgs">The <c>eventArgs</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        if (ShouldIgnore(eventArgs.FullPath) && ShouldIgnore(eventArgs.OldFullPath))
        {
            return;
        }

        FileChanged?.Invoke(
            this,
            new WorkspaceFileChangedEventArgs(
                eventArgs.FullPath,
                eventArgs.ChangeType,
                eventArgs.OldFullPath));
    }

    /// <summary>
    /// Performs the <c>ShouldIgnore</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool ShouldIgnore(string path)
    {
        string fileName = Path.GetFileName(path);

        return fileName.StartsWith(".", StringComparison.Ordinal) &&
               fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}
