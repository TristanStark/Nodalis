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

    public event EventHandler<ExternalModificationException>? ConflictDetected;

    public event EventHandler<Exception>? SaveFailed;

    public void Schedule(string content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);

        _pendingContent = content;

        _scheduledSave?.Cancel();
        _scheduledSave?.Dispose();

        _scheduledSave = new CancellationTokenSource();
        var token = _scheduledSave.Token;

        _pendingTask = SaveAfterDelayAsync(token);
    }

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

            var content = _pendingContent;
            _pendingContent = null;
            await SaveCoreAsync(content, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

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

                var content = _pendingContent;
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
            ConflictDetected?.Invoke(this, exception);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            _pendingContent = content;
            SaveFailed?.Invoke(this, exception);
        }
    }
}
