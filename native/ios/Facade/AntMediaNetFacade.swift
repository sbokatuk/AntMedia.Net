//
//  AntMediaNetFacade.swift
//
//  Part of https://github.com/sbokatuk/AntMedia.Net — not an Ant Media file.
//
//  native/ios/fetch-ios.sh copies this into the WebRTC-iOS-SDK checkout and adds it to the
//  WebRTCiOSSDK target before building the xcframework, so it compiles inside the SDK's own
//  module and its @objc symbols land in the generated WebRTCiOSSDK-Swift.h.
//
//  WHY THIS EXISTS
//  ---------------
//  WebRTCiOSSDK is written in Swift and exposes essentially nothing to Objective-C: the
//  generated header of the prebuilt xcframework declares exactly two @objc classes,
//  `AntMediaClient` (init plus two delegate callbacks) and `Config` (init). Every useful
//  member — setOptions, publish, play, the AntMediaClientDelegate protocol, AntMediaClientMode —
//  is Swift-only and therefore invisible to Objective Sharpie and to a .NET binding.
//
//  This file re-exposes that API through @objc types so `AntMedia.Net.iOS` has something real to
//  bind. It is a forwarding layer only: no behaviour lives here.
//
//  SCOPE
//  -----
//  Only Foundation/UIKit/CoreMedia types cross the boundary. Members whose signatures are built
//  from Google WebRTC types (getStats, onStats, trackAdded/trackRemoved, setDegradationPreference,
//  deliverExternalPixelBuffer, getLocalVideoTrack/getLocalAudioTrack) are deliberately left out:
//  binding them would force AntMedia.Net.iOS to also bind all of libwebrtc's Objective-C surface.
//  Track lifecycle is surfaced as ids and kinds instead, and video is rendered through
//  setLocalView/setRemoteView, which take plain UIViews.
//

import AVFoundation
import CoreMedia
import Foundation
import UIKit
// Only for the trackAdded/trackRemoved signatures required by AntMediaClientDelegate; no WebRTC
// type crosses the @objc boundary.
import WebRTC

/// Objective-C projection of `AntMediaClientMode`. Raw values match the Swift enum.
@objc(AMSMode)
public enum AMSMode: Int {
    case join = 1
    case play = 2
    case publish = 3
    case conference = 4
    case unspecified = 5

    fileprivate var native: AntMediaClientMode {
        AntMediaClientMode(rawValue: rawValue) ?? .unspecified
    }
}

/// Objective-C projection of `StreamInformation`, which is a Swift class with no @objc surface.
@objcMembers
public class AMSStreamInformation: NSObject {
    public let streamWidth: Int
    public let streamHeight: Int
    public let videoBitrate: Int
    public let audioBitrate: Int
    public let videoCodec: String

    fileprivate init(_ information: StreamInformation) {
        streamWidth = information.streamWidth
        streamHeight = information.streamHeight
        videoBitrate = information.videoBitrate
        audioBitrate = information.audioBitrate
        videoCodec = information.videoCodec
    }
}

/// Objective-C projection of `AntMediaClientDelegate`.
///
/// Every member is optional so a consumer implements only what it needs — the Swift protocol
/// achieves the same thing through an extension with default implementations, which does not
/// survive the bridge to Objective-C.
@objc(AMSClientDelegate)
public protocol AMSClientDelegate: AnyObject {
    @objc optional func clientDidConnect(_ client: AMSClient)
    @objc optional func clientDidDisconnect(_ message: String)
    @objc optional func clientHasError(_ message: String)

    @objc optional func localStreamStarted(_ streamId: String)
    @objc optional func remoteStreamStarted(_ streamId: String)
    @objc optional func remoteStreamRemoved(_ streamId: String)

    @objc optional func playStarted(_ streamId: String)
    @objc optional func playFinished(_ streamId: String)
    @objc optional func publishStarted(_ streamId: String)
    @objc optional func publishFinished(_ streamId: String)
    @objc optional func disconnected(_ streamId: String)

