namespace Nodalis.Infrastructure.Reliability;

public sealed class AutosaveConflictEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of <see cref="AutosaveConflictEventArgs"/>.
    /// </summary>
    /// <param name="exception">The <c>exception</c> value.</param>
    /// <param name="pendingContent">The <c>pendingContent</c> value.</param>
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
