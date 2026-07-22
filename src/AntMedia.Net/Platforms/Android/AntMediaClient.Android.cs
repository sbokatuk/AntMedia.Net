using Android.App;
using AntMedia.WebRTC.Api;
using Org.Webrtc;

using NativeClient = AntMedia.WebRTC.Core.WebRTCClient;

namespace AntMedia.Net;

/// <summary>
/// Android half, built on WebRTCClientBuilder and IWebRTCListener.
/// </summary>
public sealed partial class AntMediaClient
{
    private readonly Activity _activity;
    private readonly ListenerBridge _listener;

    private SurfaceViewRenderer? _localRenderer;
    private SurfaceViewRenderer? _remoteRenderer;
    private NativeClient? _client;

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="options">Connection settings. <see cref="AntMediaOptions.ServerUrl" /> is required.</param>
    /// <param name="activity">
    /// The activity the session belongs to. The SDK needs one for the camera and the renderers,
    /// so it cannot be derived from the application context. In a MAUI app,
    /// <c>AntMedia.Net.Maui</c> supplies it for you.
    /// </param>
    public AntMediaClient(AntMediaOptions options, Activity activity)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _listener = new ListenerBridge(this);
    }

    /// <summary>
    /// Renders the local camera preview into <paramref name="renderer" />. Call before publishing;
    /// the renderer is handed to the SDK when the session is built.
    /// </summary>
    public void SetLocalRenderer(SurfaceViewRenderer? renderer) => _localRenderer = renderer;

    /// <summary>Renders the stream being played into <paramref name="renderer" />.</summary>
    public void SetRemoteRenderer(SurfaceViewRenderer? renderer) => _remoteRenderer = renderer;

    /// <summary>
    /// Runs once the websocket is up, which is what the operation is really waiting for.
    /// </summary>
    private Action? _whenConnected;

    private void PublishCore(string streamId, string mainTrackId) =>
        WhenConnected(publishing: true, streamId, client =>
        {
            if (string.IsNullOrEmpty(mainTrackId))
            {
                client.Publish(streamId);
            }
            else
            {
                // The long overload is the only one that takes a main track, so the other
                // arguments are spelled out at their defaults.
                client.Publish(
                    streamId,
                    _options.Token,
                    _options.VideoEnabled,
                    _options.AudioEnabled,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    mainTrackId);
            }
        });

    private void PlayCore(string streamId) =>
        WhenConnected(publishing: false, streamId, client => client.Play(streamId));

    /// <summary>
    /// Opens the websocket first and only then publishes or plays.
    ///
    /// Calling publish() straight after building sends the publish command *twice* against
    /// SDK 2.17.2: isWebSocketConnected() reports true as soon as connect() has been called
    /// rather than when the socket opens, so publish() sends immediately, and the SDK sends it
    /// again from its own onWebSocketConnected handler. The server answers both, two peer
    /// connections are created, and the second offer dies with
    ///
    ///     Failed to set local offer sdp: The order of m-lines in subsequent offer doesn't match
    ///
    /// Init() opens the socket without requesting a stream, so by the time publish() runs there
    /// is nothing queued for the connect handler to re-send.
    /// </summary>
    private void WhenConnected(bool publishing, string streamId, Action<NativeClient> start)
    {
        var client = Build(publishing, streamId);

        _whenConnected = () =>
        {
            _whenConnected = null;
            start(client);
        };

        client.Init();
    }

    private NativeClient Build(bool publishing, string streamId)
    {
        StopCore();

        var builder = new WebRTCClientBuilder()
            .SetActivity(_activity)
            .SetServerUrl(_options.ServerUrl)
            .SetStreamId(streamId)
            .SetToken(_options.Token)
            .SetVideoCallEnabled(_options.VideoEnabled)
            .SetAudioCallEnabled(_options.AudioEnabled)
            .SetDataChannelEnabled(_options.DataChannelEnabled)
            .SetVideoWidth(_options.VideoWidth)
            .SetVideoHeight(_options.VideoHeight)
            .SetVideoFps(_options.VideoFps)
            .SetWebRTCListener(_listener);

        // Only the renderer that will actually show something is attached: handing the builder one
        // that stays empty still initialises an EGL surface for it.
        if (publishing && _localRenderer is not null)
        {
            builder = builder.SetLocalVideoRenderer(_localRenderer);
        }
        else if (!publishing && _remoteRenderer is not null)
        {
            builder = builder.AddRemoteVideoRenderer(_remoteRenderer);
        }

        _client = builder.Build();
        return _client;
    }

    private void StopCore()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            _client.Stop(StreamId ?? string.Empty);
        }
        catch (Exception)
        {
            // Stopping a session that never fully started throws from the native layer. There is
            // nothing a caller could do about it, and it would mask the real failure.
        }

        _client = null;
    }

    private void DisposeCore()
    {
        // The renderers hold an EGL context that managed collection alone does not reclaim.
        _localRenderer?.Release();
        _remoteRenderer?.Release();
        _localRenderer = null;
        _remoteRenderer = null;
    }

    /// <inheritdoc />
    public void SwitchCamera() => _client?.SwitchCamera();

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled) => _client?.SetAudioEnabled(enabled);

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled) => _client?.SetVideoEnabled(enabled);

    /// <inheritdoc />
    public void SendData(byte[] data, bool binary = false) =>
        _client?.SendMessageViaDataChannel(
            StreamId ?? string.Empty,
            new DataChannel.Buffer(Java.Nio.ByteBuffer.Wrap(data)!, binary));

    /// <summary>
    /// Translates the SDK's 40-callback listener into this client's events. Derives from
    /// DefaultWebRTCListener so the callbacks not handled here keep their default behaviour
    /// rather than having to be stubbed out.
    /// </summary>
    private sealed class ListenerBridge(AntMediaClient owner) : DefaultWebRTCListener
    {
        public override void OnWebSocketConnected()
        {
            // Deferred until now rather than issued straight after Build() — see WhenConnected.
            var pending = owner._whenConnected;
            pending?.Invoke();
        }

        public override void OnPublishStarted(string streamId)
        {
            owner.CompletePending();
            owner.RaisePublishStarted(streamId);
        }

        public override void OnPublishFinished(string streamId) => owner.RaisePublishFinished(streamId);

        public override void OnPlayStarted(string streamId)
        {
            owner.CompletePending();
            owner.RaisePlayStarted(streamId);
        }

        public override void OnPlayFinished(string streamId) => owner.RaisePlayFinished(streamId);

        public override void OnError(string description, string streamId) =>
            owner.RaiseError(description, streamId);

        public override void OnDisconnected() => owner.RaiseDisconnected(owner.StreamId ?? string.Empty);

        public override void OnNewVideoTrack(VideoTrack? track)
        {
            if (track is not null)
            {
                owner.RaiseTrackAdded(track.Id() ?? string.Empty, track.Kind() ?? "video");
            }
        }

        public override void OnVideoTrackEnded(VideoTrack? track)
        {
            if (track is not null)
            {
                owner.RaiseTrackRemoved(track.Id() ?? string.Empty, track.Kind() ?? "video");
            }
        }
    }
}