    @objc optional func audioSessionDidStartPlayOrRecord(_ streamId: String)
    @objc optional func audioLevelChanged(_ audioLevel: Double, hasAudio: Bool)

    @objc optional func dataReceived(_ streamId: String, data: Data, binary: Bool)
    @objc optional func streamInformation(_ streamInformation: [AMSStreamInformation])
    @objc optional func eventHappened(_ streamId: String, eventType: String, payload: [String: Any]?)
    @objc optional func broadcastObjectLoaded(_ streamId: String, message: [String: Any])
    @objc optional func streamIdToPublish(_ streamId: String)

    /// `AntMediaClientDelegate.trackAdded` hands over an `RTCMediaStreamTrack`. Only its id and
    /// kind ("audio"/"video") are forwarded, so consumers do not need a WebRTC binding.
    @objc optional func trackAdded(_ trackId: String, kind: String)
    @objc optional func trackRemoved(_ trackId: String, kind: String)
}

/// Objective-C projection of `AntMediaClient`.
///
/// Wraps rather than subclasses: `AntMediaClient` is a Swift class whose members are not @objc,
/// so inheriting it would expose nothing. Delegate callbacks are received by a private bridge
/// (`DelegateBridge`) and re-issued on this type's `delegate`.
@objcMembers
public class AMSClient: NSObject {
    private let client = AntMediaClient()
    private var bridge: DelegateBridge?

    /// Held weakly, matching `AntMediaClient.delegate`: the consumer owns the delegate.
    public weak var delegate: AMSClientDelegate? {
        didSet { attachBridge() }
    }

    public override init() {
        super.init()
        attachBridge()
    }

    private func attachBridge() {
        // The bridge is retained by this client and holds the client weakly, so the
        // consumer -> AMSClient -> bridge -> AntMediaClient chain has no cycle.
        let bridge = DelegateBridge(owner: self)
        self.bridge = bridge
        client.delegate = bridge
    }

    // MARK: - Configuration

    public static func setDebug(_ enabled: Bool) {
        AntMediaClient.setDebug(enabled)
    }

    /// Full websocket url, e.g. `wss://example.com:5443/WebRTCAppEE/websocket`.
    public func setWebSocketServerUrl(_ url: String) {
        client.setWebSocketServerUrl(url: url)
    }

    public func setEnableDataChannel(_ enabled: Bool) {
        client.setEnableDataChannel(enableDataChannel: enabled)
    }

    public func setUseExternalCameraSource(_ useExternalCameraSource: Bool) {
        client.setUseExternalCameraSource(useExternalCameraSource: useExternalCameraSource)
    }

    /// Disables video for the whole session. Must be called before `initPeerConnection`; video
    /// cannot be re-enabled within the same session.
    public func setVideoEnable(_ enabled: Bool) {
        client.setVideoEnable(enable: enabled)
    }

    /// `AVCaptureDevice.Position` is projected as a bool: the SDK only ever uses front/back.
    public func setCameraPosition(front: Bool) {
        client.setCameraPosition(position: front ? .front : .back)
    }

    public func setTargetResolution(width: Int, height: Int) {
        client.setTargetResolution(width: width, height: height)
    }

    public func setTargetFps(_ fps: Int) {
        client.setTargetFps(fps: fps)
    }

    public func setMaxVideoBps(_ bitsPerSecond: NSNumber) {
        client.setMaxVideoBps(videoBitratePerSecond: bitsPerSecond)
    }

    // MARK: - Session lifecycle

    /// Opens the camera and prepares the peer connection without starting to stream. Optional —
    /// `publish`/`play` do this themselves — but useful for showing a preview before connecting.
    public func initPeerConnection(streamId: String, mode: AMSMode, token: String) {
        client.initPeerConnection(streamId: streamId, mode: mode.native, token: token)
    }

    public func publish(streamId: String, token: String, mainTrackId: String) {
        client.publish(streamId: streamId, token: token, mainTrackId: mainTrackId)
    }

    public func play(streamId: String, token: String) {
        client.play(streamId: streamId, token: token)
    }

