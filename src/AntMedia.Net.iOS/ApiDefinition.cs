using System;
using CoreMedia;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace AntMedia.Net.iOS;

// Bound against the @objc facade in native/ios/Facade/AntMediaNetFacade.swift, which is compiled
// into WebRTCiOSSDK.xcframework by native/ios/fetch-ios.sh. The upstream Swift SDK exposes almost
// nothing to Objective-C on its own — see docs/BUILD.md.
//
// Written by hand rather than generated with Objective Sharpie: the facade's surface is small and
// designed here, so the selectors are known exactly, and sharpie's output for a Swift-generated
// header needs more cleanup than the file is worth.

/// <summary>
/// A rendition of a stream, as reported by the server in response to
/// <see cref="AMSClient.GetStreamInfo" />.
/// </summary>
[BaseType(typeof(NSObject), Name = "AMSStreamInformation")]
[DisableDefaultCtor] // Swift marks -init unavailable; instances only ever arrive via the delegate.
interface AMSStreamInformation
{
    [Export("streamWidth")]
    nint StreamWidth { get; }

    [Export("streamHeight")]
    nint StreamHeight { get; }

    [Export("videoBitrate")]
    nint VideoBitrate { get; }

    [Export("audioBitrate")]
    nint AudioBitrate { get; }

    [Export("videoCodec")]
    string VideoCodec { get; }
}

/// <summary>
/// Callbacks raised by <see cref="AMSClient" />. Every member is optional.
/// </summary>
[Protocol, Model]
[BaseType(typeof(NSObject), Name = "AMSClientDelegate")]
interface AMSClientDelegate
{
    /// <summary>The websocket connection to the server is open.</summary>
    [Export("clientDidConnect:")]
    void ClientDidConnect(AMSClient client);

    [Export("clientDidDisconnect:")]
    void ClientDidDisconnect(string message);

    [Export("clientHasError:")]
    void ClientHasError(string message);

    /// <summary>The local camera and microphone are capturing.</summary>
    [Export("localStreamStarted:")]
    void LocalStreamStarted(string streamId);

    [Export("remoteStreamStarted:")]
    void RemoteStreamStarted(string streamId);

    [Export("remoteStreamRemoved:")]
    void RemoteStreamRemoved(string streamId);

    [Export("playStarted:")]
    void PlayStarted(string streamId);

    [Export("playFinished:")]
    void PlayFinished(string streamId);

    [Export("publishStarted:")]
    void PublishStarted(string streamId);

    [Export("publishFinished:")]
    void PublishFinished(string streamId);

    /// <summary>The peer connection failed, disconnected or closed.</summary>
    [Export("disconnected:")]
    void Disconnected(string streamId);

    /// <summary>
    /// The audio session started playing or recording. This is the earliest point at which
    /// <see cref="AMSClient.SpeakerOn" /> takes effect.
    /// </summary>
    [Export("audioSessionDidStartPlayOrRecord:")]
    void AudioSessionDidStartPlayOrRecord(string streamId);

    /// <summary>
    /// Microphone level between 0 and 1. Raised only after
    /// <see cref="AMSClient.RegisterAudioLevelExtractor(double)" />.
    /// </summary>
    [Export("audioLevelChanged:hasAudio:")]
    void AudioLevelChanged(double audioLevel, bool hasAudio);

    [Export("dataReceived:data:binary:")]
    void DataReceived(string streamId, NSData data, bool binary);

    [Export("streamInformation:")]
    void StreamInformation(AMSStreamInformation[] streamInformation);

    [Export("eventHappened:eventType:payload:")]
    void EventHappened(string streamId, string eventType, [NullAllowed] NSDictionary payload);

    [Export("broadcastObjectLoaded:message:")]
    void BroadcastObjectLoaded(string streamId, NSDictionary message);

    /// <summary>
    /// The server assigned a stream id to publish with after joining a room. It may differ from
    /// the one that was requested.
    /// </summary>
    [Export("streamIdToPublish:")]
    void StreamIdToPublish(string streamId);

    /// <summary>
    /// A track was added. <paramref name="kind" /> is "audio" or "video".
    /// </summary>
    [Export("trackAdded:kind:")]
    void TrackAdded(string trackId, string kind);

