using Android.App;
using Android.OS;
using AntMedia.WebRTC.Api;
using Org.Webrtc;

using NativeClient = AntMedia.WebRTC.Core.WebRTCClient;

namespace AntMedia.Net;

/// <summary>
/// Android half, built on WebRTCClientBuilder and IWebRTCListener.
/// </summary>
public sealed partial class AntMediaClient
{
    private readonly Func<Activity> _activity;

    private SurfaceViewRenderer? _localRenderer;
    private SurfaceViewRenderer? _remoteRenderer;
    private NativeClient? _client;

    /// <summary>
    /// Held by a track callback while it checks that its client is still <see cref="_client" />
    /// and lets the SDK at the renderers, and by <see cref="StopCore" /> to let the client go. The
    /// SDK does its renderer work in posts to the main thread, so a callback's work is either
    /// queued before the client goes, ahead of anything the next session or
    /// <see cref="DisposeCore" /> queues, or not queued at all.
    /// </summary>
    private readonly object _rendererLock = new();

    private static readonly Handler MainThread = new(Looper.MainLooper!);

    /// <summary>
    /// Incremented by <see cref="Build" />. The bridges carry the value they were created with,
    /// so a callback from a native client that has since been replaced — a publish that timed
    /// out and was retried, say — is recognised as stale and dropped instead of completing the
    /// new operation.
    /// </summary>
    private int _session;

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="options">Connection settings. <see cref="AntMediaOptions.ServerUrl" /> is required.</param>
    /// <param name="activity">
    /// The activity the session belongs to. The SDK needs one for the camera and the renderers,
    /// so it cannot be derived from the application context. In a MAUI app,
    /// <c>AntMedia.Net.Maui</c> supplies it for you. The client must not outlive it: an Android
    /// <c>Activity</c> can be recreated by a configuration change, and a client holding the old
    /// one both leaks it and builds the next session against a dead surface — use the
    /// <see cref="AntMediaClient(AntMediaOptions, Func{Activity})" /> overload when the client
    /// lives longer than a single activity instance.
    /// </param>
    public AntMediaClient(AntMediaOptions options, Activity activity)
        : this(options, ToProvider(activity))
    {
    }

    /// <summary>
    /// Creates a client that resolves the activity when a session starts rather than holding one
    /// for its lifetime — the right constructor when the client is long-lived (in DI, say) and
    /// the current activity can change underneath it.
    /// </summary>
    /// <param name="options">Connection settings. <see cref="AntMediaOptions.ServerUrl" /> is required.</param>
    /// <param name="activityProvider">
    /// Returns the activity to build the next session against. Called at every
    /// <see cref="PublishAsync" /> and <see cref="PlayAsync" />.
    /// </param>
    public AntMediaClient(AntMediaOptions options, Func<Activity> activityProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _activity = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
    }

    private static Func<Activity> ToProvider(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return () => activity;
    }

    /// <summary>
    /// Renders the local camera preview into <paramref name="renderer" />. Call before
    /// publishing; the renderer is handed to the SDK when the session is built, so attaching one
    /// mid-session takes effect at the next publish.
    /// </summary>
    public void SetLocalRenderer(SurfaceViewRenderer? renderer) => _localRenderer = renderer;

    /// <summary>Renders the stream being played into <paramref name="renderer" />. Call before playing.</summary>
    public void SetRemoteRenderer(SurfaceViewRenderer? renderer) => _remoteRenderer = renderer;

    /// <summary>
    /// Runs once the websocket is up, which is what the operation is really waiting for.
    /// </summary>
    private Action? _whenConnected;