    /// Joins a peer-to-peer call.
    public func join(streamId: String) {
        client.join(streamId: streamId)
    }

    /// Stops publishing, playing, p2p or conferencing and releases the session's resources.
    public func stop(streamId: String) {
        client.stop(streamId: streamId)
    }

    public func disconnect() {
        client.disconnect()
    }

    public func isConnected() -> Bool {
        client.isConnected()
    }

    public func getStreamId(_ streamId: String) -> String {
        client.getStreamId(streamId)
    }

    // MARK: - Rendering

    public func setLocalView(_ container: UIView, mode: UIView.ContentMode) {
        client.setLocalView(container: container, mode: mode)
    }

    public func setRemoteView(_ container: UIView, mode: UIView.ContentMode) {
        client.setRemoteView(remoteContainer: container, mode: mode)
    }

    // MARK: - Media control

    public func switchCamera() {
        client.switchCamera()
    }

    public func toggleAudio() {
        client.toggleAudio()
    }

    public func toggleVideo() {
        client.toggleVideo()
    }

    /// Enables/disables the local audio track. Does not change microphone state — see `setMicMute`.
    public func setAudioTrack(enabled: Bool) {
        client.setAudioTrack(enableTrack: enabled)
    }

    public func setVideoTrack(enabled: Bool) {
        client.setVideoTrack(enableTrack: enabled)
    }

    public func setMicMute(_ mute: Bool, completionHandler: @escaping (Bool, Error?) -> Void) {
        client.setMicMute(mute: mute, completionHandler: completionHandler)
    }

    public func speakerOn() {
        client.speakerOn()
    }

    public func speakerOff() {
        client.speakerOff()
    }

    /// Enables/disables a *remote* track, i.e. asks the server to stop sending it.
    public func enableTrack(trackId: String, enabled: Bool) {
        client.enableTrack(trackId: trackId, enabled: enabled)
    }

    public func enableVideoTrack(trackId: String, enabled: Bool) {
        client.enableVideoTrack(trackId: trackId, enabled: enabled)
    }

    public func enableAudioTrack(trackId: String, enabled: Bool) {
        client.enableAudioTrack(trackId: trackId, enabled: enabled)
    }

    // MARK: - Zoom

    /// 1.0 is no zoom, 2.0 is 2x. Clamped to the camera's limits by the SDK.
    public func setZoomLevel(_ zoomFactor: CGFloat) {
        client.setZoomLevel(zoomFactor: zoomFactor)
    }

    public func smoothZoom(to zoomFactor: CGFloat, rate: Float) {
        client.smoothZoom(to: zoomFactor, rate: rate)
    }

    public func stopZoomRamp() {
        client.stopZoomRamp()
    }

    // MARK: - Data channel

    public func sendData(_ data: Data, binary: Bool, streamId: String) {
        client.sendData(data: data, binary: binary, streamId: streamId)
    }

    public func isDataChannelActive(streamId: String) -> Bool {
        client.isDataChannelActive(streamId: streamId)
    }

    // MARK: - Stream information

    /// Asks the server for the available renditions. The answer arrives on
    /// `AMSClientDelegate.streamInformation`.
    public func getStreamInfo() {
        client.getStreamInfo()
    }

    /// Forces a rendition by height; 0 restores automatic quality selection.
    public func forceStreamQuality(resolutionHeight: Int, streamId: String) {
        client.forceStreamQuality(resolutionHeight: resolutionHeight, streamId: streamId)
    }

    /// Result arrives on `AMSClientDelegate.broadcastObjectLoaded`.
    public func getBroadcastObject(streamId: String) {
        client.getBroadcastObject(forStreamId: streamId)
    }

    // MARK: - Audio level

    /// Starts periodic `audioLevelChanged` callbacks — useful for "you are muted" detection.
    public func registerAudioLevelExtractor(timeInterval: Double) {
        client.registerAudioLevelExtractor(timeInterval: timeInterval)
    }

