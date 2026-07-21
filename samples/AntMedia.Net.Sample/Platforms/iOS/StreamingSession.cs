using AntMedia.Net.iOS;
using UIKit;

namespace AntMedia.Net.Sample.Streaming;

/// <summary>
/// iOS implementation, built on the @objc facade's AMSClient and AMSClientDelegate.
/// </summary>
public sealed class StreamingSession : IStreamingSession
{
    private readonly UIView _localView;
    private readonly UIView _remoteView;
    private readonly AMSClient _client = new();
    private readonly DelegateBridge _delegate;

    private string? _streamId;

    public StreamingSession(VideoSurface localSurface, VideoSurface remoteSurface)
    {
        _localView = PlatformView(localSurface);
        _remoteView = PlatformView(remoteSurface);

        // Held in a field, not a local: AMSClient.Delegate is a weak reference, so a delegate that
        // only the assignment referenced would be collected and the callbacks would stop silently.
        _delegate = new DelegateBridge(Report);
        _client.Delegate = _delegate;

        _client.SetLocalView(_localView, UIViewContentMode.ScaleAspectFill);
        _client.SetRemoteView(_remoteView, UIViewContentMode.ScaleAspectFit);
    }

    public event EventHandler<string>? Status;

    public bool IsStreaming { get; private set; }

    public void Publish(string serverUrl, string streamId)
    {
        Prepare(serverUrl, streamId, AMSMode.Publish);
        _client.Publish(streamId, token: string.Empty, mainTrackId: string.Empty);

        IsStreaming = true;
        Report($"publishing {streamId}");
    }

    public void Play(string serverUrl, string streamId)
    {
        Prepare(serverUrl, streamId, AMSMode.Play);
        _client.Play(streamId, token: string.Empty);

        IsStreaming = true;
        Report($"playing {streamId}");
    }

    private void Prepare(string serverUrl, string streamId, AMSMode mode)
    {
        Stop();

        _client.SetWebSocketServerUrl(serverUrl);
        _client.SetVideoEnable(true);
        _client.SetTargetResolution(640, 480);
        _client.SetTargetFps(30);

        // Opens the camera and builds the peer connection up front, so the local preview appears
        // before the server is contacted.
        _client.InitPeerConnection(streamId, mode, token: string.Empty);
        _streamId = streamId;
    }

    public void Stop()
    {
        if (_streamId is null)
        {
            return;
        }

        _client.Stop(_streamId);
        _streamId = null;
        IsStreaming = false;
        Report("stopped");
    }

    public void Dispose()
    {
        Stop();
        _client.Dispose();
    }

    private void Report(string message) =>
        MainThread.BeginInvokeOnMainThread(() => Status?.Invoke(this, message));

    private static UIView PlatformView(VideoSurface surface) =>
        surface.Handler?.PlatformView as UIView
        ?? throw new InvalidOperationException(
            "the VideoSurface has no handler yet — wait until the page has been laid out.");

    /// <summary>
    /// Forwards the facade's callbacks to <see cref="Status" />. Every member of
    /// AMSClientDelegate is optional, so only the interesting ones are overridden.
    /// </summary>
    private sealed class DelegateBridge(Action<string> report) : AMSClientDelegate
    {
        public override void ClientDidConnect(AMSClient client) => report("websocket connected");

        public override void ClientDidDisconnect(string message) => report($"disconnected: {message}");

        public override void ClientHasError(string message) => report($"error: {message}");

        public override void PublishStarted(string streamId) => report($"publish started: {streamId}");

        public override void PublishFinished(string streamId) => report($"publish finished: {streamId}");

        public override void PlayStarted(string streamId) => report($"play started: {streamId}");

        public override void PlayFinished(string streamId) => report($"play finished: {streamId}");
    }
}
