#if MACCATALYST
using AntMedia.Net.Mac;
#else
using AntMedia.Net.iOS;
#endif
using UIKit;

namespace AntMedia.Net;

/// <summary>
/// Apple half, built on the @objc facade's AMSClient and AMSClientDelegate.
///
/// One file for iOS and Mac Catalyst: the two bindings are generated from the same ApiDefinition
/// and expose the same facade, so only the namespace differs. What differs underneath — Ant
/// Media's own libwebrtc on iOS, a stock Catalyst-capable build plus a shim on macOS — is settled
/// in native/mac/fetch-mac.sh and never reaches here.
/// </summary>
public sealed partial class AntMediaClient
{
    private readonly AMSClient _client = new();
    private readonly DelegateBridge _delegate;

    private UIView? _localView;
    private UIView? _remoteView;

    /// <summary>
    /// Creates a client.
    /// </summary>
    /// <param name="options">Connection settings. <see cref="AntMediaOptions.ServerUrl" /> is required.</param>
    public AntMediaClient(AntMediaOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Kept in a field, not a local: AMSClient.Delegate is a weak reference, so a bridge that
        // only the assignment referenced would be collected and the callbacks would stop arriving
        // with no error anywhere.
        _delegate = new DelegateBridge(this);
        _client.Delegate = _delegate;
    }

    /// <summary>
    /// Renders the local camera preview into <paramref name="view" />, cropped to fill. The SDK
    /// adds its own renderer as a subview. Passing null clears only this client's reference —
    /// the SDK has no detach call, so it keeps rendering into the old view until the session
    /// stops.
    /// </summary>
    public void SetLocalView(UIView? view) => SetLocalView(view, AntMediaVideoScaling.Fill);

    /// <summary>
    /// Renders the local camera preview into <paramref name="view" /> with the given scaling.
    /// </summary>
    public void SetLocalView(UIView? view, AntMediaVideoScaling scaling)
    {
        _localView = view;

        if (view is not null)
        {
            // The SDK's renderer scales to fill its container and UIKit does not clip subviews
            // by default, so a frame whose aspect (or rotation, see below) mismatches the
            // container paints far outside it — over the rest of the page.
            view.ClipsToBounds = true;

            _client.SetLocalView(view, ToContentMode(scaling));

#if MACCATALYST
            PinLocalRotation(view);
#endif
        }
    }

#if MACCATALYST
    /// <summary>
    /// The capturer stamps every frame with the device orientation, which a Mac does not have:
    /// UIDevice reports unknown, the SDK assumes portrait, and the webcam's landscape frames
    /// render rotated 90°. The renderer honours rotationOverride, which is reached by selector
    /// so this package does not need to bind libwebrtc's types. Local preview only — remote
    /// frames carry their sender's real rotation and must keep it.
    /// </summary>
    private static void PinLocalRotation(UIView container)
    {
        var setRotationOverride = new ObjCRuntime.Selector("setRotationOverride:");

        // The SDK embeds its renderer synchronously, so it is in the subviews by now.
        foreach (var subview in container.Subviews)
        {
            if (subview.RespondsToSelector(setRotationOverride))
            {
                // A long-backed NSNumber: the renderer reads the value with getValue:, which
                // copies the number's own width — an int-backed one would underfill the
                // NSInteger-sized rotation.
                subview.PerformSelector(setRotationOverride, Foundation.NSNumber.FromLong(0));
            }
        }
    }
#endif

    /// <summary>
    /// Renders the stream being played into <paramref name="view" />, letterboxed to fit. Null
    /// behaves as on <see cref="SetLocalView(UIView?)" />.
    /// </summary>
    public void SetRemoteView(UIView? view) => SetRemoteView(view, AntMediaVideoScaling.Fit);

    /// <summary>
    /// Renders the stream being played into <paramref name="view" /> with the given scaling.
    /// </summary>
    public void SetRemoteView(UIView? view, AntMediaVideoScaling scaling)
    {
        _remoteView = view;

        if (view is not null)
        {
            // Same containment rule as the local view; rotation is left alone here, because
            // remote frames carry the sender's real rotation.
            view.ClipsToBounds = true;

            _client.SetRemoteView(view, ToContentMode(scaling));
        }
    }

    private static UIViewContentMode ToContentMode(AntMediaVideoScaling scaling) => scaling switch
    {
        AntMediaVideoScaling.Fit => UIViewContentMode.ScaleAspectFit,
        // Balanced is an Android renderer concept; Fill is the closer of UIKit's two.
        _ => UIViewContentMode.ScaleAspectFill,
    };

    private void PublishCore(string streamId, string mainTrackId)
    {
        // The interface promises that starting a new operation ends the previous session.
        // Android gets that from Build(), which stops before it constructs; without the same
        // here, a second Publish collides with this client's own live stream server-side —
        // streamIdInUse against yourself — and tears both sessions down.
        StopCore();
        Configure(streamId, AMSMode.Publish);
        _client.Publish(streamId, _options.Token, mainTrackId);
    }

    private void PlayCore(string streamId)
    {
        StopCore();
        Configure(streamId, AMSMode.Play);
        _client.Play(streamId, _options.Token);
    }

