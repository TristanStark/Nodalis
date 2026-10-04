namespace Nodalis.Infrastructure.Reliability;

public sealed class DocumentAutosaveController : IAsyncDisposable
{
    private readonly TextDocumentSession _session;
    private readonly TimeSpan _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _scheduledSave;
    private string? _pendingContent;
    private Task _pendingTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentAutosaveController"/>.
    /// </summary>
    /// <param name="session">The <c>session</c> value.</param>
    /// <param name="delay">The <c>delay</c> value.</param>
    public DocumentAutosaveController(
            TextDocumentSession session,
            TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        _session = session;
        _delay = delay;
    }

    public event EventHandler? Saved;

    public event EventHandler<AutosaveConflictEventArgs>? ConflictDetected;

    public event EventHandler<AutosaveFailureEventArgs>? SaveFailed;

    /// <summary>
    /// Performs the <c>Schedule</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public void Schedule(string content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);

        _pendingContent = content;

        _scheduledSave?.Cancel();
        _scheduledSave?.Dispose();

        _scheduledSave = new CancellationTokenSource();
        global::System.Threading.CancellationToken token = _scheduledSave.Token;

        _pendingTask = SaveAfterDelayAsync(token);
    }

    /// <summary>
    /// Performs the <c>FlushAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task FlushAsync(
            CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _scheduledSave?.Cancel();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_pendingContent is null)
            {
                return;
            }

            string content = _pendingContent;
            _pendingContent = null;
            await SaveCoreAsync(content, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs the <c>DisposeAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _scheduledSave?.Cancel();

        try
        {
            await _pendingTask;
        }
        catch (OperationCanceledException)
        {
        }

        _scheduledSave?.Dispose();
        _gate.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Performs the <c>SaveAfterDelayAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_delay, cancellationToken);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_pendingContent is null)
                {
                    return;
                }

                string content = _pendingContent;
                _pendingContent = null;
                await SaveCoreAsync(content, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Performs the <c>SaveCoreAsync</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task SaveCoreAsync(
            string content,
            CancellationToken cancellationToken)
    {
        try
        {
            await _session.SaveAsync(content, cancellationToken);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ExternalModificationException exception)
        {
            _pendingContent = content;
            ConflictDetected?.Invoke(this, new AutosaveConflictEventArgs(exception, content));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            _pendingContent = content;
            SaveFailed?.Invoke(this, new AutosaveFailureEventArgs(exception, content));
        }
    }
}
