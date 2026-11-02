using Android.App;
using Android.OS;
using Android.Util;
using AntMedia.WebRTC.Api;
using Org.Webrtc;

using NativeClient = AntMedia.WebRTC.Core.WebRTCClient;

namespace AntMedia.Net;

/// <summary>
/// Android half, built on WebRTCClientBuilder and IWebRTCListener.
/// </summary>
public sealed partial class AntMediaClient
{
    private const string Tag = "AntMediaClient";

    /// <summary>
    /// How long a stopped session waits for the server to say it has finished. Ant Media answers
    /// straight away; this is for a server that has hung, or a network that has gone.
    /// </summary>
    private static readonly TimeSpan FinishLimit = TimeSpan.FromSeconds(5);

    private readonly Func<Activity> _activity;

    private SurfaceViewRenderer? _localRenderer;
    private SurfaceViewRenderer? _remoteRenderer;
    private NativeClient? _client;
    private BackgroundWebSocketHandler? _webSocket;

    /// <summary>
    /// The thread <see cref="_client" /> was built on. The SDK's websocket calls throw anywhere
    /// else, and a failed start ends on a pool thread, where the stop used to be lost.
    /// </summary>
    private Handler? _clientThread;

    /// <summary>
    /// The stream <see cref="_client" /> was built for. Owned here rather than read from the
    /// shared <see cref="StreamId" />, which already names the new stream when a restart stops
    /// the old one.
    /// </summary>
    private string? _sessionStreamId;

    /// <summary>
    /// True from the server confirming the session until it says the session has finished, which
    /// is when stopping it has an answer to wait for.
    /// </summary>
    private bool _confirmed;

    /// <summary>
    /// A client that has been stopped and is waiting for that answer. Touched only on its thread.
    /// </summary>
    private NativeClient? _finishing;

    /// <summary>
    /// The end of a session stopped from another thread, queued to the client's thread and not
    /// run yet. Whichever comes first runs it: that post, or the next session starting.
    /// </summary>
    private Action? _ending;

    /// <summary>
    /// Incremented by <see cref="Build" />, and by <see cref="StopCore" /> for a session nobody is
    /// waiting to hear from. The bridges carry the value they were created with, so a callback
    /// from a native client that has since been replaced — a publish that timed out and was
    /// retried, say — is recognised as stale and dropped instead of completing the new operation.
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
    /// ConnectWebSocket() opens the socket without requesting a stream, so by the time publish()
    /// runs there is nothing queued for the connect handler to re-send.
    ///
    /// It is ConnectWebSocket() rather than Init(), because publish(), play() and join() each call
    /// Init() again, and Init() isn't safe to repeat while its first run is still going. It checks
    /// for a peer connection factory, then has a shared executor build one, and a second call can
    /// land while the first factory is still being built: either the SDK throws "PeerConnectionFactory
    /// has already been constructed" from inside this callback, which ends the process, or it builds
    /// a second factory and the tracks and the peer connection come from different ones. Releasing
    /// the previous session queues work on that executor ahead of the new factory, which made both
    /// common.
    ///
    /// It also keeps the renderers apart. They carry over from one session to the next, and
    /// release() hands them back through a post to this thread, which Init() inside publish() now
    /// always comes after. Before, a renderer still held by the last session made Init() throw
    /// "Already initialized".
    /// </summary>
    private void WhenConnected(SessionKind kind, string streamId, Action<NativeClient> start)
    {
        var client = Build(kind, streamId);

        _whenConnected = () =>
        {
            _whenConnected = null;

            // Called from the SDK's callback, where an exception would take the process down.
            try
            {
                start(client);
            }
            catch (Exception exception)
            {
                _pending?.TrySetException(exception);
            }
        };

        // Installed before ConnectWebSocket(), which otherwise builds the SDK's own handler and
        // connects on this thread, and only sets up reconnection on a handler it built itself.
        var webSocket = new BackgroundWebSocketHandler(client, _clientThread!);
        client.SetWsHandler(webSocket);
        _webSocket = webSocket;

        if (_options.ReconnectionEnabled)
        {
            webSocket.SetupWsReconnection();
        }

        client.ConnectWebSocket();
    }

    private NativeClient Build(SessionKind kind, string streamId)
    {
        StopCore();

        // The last session ends completely before this one starts: an end still queued from
        // another thread runs now, and a stopped session isn't waited for any longer. Its
        // callbacks are about to go stale, and it has to give up the renderers first.
        RunEnding();
        ReleaseFinishing();

        var session = Interlocked.Increment(ref _session);

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
            .SetWebRTCListener(new ListenerBridge(this, session));

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
        }