    public func removeAudioLevelExtractor() {
        client.removeAudioLevelExtractor()
    }

    // MARK: - External capture (Broadcast Extension)

    public func setExternalAudio(_ enabled: Bool) {
        client.setExternalAudio(externalAudioEnabled: enabled)
    }

    public func setExternalVideoCapture(_ enabled: Bool) {
        client.setExternalVideoCapture(externalVideoCapture: enabled)
    }

    public func deliverExternalAudio(_ sampleBuffer: CMSampleBuffer) {
        client.deliverExternalAudio(sampleBuffer: sampleBuffer)
    }

    /// `rotation` is 0/90/180/270, or -1 to read it from the sample buffer.
    public func deliverExternalVideo(_ sampleBuffer: CMSampleBuffer, rotation: Int) {
        client.deliverExternalVideo(sampleBuffer: sampleBuffer, rotation: rotation)
    }
}

/// Receives Swift-only `AntMediaClientDelegate` callbacks and re-issues them on `AMSClient`.
///
/// Separate from `AMSClient` because `AntMediaClientDelegate` has default implementations
/// supplied by a protocol extension; conforming directly on an @objc class would drag the
/// non-@objc protocol into `AMSClient`'s own surface.
private class DelegateBridge: AntMediaClientDelegate {
    private weak var owner: AMSClient?

    init(owner: AMSClient) {
        self.owner = owner
    }

    private var delegate: AMSClientDelegate? { owner?.delegate }

    func clientDidConnect(_ client: AntMediaClient) {
        guard let owner else { return }
        delegate?.clientDidConnect?(owner)
    }

    func clientDidDisconnect(_ message: String) {
        delegate?.clientDidDisconnect?(message)
    }

    func clientHasError(_ message: String) {
        delegate?.clientHasError?(message)
    }

    func localStreamStarted(streamId: String) {
        delegate?.localStreamStarted?(streamId)
    }

    func remoteStreamStarted(streamId: String) {
        delegate?.remoteStreamStarted?(streamId)
    }

    func remoteStreamRemoved(streamId: String) {
        delegate?.remoteStreamRemoved?(streamId)
    }

    func playStarted(streamId: String) {
        delegate?.playStarted?(streamId)
    }

    func playFinished(streamId: String) {
        delegate?.playFinished?(streamId)
    }

    func publishStarted(streamId: String) {
        delegate?.publishStarted?(streamId)
    }

    func publishFinished(streamId: String) {
        delegate?.publishFinished?(streamId)
    }

    func disconnected(streamId: String) {
        delegate?.disconnected?(streamId)
    }

    func audioSessionDidStartPlayOrRecord(streamId: String) {
        delegate?.audioSessionDidStartPlayOrRecord?(streamId)
    }

    func audioLevelChanged(_ client: AntMediaClient, audioLevel: Double, hasAudio: Bool) {
        delegate?.audioLevelChanged?(audioLevel, hasAudio: hasAudio)
    }

    func dataReceivedFromDataChannel(streamId: String, data: Data, binary: Bool) {
        delegate?.dataReceived?(streamId, data: data, binary: binary)
    }

    func streamInformation(streamInfo: [StreamInformation]) {
        delegate?.streamInformation?(streamInfo.map(AMSStreamInformation.init))
    }

    func eventHappened(streamId: String, eventType: String, payload: [String: Any]?) {
        delegate?.eventHappened?(streamId, eventType: eventType, payload: payload)
    }

    func onLoadBroadcastObject(streamId: String, message: [String: Any]) {
        delegate?.broadcastObjectLoaded?(streamId, message: message)
    }

    func streamIdToPublish(streamId: String) {
        delegate?.streamIdToPublish?(streamId)
    }

    func trackAdded(track: RTCMediaStreamTrack, stream: [RTCMediaStream]) {
        delegate?.trackAdded?(track.trackId, kind: track.kind)
    }

    func trackRemoved(track: RTCMediaStreamTrack) {
        delegate?.trackRemoved?(track.trackId, kind: track.kind)
    }
}
