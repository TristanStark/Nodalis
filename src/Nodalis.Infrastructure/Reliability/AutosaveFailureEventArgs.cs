namespace Nodalis.Infrastructure.Reliability;

public sealed class AutosaveFailureEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of <see cref="AutosaveFailureEventArgs"/>.
    /// </summary>
    /// <param name="exception">The <c>exception</c> value.</param>
    /// <param name="pendingContent">The <c>pendingContent</c> value.</param>
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