        _sessionStreamId = streamId;
        _confirmed = false;
        _clientThread = new Handler(Looper.MyLooper()!);
        _client = builder.Build();
        return _client;
    }

    /// <summary>
    /// Ends the running session. WebRTCClient.stop() alone doesn't: it tells the server if the
    /// socket is up and leaves everything else to release(), which the SDK only calls itself when
    /// ICE disconnects after a stop. A start that failed never got that far, so it was left with
    /// the camera, the factory and a reconnector calling connect() every five seconds for the life
    /// of the process, and the first socket to open would publish for a caller long gone.
    ///
    /// A stream the server has confirmed can't be released straight away, because release() closes
    /// the socket that publish_finished or play_finished comes back on. Those sessions wait for it,
    /// within <see cref="FinishLimit" />; every other session has nothing to wait for.
    /// </summary>
    private void StopCore()
    {
        _whenConnected = null;

        // Exchanged: Stop() and the failure it causes in RunAsync both get here.
        if (Interlocked.Exchange(ref _client, null) is not { } client)
        {
            return;
        }

        // Cleared as well: the SDK's handler holds the client, and the client's config holds the
        // Activity, which an idle client would otherwise keep alive.
        var webSocket = Interlocked.Exchange(ref _webSocket, null);
        var thread = _clientThread!;
        var streamId = _sessionStreamId ?? string.Empty;
        var waitForServer = _confirmed && !_disposed;

        if (!waitForServer)
        {
            Interlocked.Increment(ref _session);
        }

        if (Looper.MyLooper() == thread.Looper)
        {
            EndSession(client, webSocket, thread, streamId, waitForServer);
        }
        else
        {
            _ending = () => EndSession(client, webSocket, thread, streamId, waitForServer);
            thread.Post(RunEnding);
        }
    }

    private void RunEnding() => Interlocked.Exchange(ref _ending, null)?.Invoke();

    private void EndSession(
        NativeClient client,
        BackgroundWebSocketHandler? webSocket,
        Handler thread,
        string streamId,
        bool waitForServer)
    {
        try
        {
            client.Stop(streamId);
        }
        catch (Exception)
        {
            // Stopping a session that never fully started throws from the native layer. There is
            // nothing a caller could do about it, and it would mask the real failure.
        }

        // With the socket down, no answer can arrive and the server was not told to stop, so only
        // closing the peer connection ends the stream.
        if (!waitForServer || !client.IsWebSocketConnected)
        {
            Release(client);
            return;
        }

        // Leaves the open socket alone, so the answer still arrives.
        webSocket?.StopReconnector();

        _finishing = client;

        thread.PostDelayed(
            () =>
            {
                if (ReferenceEquals(_finishing, client))
                {
                    ReleaseFinishing();
                }
            },
            (long)FinishLimit.TotalMilliseconds);
    }

    /// <summary>Called on the client's thread once the server has finished the session.</summary>
    private void OnSessionFinished()
    {
        _confirmed = false;
        ReleaseFinishing();
    }

    private void ReleaseFinishing()
    {
        if (_finishing is { } client)
        {
            _finishing = null;
            Release(client);
        }
    }

    /// <summary>Safe to repeat: release() returns at once if it has run, as it may have on ICE.</summary>
    private static void Release(NativeClient client)
    {
        try
        {
            client.Release(true);
        }
        catch (Exception exception)
        {
            // Often on a posted callback, where an exception would take the process down.
            Log.Error(Tag, $"releasing the native client failed: {exception}");
        }
    }

    private static void RunOn(Handler thread, Action action)
    {
        if (Looper.MyLooper() == thread.Looper)
        {
            action();
        }
        else
        {
            thread.Post(action);
        }
    }

    private void DisposeCore()
    {
        // Disposing cuts short whatever Stop() was still waiting for, as Stop() warns it will.
        if (_clientThread is { } thread)
        {
            RunOn(thread, ReleaseFinishing);
        }

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
    public void SendData(byte[] data, bool binary = true) =>
        _client?.SendMessageViaDataChannel(
            StreamId ?? string.Empty,
            new DataChannel.Buffer(Java.Nio.ByteBuffer.Wrap(data)!, binary));

    /// <summary>
    /// Translates the SDK's 40-callback listener into this client's events. Derives from
    /// DefaultWebRTCListener so the callbacks not handled here keep their default behaviour
    /// rather than having to be stubbed out. Callbacks from a superseded session — the native
    /// client is rebuilt for every operation — are dropped by the session check.
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

            owner._confirmed = true;
            owner.CompletePending();
            owner.RaisePublishStarted(streamId);
        }

        public override void OnPublishFinished(string streamId)
        {
            if (!Stale)
            {
                owner.RaisePublishFinished(streamId);
                owner.OnSessionFinished();
            }
        }

        public override void OnPlayStarted(string streamId)
        {
            if (Stale)
            {
                return;
            }

            owner._confirmed = true;
            owner.CompletePending();
            owner.RaisePlayStarted(streamId);
        }

        public override void OnPlayFinished(string streamId)
        {
            if (!Stale)
            {
                owner.RaisePlayFinished(streamId);
                owner.OnSessionFinished();
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
                owner._confirmed = true;
                owner.CompletePending();
            }
        }

        public override void OnNewVideoTrack(VideoTrack? track)
        {
            if (!Stale && track is not null)
            {
                owner.RaiseTrackAdded(track.Id() ?? string.Empty, track.Kind() ?? "video");
            }
        }

        public override void OnVideoTrackEnded(VideoTrack? track)
        {
            if (!Stale && track is not null)
            {
                owner.RaiseTrackRemoved(track.Id() ?? string.Empty, track.Kind() ?? "video");
            }
        }
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
