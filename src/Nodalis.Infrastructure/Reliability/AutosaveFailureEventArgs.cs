namespace Nodalis.Infrastructure.Reliability;

public sealed class AutosaveFailureEventArgs : EventArgs
{
    public AutosaveFailureEventArgs(
        Exception exception,
        string pendingContent)
    {
        Exception = exception;
        PendingContent = pendingContent;
    }

    public Exception Exception { get; }

    public string PendingContent { get; }
}
