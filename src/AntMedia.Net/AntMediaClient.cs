namespace AntMedia.Net;

/// <summary>
/// Cross-platform <see cref="IAntMediaClient" />. The platform halves live in
/// Platforms/Android and Platforms/iOS; everything shared — events, state, and turning the
/// SDKs' callbacks into awaitable operations — lives here.
/// </summary>
public sealed partial class AntMediaClient : IAntMediaClient
{
    private readonly AntMediaOptions _options;

    /// <summary>
    /// The in-flight <see cref="PublishAsync" /> or <see cref="PlayAsync" />. Only one at a time:
    /// neither SDK tells you which call a callback belongs to, so a second concurrent operation
    /// could not be matched to its result.
    /// </summary>
    private TaskCompletionSource<bool>? _pending;

    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<AntMediaStreamEventArgs>? PublishStarted;

    /// <inheritdoc />
    public event EventHandler<AntMediaStreamEventArgs>? PublishFinished;

    /// <inheritdoc />
    public event EventHandler<AntMediaStreamEventArgs>? PlayStarted;

    /// <inheritdoc />
    public event EventHandler<AntMediaStreamEventArgs>? PlayFinished;

    /// <inheritdoc />
    public event EventHandler<AntMediaStreamEventArgs>? Disconnected;

    /// <inheritdoc />
    public event EventHandler<AntMediaErrorEventArgs>? Error;

    /// <inheritdoc />
    public event EventHandler<AntMediaTrackEventArgs>? TrackAdded;

    /// <inheritdoc />
    public event EventHandler<AntMediaTrackEventArgs>? TrackRemoved;

    /// <inheritdoc />
    public event EventHandler<AntMediaDataEventArgs>? DataReceived;

    /// <inheritdoc />
    public bool IsStreaming { get; private set; }

    /// <inheritdoc />
    public string? StreamId { get; private set; }

    /// <inheritdoc />
    public Task PublishAsync(
        string streamId,
        string mainTrackId = "",
        CancellationToken cancellationToken = default) =>
        RunAsync(streamId, () => PublishCore(streamId, mainTrackId), cancellationToken);

    /// <inheritdoc />
    public Task PlayAsync(string streamId, CancellationToken cancellationToken = default) =>
        RunAsync(streamId, () => PlayCore(streamId), cancellationToken);

    /// <summary>
    /// Starts an operation and waits for the platform to report that it succeeded.
    ///
    /// Both SDKs are callback-driven with no completion handle, so the awaitable is built here:
    /// the platform half calls <see cref="CompletePending" /> from its started callback and
    /// <see cref="FailPending" /> from its error callback. The timeout is not belt-and-braces —
    /// an unreachable host or an unknown stream id can produce no callback at all, and without it
    /// the returned task would never finish.
    /// </summary>
    private async Task RunAsync(string streamId, Action start, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _options.Validate();

        if (_pending is not null)
        {
            throw new InvalidOperationException(
                "another publish or play is already in progress; await or Stop() it first.");
        }

        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        StreamId = streamId;

        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);

        await using var registration = linked.Token.Register(() =>
        {
            // Distinguishes the two reasons the token can fire: the caller's cancellation should
            // surface as OperationCanceledException, an expired timeout as a failure to start.
            if (cancellationToken.IsCancellationRequested)
            {
                pending.TrySetCanceled(cancellationToken);
            }
            else
            {
                pending.TrySetException(new AntMediaException(
                    $"the server did not confirm '{streamId}' within {_options.Timeout.TotalSeconds:0}s."));
            }
        }).ConfigureAwait(false);

        try
        {
            start();
            await pending.Task.ConfigureAwait(false);
            IsStreaming = true;
        }
        catch
        {
            // A failed start leaves the native client holding a camera and a peer connection.
            StreamId = null;
            StopCore();
            throw;
        }
        finally
        {
            _pending = null;
        }
    }

    /// <summary>Called by the platform half when the server confirms the operation started.</summary>
    private void CompletePending() => _pending?.TrySetResult(true);

    /// <summary>Called by the platform half when the operation cannot proceed.</summary>
    private void FailPending(string message) =>
        _pending?.TrySetException(new AntMediaException(message));

    /// <inheritdoc />
    public void Stop()
    {
        StopCore();
        IsStreaming = false;
        StreamId = null;
    }

    /// <inheritdoc />
    public void SendData(string text) =>
        SendData(System.Text.Encoding.UTF8.GetBytes(text), binary: false);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        DisposeCore();
    }

    // Raised on whichever thread the SDK used. Marshalling to the UI thread is left to the
    // caller, because this package has no dependency on a UI framework; AntMedia.Net.Maui does it.
    private void RaisePublishStarted(string streamId) =>
        PublishStarted?.Invoke(this, new AntMediaStreamEventArgs(streamId));

    private void RaisePublishFinished(string streamId)
    {
        IsStreaming = false;
        PublishFinished?.Invoke(this, new AntMediaStreamEventArgs(streamId));
    }

    private void RaisePlayStarted(string streamId) =>
        PlayStarted?.Invoke(this, new AntMediaStreamEventArgs(streamId));

    private void RaisePlayFinished(string streamId)
    {
        IsStreaming = false;
        PlayFinished?.Invoke(this, new AntMediaStreamEventArgs(streamId));
    }

    private void RaiseDisconnected(string streamId)
    {
        IsStreaming = false;
        Disconnected?.Invoke(this, new AntMediaStreamEventArgs(streamId));
    }

    private void RaiseError(string message, string streamId)
    {
        // An error while starting is the operation's result, not just a notification.
        FailPending(message);
        Error?.Invoke(this, new AntMediaErrorEventArgs(message, streamId));
    }

    private void RaiseTrackAdded(string trackId, string kind) =>
        TrackAdded?.Invoke(this, new AntMediaTrackEventArgs(trackId, kind));

    private void RaiseTrackRemoved(string trackId, string kind) =>
        TrackRemoved?.Invoke(this, new AntMediaTrackEventArgs(trackId, kind));

    private void RaiseDataReceived(string streamId, byte[] data, bool binary) =>
        DataReceived?.Invoke(this, new AntMediaDataEventArgs(streamId, data, binary));
}