    [Export("trackRemoved:kind:")]
    void TrackRemoved(string trackId, string kind);
}

/// <summary>
/// Client for publishing to and playing from an Ant Media Server.
/// </summary>
[BaseType(typeof(NSObject), Name = "AMSClient")]
interface AMSClient
{
    [NullAllowed, Export("delegate", ArgumentSemantic.Weak)]
    NSObject WeakDelegate { get; set; }

    /// <summary>
    /// The callback receiver. Held weakly, so the caller must keep its own reference to it.
    /// </summary>
    [Wrap("WeakDelegate")]
    [NullAllowed]
    IAMSClientDelegate Delegate { get; set; }

    /// <summary>Writes SDK log messages to the console. Off by default.</summary>
    [Static]
    [Export("setDebug:")]
    void SetDebug(bool enabled);

    // MARK: Configuration

    /// <summary>
    /// The server's websocket url, e.g. <c>wss://example.com:5443/WebRTCAppEE/websocket</c>.
    /// </summary>
    [Export("setWebSocketServerUrl:")]
    void SetWebSocketServerUrl(string url);

    /// <summary>Must also be enabled server-side for the data channel to work.</summary>
    [Export("setEnableDataChannel:")]
    void SetEnableDataChannel(bool enabled);

    /// <summary>
    /// Stops the SDK opening the local camera, for screen capture or externally supplied frames.
    /// </summary>
    [Export("setUseExternalCameraSource:")]
    void SetUseExternalCameraSource(bool useExternalCameraSource);

    /// <summary>
    /// Disables video for the whole session, for audio-only streaming. Call before
    /// <see cref="InitPeerConnection" />; video cannot be re-enabled in the same session.
    /// </summary>
    [Export("setVideoEnable:")]
    void SetVideoEnable(bool enabled);

    /// <summary>Front camera when true, back camera when false.</summary>
    [Export("setCameraPositionWithFront:")]
    void SetCameraPosition(bool front);

    [Export("setTargetResolutionWithWidth:height:")]
    void SetTargetResolution(nint width, nint height);

    /// <summary>Capture frame rate. 30 by default.</summary>
    [Export("setTargetFps:")]
    void SetTargetFps(nint fps);

    [Export("setMaxVideoBps:")]
    void SetMaxVideoBps(NSNumber bitsPerSecond);

    // MARK: Session lifecycle

    /// <summary>
    /// Opens the camera and prepares the peer connection without streaming. Optional —
    /// <see cref="Publish" /> and <see cref="Play" /> do this themselves — but useful for showing
    /// a local preview before connecting.
    /// </summary>
    [Export("initPeerConnectionWithStreamId:mode:token:")]
    void InitPeerConnection(string streamId, AMSMode mode, string token);

    /// <summary>
    /// Publishes a stream. <paramref name="mainTrackId" /> is the conference room id, or empty
    /// for a standalone stream.
    /// </summary>
    [Export("publishWithStreamId:token:mainTrackId:")]
    void Publish(string streamId, string token, string mainTrackId);

    /// <summary>Plays a stream, or a conference room by its id.</summary>
    [Export("playWithStreamId:token:")]
    void Play(string streamId, string token);

    /// <summary>Joins a peer-to-peer call.</summary>
    [Export("joinWithStreamId:")]
    void Join(string streamId);

    /// <summary>
    /// Stops publishing, playing, p2p or conferencing and releases the session's resources.
    /// </summary>
    [Export("stopWithStreamId:")]
    void Stop(string streamId);

    [Export("disconnect")]
    void Disconnect();

    [Export("isConnected")]
    bool IsConnected();

    [Export("getStreamId:")]
    string GetStreamId(string streamId);

    // MARK: Rendering

    /// <summary>Renders the local camera preview into a view.</summary>
    [Export("setLocalView:mode:")]
    void SetLocalView(UIView container, UIViewContentMode mode);

    /// <summary>Renders the stream being played into a view.</summary>
    [Export("setRemoteView:mode:")]
    void SetRemoteView(UIView container, UIViewContentMode mode);

