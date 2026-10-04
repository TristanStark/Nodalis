namespace Nodalis.Infrastructure.Reliability;

public sealed class WorkspaceFileChangeMonitor : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    public WorkspaceFileChangeMonitor(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var fullPath = Path.GetFullPath(workspaceRoot);
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

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
    }

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

    private static bool ShouldIgnore(string path)
    {
        var fileName = Path.GetFileName(path);

        return fileName.StartsWith('.', StringComparison.Ordinal) &&
               fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}
