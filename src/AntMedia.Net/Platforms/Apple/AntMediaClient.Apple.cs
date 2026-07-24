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
            _client.SetLocalView(view, ToContentMode(scaling));
        }
    }

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
        Configure(streamId, AMSMode.Publish);
        _client.Publish(streamId, _options.Token, mainTrackId);
    }

    private void PlayCore(string streamId)
    {
        Configure(streamId, AMSMode.Play);
        _client.Play(streamId, _options.Token);
    }

    private void Configure(string streamId, AMSMode mode)
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

        // Opens the camera and builds the peer connection before contacting the server, so the
        // local preview appears immediately rather than after the round trip.
        _client.InitPeerConnection(streamId, mode, _options.Token);
    }

    private void StopCore()
    {
        if (StreamId is not null)
        {
            _client.Stop(StreamId);
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

        public override void TrackAdded(string trackId, string kind) =>
            owner.RaiseTrackAdded(trackId, kind);

        public override void TrackRemoved(string trackId, string kind) =>
            owner.RaiseTrackRemoved(trackId, kind);

        public override void DataReceived(string streamId, Foundation.NSData data, bool binary) =>
            owner.RaiseDataReceived(streamId, data.ToArray(), binary);
    }
}