    private void PublishCore(string streamId, string mainTrackId) =>
        WhenConnected(SessionKind.Publish, streamId, client =>
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
        WhenConnected(SessionKind.Play, streamId, client => client.Play(streamId));

    private void JoinCore(string streamId) =>
        WhenConnected(SessionKind.Join, streamId, client => client.Join(streamId));

    /// <summary>
    /// What the session renders: a publish shows the local preview, a play the remote stream,
    /// and a p2p join both at once.
    /// </summary>
    private enum SessionKind
    {
        Publish,
        Play,
        Join,
    }

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
    private void WhenConnected(SessionKind kind, string streamId, Action<NativeClient> start)
    {
        var client = Build(kind, streamId);

        _whenConnected = () =>
        {
            _whenConnected = null;
            start(client);
        };

        // Installed before Init(), which otherwise builds the SDK's own handler and connects on
        // this thread. Init() only sets up reconnection on a handler it built itself.
        var webSocket = new BackgroundWebSocketHandler(client, new Handler(Looper.MyLooper()!));
        client.SetWsHandler(webSocket);

        if (_options.ReconnectionEnabled)
        {
            webSocket.SetupWsReconnection();
        }

        client.Init();
    }

    private NativeClient Build(SessionKind kind, string streamId)
    {
        StopCore();

        var session = ++_session;
        var listener = new ListenerBridge(this, session);

        var builder = new WebRTCClientBuilder()
            .SetActivity(_activity())
            .SetServerUrl(_options.ServerUrl)
            .SetStreamId(streamId)
            .SetToken(_options.Token)
            .SetVideoCallEnabled(_options.VideoEnabled)
            .SetAudioCallEnabled(_options.AudioEnabled)
            .SetDataChannelEnabled(_options.DataChannelEnabled)
            .SetVideoWidth(_options.VideoWidth)
            .SetVideoHeight(_options.VideoHeight)
            .SetVideoFps(_options.VideoFps)
            .SetVideoSource(_options.UseFrontCamera
                ? IWebRTCClient.StreamSource.FrontCamera
                : IWebRTCClient.StreamSource.RearCamera)
            .SetReconnectionEnabled(_options.ReconnectionEnabled)
            .SetWebRTCListener(listener);

        if (_options.DataChannelEnabled)
        {
            builder = builder.SetDataChannelObserver(new DataChannelObserverBridge(this, session));
        }

        if (_options.VideoBitrateKbps > 0)
        {
            builder = builder.SetVideoStartBitrate(_options.VideoBitrateKbps);
        }

        if (_options.AudioBitrateKbps > 0)
        {
            builder = builder.SetAudioStartBitrate(_options.AudioBitrateKbps);
        }

        // Only the renderers that will actually show something are attached: handing the builder
        // one that stays empty still initialises an EGL surface for it. A p2p join shows both
        // sides at once.
        if (kind is SessionKind.Publish or SessionKind.Join && _localRenderer is not null)
        {
            builder = builder.SetLocalVideoRenderer(_localRenderer);
        }

        if (kind is SessionKind.Play or SessionKind.Join && _remoteRenderer is not null)
        {
            builder = builder.AddRemoteVideoRenderer(_remoteRenderer);
            listener.RemoteRenderer = _remoteRenderer;
        }

        var client = builder.Build();
        listener.Client = client;

        lock (_rendererLock)
        {
            _client = client;
        }

        return client;
    }

    private void StopCore()
    {
        if (_client is not { } client)
        {
            return;
        }

        // Let go of first, so that a track arriving during or after the stop leaves the renderers
        // alone and raises nothing.
        lock (_rendererLock)
        {
            _client = null;
        }

        try
        {
            client.Stop(StreamId ?? string.Empty);
        }
        catch (Exception)
        {
            // Stopping a session that never fully started throws from the native layer. There is
            // nothing a caller could do about it, and it would mask the real failure.
        }
    }

    private void DisposeCore()
    {
        // The renderers hold an EGL context that managed collection alone does not reclaim.
        _localRenderer?.Release();
        _localRenderer = null;

        // Posted, so that it runs after any attach the SDK queued for a track that arrived just
        // before the stop. Released first, the renderer would be initialised again after it.
        if (_remoteRenderer is { } remote)
        {
            _remoteRenderer = null;
            MainThread.Post(remote.Release);
        }
    }

    /// <inheritdoc />
    public void SwitchCamera() => _client?.SwitchCamera();

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled) => _client?.SetAudioEnabled(enabled);

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled) => _client?.SetVideoEnabled(enabled);

    /// <inheritdoc />
    public void SendData(byte[] data, bool binary = true) =>
        _client?.SendMessageViaDataChannel(
            StreamId ?? string.Empty,
            new DataChannel.Buffer(Java.Nio.ByteBuffer.Wrap(data)!, binary));

    /// <summary>
    /// Translates the SDK's 40-callback listener into this client's events. Derives from
    /// DefaultWebRTCListener so the callbacks not handled here keep their default behaviour
    /// rather than having to be stubbed out. Callbacks from a superseded session — the native
    /// client is rebuilt for every operation — are dropped by the session check. The track
    /// callbacks go further and stop once the client is stopped, because they touch the renderers.
    /// </summary>
    private sealed class ListenerBridge(AntMediaClient owner, int session) : DefaultWebRTCListener
    {
        private bool Stale => owner._session != session;

        public override void OnWebSocketConnected()
        {
            if (Stale)
            {
                return;
            }

            // Deferred until now rather than issued straight after Build() — see WhenConnected.
            var pending = owner._whenConnected;
            pending?.Invoke();
        }

        public override void OnPublishStarted(string streamId)
        {
            if (Stale)
            {
                return;
            }

            owner.CompletePending();
            owner.RaisePublishStarted(streamId);
        }

        public override void OnPublishFinished(string streamId)
        {
            if (!Stale)
            {
                owner.RaisePublishFinished(streamId);
            }
        }

        public override void OnPlayStarted(string streamId)
        {
            if (Stale)
            {
                return;
            }

            owner.CompletePending();
            owner.RaisePlayStarted(streamId);
        }

        public override void OnPlayFinished(string streamId)
        {
            if (!Stale)
            {
                owner.RaisePlayFinished(streamId);
            }
        }

        public override void OnError(string description, string streamId)
        {
            if (!Stale)
            {
                owner.RaiseError(description, streamId);
            }
        }

        // Distinct callbacks upstream, not onError variants — without these two overrides a
        // missing stream or a taken id would stall the operation until the timeout.
        public override void NoStreamExistsToPlay(string streamId)
        {
            if (!Stale)
            {
                owner.RaiseError("no_stream_exist", streamId);
            }
        }

        public override void StreamIdInUse(string streamId)
        {
            if (!Stale)
            {
                owner.RaiseError("streamIdInUse", streamId);
            }
        }

        public override void OnDisconnected()
        {
            if (!Stale)
            {
                owner.RaiseDisconnected(owner.StreamId ?? string.Empty);
            }
        }

        public override void OnJoined(string streamId)
        {
            // The p2p join acknowledgement — this side is in the call, media follows when the
            // other peer arrives.
            if (!Stale)
            {
                owner.CompletePending();
            }
        }

        /// <summary>The native client this listener was built into, set once it exists.</summary>
        public NativeClient? Client { get; set; }

        /// <summary>The remote renderer handed to the SDK along with it, if any.</summary>
        public SurfaceViewRenderer? RemoteRenderer { get; set; }

        /// <summary>
        /// The latest track that arrived to find the renderer taken. libwebrtc reports the tracks a
        /// renegotiation adds before the ones it removes, so a track that replaces another in one
        /// offer always finds the renderer still held by the one it replaces. Under the lock.
        /// </summary>
        private (VideoTrack Track, string? Id)? _waiting;

        /// <summary>
        /// The renderers are shared by every session, so only the client the owner still holds may
        /// touch them: not one that has been replaced, stopped or disposed. Read under the lock.
        /// </summary>
        private bool OwnsRenderers => Client is not null && ReferenceEquals(owner._client, Client);

        // The two-argument overload, renamed in Metadata.xml, is the only one the SDK calls:
        // onAddTrack passes the track id as well. The one-argument overload just forwards here.
        //
        // Base attaches a new track to a free remote renderer, and releases the renderer when its
        // track ends, so it has to run. Ant Media gives every session the same track ids, so a
        // client the owner no longer holds would take or release the current session's renderer:
        // its callbacks skip base along with the event.
        public override void OnNewVideoTrackWithId(VideoTrack? track, string? trackId)
        {
            if (track is null)
            {
                return;
            }

            lock (owner._rendererLock)
            {
                if (!OwnsRenderers)
                {
                    return;
                }

                base.OnNewVideoTrackWithId(track, trackId);

                if (RemoteRenderer is { } renderer && !Holds(renderer, track))
                {
                    _waiting = (track, trackId);
                }
            }

            owner.RaiseTrackAdded(track.Id() ?? string.Empty, track.Kind() ?? "video");
        }

        public override void OnVideoTrackEnded(VideoTrack? track)
        {
            if (track is null)
            {
                return;
            }

            lock (owner._rendererLock)
            {
                if (!OwnsRenderers)
                {
                    return;
                }

                if (_waiting is { } waiting && waiting.Track.Id() == track.Id())
                {
                    _waiting = null;
                }

                base.OnVideoTrackEnded(track);

                // Base queues the release on the main thread, so this goes in behind it.
                if (_waiting is not null)
                {
                    MainThread.Post(AttachWaiting);
                }
            }

            owner.RaiseTrackRemoved(track.Id() ?? string.Empty, track.Kind() ?? "video");
        }

        private void AttachWaiting()
        {
            lock (owner._rendererLock)
            {
                if (!OwnsRenderers || _waiting is not { } waiting)
                {
                    return;
                }

                base.OnNewVideoTrackWithId(waiting.Track, waiting.Id);

                if (RemoteRenderer is not { } renderer || Holds(renderer, waiting.Track))
                {
                    _waiting = null;
                }
            }
        }

        /// <summary>The SDK marks a renderer as taken by setting its tag to the track.</summary>
        private static bool Holds(SurfaceViewRenderer renderer, VideoTrack track) =>
            renderer.Tag is VideoTrack held && held.Id() == track.Id();
    }

    /// <summary>
    /// Forwards inbound data-channel messages. Registered with the builder only when
    /// <see cref="AntMediaOptions.DataChannelEnabled" /> is set — without this bridge
    /// <see cref="DataReceived" /> could never fire on Android, only sending worked.
    /// </summary>
    private sealed class DataChannelObserverBridge(AntMediaClient owner, int session)
        : DefaultDataChannelObserver
    {
        public override void OnMessage(DataChannel.Buffer? buffer, string? dataChannelLabel)
        {
            if (owner._session != session || buffer?.Data is not { } data)
            {
                return;
            }

            // Copied out: the SDK owns the ByteBuffer and reuses it after this returns.
            var bytes = new byte[data.Remaining()];
            data.Get(bytes);

            // Ant Media labels the channel with the stream id.
            owner.RaiseDataReceived(
                string.IsNullOrEmpty(dataChannelLabel) ? owner.StreamId ?? string.Empty : dataChannelLabel,
                bytes,
                buffer.Binary);
        }
    }
}
