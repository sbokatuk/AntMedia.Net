using Android.App;
using AntMedia.WebRTC.Api;
using Org.Webrtc;

// The SDK has a WebRTCClient in both AntMedia.WebRTC.Api and AntMedia.WebRTC.Core; the one
// WebRTCClientBuilder.Build() returns is the Core type.
using WebRTCClient = AntMedia.WebRTC.Core.WebRTCClient;

namespace AntMedia.Net.Sample.Streaming;

/// <summary>
/// Android implementation, built on WebRTCClientBuilder and IWebRTCListener.
/// </summary>
public sealed class StreamingSession : IStreamingSession
{
    private readonly Activity _activity;
    private readonly SurfaceViewRenderer _localRenderer;
    private readonly SurfaceViewRenderer _remoteRenderer;

    private WebRTCClient? _client;
    // WebRTCClient does not expose the stream id it was started with, and Stop() requires it.
    private string? _streamId;

    public StreamingSession(Activity activity, VideoSurface localSurface, VideoSurface remoteSurface)
    {
        _activity = activity;
        _localRenderer = Renderer(localSurface);
        _remoteRenderer = Renderer(remoteSurface);
    }

    public event EventHandler<string>? Status;

    public bool IsStreaming { get; private set; }

    public void Publish(string serverUrl, string streamId) => Start(serverUrl, streamId, publish: true);

    public void Play(string serverUrl, string streamId) => Start(serverUrl, streamId, publish: false);

    private void Start(string serverUrl, string streamId, bool publish)
    {
        Stop();

        var builder = new WebRTCClientBuilder()
            .SetActivity(_activity)
            .SetServerUrl(serverUrl)
            .SetStreamId(streamId)
            .SetVideoCallEnabled(true)
            .SetAudioCallEnabled(true)
            .SetWebRTCListener(new ListenerBridge(Report));

        // Only the side that is actually rendered is attached: handing the builder a renderer that
        // stays empty still initialises an EGL surface for it.
        builder = publish
            ? builder.SetLocalVideoRenderer(_localRenderer)
            : builder.AddRemoteVideoRenderer(_remoteRenderer);

        _client = builder.Build();
        _streamId = streamId;

        if (publish)
        {
            _client.Publish(streamId);
        }
        else
        {
            _client.Play(streamId);
        }

        IsStreaming = true;
        Report(publish ? $"publishing {streamId}" : $"playing {streamId}");
    }

    public void Stop()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            _client.Stop(_streamId ?? string.Empty);
        }
        catch (Exception exception)
        {
            // Stopping a session that never connected throws from the native layer; that is not
            // worth surfacing to the user as a failure.
            Report($"stop: {exception.Message}");
        }

        _client = null;
        _streamId = null;
        IsStreaming = false;
        Report("stopped");
    }

    public void Dispose() => Stop();

    private void Report(string message) =>
        MainThread.BeginInvokeOnMainThread(() => Status?.Invoke(this, message));

    private static SurfaceViewRenderer Renderer(VideoSurface surface) =>
        surface.Handler?.PlatformView as SurfaceViewRenderer
        ?? throw new InvalidOperationException(
            "the VideoSurface has no handler yet — wait until the page has been laid out.");

    /// <summary>
    /// Forwards the SDK's callbacks to <see cref="Status" />. Derives from DefaultWebRTCListener
    /// rather than implementing IWebRTCListener so that the ~40 callbacks this sample does not
    /// care about keep their default behaviour.
    /// </summary>
    private sealed class ListenerBridge(Action<string> report) : DefaultWebRTCListener
    {
        public override void OnPublishStarted(string streamId) => report($"publish started: {streamId}");

        public override void OnPublishFinished(string streamId) => report($"publish finished: {streamId}");

        public override void OnPlayStarted(string streamId) => report($"play started: {streamId}");

        public override void OnPlayFinished(string streamId) => report($"play finished: {streamId}");

        public override void OnError(string description, string streamId) => report($"error: {description}");

        public override void OnDisconnected() => report("disconnected");
    }
}