    /// <summary>
    /// True while the current session is a p2p join. The Apple SDK's joined acknowledgement is
    /// swallowed internally (an empty private handler), so the join operation completes on the
    /// first sign of the other peer instead — this flag is what tells the delegate bridge that
    /// remote media should complete the pending operation.
    /// </summary>
    private bool _joining;

    private void JoinCore(string streamId)
    {
        StopCore();

        // No Configure(): the SDK's join() runs its own initPeerConnection with the join mode,
        // and a second init with a different mode would fight it. Only the settings are applied.
        ApplyOptions();
        _sessionStreamId = streamId;
        _joining = true;

        _client.Join(streamId);
    }

    /// <summary>
    /// The stream this half most recently started. Owned here rather than read from the shared
    /// <see cref="StreamId" />, because the shared property is reassigned to the *new* id before
    /// the platform code runs — a restart would otherwise stop the wrong session.
    /// </summary>
    private string? _sessionStreamId;

    private void Configure(string streamId, AMSMode mode)
    {
        ApplyOptions();
        _sessionStreamId = streamId;
        _joining = false;

        // Opens the camera and builds the peer connection before contacting the server, so the
        // local preview appears immediately rather than after the round trip.
        _client.InitPeerConnection(streamId, mode, _options.Token);
    }

    private void ApplyOptions()
    {
        _client.SetWebSocketServerUrl(_options.ServerUrl);
        _client.SetEnableDataChannel(_options.DataChannelEnabled);
        _client.SetVideoEnable(_options.VideoEnabled);
        _client.SetCameraPosition(_options.UseFrontCamera);
        _client.SetTargetResolution(_options.VideoWidth, _options.VideoHeight);
        _client.SetTargetFps(_options.VideoFps);

        // The SDK takes bits per second; the option is kbit/s to match Android's builder. Audio
        // has no equivalent here, and reconnection is built in and always on — both are noted on
        // the options. AntMediaOptions.ReconnectionEnabled therefore only steers Android.
        if (_options.VideoBitrateKbps > 0)
        {
            _client.SetMaxVideoBps(Foundation.NSNumber.FromInt64(_options.VideoBitrateKbps * 1000L));
        }
    }

    private void StopCore()
    {
        _joining = false;

        if (_sessionStreamId is { } id)
        {
            _sessionStreamId = null;
            // For a p2p session the SDK's stop(explicit id) routes to leave(), so the one call
            // covers publish, play and join alike.
            _client.Stop(id);
        }
    }

    private void DisposeCore()
    {
        _client.Delegate = null;
        _client.Dispose();
        _localView = null;
        _remoteView = null;
    }

    /// <inheritdoc />
    public void SwitchCamera() => _client.SwitchCamera();

    /// <inheritdoc />
    public void SetAudioEnabled(bool enabled) => _client.SetAudioTrack(enabled);

    /// <inheritdoc />
    public void SetVideoEnabled(bool enabled) => _client.SetVideoTrack(enabled);

    /// <inheritdoc />
    public void SendData(byte[] data, bool binary = true) =>
        _client.SendData(Foundation.NSData.FromArray(data), binary, StreamId ?? string.Empty);

    /// <summary>
    /// Translates the facade's delegate callbacks into this client's events. Every member of
    /// AMSClientDelegate is optional, so only the ones that matter here are overridden.
    /// </summary>
    private sealed class DelegateBridge(AntMediaClient owner) : AMSClientDelegate
    {
        public override void PublishStarted(string streamId)
        {
            owner.CompletePending();
            owner.RaisePublishStarted(streamId);
        }

        public override void PublishFinished(string streamId) => owner.RaisePublishFinished(streamId);

        public override void PlayStarted(string streamId)
        {
            owner.CompletePending();
            owner.RaisePlayStarted(streamId);
        }

        public override void PlayFinished(string streamId) => owner.RaisePlayFinished(streamId);

        public override void ClientHasError(string message) =>
            owner.RaiseError(message, owner.StreamId ?? string.Empty);

        public override void ClientDidDisconnect(string message) =>
            owner.RaiseDisconnected(owner.StreamId ?? string.Empty);

        public override void Disconnected(string streamId) => owner.RaiseDisconnected(streamId);

        public override void RemoteStreamStarted(string streamId)
        {
            // In a p2p call this is the first observable proof the other peer is connected —
            // the SDK's own joined acknowledgement never reaches the delegate.
            if (owner._joining)
            {
                owner.CompletePending();
            }
        }

        public override void TrackAdded(string trackId, string kind)
        {
            // Belt to RemoteStreamStarted's braces: whichever arrives first completes the join.
            if (owner._joining)
            {
                owner.CompletePending();
            }

            owner.RaiseTrackAdded(trackId, kind);
        }

        public override void TrackRemoved(string trackId, string kind) =>
            owner.RaiseTrackRemoved(trackId, kind);

        public override void DataReceived(string streamId, Foundation.NSData data, bool binary) =>
            owner.RaiseDataReceived(streamId, data.ToArray(), binary);
    }
}