    // MARK: Media control

    [Export("switchCamera")]
    void SwitchCamera();

    [Export("toggleAudio")]
    void ToggleAudio();

    [Export("toggleVideo")]
    void ToggleVideo();

    /// <summary>
    /// Enables or disables the local audio track. Does not change microphone state — see
    /// <see cref="SetMicMute" />.
    /// </summary>
    [Export("setAudioTrackWithEnabled:")]
    void SetAudioTrack(bool enabled);

    [Export("setVideoTrackWithEnabled:")]
    void SetVideoTrack(bool enabled);

    /// <summary>Mutes or unmutes the microphone itself.</summary>
    [Export("setMicMute:completionHandler:")]
    void SetMicMute(bool mute, Action<bool, NSError> completionHandler);

    [Export("speakerOn")]
    void SpeakerOn();

    [Export("speakerOff")]
    void SpeakerOff();

    /// <summary>
    /// Enables or disables a track of the stream being played, i.e. asks the server to stop
    /// sending it.
    /// </summary>
    [Export("enableTrackWithTrackId:enabled:")]
    void EnableTrack(string trackId, bool enabled);

    [Export("enableVideoTrackWithTrackId:enabled:")]
    void EnableVideoTrack(string trackId, bool enabled);

    [Export("enableAudioTrackWithTrackId:enabled:")]
    void EnableAudioTrack(string trackId, bool enabled);

    // MARK: Zoom

    /// <summary>1.0 is no zoom, 2.0 is 2x. Clamped to the camera's limits.</summary>
    [Export("setZoomLevel:")]
    void SetZoomLevel(nfloat zoomFactor);

    /// <summary>
    /// Zooms gradually. <paramref name="rate" /> controls the speed — 1.0 is slow, 5.0 is fast.
    /// </summary>
    [Export("smoothZoomTo:rate:")]
    void SmoothZoom(nfloat zoomFactor, float rate);

    [Export("stopZoomRamp")]
    void StopZoomRamp();

    // MARK: Data channel

    [Export("sendData:binary:streamId:")]
    void SendData(NSData data, bool binary, string streamId);

    [Export("isDataChannelActiveWithStreamId:")]
    bool IsDataChannelActive(string streamId);

    // MARK: Stream information

    /// <summary>
    /// Asks the server for the available renditions. The answer arrives on
    /// <see cref="AMSClientDelegate.StreamInformation" />.
    /// </summary>
    [Export("getStreamInfo")]
    void GetStreamInfo();

    /// <summary>
    /// Forces a rendition by height. 0 restores automatic quality selection.
    /// </summary>
    [Export("forceStreamQualityWithResolutionHeight:streamId:")]
    void ForceStreamQuality(nint resolutionHeight, string streamId);

    /// <summary>
    /// Result arrives on <see cref="AMSClientDelegate.BroadcastObjectLoaded" />.
    /// </summary>
    [Export("getBroadcastObjectWithStreamId:")]
    void GetBroadcastObject(string streamId);

    // MARK: Audio level

    /// <summary>
    /// Starts periodic <see cref="AMSClientDelegate.AudioLevelChanged" /> callbacks — useful for
    /// detecting that someone is speaking while muted.
    /// </summary>
    [Export("registerAudioLevelExtractorWithTimeInterval:")]
    void RegisterAudioLevelExtractor(double timeInterval);

    [Export("removeAudioLevelExtractor")]
    void RemoveAudioLevelExtractor();

    // MARK: External capture (Broadcast Extension)

    [Export("setExternalAudio:")]
    void SetExternalAudio(bool enabled);

    [Export("setExternalVideoCapture:")]
    void SetExternalVideoCapture(bool enabled);

    [Export("deliverExternalAudio:")]
    void DeliverExternalAudio(CMSampleBuffer sampleBuffer);

    /// <summary>
    /// <paramref name="rotation" /> is 0, 90, 180 or 270, or -1 to read it from the buffer.
    /// </summary>
    [Export("deliverExternalVideo:rotation:")]
    void DeliverExternalVideo(CMSampleBuffer sampleBuffer, nint rotation);
}
