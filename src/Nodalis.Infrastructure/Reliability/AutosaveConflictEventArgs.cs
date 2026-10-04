namespace Nodalis.Infrastructure.Reliability;

public sealed class AutosaveConflictEventArgs : EventArgs
{
    public AutosaveConflictEventArgs(
        ExternalModificationException exception,
        string pendingContent)
    {
        Exception = exception;
        PendingContent = pendingContent;
    }

    public ExternalModificationException Exception { get; }

    public string PendingContent { get; }
}
